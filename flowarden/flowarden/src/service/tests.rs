use std::{
    collections::HashSet,
    net::{IpAddr, Ipv4Addr, SocketAddr},
};

use flowarden_core::{
    analysis::TransportProtocol,
    capture::RuntimeReport,
    flow::{
        AggregateTotals, ConnectionSummary, HostSummary, OfflineGap, PacketTimestamp, TickSnapshot,
    },
};

use super::{
    bpf::resident_capture_bpf,
    constants::{
        OFFLINE_TIMELINE_POINTS, PROJECTION_MAX_TOP_N, PROJECTION_TICK_WINDOW, PROJECTION_TOP_N,
    },
    convert::{build_top_destinations, normalize_projection_top_n, overview_top_hosts_to_proto},
    proto::projection::ProjectionMode,
    state::{RuntimeOverviewMeta, overview_snapshot_from_report},
    timeline::{compressed_offline_timeline_points, tick_timeline_bytes},
};
use crate::geo::GeoCountryResolver;

const TEST_CONTROL_PORT: u16 = 45_123;
const ALTERNATE_CONTROL_PORT: u16 = 45_124;

fn socket(value: &str) -> SocketAddr {
    value.parse().unwrap()
}

#[test]
fn resident_capture_bpf_excludes_bound_loopback_control_port() {
    assert_eq!(
        resident_capture_bpf(None, socket(&format!("127.0.0.1:{TEST_CONTROL_PORT}"))),
        Some(format!(
            "not (tcp and host 127.0.0.1 and port {TEST_CONTROL_PORT})"
        ))
    );
}

#[test]
fn resident_capture_bpf_preserves_user_filter_and_excludes_control_port() {
    assert_eq!(
        resident_capture_bpf(
            Some("tcp and port 443"),
            socket(&format!("127.0.0.1:{TEST_CONTROL_PORT}"))
        ),
        Some(format!(
            "(tcp and port 443) and not (tcp and host 127.0.0.1 and port {TEST_CONTROL_PORT})"
        ))
    );
}

#[test]
fn resident_capture_bpf_treats_empty_user_filter_as_no_filter() {
    assert_eq!(
        resident_capture_bpf(
            Some("   "),
            socket(&format!("127.0.0.1:{TEST_CONTROL_PORT}"))
        ),
        Some(format!(
            "not (tcp and host 127.0.0.1 and port {TEST_CONTROL_PORT})"
        ))
    );
}

#[test]
fn resident_capture_bpf_supports_ipv6_loopback_bind() {
    assert_eq!(
        resident_capture_bpf(None, socket(&format!("[::1]:{TEST_CONTROL_PORT}"))),
        Some(format!(
            "not (tcp and host ::1 and port {TEST_CONTROL_PORT})"
        ))
    );
}

#[test]
fn resident_capture_bpf_limits_unspecified_bind_to_loopback_hosts() {
    assert_eq!(
        resident_capture_bpf(None, socket(&format!("0.0.0.0:{TEST_CONTROL_PORT}"))),
        Some(format!(
            "not (tcp and (host 127.0.0.1 or host ::1) and port {TEST_CONTROL_PORT})"
        ))
    );
}

#[test]
fn resident_capture_bpf_keeps_user_filter_when_port_is_unknown() {
    assert_eq!(
        resident_capture_bpf(Some("udp"), socket("127.0.0.1:0")),
        Some("udp".to_string())
    );
    assert_eq!(resident_capture_bpf(None, socket("127.0.0.1:0")), None);
}

#[test]
fn resident_capture_bpf_uses_actual_bound_port() {
    assert_eq!(
        resident_capture_bpf(None, socket(&format!("127.0.0.1:{ALTERNATE_CONTROL_PORT}"))),
        Some(format!(
            "not (tcp and host 127.0.0.1 and port {ALTERNATE_CONTROL_PORT})"
        ))
    );
}

#[test]
fn overview_top_hosts_hide_local_capture_addresses() {
    let local = IpAddr::V4(Ipv4Addr::new(10, 77, 4, 15));
    let remote = IpAddr::V4(Ipv4Addr::new(35, 223, 238, 178));
    let mut local_ips = HashSet::new();
    local_ips.insert(local);
    let mut geo = GeoCountryResolver::new().unwrap();
    let rows = overview_top_hosts_to_proto(
        &[
            HostSummary {
                host: local,
                counters: host_counters(21_206, 57),
            },
            HostSummary {
                host: remote,
                counters: host_counters(20_456, 47),
            },
        ],
        &local_ips,
        &mut geo,
        None,
        PROJECTION_TOP_N,
    );

    assert_eq!(rows.len(), 1);
    assert_eq!(rows[0].host, "35.223.238.178");
    assert_eq!(rows[0].asn_number, 396982);
    assert!(
        rows[0].asn_label.starts_with("AS396982"),
        "asn_label={}",
        rows[0].asn_label
    );
}

