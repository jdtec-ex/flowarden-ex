use std::{fs, net::IpAddr, path::Path};

use flowarden_core::flow::{
    ConnectionSummary, FinalSnapshot, HostSummary, OfflineGap, PacketTimestamp, ServiceSummary,
    TickSnapshot,
};
use flowarden_error::{ErrorType, OrErr, Result};
use serde::Serialize;

use crate::cli::OutputFormat;
use crate::geo::{CountryInfo, CountryKind, GeoCountryResolver};
use crate::service::{CliFinding, evaluate_cli_findings};

#[derive(Debug, Serialize)]
pub struct CaptureOutput<'a> {
    pub tick_snapshots: &'a [TickSnapshot],
    pub offline_gaps: &'a [OfflineGap],
    pub final_snapshot: &'a FinalSnapshot,
    pub top_hosts_enriched: Vec<EnrichedHostSummary>,
    pub top_connections_enriched: Vec<EnrichedConnectionSummary>,
    /// Behavior findings from the same detector as resident core (CLI/UI contract).
    pub findings: Vec<CliFinding>,
}

/// Optional signal policy for CLI batch evaluation.
#[derive(Debug, Clone)]
pub struct CliSignalOptions {
    pub is_offline: bool,
    pub data_threshold_bytes: u64,
    pub watched: Vec<String>,
    pub known_bad: Vec<String>,
}

impl Default for CliSignalOptions {
    fn default() -> Self {
        Self {
            is_offline: true,
            data_threshold_bytes: 50_000_000,
            watched: Vec::new(),
            known_bad: Vec::new(),
        }
    }
}

#[derive(Debug, Serialize)]
pub struct EnrichedHostSummary {
    pub host: String,
    pub country_code: String,
    pub country_label: String,
    pub country_kind: &'static str,
    pub asn_number: u32,
    pub asn_organization: String,
    pub asn_label: String,
    /// TLS SNI when observed for this host (CLI/UI contract field).
    pub sni: Option<String>,
    pub packets: u64,
    pub bytes: u64,
    pub bytes_in: u64,
    pub bytes_out: u64,
}

#[derive(Debug, Serialize)]
pub struct EnrichedConnectionSummary {
    pub source: EnrichedEndpoint,
    pub source_port: Option<u16>,
    pub destination: EnrichedEndpoint,
    pub destination_port: Option<u16>,
    pub protocol: String,
    /// TLS ClientHello SNI when observed on this flow (CLI/UI contract field).
    pub sni: Option<String>,
    pub packets: u64,
    pub bytes: u64,
}

#[derive(Debug, Serialize)]
pub struct EnrichedEndpoint {
    pub address: String,
    pub country_code: String,
    pub country_label: String,
    pub country_kind: &'static str,
    pub asn_number: u32,
    pub asn_organization: String,
    pub asn_label: String,
}

pub fn render_capture_output_with_signals(
    format: OutputFormat,
    tick_snapshots: &[TickSnapshot],
    offline_gaps: &[OfflineGap],
    final_snapshot: &FinalSnapshot,
    top_n: usize,
    signal_options: &CliSignalOptions,
) -> Result<String> {
    let mut geo = geo_resolver()?;
    match format {
        OutputFormat::Json => render_capture_json(
            tick_snapshots,
            offline_gaps,
            final_snapshot,
            top_n,
            signal_options,
            &mut geo,
        ),
        OutputFormat::Table => Ok(render_capture_table(
            tick_snapshots,
            offline_gaps,
            final_snapshot,
            top_n,
            signal_options,
            &mut geo,
        )),
    }
}

pub fn emit_output(content: &str, output_path: Option<&Path>) -> Result<()> {
    match output_path {
        Some(path) => fs::write(path, content)
            .or_err_with(ErrorType::FileWriteError, || {
                format!("Failed to write capture output to `{}`", path.display())
            })
            .map_err(|e| e.into_cli()),
        None => {
            println!("{content}");
            Ok(())
        }
    }
}

fn render_capture_json(
    tick_snapshots: &[TickSnapshot],
    offline_gaps: &[OfflineGap],
    final_snapshot: &FinalSnapshot,
    top_n: usize,
    signal_options: &CliSignalOptions,
    geo: &mut GeoCountryResolver,
) -> Result<String> {
    let final_snapshot = top_limited_final_snapshot(final_snapshot, top_n);
    let top_hosts_enriched =
        enriched_hosts(&final_snapshot.aggregate_summary.top_hosts, top_n, geo);
    let top_connections_enriched = enriched_connections(
        &final_snapshot.aggregate_summary.top_connections,
        top_n,
        geo,
    );
    let findings = evaluate_cli_findings(
        &final_snapshot,
        signal_options.is_offline,
        top_n,
        signal_options.data_threshold_bytes,
        signal_options.watched.clone(),
        signal_options.known_bad.clone(),
    );

    serde_json::to_string_pretty(&CaptureOutput {
        tick_snapshots,
        offline_gaps,
        final_snapshot: &final_snapshot,
        top_hosts_enriched,
        top_connections_enriched,
        findings,
    })
    .or_err(
        ErrorType::InternalError,
        "Failed to serialize capture output to JSON",
    )
    .map_err(|e| e.into_cli())
}

