//! Syslog export for signals and Inspect-sourced flow summaries.
//!
//! Wire format (industry SIEM/firewall convention):
//! - Transport envelope: **RFC 5424** (`<PRI>1 TIMESTAMP HOST APP - - MSG`)
//! - Message body: **CEF** (Common Event Format) `CEF:0|Vendor|Product|Version|…`
//! - Extension keys use ArcSight/CEF dictionary names (`src`, `dst`, `spt`, `dpt`,
//!   `proto`, `in`, `out`, `act`, `app`, `msg`, …) so collectors parse both flow
//!   and signal events the same way.

use std::{
    collections::{HashMap, HashSet},
    io::Write,
    net::{SocketAddr, TcpStream, ToSocketAddrs, UdpSocket},
    sync::{
        Arc, Mutex,
        atomic::{AtomicU64, Ordering},
        mpsc::{self, Receiver, SyncSender, TrySendError},
    },
    thread,
    time::{Duration, Instant, SystemTime, UNIX_EPOCH},
};

use flowarden_core::flow::ConnectionSummary;

const QUEUE_CAP: usize = 512;
const FACILITY_LOCAL0: u8 = 16;
const CEF_VENDOR: &str = "jdtec";
const CEF_PRODUCT: &str = "Flowarden";
const CEF_VERSION: &str = env!("CARGO_PKG_VERSION");

#[derive(Clone, Debug, PartialEq, Eq)]
pub(crate) enum SyslogProto {
    Udp,
    Tcp,
}

impl SyslogProto {
    pub(crate) fn parse(s: &str) -> Self {
        match s.trim().to_ascii_lowercase().as_str() {
            "tcp" => Self::Tcp,
            _ => Self::Udp,
        }
    }

    pub(crate) fn as_str(&self) -> &'static str {
        match self {
            Self::Udp => "udp",
            Self::Tcp => "tcp",
        }
    }
}

#[derive(Clone, Debug)]
pub(crate) struct SyslogConfig {
    pub(crate) enabled: bool,
    pub(crate) target: Option<SocketAddr>,
    pub(crate) proto: SyslogProto,
    pub(crate) facility: u8,
    pub(crate) tag: String,
    pub(crate) emit_signals: bool,
    pub(crate) emit_flows: bool,
    pub(crate) flow_min_bytes: u64,
    pub(crate) flow_delta_bytes: u64,
    pub(crate) flow_interval_secs: u64,
}

impl Default for SyslogConfig {
    fn default() -> Self {
        Self {
            enabled: false,
            target: None,
            proto: SyslogProto::Udp,
            facility: FACILITY_LOCAL0,
            tag: "flowarden".into(),
            emit_signals: true,
            emit_flows: true,
            flow_min_bytes: 10_000,
            flow_delta_bytes: 1_000_000,
            flow_interval_secs: 60,
        }
    }
}

impl SyslogConfig {
    pub(crate) fn from_target_str(
        target: Option<&str>,
        proto: &str,
        emit_signals: bool,
        emit_flows: bool,
    ) -> Self {
        let (addr, enabled) = match target.map(str::trim).filter(|s| !s.is_empty()) {
            Some(raw) => match parse_syslog_target(raw) {
                Ok(a) => (Some(a), true),
                Err(_) => (None, false),
            },
            None => (None, false),
        };
        Self {
            enabled,
            target: addr,
            proto: SyslogProto::parse(proto),
            emit_signals,
            emit_flows,
            ..Self::default()
        }
    }

    pub(crate) fn from_env() -> Self {
        let target = std::env::var("FLOWARDEN_SYSLOG_TARGET").ok();
        let proto = std::env::var("FLOWARDEN_SYSLOG_PROTO").unwrap_or_else(|_| "udp".into());
        let emit_signals = env_flag("FLOWARDEN_SYSLOG_EMIT_SIGNALS", true);
        let emit_flows = env_flag("FLOWARDEN_SYSLOG_EMIT_FLOWS", true);
        Self::from_target_str(target.as_deref(), &proto, emit_signals, emit_flows)
    }
}

