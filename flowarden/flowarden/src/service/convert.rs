//! Domain-model to gRPC DTO conversion and inspect filtering.

use std::{
    collections::{HashMap, HashSet},
    net::IpAddr,
    sync::{Arc, Mutex},
};

use flowarden_core::{
    analysis::{TrafficDirection, TransportProtocol},
    flow::{
        AggregateTotals, ConnectionSummary, HostSummary, PacketTimestamp, ServiceSummary,
        TcpConnectionState, TcpConnectionSummary, TickSnapshot,
    },
};
use flowarden_error::{Error, ErrorType, Result};

use super::{
    constants::{PROJECTION_MAX_TOP_N, PROJECTION_TOP_N},
    proto::projection::{
        AggregateTotals as ProtoAggregateTotals, ConnectionRow, DestinationMapPlaceholder,
        DestinationSummary, GetInspectPageRequest, GetTcpConnectionsPageRequest, HostRow,
        OverviewSnapshotResponse, PacketTimestamp as ProtoPacketTimestamp, ProjectionMode,
        ServiceRow, TcpConnectionRow,
    },
    state::OverviewRuntimeSnapshot,
    timeline::timeline_points_for_snapshot,
};
use crate::geo::{AsnInfo, CountryInfo, CountryKind, GeoCountryResolver};
// AsnInfo used by destination aggregation.

pub(crate) fn packet_timestamp_to_proto(timestamp: PacketTimestamp) -> ProtoPacketTimestamp {
    ProtoPacketTimestamp {
        seconds: timestamp.seconds,
        microseconds: timestamp.microseconds,
    }
}

pub(crate) fn aggregate_totals_to_proto(
    totals: AggregateTotals,
    bytes_in: u64,
    bytes_out: u64,
) -> ProtoAggregateTotals {
    ProtoAggregateTotals {
        packets: totals.packets,
        bytes: totals.bytes,
        bytes_in,
        bytes_out,
    }
}

pub(crate) fn connection_to_proto_with_process(
    connection: &ConnectionSummary,
    process: Option<&super::process_lookup::ProcessInfo>,
    remote_asn_label: String,
    local_ips: &HashSet<IpAddr>,
) -> ConnectionRow {
    ConnectionRow {
        source_address: connection.key.source_ip.to_string(),
        source_port: connection
            .key
            .source_port
            .map(u32::from)
            .unwrap_or_default(),
        destination_address: connection.key.destination_ip.to_string(),
        destination_port: connection
            .key
            .destination_port
            .map(u32::from)
            .unwrap_or_default(),
        protocol: transport_protocol_label(&connection.key.protocol).to_string(),
        service_name: service_name_for_connection(connection).to_string(),
        direction: direction_label_for_connection(connection, local_ips).to_string(),
        packets: connection.counters.packets,
        bytes: connection.counters.bytes,
        process_name: process.map(|p| p.name.clone()).unwrap_or_default(),
        process_pid: process.map(|p| p.pid).unwrap_or_default(),
        process_inferred: process.is_some(),
        sni: connection.counters.sni.clone().unwrap_or_default(),
        process_path: process.map(|p| p.path.clone()).unwrap_or_default(),
        process_bundle_id: process.map(|p| p.bundle_id.clone()).unwrap_or_default(),
        remote_asn_label,
    }
}

pub(crate) fn host_to_proto_enriched(
    host: &HostSummary,
    country: CountryInfo,
    hostname: Option<String>,
    asn: AsnInfo,
) -> HostRow {
    HostRow {
        host: host.host.to_string(),
        packets: host.counters.packets,
        bytes: host.counters.bytes,
        country_label: country.display_label(),
        hostname: hostname.unwrap_or_default(),
        sni: host.counters.sni.clone().unwrap_or_default(),
        asn_number: asn.number,
        asn_organization: asn.organization.clone(),
        asn_label: asn.display_label(),
    }
}

pub(crate) fn tcp_connection_to_proto(connection: &TcpConnectionSummary) -> TcpConnectionRow {
    TcpConnectionRow {
        endpoint_a_address: connection.key.endpoint_a.ip.to_string(),
        endpoint_a_port: u32::from(connection.key.endpoint_a.port),
        endpoint_b_address: connection.key.endpoint_b.ip.to_string(),
        endpoint_b_port: u32::from(connection.key.endpoint_b.port),
        state: tcp_connection_state_label(&connection.stats.state).to_string(),
        syn_count: connection.stats.syn_count,
        fin_count: connection.stats.fin_count,
        rst_count: connection.stats.rst_count,
        packets: connection.stats.packets,
        bytes: connection.stats.bytes,
        first_seen: Some(packet_timestamp_to_proto(connection.stats.first_seen)),
        last_seen: Some(packet_timestamp_to_proto(connection.stats.last_seen)),
    }
}

