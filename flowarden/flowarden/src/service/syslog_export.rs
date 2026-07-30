//! RFC5424 syslog export for signals and Inspect-sourced flow summaries.

use std::{
    collections::HashMap,
    io::Write,
    net::{SocketAddr, TcpStream, UdpSocket},
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
            Some(raw) => match raw.parse::<SocketAddr>() {
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
        // Restart worker only when enabling with a target.
        // For simplicity: always spawn a new worker if none; old channels drain.
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
        let line = format_rfc5424(&cfg, &event);
        if let Err(err) = send_line(&cfg, addr, &line)
            && let Ok(mut g) = last_error.lock()
        {
            *g = err;
        }
    }
}

fn format_rfc5424(cfg: &SyslogConfig, event: &SyslogEvent) -> String {
    let pri = i32::from(cfg.facility) * 8 + 6; // informational
    let ts = rfc3339_now();
    let host = hostname_fallback();
    match event {
        SyslogEvent::Signal(s) => {
            format!(
                "<{pri}>1 {ts} {host} {} - signal [flowarden@0 kind=\"{}\" severity=\"{}\" mode=\"{}\" status=\"{}\" subject=\"{}\" confidence=\"{:.2}\" pivot_kind=\"{}\" pivot_value=\"{}\"] {}",
                escape_sd(&cfg.tag),
                escape_sd(&s.kind),
                escape_sd(&s.severity),
                escape_sd(&s.mode),
                escape_sd(&s.status),
                escape_sd(&s.subject),
                s.confidence,
                escape_sd(&s.pivot_kind),
                escape_sd(&s.pivot_value),
                s.summary.replace('\n', " ")
            )
        }
        SyslogEvent::Flow(f) => {
            format!(
                "<{pri}>1 {ts} {host} {} - flow [flowarden@0 event=\"{}\" src=\"{}\" sport=\"{}\" dst=\"{}\" dport=\"{}\" proto=\"{}\" service=\"{}\" sni=\"{}\" process=\"{}\" bytes_in=\"{}\" bytes_out=\"{}\" packets=\"{}\" direction=\"{}\"] flow {}:{} -> {}:{} out={} in={}",
                escape_sd(&cfg.tag),
                escape_sd(&f.event),
                escape_sd(&f.src),
                f.sport,
                escape_sd(&f.dst),
                f.dport,
                escape_sd(&f.proto),
                escape_sd(&f.service),
                escape_sd(&f.sni),
                escape_sd(&f.process),
                f.bytes_in,
                f.bytes_out,
                f.packets,
                escape_sd(&f.direction),
                f.src,
                f.sport,
                f.dst,
                f.dport,
                f.bytes_out,
                f.bytes_in
            )
        }
    }
}

fn escape_sd(value: &str) -> String {
    value
        .replace('\\', "\\\\")
        .replace('"', "\\\"")
        .replace(']', "\\]")
}

fn rfc3339_now() -> String {
    let ok = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default();
    // Simple UTC timestamp without chrono dependency.
    format!("{}.{:03}Z", ok.as_secs(), ok.subsec_millis())
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
    fn formats_signal_line() {
        let cfg = SyslogConfig {
            enabled: true,
            target: Some("127.0.0.1:514".parse().unwrap()),
            ..SyslogConfig::default()
        };
        let line = format_rfc5424(
            &cfg,
            &SyslogEvent::Signal(SignalSyslogPayload {
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
        assert!(line.contains("signal"));
        assert!(line.contains("DataThresholdExceeded"));
        assert!(line.contains("flowarden@0"));
    }
}