/// Parse `IP:port` or resolve `hostname:port` to a concrete socket address.
pub(crate) fn parse_syslog_target(raw: &str) -> Result<SocketAddr, String> {
    let raw = raw.trim();
    if raw.is_empty() {
        return Err("empty syslog target".into());
    }
    if let Ok(addr) = raw.parse::<SocketAddr>() {
        return Ok(addr);
    }
    // Hostname form requires an explicit port (ToSocketAddrs).
    if !raw.rsplit_once(':').is_some_and(|(_, p)| !p.is_empty() && p.chars().all(|c| c.is_ascii_digit()))
    {
        return Err("syslog target must be HOST:PORT".into());
    }
    raw.to_socket_addrs()
        .map_err(|e| format!("resolve syslog target: {e}"))?
        .next()
        .ok_or_else(|| format!("could not resolve syslog target `{raw}`"))
}

fn env_flag(name: &str, default: bool) -> bool {
    match std::env::var(name) {
        Ok(v) => matches!(
            v.trim().to_ascii_lowercase().as_str(),
            "1" | "true" | "yes" | "on"
        ),
        Err(_) => default,
    }
}

#[derive(Clone, Debug)]
pub(crate) struct SignalSyslogPayload {
    pub id: String,
    pub kind: String,
    pub severity: String,
    pub mode: String,
    pub status: String,
    pub subject: String,
    pub summary: String,
    pub confidence: f64,
    pub pivot_kind: String,
    pub pivot_value: String,
}

#[derive(Clone, Debug)]
pub(crate) struct FlowSyslogPayload {
    pub event: String,
    pub src: String,
    pub sport: u16,
    pub dst: String,
    pub dport: u16,
    pub proto: String,
    pub service: String,
    pub sni: String,
    pub process: String,
    pub bytes_in: u64,
    pub bytes_out: u64,
    pub packets: u64,
    pub direction: String,
}

#[derive(Clone, Debug)]
#[allow(clippy::large_enum_variant)]
pub(crate) enum SyslogEvent {
    Signal(SignalSyslogPayload),
    Flow(FlowSyslogPayload),
}

pub(crate) struct SyslogExporter {
    config: Arc<Mutex<SyslogConfig>>,
    tx: Option<SyncSender<SyslogEvent>>,
    dropped: Arc<AtomicU64>,
    last_error: Arc<Mutex<String>>,
    flow_state: Mutex<HashMap<String, FlowEmitState>>,
    /// Signal ids already exported (overview re-lists the full log every tick).
    emitted_signal_ids: Mutex<HashSet<String>>,
}

#[derive(Clone, Debug)]
struct FlowEmitState {
    last_bytes: u64,
    last_sent: Instant,
}

impl SyslogExporter {
    pub(crate) fn disabled() -> Self {
        Self {
            config: Arc::new(Mutex::new(SyslogConfig::default())),
            tx: None,
            dropped: Arc::new(AtomicU64::new(0)),
            last_error: Arc::new(Mutex::new(String::new())),
            flow_state: Mutex::new(HashMap::new()),
            emitted_signal_ids: Mutex::new(HashSet::new()),
        }
    }

    pub(crate) fn start(config: SyslogConfig) -> Self {
        let exporter = Self::disabled();
        exporter.reconfigure(config);
        exporter
    }

    pub(crate) fn reconfigure(&self, config: SyslogConfig) {
        if let Ok(mut guard) = self.config.lock() {
            *guard = config.clone();
        }
        // Worker is started lazily on first submit / ensure_worker from SetSyslogConfig.
    }

    pub(crate) fn ensure_worker(&mut self) {
        if self.tx.is_some() {
            return;
        }
        let (tx, rx) = mpsc::sync_channel(QUEUE_CAP);
        let config = Arc::clone(&self.config);
        let last_error = Arc::clone(&self.last_error);
        thread::Builder::new()
            .name("flowarden-syslog".into())
            .spawn(move || worker_loop(rx, config, last_error))
            .ok();
        self.tx = Some(tx);
    }

    pub(crate) fn dropped(&self) -> u64 {
        self.dropped.load(Ordering::Relaxed)
    }

    pub(crate) fn last_error(&self) -> String {
        self.last_error
            .lock()
            .map(|g| g.clone())
            .unwrap_or_default()
    }

    pub(crate) fn snapshot_config(&self) -> SyslogConfig {
        self.config
            .lock()
            .map(|g| g.clone())
            .unwrap_or_default()
    }

