//! Resident-core BehaviorSignal detectors (Sniffnet-compatible kinds + service/process).

use std::{
    collections::{HashMap, HashSet, VecDeque},
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

use flowarden_core::flow::{
    ConnectionSummary, FinalSnapshot, HostSummary, PacketTimestamp, ServiceSummary,
    TcpConnectionState, TcpConnectionSummary,
};
use serde::Serialize;

use super::{
    p2p_proxy_rules,
    proto::projection::{BehaviorSignalRow, PacketTimestamp as ProtoTs, ProjectionMode},
    state::OverviewRuntimeSnapshot,
};

const MAX_SIGNALS: usize = 30;
const THRESHOLD_COOLDOWN: Duration = Duration::from_secs(30);
const ENTITY_SIGNAL_COOLDOWN: Duration = Duration::from_secs(20);
const DPI_SIGNAL_COOLDOWN: Duration = Duration::from_secs(60);

#[derive(Clone, Debug)]
pub(crate) struct SignalPolicy {
    pub(crate) data_threshold_bytes: u64,
    pub(crate) watched_hosts: Vec<String>,
    pub(crate) known_bad_hosts: Vec<String>,
    pub(crate) watched_services: Vec<String>,
    pub(crate) watched_processes: Vec<String>,
    pub(crate) known_bad_services: Vec<String>,
    pub(crate) known_bad_processes: Vec<String>,
    pub(crate) dpi_exfil_enabled: bool,
    pub(crate) dpi_exfil_min_bytes: u64,
    pub(crate) dpi_exfil_ratio: f64,
    pub(crate) dpi_idle_enabled: bool,
    pub(crate) dpi_idle_min_age_secs: u64,
    pub(crate) dpi_idle_silence_secs: u64,
    pub(crate) dpi_idle_max_bytes: u64,
    pub(crate) dpi_p2p_enabled: bool,
    pub(crate) dpi_p2p_allow: Vec<String>,
}

impl Default for SignalPolicy {
    fn default() -> Self {
        Self {
            data_threshold_bytes: 50_000_000,
            watched_hosts: Vec::new(),
            known_bad_hosts: Vec::new(),
            watched_services: Vec::new(),
            watched_processes: Vec::new(),
            known_bad_services: Vec::new(),
            known_bad_processes: Vec::new(),
            dpi_exfil_enabled: true,
            dpi_exfil_min_bytes: 50_000_000,
            dpi_exfil_ratio: 20.0,
            dpi_idle_enabled: true,
            dpi_idle_min_age_secs: 3600,
            dpi_idle_silence_secs: 600,
            dpi_idle_max_bytes: 65_536,
            dpi_p2p_enabled: true,
            dpi_p2p_allow: Vec::new(),
        }
    }
}

#[derive(Clone, Debug)]
struct ActiveSignal {
    id: String,
    kind: String,
    mode: String,
    status: String,
    severity: String,
    subject: String,
    summary: String,
    detail: String,
    first_seen: PacketTimestamp,
    last_seen: PacketTimestamp,
    update_count: u32,
    confidence: f64,
    dedupe_key: String,
    pivot_kind: String,
    pivot_value: String,
}

#[derive(Default)]
pub(crate) struct SignalEngine {
    policy: SignalPolicy,
    log: VecDeque<ActiveSignal>,
    last_emit: HashMap<String, Instant>,
    last_threshold_at: Option<Instant>,
    /// Offline findings are stable: once a threshold finding exists for this session, keep it.
    offline_threshold_emitted: bool,
}

impl SignalEngine {
    pub(crate) fn set_policy(&mut self, policy: SignalPolicy) {
        self.policy = policy;
    }

    /// Clear live/offline session state when a new capture starts.
    pub(crate) fn reset_session(&mut self) {
        self.log.clear();
        self.last_emit.clear();
        self.last_threshold_at = None;
        self.offline_threshold_emitted = false;
    }

    pub(crate) fn evaluate_and_list(
        &mut self,
        snapshot: &OverviewRuntimeSnapshot,
    ) -> Vec<BehaviorSignalRow> {
        let now = PacketTimestamp::now().unwrap_or(PacketTimestamp::tick(
            SystemTime::now()
                .duration_since(UNIX_EPOCH)
                .map(|d| d.as_secs() as i64)
                .unwrap_or(0),
        ));
        let is_offline = matches!(snapshot.mode, ProjectionMode::Offline);
        let mode = if is_offline { "offline" } else { "live" };

        if self.policy.data_threshold_bytes > 0
            && snapshot.totals.bytes >= self.policy.data_threshold_bytes
            && self.may_emit_threshold(is_offline)
        {
            if is_offline {
                self.offline_threshold_emitted = true;
            } else {
                self.last_threshold_at = Some(Instant::now());
            }
            self.push(
                ActiveSignal {
                    id: format!("threshold-{}", now.seconds),
                    kind: "DataThresholdExceeded".to_string(),
                    mode: mode.to_string(),
                    status: initial_status(is_offline).to_string(),
                    severity: "warning".to_string(),
                    subject: snapshot.source_label.clone(),
                    summary: if is_offline {
                        "Data threshold exceeded (offline finding)".to_string()
                    } else {
                        "Data threshold exceeded".to_string()
                    },
                    detail: format!(
                        "Observed {} bytes (threshold {}).",
                        snapshot.totals.bytes, self.policy.data_threshold_bytes
                    ),
                    first_seen: now,
                    last_seen: now,
                    update_count: 1,
                    confidence: 0.9,
                    dedupe_key: format!(
                        "DataThresholdExceeded|{}",
                        self.policy.data_threshold_bytes
                    ),
                    pivot_kind: "none".to_string(),
                    pivot_value: String::new(),
                },
                is_offline,
            );
        }

        for host in &snapshot.top_hosts {
            self.evaluate_host(host, mode, is_offline, now);
        }
        for service in &snapshot.top_services {
            self.evaluate_service(service, mode, is_offline, now);
        }
        if self.policy.dpi_exfil_enabled {
            for conn in &snapshot.top_connections {
                self.evaluate_exfil(conn, mode, is_offline, now);
            }
        }
        if self.policy.dpi_idle_enabled {
            for tcp in &snapshot.tcp_connections {
                self.evaluate_idle_tcp(tcp, mode, is_offline, now);
            }
        }
        if self.policy.dpi_p2p_enabled {
            for conn in &snapshot.top_connections {
                self.evaluate_p2p_or_proxy_connection(conn, mode, is_offline, now);
            }
        }
        self.log.iter().map(signal_to_proto).collect()
    }

    /// Process names are resolved during projection enrichment; call after process lookup.
    pub(crate) fn evaluate_processes(
        &mut self,
        processes: &[(String, u64)],
        mode: &str,
    ) {
        let is_offline = mode == "offline";
        let now = PacketTimestamp::now().unwrap_or(PacketTimestamp::tick(0));
        let watched = self.policy.watched_processes.clone();
        let bad = self.policy.known_bad_processes.clone();
        for (name, bytes) in processes {
            if name.is_empty() {
                continue;
            }
            if watched.iter().any(|p| text_matches(name, p)) {
                let key = format!("WatchedEntityTransmitted|process|{name}");
                if self.may_emit_entity(&key, is_offline) {
                    self.push(
                        ActiveSignal {
                            id: format!("watch-proc-{}-{}", name, now.seconds),
                            kind: "WatchedEntityTransmitted".to_string(),
                            mode: mode.to_string(),
                            status: initial_status(is_offline).to_string(),
                            severity: "info".to_string(),
                            subject: name.clone(),
                            summary: if is_offline {
                                "Watched process (offline finding)".to_string()
                            } else {
                                "Watched process active".to_string()
                            },
                            detail: format!("Process `{name}` transmitted {bytes} bytes."),
                            first_seen: now,
                            last_seen: now,
                            update_count: 1,
                            confidence: 0.8,
                            dedupe_key: key,
                            pivot_kind: "process".to_string(),
                            pivot_value: name.clone(),
                        },
                        is_offline,
                    );
                }
            }
            if bad.iter().any(|p| text_matches(name, p)) {
                let key = format!("KnownBadHostTransmitted|process|{name}");
                if self.may_emit_entity(&key, is_offline) {
                    self.push(
                        ActiveSignal {
                            id: format!("bad-proc-{}-{}", name, now.seconds),
                            kind: "KnownBadHostTransmitted".to_string(),
                            mode: mode.to_string(),
                            status: initial_status(is_offline).to_string(),
                            severity: "error".to_string(),
                            subject: name.clone(),
                            summary: if is_offline {
                                "Known-bad process (offline finding)".to_string()
                            } else {
                                "Known-bad process active".to_string()
                            },
                            detail: format!(
                                "Process `{name}` matched a known-bad entry ({bytes} bytes)."
                            ),
                            first_seen: now,
                            last_seen: now,
                            update_count: 1,
                            confidence: 0.85,
                            dedupe_key: key,
                            pivot_kind: "process".to_string(),
                            pivot_value: name.clone(),
                        },
                        is_offline,
                    );
                }
            }
            if self.policy.dpi_p2p_enabled
                && p2p_proxy_rules::process_looks_like_p2p_or_proxy(name)
                && !self.is_p2p_allowed(&format!("process:{name}"))
            {
                let key = format!("UnauthorizedP2pOrProxy|process|{name}");
                if self.may_emit_dpi(&key, is_offline) {
                    self.push(
                        ActiveSignal {
                            id: format!("p2p-proc-{}-{}", name, now.seconds),
                            kind: "UnauthorizedP2pOrProxy".to_string(),
                            mode: mode.to_string(),
                            status: initial_status(is_offline).to_string(),
                            severity: "warning".to_string(),
                            subject: name.clone(),
                            summary: "Unauthorized P2P or proxy process".to_string(),
                            detail: format!(
                                "Process `{name}` matched built-in P2P/proxy heuristics ({bytes} bytes)."
                            ),
                            first_seen: now,
                            last_seen: now,
                            update_count: 1,
                            confidence: 0.85,
                            dedupe_key: key,
                            pivot_kind: "process".to_string(),
                            pivot_value: name.clone(),
                        },
                        is_offline,
                    );
                }
            }
        }
    }

    pub(crate) fn list_proto(&self) -> Vec<BehaviorSignalRow> {
        self.log.iter().map(signal_to_proto).collect()
    }

    fn evaluate_host(
        &mut self,
        host: &HostSummary,
        mode: &str,
        is_offline: bool,
        now: PacketTimestamp,
    ) {
        let host_s = host.host.to_string();
        let sni = host.counters.sni.clone().unwrap_or_default();
        let watched = self.policy.watched_hosts.clone();
        let bad = self.policy.known_bad_hosts.clone();

        if watched.iter().any(|p| host_or_sni_matches(&host_s, &sni, p)) {
            let key = format!("WatchedEntityTransmitted|host|{host_s}");
            if self.may_emit_entity(&key, is_offline) {
                let pivot = if sni.is_empty() {
                    ("host", host_s.as_str())
                } else {
                    ("sni", sni.as_str())
                };
                self.push(
                    ActiveSignal {
                        id: format!("watch-host-{}-{}", host_s, now.seconds),
                        kind: "WatchedEntityTransmitted".to_string(),
                        mode: mode.to_string(),
                        status: initial_status(is_offline).to_string(),
                        severity: "info".to_string(),
                        subject: host_s.clone(),
                        summary: if is_offline {
                            "Watched host (offline finding)".to_string()
                        } else {
                            "Watched host active".to_string()
                        },
                        detail: format!(
                            "{host_s} transmitted {} bytes{}.",
                            host.counters.bytes,
                            if sni.is_empty() {
                                String::new()
                            } else {
                                format!(" (sni={sni})")
                            }
                        ),
                        first_seen: now,
                        last_seen: now,
                        update_count: 1,
                        confidence: 0.85,
                        dedupe_key: key,
                        pivot_kind: pivot.0.to_string(),
                        pivot_value: pivot.1.to_string(),
                    },
                    is_offline,
                );
            }
        }

        if bad.iter().any(|p| host_or_sni_matches(&host_s, &sni, p)) {
            let key = format!("KnownBadHostTransmitted|host|{host_s}");
            if self.may_emit_entity(&key, is_offline) {
                self.push(
                    ActiveSignal {
                        id: format!("bad-host-{}-{}", host_s, now.seconds),
                        kind: "KnownBadHostTransmitted".to_string(),
                        mode: mode.to_string(),
                        status: initial_status(is_offline).to_string(),
                        severity: "error".to_string(),
                        subject: host_s.clone(),
                        summary: if is_offline {
                            "Known-bad host (offline finding)".to_string()
                        } else {
                            "Known-bad host active".to_string()
                        },
                        detail: format!(
                            "{host_s} matched a known-bad entry ({} bytes).",
                            host.counters.bytes
                        ),
                        first_seen: now,
                        last_seen: now,
                        update_count: 1,
                        confidence: 0.9,
                        dedupe_key: key,
                        pivot_kind: "host".to_string(),
                        pivot_value: host_s,
                    },
                    is_offline,
                );
            }
        }
    }

    fn evaluate_service(
        &mut self,
        service: &ServiceSummary,
        mode: &str,
        is_offline: bool,
        now: PacketTimestamp,
    ) {
        let name = service.service.name.clone();
        let watched = self.policy.watched_services.clone();
        let bad = self.policy.known_bad_services.clone();

        if watched.iter().any(|p| text_matches(&name, p)) {
            let key = format!("WatchedEntityTransmitted|service|{name}");
            if self.may_emit_entity(&key, is_offline) {
                self.push(
                    ActiveSignal {
                        id: format!("watch-svc-{}-{}", name, now.seconds),
                        kind: "WatchedEntityTransmitted".to_string(),
                        mode: mode.to_string(),
                        status: initial_status(is_offline).to_string(),
                        severity: "info".to_string(),
                        subject: name.clone(),
                        summary: if is_offline {
                            "Watched service (offline finding)".to_string()
                        } else {
                            "Watched service active".to_string()
                        },
                        detail: format!(
                            "Service `{name}` transmitted {} bytes.",
                            service.counters.bytes
                        ),
                        first_seen: now,
                        last_seen: now,
                        update_count: 1,
                        confidence: 0.8,
                        dedupe_key: key,
                        pivot_kind: "service".to_string(),
                        pivot_value: name.clone(),
                    },
                    is_offline,
                );
            }
        }

        if bad.iter().any(|p| text_matches(&name, p)) {
            let key = format!("KnownBadHostTransmitted|service|{name}");
            if self.may_emit_entity(&key, is_offline) {
                self.push(
                    ActiveSignal {
                        id: format!("bad-svc-{}-{}", name, now.seconds),
                        kind: "KnownBadHostTransmitted".to_string(),
                        mode: mode.to_string(),
                        status: initial_status(is_offline).to_string(),
                        severity: "error".to_string(),
                        subject: name.clone(),
                        summary: if is_offline {
                            "Known-bad service (offline finding)".to_string()
                        } else {
                            "Known-bad service active".to_string()
                        },
                        detail: format!(
                            "Service `{name}` matched a known-bad entry ({} bytes).",
                            service.counters.bytes
                        ),
                        first_seen: now,
                        last_seen: now,
                        update_count: 1,
                        confidence: 0.85,
                        dedupe_key: key,
                        pivot_kind: "service".to_string(),
                        pivot_value: name.clone(),
                    },
                    is_offline,
                );
            }
        }
    }

    fn evaluate_exfil(
        &mut self,
        conn: &ConnectionSummary,
        mode: &str,
        is_offline: bool,
        now: PacketTimestamp,
    ) {
        let out = conn.counters.bytes_out;
        let inn = conn.counters.bytes_in;
        if out < self.policy.dpi_exfil_min_bytes {
            return;
        }
        let ratio = out as f64 / (inn.max(1) as f64);
        if ratio < self.policy.dpi_exfil_ratio {
            return;
        }
        let peer = conn.key.destination_ip.to_string();
        let key = format!("UnidirectionalLargeTransfer|{peer}|{}", conn.key.destination_port.unwrap_or(0));
        if !self.may_emit_dpi(&key, is_offline) {
            return;
        }
        let service = conn
            .counters
            .sni
            .clone()
            .unwrap_or_else(|| "unknown".into());
        self.push(
            ActiveSignal {
                id: format!("exfil-{}-{}", peer, now.seconds),
                kind: "UnidirectionalLargeTransfer".to_string(),
                mode: mode.to_string(),
                status: initial_status(is_offline).to_string(),
                severity: "warning".to_string(),
                subject: peer.clone(),
                summary: "Unidirectional large transfer (possible exfil)".to_string(),
                detail: format!(
                    "Outbound {out} bytes vs inbound {inn} (ratio {ratio:.1}x, min {}, ratio≥{}). peer={peer} sni/service≈{service}",
                    self.policy.dpi_exfil_min_bytes, self.policy.dpi_exfil_ratio
                ),
                first_seen: conn.counters.first_seen,
                last_seen: conn.counters.last_seen,
                update_count: 1,
                confidence: 0.8,
                dedupe_key: key,
                pivot_kind: "host".to_string(),
                pivot_value: peer,
            },
            is_offline,
        );
    }

    fn evaluate_idle_tcp(
        &mut self,
        tcp: &TcpConnectionSummary,
        mode: &str,
        is_offline: bool,
        now: PacketTimestamp,
    ) {
        if tcp.stats.state != TcpConnectionState::Established {
            return;
        }
        if tcp.stats.bytes > self.policy.dpi_idle_max_bytes {
            return;
        }
        let age = now.seconds.saturating_sub(tcp.stats.first_seen.seconds);
        if age < self.policy.dpi_idle_min_age_secs as i64 {
            return;
        }
        let last_data = tcp
            .stats
            .last_payload_seen
            .unwrap_or(tcp.stats.first_seen);
        let silence = now.seconds.saturating_sub(last_data.seconds);
        if silence < self.policy.dpi_idle_silence_secs as i64 {
            return;
        }
        let a = format!(
            "{}:{}",
            tcp.key.endpoint_a.ip, tcp.key.endpoint_a.port
        );
        let b = format!(
            "{}:{}",
            tcp.key.endpoint_b.ip, tcp.key.endpoint_b.port
        );
        let key = format!("LongIdleTcpConnection|{a}-{b}");
        if !self.may_emit_dpi(&key, is_offline) {
            return;
        }
        self.push(
            ActiveSignal {
                id: format!("idle-{}-{}", a, now.seconds),
                kind: "LongIdleTcpConnection".to_string(),
                mode: mode.to_string(),
                status: initial_status(is_offline).to_string(),
                severity: "info".to_string(),
                subject: format!("{a} ↔ {b}"),
                summary: "Long-lived idle TCP connection".to_string(),
                detail: format!(
                    "Established age={age}s silence={silence}s total_bytes={} payload_bytes={} (min_age={}s silence≥{}s max_bytes={})",
                    tcp.stats.bytes,
                    tcp.stats.payload_bytes,
                    self.policy.dpi_idle_min_age_secs,
                    self.policy.dpi_idle_silence_secs,
                    self.policy.dpi_idle_max_bytes
                ),
                first_seen: tcp.stats.first_seen,
                last_seen: tcp.stats.last_seen,
                update_count: 1,
                confidence: 0.75,
                dedupe_key: key,
                pivot_kind: "host".to_string(),
                pivot_value: tcp.key.endpoint_b.ip.to_string(),
            },
            is_offline,
        );
    }

    fn evaluate_p2p_or_proxy_connection(
        &mut self,
        conn: &ConnectionSummary,
        mode: &str,
        is_offline: bool,
        now: PacketTimestamp,
    ) {
        let dport = conn.key.destination_port.unwrap_or(0);
        let sport = conn.key.source_port.unwrap_or(0);
        let sni = conn.counters.sni.clone().unwrap_or_default();
        // Service name is not on FlowCounters; use SNI / ports only here.
        let port_hit = p2p_proxy_rules::is_proxy_port(dport)
            || p2p_proxy_rules::is_proxy_port(sport)
            || p2p_proxy_rules::is_p2p_port(dport)
            || p2p_proxy_rules::is_p2p_port(sport);
        let sni_hit = !sni.is_empty() && p2p_proxy_rules::sni_looks_like_proxy_infra(&sni);
        if !port_hit && !sni_hit {
            return;
        }
        if self.is_p2p_allowed(&format!("port:{dport}"))
            || self.is_p2p_allowed(&format!("port:{sport}"))
            || (!sni.is_empty() && self.is_p2p_allowed(&format!("sni:{sni}")))
        {
            return;
        }
        let peer = conn.key.destination_ip.to_string();
        let key = format!("UnauthorizedP2pOrProxy|port|{dport}|{peer}");
        if !self.may_emit_dpi(&key, is_offline) {
            return;
        }
        let confidence = if port_hit && sni_hit {
            0.9
        } else if port_hit {
            0.55
        } else {
            0.6
        };
        self.push(
            ActiveSignal {
                id: format!("p2p-{}-{}-{}", dport, peer, now.seconds),
                kind: "UnauthorizedP2pOrProxy".to_string(),
                mode: mode.to_string(),
                status: initial_status(is_offline).to_string(),
                severity: "warning".to_string(),
                subject: peer.clone(),
                summary: "Unauthorized P2P or proxy traffic".to_string(),
                detail: format!(
                    "Heuristic match sport={sport} dport={dport} sni={sni} peer={peer} bytes={}",
                    conn.counters.bytes
                ),
                first_seen: conn.counters.first_seen,
                last_seen: conn.counters.last_seen,
                update_count: 1,
                confidence,
                dedupe_key: key,
                pivot_kind: if sni.is_empty() {
                    "host".to_string()
                } else {
                    "sni".to_string()
                },
                pivot_value: if sni.is_empty() { peer } else { sni },
            },
            is_offline,
        );
    }

    fn is_p2p_allowed(&self, token: &str) -> bool {
        self.policy
            .dpi_p2p_allow
            .iter()
            .any(|p| text_matches(token, p) || text_matches(token, &format!("process:{p}")))
    }

    fn may_emit_threshold(&self, is_offline: bool) -> bool {
        if is_offline {
            // Stable offline finding: emit once per capture session.
            return !self.offline_threshold_emitted;
        }
        self.last_threshold_at
            .map(|t| t.elapsed() >= THRESHOLD_COOLDOWN)
            .unwrap_or(true)
    }

    /// Live uses wall-clock cooldown; offline always attempts emit (dedupe merges findings).
    fn may_emit_entity(&mut self, key: &str, is_offline: bool) -> bool {
        if is_offline {
            return true;
        }
        if let Some(prev) = self.last_emit.get(key)
            && prev.elapsed() < ENTITY_SIGNAL_COOLDOWN
        {
            return false;
        }
        self.last_emit.insert(key.to_string(), Instant::now());
        true
    }

    fn may_emit_dpi(&mut self, key: &str, is_offline: bool) -> bool {
        if is_offline {
            return true;
        }
        if let Some(prev) = self.last_emit.get(key)
            && prev.elapsed() < DPI_SIGNAL_COOLDOWN
        {
            return false;
        }
        self.last_emit.insert(key.to_string(), Instant::now());
        true
    }

    fn push(&mut self, signal: ActiveSignal, is_offline: bool) {
        if let Some(existing) = self
            .log
            .iter_mut()
            .find(|s| s.dedupe_key == signal.dedupe_key)
        {
            existing.last_seen = signal.last_seen;
            existing.update_count = existing.update_count.saturating_add(1);
            // Offline findings stay stable; live signals mark as updated.
            existing.status = if is_offline {
                "finding".to_string()
            } else {
                "updated".to_string()
            };
            existing.detail = signal.detail;
            existing.summary = signal.summary;
            existing.mode = signal.mode;
            return;
        }

        self.log.push_front(signal);
        while self.log.len() > MAX_SIGNALS {
            self.log.pop_back();
        }
    }
}

fn initial_status(is_offline: bool) -> &'static str {
    if is_offline {
        "finding"
    } else {
        "active"
    }
}