pub(crate) fn service_to_proto(service: &ServiceSummary) -> ServiceRow {
    ServiceRow {
        name: service.service.name.clone(),
        transport: transport_protocol_label(&service.service.transport).to_string(),
        packets: service.counters.packets,
        bytes: service.counters.bytes,
    }
}

pub(crate) fn build_top_destinations(
    connections: &[ConnectionSummary],
    geo: &mut GeoCountryResolver,
    top_n: usize,
) -> Vec<DestinationSummary> {
    // Country remains the aggregation key for map markers. Track dominant ASN per country
    // for secondary labels without changing the grouping model.
    let mut by_country: HashMap<String, CountryDestinationAgg> = HashMap::new();

    for connection in connections {
        let destination_ip = connection.key.destination_ip;
        if destination_ip.is_loopback()
            || matches!(destination_ip, std::net::IpAddr::V4(v4) if v4.is_private() || v4.is_link_local())
            || matches!(destination_ip, std::net::IpAddr::V6(v6) if v6.is_unique_local() || v6.is_unicast_link_local())
        {
            continue;
        }

        let country = geo.resolve(destination_ip);
        if !matches!(country.kind, CountryKind::Country) {
            continue;
        }

        let asn = geo.resolve_asn(destination_ip);
        let bytes = connection.counters.bytes;
        let entry = by_country
            .entry(country.code.clone())
            .or_insert_with(|| CountryDestinationAgg {
                country,
                bytes: 0,
                top_asn: AsnInfo::default(),
                top_asn_bytes: 0,
            });
        entry.bytes += bytes;
        if asn.is_known() && bytes >= entry.top_asn_bytes {
            entry.top_asn = asn;
            entry.top_asn_bytes = bytes;
        }
    }

    let mut items = by_country.into_values().collect::<Vec<_>>();
    items.sort_by(|left, right| {
        right
            .bytes
            .cmp(&left.bytes)
            .then_with(|| left.country.code.cmp(&right.country.code))
    });
    let total_region_bytes = items.iter().map(|item| item.bytes).sum::<u64>() as f64;

    items
        .into_iter()
        .take(top_n)
        .map(|item| DestinationSummary {
            label: item.country.display_label(),
            bytes: item.bytes,
            ratio: if total_region_bytes == 0.0 {
                0.0
            } else {
                item.bytes as f64 / total_region_bytes
            },
            country_label: item.country.display_label(),
            country_code: item.country.code,
            asn_number: item.top_asn.number,
            asn_organization: item.top_asn.organization.clone(),
            asn_label: item.top_asn.display_label(),
        })
        .collect()
}