    pub(crate) fn submit_signal(&mut self, payload: SignalSyslogPayload) {
        let cfg = self.snapshot_config();
        if !cfg.enabled || !cfg.emit_signals || cfg.target.is_none() {
            return;
        }
        let key = if payload.id.is_empty() {
            format!("{}|{}|{}", payload.kind, payload.subject, payload.summary)
        } else {
            payload.id.clone()
        };
        {
            let mut seen = self
                .emitted_signal_ids
                .lock()
                .unwrap_or_else(|e| e.into_inner());
            if seen.contains(&key) {
                return;
            }
            // Bound memory for long-running cores.
            if seen.len() >= 2_048 {
                seen.clear();
            }
            seen.insert(key);
        }
        self.ensure_worker();
        self.try_send(SyslogEvent::Signal(payload));
    }

    /// Inspect-sourced flow rows: apply min/delta/interval gates then enqueue.
    pub(crate) fn consider_inspect_flows(
        &mut self,
        flows: &[ConnectionSummary],
        processes: &HashMap<String, String>,
    ) {
        let cfg = self.snapshot_config();
        if !cfg.enabled || !cfg.emit_flows || cfg.target.is_none() {
            return;
        }
        self.ensure_worker();
        let interval = Duration::from_secs(cfg.flow_interval_secs.max(1));
        let now = Instant::now();
        let mut state = self.flow_state.lock().unwrap_or_else(|e| e.into_inner());
        let mut present = std::collections::HashSet::new();

        for conn in flows {
            let key = flow_key_string(conn);
            present.insert(key.clone());
            let total = conn.counters.bytes;
            if total < cfg.flow_min_bytes {
                continue;
            }
            let should = match state.get(&key) {
                None => true,
                Some(prev) => {
                    let grew = total.saturating_sub(prev.last_bytes) >= cfg.flow_delta_bytes;
                    let due = now.duration_since(prev.last_sent) >= interval;
                    grew || due
                }
            };
            if !should {
                continue;
            }
            let process = processes.get(&key).cloned().unwrap_or_default();
            let sport = conn.key.source_port.unwrap_or(0);
            let dport = conn.key.destination_port.unwrap_or(0);
            self.try_send(SyslogEvent::Flow(FlowSyslogPayload {
                event: "active".into(),
                src: conn.key.source_ip.to_string(),
                sport,
                dst: conn.key.destination_ip.to_string(),
                dport,
                proto: format!("{:?}", conn.key.protocol).to_ascii_lowercase(),
                service: String::new(),
                sni: conn.counters.sni.clone().unwrap_or_default(),
                process,
                bytes_in: conn.counters.bytes_in,
                bytes_out: conn.counters.bytes_out,
                packets: conn.counters.packets,
                direction: "session".into(),
            }));
            state.insert(
                key,
                FlowEmitState {
                    last_bytes: total,
                    last_sent: now,
                },
            );
        }

        // Closed: left the Inspect-visible set after having been emitted.
        let stale: Vec<String> = state
            .keys()
            .filter(|k| !present.contains(*k))
            .cloned()
            .collect();
        for key in stale {
            if let Some(prev) = state.remove(&key) {
                // Best-effort close line from key parse is skipped if key opaque;
                // emit minimal closed event.
                let _ = prev;
                self.try_send(SyslogEvent::Flow(FlowSyslogPayload {
                    event: "closed".into(),
                    src: key.clone(),
                    sport: 0,
                    dst: String::new(),
                    dport: 0,
                    proto: String::new(),
                    service: String::new(),
                    sni: String::new(),
                    process: String::new(),
                    bytes_in: 0,
                    bytes_out: 0,
                    packets: 0,
                    direction: String::new(),
                }));
            }
        }
    }

    fn try_send(&self, event: SyslogEvent) {
        let Some(tx) = &self.tx else {
            return;
        };
        match tx.try_send(event) {
            Ok(()) => {}
            Err(TrySendError::Full(_)) | Err(TrySendError::Disconnected(_)) => {
                self.dropped.fetch_add(1, Ordering::Relaxed);
            }
        }
    }
}

fn flow_key_string(conn: &ConnectionSummary) -> String {
    format!(
        "{}:{}-{}:{}-{:?}",
        conn.key.source_ip,
        conn.key.source_port.unwrap_or(0),
        conn.key.destination_ip,
        conn.key.destination_port.unwrap_or(0),
        conn.key.protocol
    )
}