fn render_capture_table(
    tick_snapshots: &[TickSnapshot],
    offline_gaps: &[OfflineGap],
    final_snapshot: &FinalSnapshot,
    top_n: usize,
    signal_options: &CliSignalOptions,
    geo: &mut GeoCountryResolver,
) -> String {
    let mut lines = Vec::new();

    lines.push(format!("capture_id: {}", final_snapshot.capture_id));
    lines.push(format!(
        "started_at: {}",
        format_timestamp(final_snapshot.started_at)
    ));
    lines.push(format!(
        "ended_at: {}",
        format_timestamp(final_snapshot.ended_at)
    ));
    lines.push(format!(
        "totals: packets={}, bytes={}",
        final_snapshot.totals.packets, final_snapshot.totals.bytes
    ));
    lines.push(format!(
        "dropped_packets: {}",
        final_snapshot.dropped_packets
    ));
    lines.push(format!(
        "last_packet_timestamp: {}",
        final_snapshot
            .last_packet_timestamp
            .map(format_timestamp)
            .unwrap_or_else(|| "-".to_string())
    ));
    lines.push(format!("tick_snapshots: {}", tick_snapshots.len()));
    lines.push(format!("offline_gaps: {}", offline_gaps.len()));
    lines.push(String::new());

    lines.push("top connections:".to_string());
    append_connections(
        &mut lines,
        &final_snapshot.aggregate_summary.top_connections,
        top_n,
        geo,
    );
    lines.push(String::new());

    lines.push("top hosts:".to_string());
    append_hosts(
        &mut lines,
        &final_snapshot.aggregate_summary.top_hosts,
        top_n,
        geo,
    );
    lines.push(String::new());

    lines.push("top services:".to_string());
    append_services(
        &mut lines,
        &final_snapshot.aggregate_summary.top_services,
        top_n,
    );
    lines.push(String::new());

    let findings = evaluate_cli_findings(
        final_snapshot,
        signal_options.is_offline,
        top_n,
        signal_options.data_threshold_bytes,
        signal_options.watched.clone(),
        signal_options.known_bad.clone(),
    );
    lines.push(format!("findings: {}", findings.len()));
    if findings.is_empty() {
        lines.push("  -".to_string());
    } else {
        for finding in findings {
            lines.push(format!(
                "  [{}] {} · {} · {} · pivot={}:{}",
                finding.status,
                finding.kind,
                finding.summary,
                finding.subject,
                finding.pivot_kind,
                finding.pivot_value
            ));
        }
    }

    lines.join("\n")
}

fn top_limited_final_snapshot(final_snapshot: &FinalSnapshot, top_n: usize) -> FinalSnapshot {
    let mut limited = final_snapshot.clone();
    limited.aggregate_summary.top_connections.truncate(top_n);
    limited.aggregate_summary.tcp_connections.truncate(top_n);
    limited.aggregate_summary.top_hosts.truncate(top_n);
    limited.aggregate_summary.top_services.truncate(top_n);
    limited
}

fn append_connections(
    lines: &mut Vec<String>,
    connections: &[ConnectionSummary],
    top_n: usize,
    geo: &mut GeoCountryResolver,
) {
    if connections.is_empty() {
        lines.push("  -".to_string());
        return;
    }

    for connection in connections.iter().take(top_n) {
        let source_ip = format_ip_with_country_code(connection.key.source_ip, geo);
        let destination_ip = format_ip_with_country_code(connection.key.destination_ip, geo);
        let tcp_suffix = connection
            .counters
            .tcp_stats
            .as_ref()
            .map(|stats| {
                format!(
                    " tcp_state={:?} syn={} fin={} rst={}",
                    stats.state, stats.syn_count, stats.fin_count, stats.rst_count
                )
            })
            .unwrap_or_default();
        lines.push(format!(
            "  {}:{} -> {}:{} {:?} packets={} bytes={}{}",
            source_ip,
            connection
                .key
                .source_port
                .map(|port| port.to_string())
                .unwrap_or_else(|| "-".to_string()),
            destination_ip,
            connection
                .key
                .destination_port
                .map(|port| port.to_string())
                .unwrap_or_else(|| "-".to_string()),
            connection.key.protocol,
            connection.counters.packets,
            connection.counters.bytes,
            tcp_suffix
        ));
    }
}