struct CountryDestinationAgg {
    country: CountryInfo,
    bytes: u64,
    top_asn: AsnInfo,
    top_asn_bytes: u64,
}
pub(crate) fn projection_response_from_runtime_snapshot(
    runtime_snapshot: OverviewRuntimeSnapshot,
    geo: &Arc<Mutex<GeoCountryResolver>>,
    process_lookup: Option<&super::process_lookup::ProcessLookup>,
    rdns_lookup: Option<&super::rdns_lookup::RdnsLookup>,
    signal_engine: Option<&Mutex<super::signals::SignalEngine>>,
    top_n: usize,
) -> Result<OverviewSnapshotResponse> {
    if let Some(error_message) = runtime_snapshot.error_message.clone() {
        let mut response = empty_overview_response();
        response.capture_id = runtime_snapshot.capture_id;
        response.mode = runtime_snapshot.mode as i32;
        response.source_label = runtime_snapshot.source_label;
        response.filter_label = runtime_snapshot.filter_label;
        response.metric_mode = runtime_snapshot.metric_mode;
        response.capture_status = runtime_snapshot.capture_status;
        response.destination_map = Some(DestinationMapPlaceholder {
            state: "error".to_string(),
            message: error_message,
        });
        return Ok(response);
    }

    let Some(latest_tick) = runtime_snapshot.tick_snapshots.last().cloned() else {
        let mut response = empty_overview_response();
        response.capture_id = runtime_snapshot.capture_id;
        response.mode = runtime_snapshot.mode as i32;
        response.source_label = runtime_snapshot.source_label;
        response.filter_label = runtime_snapshot.filter_label;
        response.metric_mode = runtime_snapshot.metric_mode;
        response.capture_status = runtime_snapshot.capture_status;
        return Ok(response);
    };

    let mut geo = geo
        .lock()
        .map_err(|_| Error::explain(ErrorType::InternalError, "Failed to lock geo resolver"))?;

    let timeline_points = timeline_points_for_snapshot(&runtime_snapshot);
    let top_destinations =
        build_top_destinations(&runtime_snapshot.top_connections, &mut geo, top_n);
    let destination_map = if top_destinations.is_empty() {
        DestinationMapPlaceholder {
            state: "empty".to_string(),
            message: "No public destination regions resolved yet.".to_string(),
        }
    } else {
        DestinationMapPlaceholder {
            state: "ready".to_string(),
            message: format!(
                "{} destination region(s) from resolved public traffic.",
                top_destinations.len()
            ),
        }
    };

    let mut top_connections = Vec::with_capacity(top_n.min(runtime_snapshot.top_connections.len()));
    for connection in runtime_snapshot.top_connections.iter().take(top_n) {
        let process =
            process_lookup.and_then(|lookup| lookup.resolve(connection, &runtime_snapshot.local_ips));
        // Warm rDNS cache for remote endpoints that may not yet rank in top_hosts.
        if let Some(rdns) = rdns_lookup {
            let _ = rdns.resolve(connection.key.destination_ip);
            let _ = rdns.resolve(connection.key.source_ip);
        }
        let remote_ip = remote_endpoint_ip(connection, &runtime_snapshot.local_ips);
        let remote_asn_label = geo.resolve_asn(remote_ip).display_label();
        top_connections.push(connection_to_proto_with_process(
            connection,
            process.as_ref(),
            remote_asn_label,
            &runtime_snapshot.local_ips,
        ));
    }

    let mode_label = match runtime_snapshot.mode {
        super::proto::projection::ProjectionMode::Offline => "offline",
        _ => "live",
    };
    let process_bytes: Vec<(String, u64)> = top_connections
        .iter()
        .filter(|row| !row.process_name.is_empty())
        .map(|row| (row.process_name.clone(), row.bytes))
        .collect();
    let signals = signal_engine
        .and_then(|engine| engine.lock().ok())
        .map(|mut engine| {
            let mut list = engine.evaluate_and_list(&runtime_snapshot);
            engine.evaluate_processes(&process_bytes, mode_label);
            if !process_bytes.is_empty() {
                list = engine.list_proto();
            }
            list
        })
        .unwrap_or_default();

    Ok(OverviewSnapshotResponse {
        capture_id: runtime_snapshot.capture_id,
        mode: runtime_snapshot.mode as i32,
        sequence: latest_tick.sequence,
        timestamp: Some(packet_timestamp_to_proto(latest_tick.timestamp)),
        totals: Some(aggregate_totals_to_proto(
            runtime_snapshot.totals.clone(),
            aggregate_inbound_bytes(&runtime_snapshot.tick_snapshots),
            aggregate_outbound_bytes(&runtime_snapshot.tick_snapshots),
        )),
        dropped_packets: runtime_snapshot.dropped_packets,
        last_packet_timestamp: runtime_snapshot
            .last_packet_timestamp
            .map(packet_timestamp_to_proto),
        top_connections,
        top_hosts: overview_top_hosts_to_proto(
            &runtime_snapshot.top_hosts,
            &runtime_snapshot.local_ips,
            &mut geo,
            rdns_lookup,
            top_n,
        ),
        top_services: runtime_snapshot
            .top_services
            .iter()
            .take(top_n)
            .map(service_to_proto)
            .collect(),
        destination_map: Some(destination_map),
        top_destinations,
        source_label: runtime_snapshot.source_label,
        filter_label: runtime_snapshot.filter_label,
        metric_mode: runtime_snapshot.metric_mode,
        timeline_points,
        capture_status: runtime_snapshot.capture_status,
        top_tcp_connections: runtime_snapshot
            .tcp_connections
            .iter()
            .take(top_n)
            .map(tcp_connection_to_proto)
            .collect(),
        signals,
        process_lookup_pending: process_lookup
            .map(|lookup| lookup.pending_count() as u32)
            .unwrap_or(0),
        process_lookup_cache_size: process_lookup
            .map(|lookup| lookup.cache_size() as u32)
            .unwrap_or(0),
    })
}