fn worker_loop(
    rx: Receiver<SyslogEvent>,
    config: Arc<Mutex<SyslogConfig>>,
    last_error: Arc<Mutex<String>>,
) {
    while let Ok(event) = rx.recv() {
        let cfg = config.lock().map(|g| g.clone()).unwrap_or_default();
        if !cfg.enabled {
            continue;
        }
        let Some(addr) = cfg.target else {
            continue;
        };
        let line = format_syslog_line(&cfg, &event);
        if let Err(err) = send_line(&cfg, addr, &line)
            && let Ok(mut g) = last_error.lock()
        {
            *g = err;
        }
    }
}

/// RFC5424 envelope + CEF body (same shape for flow and signal).
fn format_syslog_line(cfg: &SyslogConfig, event: &SyslogEvent) -> String {
    let (severity_pri, cef) = match event {
        SyslogEvent::Signal(s) => (cef_pri_from_severity(&s.severity), format_signal_cef(s)),
        SyslogEvent::Flow(f) => (6u8, format_flow_cef(f)), // informational
    };
    let pri = i32::from(cfg.facility) * 8 + i32::from(severity_pri);
    let ts = rfc3339_now();
    let host = hostname_fallback();
    let app = if cfg.tag.trim().is_empty() {
        "flowarden"
    } else {
        cfg.tag.trim()
    };
    // PROCID MSGID STRUCT-DATA empty ("-"); MSG is the CEF payload.
    format!("<{pri}>1 {ts} {host} {app} - - {cef}")
}

fn format_signal_cef(s: &SignalSyslogPayload) -> String {
    let signature = if s.kind.is_empty() {
        "signal"
    } else {
        s.kind.as_str()
    };
    let name = if s.summary.is_empty() {
        signature
    } else {
        // CEF Name is the first segment before " — " detail separator if present.
        s.summary.split(" — ").next().unwrap_or(s.summary.as_str())
    };
    let severity = cef_severity_0_10(&s.severity);
    let mut ext = Vec::new();
    push_kv(&mut ext, "rt", &unix_millis_now().to_string());
    push_kv(&mut ext, "cat", "signal");
    if !s.id.is_empty() {
        push_kv(&mut ext, "externalId", &s.id);
    }
    push_kv(&mut ext, "msg", &s.summary);
    push_kv(&mut ext, "cs1", &s.subject);
    push_kv(&mut ext, "cs1Label", "Subject");
    push_kv(&mut ext, "cs2", &s.mode);
    push_kv(&mut ext, "cs2Label", "Mode");
    push_kv(&mut ext, "cs3", &s.status);
    push_kv(&mut ext, "cs3Label", "Status");
    push_kv(&mut ext, "cs4", &s.kind);
    push_kv(&mut ext, "cs4Label", "SignalKind");
    if !s.pivot_kind.is_empty() && s.pivot_kind != "none" {
        push_kv(&mut ext, "cs5", &s.pivot_kind);
        push_kv(&mut ext, "cs5Label", "PivotKind");
        if !s.pivot_value.is_empty() {
            push_kv(&mut ext, "cs6", &s.pivot_value);
            push_kv(&mut ext, "cs6Label", "PivotValue");
            // Map common pivots onto dictionary keys for SIEM correlation.
            match s.pivot_kind.as_str() {
                "host" => push_kv(&mut ext, "dst", &s.pivot_value),
                "sni" => push_kv(&mut ext, "request", &s.pivot_value),
                "process" => {
                    push_kv(&mut ext, "sproc", &s.pivot_value);
                    push_kv(&mut ext, "sourceProcessName", &s.pivot_value);
                }
                "service" => push_kv(&mut ext, "app", &s.pivot_value),
                _ => {}
            }
        }
    }
    // CEF floating-point custom: cfp1
    push_kv(&mut ext, "cfp1", &format!("{:.2}", s.confidence));
    push_kv(&mut ext, "cfp1Label", "Confidence");
    push_kv(&mut ext, "outcome", &s.status);
    format_cef_header(signature, name, severity, &ext)
}