fn append_hosts(
    lines: &mut Vec<String>,
    hosts: &[HostSummary],
    top_n: usize,
    geo: &mut GeoCountryResolver,
) {
    if hosts.is_empty() {
        lines.push("  -".to_string());
        return;
    }

    for host in hosts.iter().take(top_n) {
        lines.push(format!(
            "  {} packets={} bytes={} in={} out={}",
            format_ip_with_country_label(host.host, geo),
            host.counters.packets,
            host.counters.bytes,
            host.counters.bytes_in,
            host.counters.bytes_out
        ));
    }
}

fn enriched_hosts(
    hosts: &[HostSummary],
    top_n: usize,
    geo: &mut GeoCountryResolver,
) -> Vec<EnrichedHostSummary> {
    hosts
        .iter()
        .take(top_n)
        .map(|host| {
            let country = country_view(geo.resolve(host.host));
            let asn = geo.resolve_asn(host.host);
            EnrichedHostSummary {
                host: host.host.to_string(),
                country_code: country.code,
                country_label: country.label,
                country_kind: country.kind,
                asn_number: asn.number,
                asn_organization: asn.organization.clone(),
                asn_label: asn.display_label(),
                sni: host.counters.sni.clone(),
                packets: host.counters.packets,
                bytes: host.counters.bytes,
                bytes_in: host.counters.bytes_in,
                bytes_out: host.counters.bytes_out,
            }
        })
        .collect()
}

fn enriched_connections(
    connections: &[ConnectionSummary],
    top_n: usize,
    geo: &mut GeoCountryResolver,
) -> Vec<EnrichedConnectionSummary> {
    connections
        .iter()
        .take(top_n)
        .map(|connection| EnrichedConnectionSummary {
            source: enriched_endpoint(connection.key.source_ip, geo),
            source_port: connection.key.source_port,
            destination: enriched_endpoint(connection.key.destination_ip, geo),
            destination_port: connection.key.destination_port,
            protocol: format!("{:?}", connection.key.protocol),
            sni: connection.counters.sni.clone(),
            packets: connection.counters.packets,
            bytes: connection.counters.bytes,
        })
        .collect()
}

fn enriched_endpoint(ip: IpAddr, geo: &mut GeoCountryResolver) -> EnrichedEndpoint {
    let country = country_view(geo.resolve(ip));
    let asn = geo.resolve_asn(ip);
    EnrichedEndpoint {
        address: ip.to_string(),
        country_code: country.code,
        country_label: country.label,
        country_kind: country.kind,
        asn_number: asn.number,
        asn_organization: asn.organization.clone(),
        asn_label: asn.display_label(),
    }
}

fn geo_resolver() -> Result<GeoCountryResolver> {
    GeoCountryResolver::new()
        .or_err(
            ErrorType::InternalError,
            "Failed to initialize bundled GeoLite2 country/ASN MMDB resolvers for CLI output",
        )
        .map_err(|e| e.into_cli())
}

struct CountryView {
    code: String,
    label: String,
    kind: &'static str,
}

fn country_view(country: CountryInfo) -> CountryView {
    let kind = country_kind_label(&country.kind);
    let label = country.display_label();
    CountryView {
        code: country.code,
        label,
        kind,
    }
}

fn country_kind_label(kind: &CountryKind) -> &'static str {
    match kind {
        CountryKind::Country => "country",
        CountryKind::Loopback => "loopback",
        CountryKind::Local => "local",
        CountryKind::Unknown => "unknown",
    }
}

fn format_ip_with_country_label(ip: IpAddr, geo: &mut GeoCountryResolver) -> String {
    let country = geo.resolve(ip);
    format!("{ip} ({})", country.display_label())
}

fn format_ip_with_country_code(ip: IpAddr, geo: &mut GeoCountryResolver) -> String {
    let country = geo.resolve(ip);
    format!("{ip}({})", country.code)
}

fn append_services(lines: &mut Vec<String>, services: &[ServiceSummary], top_n: usize) {
    if services.is_empty() {
        lines.push("  -".to_string());
        return;
    }

    for service in services.iter().take(top_n) {
        lines.push(format!(
            "  {} {:?} packets={} bytes={} confidence={:?}",
            service.service.name,
            service.service.transport,
            service.counters.packets,
            service.counters.bytes,
            service.service.confidence
        ));
    }
}