#[test]
fn overview_top_hosts_hide_private_local_addresses_even_when_source_addresses_are_missing() {
    let local = IpAddr::V4(Ipv4Addr::new(10, 77, 4, 15));
    let remote = IpAddr::V4(Ipv4Addr::new(35, 223, 238, 178));
    let local_ips = HashSet::new();
    let mut geo = GeoCountryResolver::new().unwrap();
    let rows = overview_top_hosts_to_proto(
        &[
            HostSummary {
                host: local,
                counters: host_counters(21_206, 57),
            },
            HostSummary {
                host: remote,
                counters: host_counters(20_456, 47),
            },
        ],
        &local_ips,
        &mut geo,
        None,
        PROJECTION_TOP_N,
    );

    assert_eq!(rows.len(), 1);
    assert_eq!(rows[0].host, "35.223.238.178");
}

#[test]
fn projection_top_n_defaults_and_caps() {
    assert_eq!(normalize_projection_top_n(0), PROJECTION_TOP_N);
    assert_eq!(normalize_projection_top_n(3), 3);
    assert_eq!(
        normalize_projection_top_n((PROJECTION_MAX_TOP_N + 1) as u32),
        PROJECTION_MAX_TOP_N
    );
}

#[test]
fn offline_timeline_points_are_compressed_to_page_width() {
    let ticks = (0..=240)
        .map(|index| tick_snapshot(index * 60, index as u64, index as u64 * 2))
        .collect::<Vec<_>>();

    let points = compressed_offline_timeline_points(&ticks, &[]);

    assert_eq!(points.len(), OFFLINE_TIMELINE_POINTS);
    assert_eq!(
        points.first().unwrap().timestamp.as_ref().unwrap().seconds,
        0
    );
    assert_eq!(
        points.last().unwrap().timestamp.as_ref().unwrap().seconds,
        14_400
    );
    assert_eq!(
        points.iter().map(|point| point.inbound_bytes).sum::<u64>(),
        ticks
            .iter()
            .map(|tick| tick_timeline_bytes(tick).0)
            .sum::<u64>()
    );
    assert_eq!(
        points.iter().map(|point| point.outbound_bytes).sum::<u64>(),
        ticks
            .iter()
            .map(|tick| tick_timeline_bytes(tick).1)
            .sum::<u64>()
    );
}

#[test]
fn offline_timeline_keeps_small_ranges_as_one_second_buckets() {
    let ticks = vec![tick_snapshot(10, 0, 100), tick_snapshot(14, 50, 0)];
    let gaps = vec![OfflineGap {
        after: PacketTimestamp::tick(10),
        seconds: 3,
    }];

    let points = compressed_offline_timeline_points(&ticks, &gaps);

    assert_eq!(points.len(), 5);
    assert_eq!(
        points
            .iter()
            .map(|point| point.timestamp.as_ref().unwrap().seconds)
            .collect::<Vec<_>>(),
        vec![10, 11, 12, 13, 14]
    );
    assert_eq!(points[0].outbound_bytes, 100);
    assert_eq!(points[1].inbound_bytes + points[1].outbound_bytes, 0);
    assert_eq!(points[2].inbound_bytes + points[2].outbound_bytes, 0);
    assert_eq!(points[3].inbound_bytes + points[3].outbound_bytes, 0);
    assert_eq!(points[4].inbound_bytes, 50);
}

#[test]
fn live_report_keeps_recent_window_but_offline_report_keeps_full_ticks() {
    let ticks = (0..(PROJECTION_TICK_WINDOW + 5))
        .map(|index| tick_snapshot(index as i64, 0, index as u64))
        .collect::<Vec<_>>();

    let live_snapshot = overview_snapshot_from_report(
        runtime_report(flowarden_core::capture::RuntimeMode::Live, ticks.clone()),
        &runtime_meta(ProjectionMode::Live),
    );
    let offline_snapshot = overview_snapshot_from_report(
        runtime_report(flowarden_core::capture::RuntimeMode::Offline, ticks.clone()),
        &runtime_meta(ProjectionMode::Offline),
    );

    assert_eq!(live_snapshot.tick_snapshots.len(), PROJECTION_TICK_WINDOW);
    assert_eq!(
        live_snapshot
            .tick_snapshots
            .first()
            .unwrap()
            .timestamp
            .seconds,
        5
    );
    assert_eq!(
        offline_snapshot.tick_snapshots.len(),
        PROJECTION_TICK_WINDOW + 5
    );
    assert_eq!(
        offline_snapshot
            .tick_snapshots
            .first()
            .unwrap()
            .timestamp
            .seconds,
        0
    );
}