fn format_flow_cef(f: &FlowSyslogPayload) -> String {
    let event = if f.event.is_empty() {
        "active"
    } else {
        f.event.as_str()
    };
    // Signature / Name aligned with traffic log conventions (start / update / end).
    let (signature, name, act) = match event {
        "closed" | "end" | "finish" => ("flow:end", "Network flow end", "end"),
        "start" | "new" => ("flow:start", "Network flow start", "start"),
        _ => ("flow:update", "Network flow update", "update"),
    };
    let mut ext = Vec::new();
    push_kv(&mut ext, "rt", &unix_millis_now().to_string());
    push_kv(&mut ext, "cat", "traffic");
    if !f.src.is_empty() {
        push_kv(&mut ext, "src", &f.src);
    }
    if !f.dst.is_empty() {
        push_kv(&mut ext, "dst", &f.dst);
    }
    if f.sport > 0 {
        push_kv(&mut ext, "spt", &f.sport.to_string());
    }
    if f.dport > 0 {
        push_kv(&mut ext, "dpt", &f.dport.to_string());
    }
    if !f.proto.is_empty() {
        push_kv(&mut ext, "proto", &normalize_proto(&f.proto));
    }
    // CEF dictionary: in = bytes into the device from source, out = bytes out to dest.
    // Endpoint monitor maps: bytes_in → in, bytes_out → out.
    push_kv(&mut ext, "in", &f.bytes_in.to_string());
    push_kv(&mut ext, "out", &f.bytes_out.to_string());
    push_kv(&mut ext, "cn1", &f.packets.to_string());
    push_kv(&mut ext, "cn1Label", "Packets");
    push_kv(&mut ext, "act", act);
    if !f.service.is_empty() {
        push_kv(&mut ext, "app", &f.service);
    }
    if !f.sni.is_empty() {
        // request = URI/host context in many traffic CEF mappings (SNI here).
        push_kv(&mut ext, "request", &f.sni);
        push_kv(&mut ext, "destinationDnsDomain", &f.sni);
    }
    if !f.process.is_empty() {
        push_kv(&mut ext, "sproc", &f.process);
        push_kv(&mut ext, "sourceProcessName", &f.process);
        push_kv(&mut ext, "cs1", &f.process);
        push_kv(&mut ext, "cs1Label", "Process");
    }
    if !f.direction.is_empty() {
        push_kv(&mut ext, "cs2", &f.direction);
        push_kv(&mut ext, "cs2Label", "Direction");
        // deviceDirection: 0 inbound, 1 outbound (when known).
        match f.direction.to_ascii_lowercase().as_str() {
            "in" | "inbound" | "download" => push_kv(&mut ext, "deviceDirection", "0"),
            "out" | "outbound" | "upload" => push_kv(&mut ext, "deviceDirection", "1"),
            _ => {}
        }
    }
    push_kv(
        &mut ext,
        "msg",
        &format!(
            "flow {} {}:{} -> {}:{} out={} in={} pkts={}",
            event, f.src, f.sport, f.dst, f.dport, f.bytes_out, f.bytes_in, f.packets
        ),
    );
    format_cef_header(signature, name, 0, &ext)
}

fn format_cef_header(signature_id: &str, name: &str, severity: u8, extensions: &[String]) -> String {
    let ext = extensions.join(" ");
    format!(
        "CEF:0|{}|{}|{}|{}|{}|{}|{}",
        escape_cef_header(CEF_VENDOR),
        escape_cef_header(CEF_PRODUCT),
        escape_cef_header(CEF_VERSION),
        escape_cef_header(signature_id),
        escape_cef_header(name),
        severity.min(10),
        ext
    )
}

fn push_kv(out: &mut Vec<String>, key: &str, value: &str) {
    if value.is_empty() {
        return;
    }
    out.push(format!("{key}={}", escape_cef_extension(value)));
}

/// CEF header field escaping: `\`, `|`, and newlines.
fn escape_cef_header(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('|', "\\|")
        .replace(['\n', '\r'], " ")
}

/// CEF extension value escaping: `\`, `=`, and newlines.
fn escape_cef_extension(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('=', "\\=")
        .replace('\n', "\\n")
        .replace('\r', "")
}

fn normalize_proto(proto: &str) -> String {
    match proto.trim().to_ascii_lowercase().as_str() {
        "tcp" | "6" => "tcp".into(),
        "udp" | "17" => "udp".into(),
        "icmp" | "1" => "icmp".into(),
        other => other.to_string(),
    }
}

/// Map Flowarden severities onto CEF 0–10 scale.
fn cef_severity_0_10(severity: &str) -> u8 {
    match severity.trim().to_ascii_lowercase().as_str() {
        "info" | "low" | "informational" => 3,
        "medium" | "med" => 5,
        "warning" | "warn" => 6,
        "high" | "error" | "err" => 8,
        "critical" | "crit" | "fatal" => 10,
        _ => 5,
    }
}