/// Serializable finding for CLI JSON (same detector as resident core).
#[derive(Clone, Debug, Serialize, PartialEq)]
pub struct CliFinding {
    pub id: String,
    pub kind: String,
    pub mode: String,
    pub status: String,
    pub severity: String,
    pub subject: String,
    pub summary: String,
    pub detail: String,
    pub confidence: f64,
    pub update_count: u32,
    pub pivot_kind: String,
    pub pivot_value: String,
    pub first_seen_seconds: i64,
    pub last_seen_seconds: i64,
}

/// Evaluate stable offline (or live batch) findings from a finished capture snapshot.
pub fn evaluate_cli_findings(
    final_snapshot: &FinalSnapshot,
    is_offline: bool,
    top_n: usize,
    data_threshold_bytes: u64,
    watched_patterns: impl IntoIterator<Item = String>,
    known_bad_patterns: impl IntoIterator<Item = String>,
) -> Vec<CliFinding> {
    let watched = parse_entity_patterns(watched_patterns);
    let bad = parse_entity_patterns(known_bad_patterns);
    let policy = SignalPolicy {
        data_threshold_bytes,
        watched_hosts: watched.hosts,
        known_bad_hosts: bad.hosts,
        watched_services: watched.services,
        watched_processes: watched.processes,
        known_bad_services: bad.services,
        known_bad_processes: bad.processes,
        ..SignalPolicy::default()
    };

    let mut engine = SignalEngine::default();
    engine.set_policy(policy);

    let mode = if is_offline {
        ProjectionMode::Offline
    } else {
        ProjectionMode::Live
    };
    let snapshot = OverviewRuntimeSnapshot {
        capture_id: final_snapshot.capture_id.clone(),
        mode,
        source_label: if is_offline {
            "Offline capture".into()
        } else {
            "Live capture".into()
        },
        filter_label: "Filter · none".into(),
        metric_mode: "bytes".into(),
        capture_status: "idle".into(),
        local_ips: HashSet::new(),
        tick_snapshots: Vec::new(),
        offline_gaps: Vec::new(),
        top_connections: final_snapshot
            .aggregate_summary
            .top_connections
            .iter()
            .take(top_n.max(1))
            .cloned()
            .collect(),
        top_hosts: final_snapshot
            .aggregate_summary
            .top_hosts
            .iter()
            .take(top_n.max(1))
            .cloned()
            .collect(),
        top_services: final_snapshot
            .aggregate_summary
            .top_services
            .iter()
            .take(top_n.max(1))
            .cloned()
            .collect(),
        tcp_connections: final_snapshot
            .aggregate_summary
            .tcp_connections
            .iter()
            .take(top_n.max(1))
            .cloned()
            .collect(),
        totals: final_snapshot.totals.clone(),
        dropped_packets: final_snapshot.dropped_packets,
        last_packet_timestamp: final_snapshot.last_packet_timestamp,
        error_message: None,
    };

    // Process names unavailable in pure CLI path (no OS lookup).
    engine
        .evaluate_and_list(&snapshot)
        .into_iter()
        .map(|row| CliFinding {
            id: row.id,
            kind: row.kind,
            mode: row.mode,
            status: row.status,
            severity: row.severity,
            subject: row.subject,
            summary: row.summary,
            detail: row.detail,
            confidence: row.confidence,
            update_count: row.update_count,
            pivot_kind: row.pivot_kind,
            pivot_value: row.pivot_value,
            first_seen_seconds: row.first_seen.map(|t| t.seconds).unwrap_or(0),
            last_seen_seconds: row.last_seen.map(|t| t.seconds).unwrap_or(0),
        })
        .collect()
}