pub(crate) fn overview_top_hosts_to_proto(
    hosts: &[HostSummary],
    local_ips: &HashSet<IpAddr>,
    geo: &mut GeoCountryResolver,
    rdns_lookup: Option<&super::rdns_lookup::RdnsLookup>,
    top_n: usize,
) -> Vec<HostRow> {
    hosts
        .iter()
        .filter_map(|host| {
            let country = geo.resolve(host.host);
            if local_ips.contains(&host.host)
                || matches!(country.kind, CountryKind::Local | CountryKind::Loopback)
            {
                return None;
            }
            let hostname = rdns_lookup.and_then(|lookup| lookup.resolve(host.host));
            let asn = geo.resolve_asn(host.host);
            Some(host_to_proto_enriched(host, country, hostname, asn))
        })
        .take(top_n)
        .collect()
}

/// Prefer the non-local endpoint when local IPs are known; otherwise destination.
fn remote_endpoint_ip(connection: &ConnectionSummary, local_ips: &HashSet<IpAddr>) -> IpAddr {
    if !local_ips.is_empty() {
        if local_ips.contains(&connection.key.source_ip)
            && !local_ips.contains(&connection.key.destination_ip)
        {
            return connection.key.destination_ip;
        }
        if local_ips.contains(&connection.key.destination_ip)
            && !local_ips.contains(&connection.key.source_ip)
        {
            return connection.key.source_ip;
        }
    }
    connection.key.destination_ip
}

pub(crate) fn normalize_projection_top_n(top_n: u32) -> usize {
    if top_n == 0 {
        return PROJECTION_TOP_N;
    }

    (top_n as usize).min(PROJECTION_MAX_TOP_N)
}

pub(crate) fn empty_overview_response() -> OverviewSnapshotResponse {
    OverviewSnapshotResponse {
        capture_id: "live:inactive".to_string(),
        mode: ProjectionMode::Live as i32,
        sequence: 0,
        timestamp: Some(packet_timestamp_to_proto(PacketTimestamp::tick(0))),
        totals: Some(aggregate_totals_to_proto(AggregateTotals::default(), 0, 0)),
        dropped_packets: 0,
        last_packet_timestamp: None,
        top_connections: Vec::new(),
        top_hosts: Vec::new(),
        top_services: Vec::new(),
        destination_map: Some(DestinationMapPlaceholder {
            state: "reserved".to_string(),
            message: "Destination map is reserved for a future phase 2 enhancement.".to_string(),
        }),
        top_destinations: Vec::new(),
        source_label: "Live source · not started".to_string(),
        filter_label: "Filter · none".to_string(),
        metric_mode: "bytes".to_string(),
        timeline_points: Vec::new(),
        capture_status: "idle".to_string(),
        top_tcp_connections: Vec::new(),
        signals: Vec::new(),
        process_lookup_pending: 0,
        process_lookup_cache_size: 0,
    }
}

pub(crate) fn aggregate_inbound_bytes(ticks: &[TickSnapshot]) -> u64 {
    ticks
        .iter()
        .flat_map(|tick| tick.top_connections.iter())
        .map(|connection| connection.counters.bytes_in)
        .sum()
}

pub(crate) fn aggregate_outbound_bytes(ticks: &[TickSnapshot]) -> u64 {
    ticks
        .iter()
        .flat_map(|tick| tick.top_connections.iter())
        .map(|connection| connection.counters.bytes_out)
        .sum()
}
pub(crate) fn service_name_for_connection(connection: &ConnectionSummary) -> &'static str {
    match connection
        .key
        .destination_port
        .or(connection.key.source_port)
        .unwrap_or_default()
    {
        443 => "https",
        53 => "dns",
        _ => "unknown",
    }
}