/// Syslog PRI severity nibble (0–7) from CEF-ish labels.
fn cef_pri_from_severity(severity: &str) -> u8 {
    match severity.trim().to_ascii_lowercase().as_str() {
        "info" | "low" | "informational" => 6, // informational
        "medium" | "med" | "warning" | "warn" => 4, // warning
        "high" | "error" | "err" => 3,              // error
        "critical" | "crit" | "fatal" => 2,         // critical
        _ => 5,                                     // notice
    }
}

fn unix_millis_now() -> u128 {
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_millis())
        .unwrap_or(0)
}

/// RFC3339 UTC timestamp for the RFC5424 TIMESTAMP field.
fn rfc3339_now() -> String {
    let dur = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    let secs = dur.as_secs();
    let millis = dur.subsec_millis();
    let (y, mo, d, h, mi, s) = civil_utc_from_unix(secs);
    format!("{y:04}-{mo:02}-{d:02}T{h:02}:{mi:02}:{s:02}.{millis:03}Z")
}

/// Convert unix seconds to UTC civil components (no chrono dependency).
fn civil_utc_from_unix(secs: u64) -> (i32, u32, u32, u32, u32, u32) {
    let s = (secs % 60) as u32;
    let mins = secs / 60;
    let mi = (mins % 60) as u32;
    let hours = mins / 60;
    let h = (hours % 24) as u32;
    let days = hours / 24;

    // Civil date from days since 1970-01-01 (Howard Hinnant algorithm).
    let z = days as i64 + 719_468;
    let era = if z >= 0 { z } else { z - 146_096 } / 146_097;
    let doe = (z - era * 146_097) as u64;
    let yoe = (doe - doe / 1460 + doe / 36524 - doe / 146_096) / 365;
    let y = (yoe as i64) + era * 400;
    let doy = doe - (365 * yoe + yoe / 4 - yoe / 100);
    let mp = (5 * doy + 2) / 153;
    let d = (doy - (153 * mp + 2) / 5 + 1) as u32;
    let mo = (if mp < 10 { mp + 3 } else { mp - 9 }) as u32;
    let y = (y + if mo <= 2 { 1 } else { 0 }) as i32;
    (y, mo, d, h, mi, s)
}

fn hostname_fallback() -> String {
    std::env::var("HOSTNAME")
        .or_else(|_| std::env::var("COMPUTERNAME"))
        .unwrap_or_else(|_| "flowarden-host".into())
}