/// Parse UI pattern tokens into policy lists.
/// Supports plain host/IP, `host:`, `sni:`, `service:`, `process:` prefixes.
pub(crate) fn parse_entity_patterns(patterns: impl IntoIterator<Item = String>) -> SignalPolicyBuckets {
    let mut buckets = SignalPolicyBuckets::default();
    for raw in patterns {
        let token = raw.trim();
        if token.is_empty() {
            continue;
        }
        if let Some(rest) = token.strip_prefix("service:") {
            buckets.services.push(rest.trim().to_string());
        } else if let Some(rest) = token.strip_prefix("process:") {
            buckets.processes.push(rest.trim().to_string());
        } else if let Some(rest) = token.strip_prefix("sni:") {
            buckets.hosts.push(rest.trim().to_string());
        } else if let Some(rest) = token.strip_prefix("host:") {
            buckets.hosts.push(rest.trim().to_string());
        } else {
            buckets.hosts.push(token.to_string());
        }
    }
    buckets
}

#[derive(Default)]
pub(crate) struct SignalPolicyBuckets {
    pub(crate) hosts: Vec<String>,
    pub(crate) services: Vec<String>,
    pub(crate) processes: Vec<String>,
}

fn host_or_sni_matches(host: &str, sni: &str, pattern: &str) -> bool {
    text_matches(host, pattern) || (!sni.is_empty() && text_matches(sni, pattern))
}