fn format_timestamp(timestamp: PacketTimestamp) -> String {
    format!("{}.{:06}", timestamp.seconds, timestamp.microseconds)
}

#[cfg(test)]
mod tests {
    use std::{
        fs,
        net::{IpAddr, Ipv4Addr},
        path::PathBuf,
        time::{SystemTime, UNIX_EPOCH},
    };

    use flowarden_core::flow::{AggregateSummary, AggregateTotals};
    use flowarden_core::{
        analysis::{ServiceConfidence, ServiceLabel, TransportProtocol},
        flow::{FlowCounters, FlowKey, HostCounters, ServiceCounters},
    };

    use super::*;

    fn timestamp(second: i64) -> PacketTimestamp {
        PacketTimestamp::tick(second)
    }

    fn sample_output() -> (Vec<TickSnapshot>, FinalSnapshot) {
        let flow = ConnectionSummary {
            key: FlowKey {
                source_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
                destination_ip: IpAddr::V4(Ipv4Addr::new(93, 184, 216, 34)),
                source_port: Some(50123),
                destination_port: Some(443),
                protocol: TransportProtocol::Tcp,
            },
            counters: FlowCounters {
                packets: 2,
                bytes: 180,
                packets_in: 0,
                packets_out: 2,
                bytes_in: 0,
                bytes_out: 180,
                first_seen: timestamp(1),
                last_seen: timestamp(2),
                tcp_stats: Some(flowarden_core::flow::TcpConnectionStats {
                    syn_count: 2,
                    fin_count: 0,
                    rst_count: 0,
                    packets: 2,
                    bytes: 180,
                    payload_bytes: 100,
                    state: flowarden_core::flow::TcpConnectionState::Established,
                    initiator: Some(flowarden_core::flow::EndpointId {
                        ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
                        port: 50123,
                    }),
                    first_seen: timestamp(1),
                    last_seen: timestamp(2),
                    last_payload_seen: Some(timestamp(2)),
                }),
                sni: Some("example.com".into()),
            },
        };
        let host = HostSummary {
            host: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
            counters: HostCounters {
                packets: 2,
                bytes: 180,
                packets_in: 0,
                packets_out: 2,
                bytes_in: 0,
                bytes_out: 180,
                first_seen: timestamp(1),
                last_seen: timestamp(2),
                sni: Some("example.com".into()),
            },
        };
        let service = ServiceSummary {
            service: ServiceLabel {
                name: "https".to_string(),
                transport: TransportProtocol::Tcp,
                confidence: ServiceConfidence::High,
            },
            counters: ServiceCounters {
                packets: 2,
                bytes: 180,
                packets_in: 0,
                packets_out: 2,
                bytes_in: 0,
                bytes_out: 180,
                confidence: ServiceConfidence::High,
            },
        };
        let tick = TickSnapshot {
            capture_id: "capture".to_string(),
            sequence: 1,
            timestamp: timestamp(1),
            totals: AggregateTotals {
                packets: 2,
                bytes: 180,
            },
            dropped_packets: 0,
            last_packet_timestamp: Some(timestamp(2)),
            top_connections: vec![flow.clone()],
            top_hosts: vec![host.clone()],
            top_services: vec![service.clone()],
        };
        let final_snapshot = FinalSnapshot {
            capture_id: "capture".to_string(),
            started_at: timestamp(1),
            ended_at: timestamp(2),
            totals: AggregateTotals {
                packets: 2,
                bytes: 180,
            },
            dropped_packets: 0,
            last_packet_timestamp: Some(timestamp(2)),
            aggregate_summary: AggregateSummary {
                top_connections: vec![flow],
                tcp_connections: Vec::new(),
                top_hosts: vec![host],
                top_services: vec![service],
            },
        };

        (vec![tick], final_snapshot)
    }