fn send_line(cfg: &SyslogConfig, addr: SocketAddr, line: &str) -> Result<(), String> {
    match cfg.proto {
        SyslogProto::Udp => {
            let sock = UdpSocket::bind("0.0.0.0:0").map_err(|e| e.to_string())?;
            sock.set_write_timeout(Some(Duration::from_millis(500)))
                .ok();
            sock.send_to(line.as_bytes(), addr)
                .map_err(|e| e.to_string())?;
            Ok(())
        }
        SyslogProto::Tcp => {
            let mut stream =
                TcpStream::connect_timeout(&addr, Duration::from_millis(800)).map_err(|e| e.to_string())?;
            stream
                .set_write_timeout(Some(Duration::from_millis(800)))
                .ok();
            stream
                .write_all(line.as_bytes())
                .and_then(|_| stream.write_all(b"\n"))
                .map_err(|e| e.to_string())?;
            Ok(())
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn formats_signal_as_rfc5424_cef() {
        let cfg = SyslogConfig {
            enabled: true,
            target: Some("127.0.0.1:514".parse().unwrap()),
            ..SyslogConfig::default()
        };
        let line = format_syslog_line(
            &cfg,
            &SyslogEvent::Signal(SignalSyslogPayload {
                id: "threshold-1".into(),
                kind: "DataThresholdExceeded".into(),
                severity: "warning".into(),
                mode: "live".into(),
                status: "active".into(),
                subject: "en0".into(),
                summary: "Data threshold exceeded".into(),
                confidence: 0.9,
                pivot_kind: "none".into(),
                pivot_value: String::new(),
            }),
        );
        assert!(line.starts_with('<'), "PRI prefix: {line}");
        assert!(line.contains("CEF:0|jdtec|Flowarden|"), "CEF header: {line}");
        assert!(line.contains("DataThresholdExceeded"), "signature: {line}");
        assert!(line.contains("cat=signal"), "cat: {line}");
        assert!(line.contains("cs1=en0"), "subject: {line}");
        assert!(line.contains("externalId=threshold-1"), "id: {line}");
        assert!(!line.contains("flowarden@0"), "old SD-ELEMENT must be gone: {line}");
    }

    #[test]
    fn formats_flow_as_rfc5424_cef_with_dictionary_keys() {
        let cfg = SyslogConfig::default();
        let line = format_syslog_line(
            &cfg,
            &SyslogEvent::Flow(FlowSyslogPayload {
                event: "active".into(),
                src: "10.0.0.2".into(),
                sport: 54321,
                dst: "1.2.3.4".into(),
                dport: 443,
                proto: "Tcp".into(),
                service: "https".into(),
                sni: "example.com".into(),
                process: "Chrome".into(),
                bytes_in: 100,
                bytes_out: 200,
                packets: 9,
                direction: "session".into(),
            }),
        );
        assert!(line.contains("CEF:0|jdtec|Flowarden|"), "CEF: {line}");
        assert!(line.contains("flow:update") || line.contains("Network flow"), "{line}");
        assert!(line.contains("src=10.0.0.2"), "{line}");
        assert!(line.contains("dst=1.2.3.4"), "{line}");
        assert!(line.contains("spt=54321"), "{line}");
        assert!(line.contains("dpt=443"), "{line}");
        assert!(line.contains("proto=tcp"), "{line}");
        assert!(line.contains("in=100"), "{line}");
        assert!(line.contains("out=200"), "{line}");
        assert!(line.contains("app=https"), "{line}");
        assert!(line.contains("request=example.com"), "{line}");
        assert!(line.contains("sourceProcessName=Chrome") || line.contains("sproc=Chrome"), "{line}");
        assert!(line.contains("cat=traffic"), "{line}");
        assert!(line.contains("act=update"), "{line}");
    }

    #[test]
    fn parse_target_accepts_ip_and_port() {
        let addr = parse_syslog_target("127.0.0.1:514").unwrap();
        assert_eq!(addr.port(), 514);
        assert!(addr.ip().is_loopback());
    }

    #[test]
    fn disabled_config_drops_signals() {
        let mut exporter = SyslogExporter::disabled();
        exporter.submit_signal(SignalSyslogPayload {
            id: "t1".into(),
            kind: "DataThresholdExceeded".into(),
            severity: "warning".into(),
            mode: "live".into(),
            status: "active".into(),
            subject: "en0".into(),
            summary: "Data threshold exceeded".into(),
            confidence: 0.9,
            pivot_kind: "none".into(),
            pivot_value: String::new(),
        });
        assert_eq!(exporter.dropped(), 0);
        assert!(exporter.snapshot_config().target.is_none());
    }

    #[test]
    fn udp_signal_is_received_by_local_listener() {
        let sock = UdpSocket::bind("127.0.0.1:0").expect("bind listener");
        sock.set_read_timeout(Some(Duration::from_secs(3)))
            .expect("timeout");
        let addr = sock.local_addr().expect("local addr");

        let mut exporter = SyslogExporter::start(SyslogConfig {
            enabled: true,
            target: Some(addr),
            emit_signals: true,
            ..SyslogConfig::default()
        });
        exporter.submit_signal(SignalSyslogPayload {
            id: "threshold-e2e".into(),
            kind: "DataThresholdExceeded".into(),
            severity: "warning".into(),
            mode: "live".into(),
            status: "active".into(),
            subject: "test-iface".into(),
            summary: "Data threshold exceeded".into(),
            confidence: 0.9,
            pivot_kind: "none".into(),
            pivot_value: String::new(),
        });

        let mut buf = [0u8; 4096];
        let (n, _) = sock.recv_from(&mut buf).expect("expected syslog UDP datagram");
        let line = std::str::from_utf8(&buf[..n]).expect("utf8");
        assert!(
            line.contains("DataThresholdExceeded"),
            "line was: {line}"
        );
        assert!(line.contains("CEF:0|"), "line was: {line}");
        assert!(line.contains("cat=signal"), "line was: {line}");
        assert_eq!(exporter.last_error(), "");
    }
}