pub(crate) fn direction_label_for_connection(
    connection: &ConnectionSummary,
    local_ips: &HashSet<IpAddr>,
) -> &'static str {
    let src = connection.key.source_ip;
    let dst = connection.key.destination_ip;

    if src.is_loopback() && dst.is_loopback() {
        return traffic_direction_label(&TrafficDirection::Local);
    }

    if !local_ips.is_empty() {
        let src_local = local_ips.contains(&src);
        let dst_local = local_ips.contains(&dst);
        return match (src_local, dst_local) {
            (true, true) => traffic_direction_label(&TrafficDirection::Local),
            // Local-oriented keys keep the local endpoint on source; use byte balance
            // for session direction after reverse-path merge.
            (true, false) => {
                if connection.counters.bytes_out >= connection.counters.bytes_in {
                    traffic_direction_label(&TrafficDirection::Outbound)
                } else {
                    traffic_direction_label(&TrafficDirection::Inbound)
                }
            }
            (false, true) => {
                // Should be rare after orientation; treat as inbound toward local.
                traffic_direction_label(&TrafficDirection::Inbound)
            }
            (false, false) => traffic_direction_label(&TrafficDirection::Unknown),
        };
    }

    // Offline / empty local list: private source ⇒ local-oriented outbound bias.
    let src_private = is_private_or_loopback(src);
    let dst_private = is_private_or_loopback(dst);
    match (src_private, dst_private) {
        (true, true) => traffic_direction_label(&TrafficDirection::Local),
        (true, false) => {
            if connection.counters.bytes_out >= connection.counters.bytes_in {
                traffic_direction_label(&TrafficDirection::Outbound)
            } else {
                traffic_direction_label(&TrafficDirection::Inbound)
            }
        }
        (false, true) => traffic_direction_label(&TrafficDirection::Inbound),
        (false, false) => {
            if connection.counters.bytes_out >= connection.counters.bytes_in {
                traffic_direction_label(&TrafficDirection::Outbound)
            } else {
                traffic_direction_label(&TrafficDirection::Inbound)
            }
        }
    }
}

fn is_private_or_loopback(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(v4) => v4.is_private() || v4.is_loopback() || v4.is_link_local(),
        IpAddr::V6(v6) => v6.is_loopback() || v6.is_unique_local() || v6.is_unicast_link_local(),
    }
}

pub(crate) fn inspect_row_matches_filter(
    row: &ConnectionRow,
    filter: &GetInspectPageRequest,
) -> bool {
    matches_filter_field(&filter.source_address, &row.source_address)
        && matches_filter_field(&filter.destination_address, &row.destination_address)
        && matches_filter_field(&filter.service_name, &row.service_name)
        && matches_filter_field(&filter.protocol, &row.protocol)
        && matches_filter_field(&filter.direction, &row.direction)
        && matches_filter_field(&filter.process_name, &row.process_name)
        && matches_filter_field(&filter.sni, &row.sni)
}

pub(crate) fn tcp_connection_matches_filter(
    row: &TcpConnectionRow,
    filter: &GetTcpConnectionsPageRequest,
) -> bool {
    let address_port_matches = if filter.address.trim().is_empty() {
        true
    } else {
        let filter = filter.address.trim().to_ascii_lowercase();
        [
            row.endpoint_a_address.to_ascii_lowercase(),
            row.endpoint_b_address.to_ascii_lowercase(),
            format!("{}:{}", row.endpoint_a_address, row.endpoint_a_port).to_ascii_lowercase(),
            format!("{}:{}", row.endpoint_b_address, row.endpoint_b_port).to_ascii_lowercase(),
        ]
        .iter()
        .any(|value| value.contains(&filter))
    };

    let port_matches = if filter.port.trim().is_empty() {
        true
    } else {
        let filter = filter.port.trim();
        row.endpoint_a_port.to_string().contains(filter)
            || row.endpoint_b_port.to_string().contains(filter)
    };

    address_port_matches && port_matches && matches_filter_field(&filter.state, &row.state)
}

pub(crate) fn matches_filter_field(filter: &str, value: &str) -> bool {
    if filter.trim().is_empty() {
        return true;
    }

    value
        .to_ascii_lowercase()
        .contains(&filter.trim().to_ascii_lowercase())
}

pub(crate) fn traffic_direction_label(direction: &TrafficDirection) -> &'static str {
    match direction {
        TrafficDirection::Inbound => "inbound",
        TrafficDirection::Outbound => "outbound",
        TrafficDirection::Local => "loopback",
        TrafficDirection::Unknown => "unknown",
    }
}

pub(crate) fn transport_protocol_label(protocol: &TransportProtocol) -> &'static str {
    match protocol {
        TransportProtocol::Tcp => "tcp",
        TransportProtocol::Udp => "udp",
        TransportProtocol::Icmp => "icmp",
        TransportProtocol::Icmpv6 => "icmpv6",
        TransportProtocol::Arp => "arp",
        TransportProtocol::Other(_) => "other",
    }
}

pub(crate) fn tcp_connection_state_label(state: &TcpConnectionState) -> &'static str {
    match state {
        TcpConnectionState::Init => "INIT",
        TcpConnectionState::SynAck => "SYN_ACK",
        TcpConnectionState::Established => "ESTABLISHED",
        TcpConnectionState::Fin => "FIN",
        TcpConnectionState::Rst => "RST",
    }
}