    fn temp_output_path() -> PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("flowarden-output-{unique}.json"))
    }

    #[test]
    fn json_output_is_stable_and_parseable() {
        let (ticks, final_snapshot) = sample_output();
        let json =
            render_capture_output_with_signals(
                OutputFormat::Json,
                &ticks,
                &[],
                &final_snapshot,
                20,
                &CliSignalOptions::default(),
            )
            .unwrap();
        let parsed: serde_json::Value = serde_json::from_str(&json).unwrap();
        assert_eq!(parsed["final_snapshot"]["totals"]["packets"], 2);
        assert_eq!(parsed["tick_snapshots"][0]["sequence"], 1);
        assert!(parsed["offline_gaps"].as_array().unwrap().is_empty());
        assert_eq!(
            parsed["final_snapshot"]["aggregate_summary"]["top_services"][0]["service"]["name"],
            "https"
        );
        assert_eq!(parsed["top_hosts_enriched"][0]["country_label"], "Local");
        assert_eq!(parsed["top_hosts_enriched"][0]["sni"], "example.com");
        assert_eq!(
            parsed["top_connections_enriched"][0]["source"]["country_code"],
            "LOCAL"
        );
        assert_eq!(
            parsed["top_connections_enriched"][0]["sni"],
            "example.com"
        );
        // Default threshold 50MB with 180 bytes → no threshold finding; empty watchlist.
        assert!(parsed["findings"].as_array().unwrap().is_empty());
    }

    #[test]
    fn json_output_includes_offline_findings_with_policy() {
        let (ticks, final_snapshot) = sample_output();
        let options = CliSignalOptions {
            is_offline: true,
            data_threshold_bytes: 100,
            watched: vec!["example.com".into(), "service:https".into()],
            known_bad: Vec::new(),
        };
        let json = render_capture_output_with_signals(
            OutputFormat::Json,
            &ticks,
            &[],
            &final_snapshot,
            20,
            &options,
        )
        .unwrap();
        let parsed: serde_json::Value = serde_json::from_str(&json).unwrap();
        let findings = parsed["findings"].as_array().unwrap();
        assert!(
            findings.len() >= 2,
            "expected threshold + watched host/service findings, got {findings:?}"
        );
        assert!(findings.iter().all(|f| f["mode"] == "offline"));
        assert!(findings.iter().all(|f| f["status"] == "finding"));
        assert!(
            findings
                .iter()
                .any(|f| f["kind"] == "DataThresholdExceeded")
        );
        assert!(
            findings
                .iter()
                .any(|f| f["kind"] == "WatchedEntityTransmitted")
        );
    }

    #[test]
    fn json_output_applies_top_n_to_summary_and_enriched_rows() {
        let (ticks, mut final_snapshot) = sample_output();
        let mut extra_connection = final_snapshot.aggregate_summary.top_connections[0].clone();
        extra_connection.key.destination_ip = IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1));
        final_snapshot
            .aggregate_summary
            .top_connections
            .push(extra_connection);

        let mut extra_host = final_snapshot.aggregate_summary.top_hosts[0].clone();
        extra_host.host = IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1));
        final_snapshot.aggregate_summary.top_hosts.push(extra_host);

        let mut extra_service = final_snapshot.aggregate_summary.top_services[0].clone();
        extra_service.service.name = "http".to_string();
        final_snapshot
            .aggregate_summary
            .top_services
            .push(extra_service);

        let json =
            render_capture_output_with_signals(
                OutputFormat::Json,
                &ticks,
                &[],
                &final_snapshot,
                1,
                &CliSignalOptions::default(),
            )
            .unwrap();
        let parsed: serde_json::Value = serde_json::from_str(&json).unwrap();

        assert_eq!(
            parsed["final_snapshot"]["aggregate_summary"]["top_connections"]
                .as_array()
                .unwrap()
                .len(),
            1
        );
        assert_eq!(
            parsed["final_snapshot"]["aggregate_summary"]["top_hosts"]
                .as_array()
                .unwrap()
                .len(),
            1
        );
        assert_eq!(
            parsed["final_snapshot"]["aggregate_summary"]["top_services"]
                .as_array()
                .unwrap()
                .len(),
            1
        );
        assert_eq!(
            parsed["top_connections_enriched"].as_array().unwrap().len(),
            1
        );
        assert_eq!(parsed["top_hosts_enriched"].as_array().unwrap().len(), 1);
    }

    #[test]
    fn table_output_is_human_readable() {
        let (ticks, final_snapshot) = sample_output();
        let table =
            render_capture_output_with_signals(
                OutputFormat::Table,
                &ticks,
                &[],
                &final_snapshot,
                20,
                &CliSignalOptions::default(),
            )
            .unwrap();
        assert!(table.contains("capture_id: capture"));
        assert!(table.contains("offline_gaps: 0"));
        assert!(table.contains("top connections:"));
        assert!(table.contains("findings:"));
        assert!(table.contains("192.168.1.10(LOCAL):50123"));
        assert!(table.contains("192.168.1.10 (Local) packets=2"));
        assert!(table.contains("https"));
    }

    #[test]
    fn file_output_writes_expected_content() {
        let path = temp_output_path();
        emit_output("{\"ok\":true}", Some(&path)).unwrap();
        let content = fs::read_to_string(&path).unwrap();
        assert_eq!(content, "{\"ok\":true}");
        let _ = fs::remove_file(path);
    }
}