fn text_matches(value: &str, pattern: &str) -> bool {
    let p = pattern.trim();
    if p.is_empty() {
        return false;
    }
    value.to_ascii_lowercase().contains(&p.to_ascii_lowercase())
}

fn signal_to_proto(signal: &ActiveSignal) -> BehaviorSignalRow {
    BehaviorSignalRow {
        id: signal.id.clone(),
        kind: signal.kind.clone(),
        mode: signal.mode.clone(),
        status: signal.status.clone(),
        severity: signal.severity.clone(),
        subject: signal.subject.clone(),
        summary: signal.summary.clone(),
        detail: signal.detail.clone(),
        first_seen: Some(ProtoTs {
            seconds: signal.first_seen.seconds,
            microseconds: signal.first_seen.microseconds,
        }),
        last_seen: Some(ProtoTs {
            seconds: signal.last_seen.seconds,
            microseconds: signal.last_seen.microseconds,
        }),
        update_count: signal.update_count,
        confidence: signal.confidence,
        pivot_kind: signal.pivot_kind.clone(),
        pivot_value: signal.pivot_value.clone(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use flowarden_core::{
        analysis::{ServiceConfidence, ServiceLabel, TransportProtocol},
        flow::{AggregateTotals, HostCounters, ServiceCounters},
    };
    use std::{
        collections::HashSet,
        net::{IpAddr, Ipv4Addr},
    };

    fn empty_snapshot() -> OverviewRuntimeSnapshot {
        OverviewRuntimeSnapshot {
            capture_id: "live:test".into(),
            mode: ProjectionMode::Live,
            source_label: "Live source · en0".into(),
            filter_label: "Filter · none".into(),
            metric_mode: "bytes".into(),
            capture_status: "running".into(),
            local_ips: HashSet::new(),
            tick_snapshots: Vec::new(),
            offline_gaps: Vec::new(),
            top_connections: Vec::new(),
            top_hosts: Vec::new(),
            top_services: Vec::new(),
            tcp_connections: Vec::new(),
            totals: AggregateTotals {
                packets: 10,
                bytes: 100,
            },
            dropped_packets: 0,
            last_packet_timestamp: None,
            error_message: None,
        }
    }

    #[test]
    fn parses_prefixed_patterns() {
        let buckets = parse_entity_patterns([
            "1.1.1.1".into(),
            "service:https".into(),
            "process:Edge".into(),
            "sni:example.com".into(),
        ]);
        assert_eq!(buckets.hosts, vec!["1.1.1.1", "example.com"]);
        assert_eq!(buckets.services, vec!["https"]);
        assert_eq!(buckets.processes, vec!["Edge"]);
    }

    #[test]
    fn watched_service_signal() {
        let mut engine = SignalEngine::default();
        engine.set_policy(SignalPolicy {
            data_threshold_bytes: 0,
            watched_hosts: Vec::new(),
            known_bad_hosts: Vec::new(),
            watched_services: vec!["https".into()],
            watched_processes: Vec::new(),
            known_bad_services: Vec::new(),
            known_bad_processes: Vec::new(),
            ..SignalPolicy::default()
        });
        let mut snap = empty_snapshot();
        snap.top_services.push(ServiceSummary {
            service: ServiceLabel {
                name: "https".into(),
                transport: TransportProtocol::Tcp,
                confidence: ServiceConfidence::High,
            },
            counters: ServiceCounters {
                packets: 1,
                bytes: 20,
                packets_in: 0,
                packets_out: 1,
                bytes_in: 0,
                bytes_out: 20,
                confidence: ServiceConfidence::High,
            },
        });
        let signals = engine.evaluate_and_list(&snap);
        assert_eq!(signals.len(), 1);
        assert_eq!(signals[0].pivot_kind, "service");
        assert_eq!(signals[0].pivot_value, "https");
    }

    #[test]
    fn watched_host_signal() {
        let mut engine = SignalEngine::default();
        engine.set_policy(SignalPolicy {
            data_threshold_bytes: 0,
            watched_hosts: vec!["93.184.216.34".into()],
            known_bad_hosts: Vec::new(),
            watched_services: Vec::new(),
            watched_processes: Vec::new(),
            known_bad_services: Vec::new(),
            known_bad_processes: Vec::new(),
            ..SignalPolicy::default()
        });
        let mut snap = empty_snapshot();
        snap.top_hosts.push(HostSummary {
            host: IpAddr::V4(Ipv4Addr::new(93, 184, 216, 34)),
            counters: HostCounters {
                packets: 1,
                bytes: 50,
                packets_in: 1,
                packets_out: 0,
                bytes_in: 50,
                bytes_out: 0,
                first_seen: PacketTimestamp::tick(1),
                last_seen: PacketTimestamp::tick(1),
                sni: Some("example.com".into()),
            },
        });
        let signals = engine.evaluate_and_list(&snap);
        assert_eq!(signals.len(), 1);
        assert_eq!(signals[0].kind, "WatchedEntityTransmitted");
        assert_eq!(signals[0].mode, "live");
        assert_eq!(signals[0].status, "active");
    }

    #[test]
    fn offline_finding_is_stable_and_deduped() {
        let mut engine = SignalEngine::default();
        engine.set_policy(SignalPolicy {
            data_threshold_bytes: 10,
            watched_hosts: vec!["93.184.216.34".into()],
            known_bad_hosts: Vec::new(),
            watched_services: Vec::new(),
            watched_processes: Vec::new(),
            known_bad_services: Vec::new(),
            known_bad_processes: Vec::new(),
            ..SignalPolicy::default()
        });
        let mut snap = empty_snapshot();
        snap.mode = ProjectionMode::Offline;
        snap.totals.bytes = 100;
        snap.top_hosts.push(HostSummary {
            host: IpAddr::V4(Ipv4Addr::new(93, 184, 216, 34)),
            counters: HostCounters {
                packets: 2,
                bytes: 100,
                packets_in: 0,
                packets_out: 2,
                bytes_in: 0,
                bytes_out: 100,
                first_seen: PacketTimestamp::tick(1),
                last_seen: PacketTimestamp::tick(2),
                sni: Some("example.com".into()),
            },
        });

        let first = engine.evaluate_and_list(&snap);
        assert_eq!(first.len(), 2);
        assert!(first.iter().all(|s| s.mode == "offline"));
        assert!(first.iter().all(|s| s.status == "finding"));

        // Re-evaluate same offline snapshot: stable findings, no duplicates.
        let second = engine.evaluate_and_list(&snap);
        assert_eq!(second.len(), 2);
        assert!(second.iter().all(|s| s.status == "finding"));
        let host = second
            .iter()
            .find(|s| s.kind == "WatchedEntityTransmitted")
            .expect("host finding");
        assert!(host.update_count >= 2);
    }

    #[test]
    fn live_entity_respects_cooldown() {
        let mut engine = SignalEngine::default();
        engine.set_policy(SignalPolicy {
            data_threshold_bytes: 0,
            watched_hosts: vec!["1.1.1.1".into()],
            known_bad_hosts: Vec::new(),
            watched_services: Vec::new(),
            watched_processes: Vec::new(),
            known_bad_services: Vec::new(),
            known_bad_processes: Vec::new(),
            ..SignalPolicy::default()
        });
        let mut snap = empty_snapshot();
        snap.top_hosts.push(HostSummary {
            host: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
            counters: HostCounters {
                packets: 1,
                bytes: 10,
                packets_in: 0,
                packets_out: 1,
                bytes_in: 0,
                bytes_out: 10,
                first_seen: PacketTimestamp::tick(1),
                last_seen: PacketTimestamp::tick(1),
                sni: None,
            },
        });
        assert_eq!(engine.evaluate_and_list(&snap).len(), 1);
        // Immediate re-eval does not re-push (cooldown); existing stays at update_count 1.
        let again = engine.evaluate_and_list(&snap);
        assert_eq!(again.len(), 1);
        assert_eq!(again[0].update_count, 1);
        assert_eq!(again[0].status, "active");
    }

    #[test]
    fn reset_session_clears_findings() {
        let mut engine = SignalEngine::default();
        engine.set_policy(SignalPolicy {
            data_threshold_bytes: 1,
            watched_hosts: Vec::new(),
            known_bad_hosts: Vec::new(),
            watched_services: Vec::new(),
            watched_processes: Vec::new(),
            known_bad_services: Vec::new(),
            known_bad_processes: Vec::new(),
            ..SignalPolicy::default()
        });
        let mut snap = empty_snapshot();
        snap.totals.bytes = 10;
        assert_eq!(engine.evaluate_and_list(&snap).len(), 1);
        engine.reset_session();
        assert!(engine.list_proto().is_empty());
    }
}