#[test]
fn top_destination_ratios_use_resolved_region_bytes() {
    let mut geo = GeoCountryResolver::new().unwrap();
    let rows = build_top_destinations(
        &[
            connection_summary(IpAddr::V4(Ipv4Addr::new(35, 223, 238, 178)), 100),
            connection_summary(IpAddr::V4(Ipv4Addr::new(210, 140, 92, 187)), 100),
            connection_summary(IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)), 9_800),
        ],
        &mut geo,
        PROJECTION_TOP_N,
    );

    assert_eq!(rows.len(), 2);
    assert!(
        rows.iter()
            .all(|row| (row.ratio - 0.5).abs() < f64::EPSILON)
    );
}

fn connection_summary(destination_ip: IpAddr, bytes: u64) -> ConnectionSummary {
    connection_summary_with_bytes(destination_ip, 0, bytes)
}

fn connection_summary_with_bytes(
    destination_ip: IpAddr,
    bytes_in: u64,
    bytes_out: u64,
) -> ConnectionSummary {
    let bytes = bytes_in + bytes_out;

    ConnectionSummary {
        key: flowarden_core::flow::FlowKey {
            source_ip: IpAddr::V4(std::net::Ipv4Addr::new(192, 168, 1, 10)),
            destination_ip,
            source_port: Some(50_000),
            destination_port: Some(443),
            protocol: TransportProtocol::Tcp,
        },
        counters: flowarden_core::flow::FlowCounters {
            packets: u64::from(bytes > 0),
            bytes,
            packets_in: u64::from(bytes_in > 0),
            packets_out: u64::from(bytes_out > 0),
            bytes_in,
            bytes_out,
            first_seen: PacketTimestamp::tick(0),
            last_seen: PacketTimestamp::tick(0),
            tcp_stats: None,
            sni: None,
        },
    }
}

fn tick_snapshot(second: i64, bytes_in: u64, bytes_out: u64) -> TickSnapshot {
    let bytes = bytes_in + bytes_out;
    TickSnapshot {
        capture_id: "test".to_string(),
        sequence: second.max(0) as u64,
        timestamp: PacketTimestamp::tick(second),
        totals: AggregateTotals {
            packets: u64::from(bytes > 0),
            bytes,
        },
        dropped_packets: 0,
        last_packet_timestamp: Some(PacketTimestamp::tick(second)),
        top_connections: vec![connection_summary_with_bytes(
            IpAddr::V4(std::net::Ipv4Addr::new(35, 223, 238, 178)),
            bytes_in,
            bytes_out,
        )],
        top_hosts: Vec::new(),
        top_services: Vec::new(),
    }
}

fn runtime_meta(mode: ProjectionMode) -> RuntimeOverviewMeta {
    RuntimeOverviewMeta {
        capture_id: "test".to_string(),
        error_capture_id: "test:error".to_string(),
        mode,
        source_label: "source".to_string(),
        filter_label: "Filter · none".to_string(),
        metric_mode: "bytes".to_string(),
        local_ips: HashSet::new(),
    }
}

fn runtime_report(
    mode: flowarden_core::capture::RuntimeMode,
    ticks: Vec<TickSnapshot>,
) -> RuntimeReport {
    let ended_at = ticks
        .last()
        .map(|tick| tick.timestamp)
        .unwrap_or(PacketTimestamp::tick(0));

    RuntimeReport {
        mode,
        link_type: flowarden_core::device::LinkTypeEx::NotYetAssigned,
        stats: flowarden_core::capture::RuntimeStats::default(),
        timed_out_ticks: 0,
        stopped_by_request: false,
        tick_snapshots: ticks,
        offline_gaps: Vec::new(),
        final_snapshot: flowarden_core::flow::FinalSnapshot {
            capture_id: "test".to_string(),
            started_at: PacketTimestamp::tick(0),
            ended_at,
            totals: AggregateTotals::default(),
            dropped_packets: 0,
            last_packet_timestamp: Some(ended_at),
            aggregate_summary: flowarden_core::flow::AggregateSummary {
                top_connections: Vec::new(),
                tcp_connections: Vec::new(),
                top_hosts: Vec::new(),
                top_services: Vec::new(),
            },
        },
    }
}

fn host_counters(bytes: u64, packets: u64) -> flowarden_core::flow::HostCounters {
    flowarden_core::flow::HostCounters {
        packets,
        bytes,
        packets_in: 0,
        packets_out: 0,
        bytes_in: 0,
        bytes_out: 0,
        first_seen: PacketTimestamp::tick(0),
        last_seen: PacketTimestamp::tick(0),
        sni: None,
    }
}
