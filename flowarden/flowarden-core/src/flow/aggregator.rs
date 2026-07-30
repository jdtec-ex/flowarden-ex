use std::{collections::HashMap, net::IpAddr};

use super::bounded::{rank_by_traffic, upsert_by_bytes};
use super::model::*;
use crate::prelude::*;

/// Aggregation retention policy.
///
/// - [`AggregatorMode::Forensic`]: unbounded maps for CLI / offline export fidelity.
/// - [`AggregatorMode::Resident`]: bounded maps for long-running UI core captures.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum AggregatorMode {
    #[default]
    Forensic,
    Resident,
}

/// Soft caps for [`AggregatorMode::Resident`] global maps.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub struct ResidentBounds {
    pub max_flows: usize,
    pub max_hosts: usize,
    pub max_tcp_connections: usize,
    pub max_services: usize,
    /// Max rows retained in aggregate summaries built for progress/finish.
    pub summary_limit: usize,
}

impl Default for ResidentBounds {
    fn default() -> Self {
        Self {
            max_flows: 30_000,
            max_hosts: 15_000,
            max_tcp_connections: 30_000,
            max_services: 512,
            summary_limit: 100,
        }
    }
}

#[derive(Clone, Debug)]
pub struct AggregatorConfig {
    pub capture_id: String,
    pub started_at: PacketTimestamp,
    pub mode: AggregatorMode,
    pub resident_bounds: ResidentBounds,
    /// Capture-host addresses used to orient flow keys (local side → source).
    pub local_ips: Vec<IpAddr>,
}

impl AggregatorConfig {
    pub fn forensic(capture_id: impl Into<String>, started_at: PacketTimestamp) -> Self {
        Self {
            capture_id: capture_id.into(),
            started_at,
            mode: AggregatorMode::Forensic,
            resident_bounds: ResidentBounds::default(),
            local_ips: Vec::new(),
        }
    }

    pub fn resident(capture_id: impl Into<String>, started_at: PacketTimestamp) -> Self {
        Self {
            capture_id: capture_id.into(),
            started_at,
            mode: AggregatorMode::Resident,
            resident_bounds: ResidentBounds::default(),
            local_ips: Vec::new(),
        }
    }

    pub fn with_local_ips(mut self, local_ips: Vec<IpAddr>) -> Self {
        self.local_ips = local_ips;
        self
    }
}

pub struct FlowAggregator {
    capture_id: String,
    started_at: PacketTimestamp,
    mode: AggregatorMode,
    bounds: ResidentBounds,
    local_ips: Vec<IpAddr>,
    tick_sequence: u64,
    current_tick_second: Option<i64>,
    current_totals: AggregateTotals,
    global_totals: AggregateTotals,
    current_flows: HashMap<FlowKey, FlowCounters>,
    global_flows: HashMap<FlowKey, FlowCounters>,
    current_tcp_connections: HashMap<TcpConnectionKey, TcpConnectionStats>,
    global_tcp_connections: HashMap<TcpConnectionKey, TcpConnectionStats>,
    current_hosts: HashMap<IpAddr, HostCounters>,
    global_hosts: HashMap<IpAddr, HostCounters>,
    current_services: HashMap<ServiceKey, ServiceCounters>,
    global_services: HashMap<ServiceKey, ServiceCounters>,
    dropped_packets: u64,
    last_packet_timestamp: Option<PacketTimestamp>,
    saw_packet: bool,
}

impl FlowAggregator {
    pub fn new(config: AggregatorConfig) -> Self {
        Self {
            capture_id: config.capture_id,
            started_at: config.started_at,
            mode: config.mode,
            bounds: config.resident_bounds,
            local_ips: config.local_ips,
            tick_sequence: 0,
            current_tick_second: None,
            current_totals: AggregateTotals::default(),
            global_totals: AggregateTotals::default(),
            current_flows: HashMap::new(),
            global_flows: HashMap::new(),
            current_tcp_connections: HashMap::new(),
            global_tcp_connections: HashMap::new(),
            current_hosts: HashMap::new(),
            global_hosts: HashMap::new(),
            current_services: HashMap::new(),
            global_services: HashMap::new(),
            dropped_packets: 0,
            last_packet_timestamp: None,
            saw_packet: false,
        }
    }

    pub fn mode(&self) -> AggregatorMode {
        self.mode
    }

    pub fn global_flow_count(&self) -> usize {
        self.global_flows.len()
    }

    pub fn global_host_count(&self) -> usize {
        self.global_hosts.len()
    }

    pub fn global_tcp_count(&self) -> usize {
        self.global_tcp_connections.len()
    }

    pub fn global_service_count(&self) -> usize {
        self.global_services.len()
    }

    fn flow_cap(&self) -> Option<usize> {
        self.cap(self.bounds.max_flows)
    }

    fn host_cap(&self) -> Option<usize> {
        self.cap(self.bounds.max_hosts)
    }

    fn tcp_cap(&self) -> Option<usize> {
        self.cap(self.bounds.max_tcp_connections)
    }

    fn service_cap(&self) -> Option<usize> {
        self.cap(self.bounds.max_services)
    }

    fn cap(&self, limit: usize) -> Option<usize> {
        match self.mode {
            AggregatorMode::Forensic => None,
            AggregatorMode::Resident => Some(limit),
        }
    }

    fn summary_limit(&self) -> Option<usize> {
        match self.mode {
            AggregatorMode::Forensic => None,
            AggregatorMode::Resident => Some(self.bounds.summary_limit),
        }
    }

    fn build_aggregate_summary(&self) -> AggregateSummary {
        let limit = self.summary_limit();
        AggregateSummary {
            top_connections: sort_connections(&self.global_flows, limit),
            tcp_connections: sort_tcp_connections(&self.global_tcp_connections, limit),
            top_hosts: sort_hosts(&self.global_hosts, limit),
            top_services: sort_services(&self.global_services, limit),
        }
    }

    pub fn observe_packet_time(
        &mut self,
        timestamp: PacketTimestamp,
        dropped_packets: u64,
    ) -> Vec<TickSnapshot> {
        self.dropped_packets = dropped_packets;
        self.last_packet_timestamp = Some(timestamp);
        if !self.saw_packet {
            self.started_at = timestamp;
            self.saw_packet = true;
        }
        self.advance_to_second(timestamp.seconds)
    }

    pub fn observe_offline_packet_time(
        &mut self,
        timestamp: PacketTimestamp,
        dropped_packets: u64,
    ) -> OfflineTickAdvance {
        self.dropped_packets = dropped_packets;
        self.last_packet_timestamp = Some(timestamp);
        if !self.saw_packet {
            self.started_at = timestamp;
            self.saw_packet = true;
        }
        self.advance_to_second_compressed(timestamp.seconds)
    }

    pub fn observe_live_time(
        &mut self,
        timestamp: PacketTimestamp,
        dropped_packets: u64,
    ) -> Vec<TickSnapshot> {
        self.dropped_packets = dropped_packets;
        if self.current_tick_second.is_none() {
            return Vec::new();
        }
        self.advance_to_second(timestamp.seconds)
    }

    pub fn record_classified_packet(&mut self, packet: &ClassifiedPacket) {
        let timestamp =
            PacketTimestamp::new(packet.decoded.timestamp_sec, packet.decoded.timestamp_usec);
        // Local-oriented session key: reverse-path packets merge into one row.
        let flow_key = FlowKey::from_packet_oriented(packet, &self.local_ips);
        let flow_cap = self.flow_cap();
        let tcp_cap = self.tcp_cap();
        let packet_len = packet.decoded.packet_len;
        let direction = &packet.direction;

        update_tcp_tracker(&mut self.current_tcp_connections, packet, timestamp, None);
        update_tcp_tracker(&mut self.global_tcp_connections, packet, timestamp, tcp_cap);

        let sni = packet.decoded.sni.as_deref();
        upsert_flow(
            &mut self.current_flows,
            flow_key.clone(),
            None,
            timestamp,
            direction,
            packet_len,
            sni,
        );
        upsert_flow(
            &mut self.global_flows,
            flow_key.clone(),
            flow_cap,
            timestamp,
            direction,
            packet_len,
            sni,
        );

        if let Some(tcp_key) = tcp_key_for_packet(packet) {
            attach_tcp_stats(
                &mut self.current_flows,
                &self.current_tcp_connections,
                &flow_key,
                &tcp_key,
            );
            attach_tcp_stats(
                &mut self.global_flows,
                &self.global_tcp_connections,
                &flow_key,
                &tcp_key,
            );
        }

        self.record_hosts(packet, timestamp);
        self.record_service(packet);
    }

    pub fn record_observed_packet(&mut self, packet_timestamp: PacketTimestamp, packet_len: u32) {
        if self.current_tick_second.is_none() {
            self.current_tick_second = Some(packet_timestamp.seconds);
        }
        self.current_totals.record(packet_len);
        self.global_totals.record(packet_len);
    }

    pub fn finish(&mut self, ended_at: PacketTimestamp, dropped_packets: u64) -> AggregationResult {
        self.dropped_packets = dropped_packets;
        let mut tick_snapshots = Vec::new();
        if let Some(current_second) = self.current_tick_second.take() {
            tick_snapshots.push(self.build_tick_snapshot(current_second));
            self.reset_current_tick();
        }

        let final_snapshot = FinalSnapshot {
            capture_id: self.capture_id.clone(),
            started_at: self.started_at,
            ended_at,
            totals: self.global_totals.clone(),
            dropped_packets: self.dropped_packets,
            last_packet_timestamp: self.last_packet_timestamp,
            aggregate_summary: self.build_aggregate_summary(),
        };

        AggregationResult {
            tick_snapshots,
            final_snapshot,
        }
    }

    pub fn runtime_progress(
        &self,
        tick_snapshots: Vec<TickSnapshot>,
        ended_at: PacketTimestamp,
        dropped_packets: u64,
    ) -> AggregationResult {
        AggregationResult {
            tick_snapshots,
            final_snapshot: FinalSnapshot {
                capture_id: self.capture_id.clone(),
                started_at: self.started_at,
                ended_at,
                totals: self.global_totals.clone(),
                dropped_packets,
                last_packet_timestamp: self.last_packet_timestamp,
                aggregate_summary: self.build_aggregate_summary(),
            },
        }
    }

    pub fn last_packet_timestamp(&self) -> Option<&PacketTimestamp> {
        self.last_packet_timestamp.as_ref()
    }

    pub fn started_at(&self) -> PacketTimestamp {
        self.started_at
    }

    fn advance_to_second(&mut self, target_second: i64) -> Vec<TickSnapshot> {
        let Some(current_second) = self.current_tick_second else {
            self.current_tick_second = Some(target_second);
            return Vec::new();
        };

        if target_second <= current_second {
            return Vec::new();
        }

        let mut snapshots = Vec::new();
        snapshots.push(self.build_tick_snapshot(current_second));
        self.reset_current_tick();

        for gap_second in (current_second + 1)..target_second {
            self.current_tick_second = Some(gap_second);
            snapshots.push(self.build_tick_snapshot(gap_second));
            self.reset_current_tick();
        }

        self.current_tick_second = Some(target_second);
        snapshots
    }

    fn advance_to_second_compressed(&mut self, target_second: i64) -> OfflineTickAdvance {
        let Some(current_second) = self.current_tick_second else {
            self.current_tick_second = Some(target_second);
            return OfflineTickAdvance::default();
        };

        if target_second <= current_second {
            return OfflineTickAdvance::default();
        }

        let mut advance = OfflineTickAdvance::default();
        advance
            .tick_snapshots
            .push(self.build_tick_snapshot(current_second));
        self.reset_current_tick();

        let gap_seconds = target_second - current_second - 1;
        if gap_seconds > 0 {
            advance.gaps.push(OfflineGap {
                after: PacketTimestamp::tick(current_second),
                seconds: gap_seconds.min(i64::from(u32::MAX)) as u32,
            });
        }

        self.current_tick_second = Some(target_second);
        advance
    }

    fn build_tick_snapshot(&mut self, second: i64) -> TickSnapshot {
        self.tick_sequence += 1;
        // Per-tick maps are short-lived; keep full ranking for the second.
        TickSnapshot {
            capture_id: self.capture_id.clone(),
            sequence: self.tick_sequence,
            timestamp: PacketTimestamp::tick(second),
            totals: self.current_totals.clone(),
            dropped_packets: self.dropped_packets,
            last_packet_timestamp: self.last_packet_timestamp,
            top_connections: sort_connections(&self.current_flows, None),
            top_hosts: sort_hosts(&self.current_hosts, None),
            top_services: sort_services(&self.current_services, None),
        }
    }

    fn reset_current_tick(&mut self) {
        self.current_totals = AggregateTotals::default();
        self.current_flows.clear();
        self.current_tcp_connections.clear();
        self.current_hosts.clear();
        self.current_services.clear();
    }

    fn record_hosts(&mut self, packet: &ClassifiedPacket, timestamp: PacketTimestamp) {
        let packet_len = packet.decoded.packet_len;
        let src = packet.decoded.source_ip;
        let dst = packet.decoded.destination_ip;
        let host_cap = self.host_cap();
        // SNI names the remote server; attach to destination for outbound ClientHello.
        let sni_for_dst = packet.decoded.sni.as_deref();

        if src == dst {
            let apply = |host: &mut HostCounters| {
                host.record_outbound(timestamp, packet_len);
                host.record_inbound(timestamp, packet_len);
            };
            upsert_host(&mut self.current_hosts, src, None, timestamp, apply);
            upsert_host(&mut self.global_hosts, src, host_cap, timestamp, apply);
            return;
        }

        upsert_host(&mut self.current_hosts, src, None, timestamp, |host| {
            host.record_outbound(timestamp, packet_len)
        });
        upsert_host(&mut self.current_hosts, dst, None, timestamp, |host| {
            host.record_inbound(timestamp, packet_len);
            if host.sni.is_none()
                && let Some(sni) = sni_for_dst
            {
                host.sni = Some(sni.to_string());
            }
        });
        upsert_host(&mut self.global_hosts, src, host_cap, timestamp, |host| {
            host.record_outbound(timestamp, packet_len)
        });
        upsert_host(&mut self.global_hosts, dst, host_cap, timestamp, |host| {
            host.record_inbound(timestamp, packet_len);
            if host.sni.is_none()
                && let Some(sni) = sni_for_dst
            {
                host.sni = Some(sni.to_string());
            }
        });
    }

    fn record_service(&mut self, packet: &ClassifiedPacket) {
        let key = ServiceKey {
            name: packet.service.name.clone(),
            transport: packet.service.transport.clone(),
        };
        let packet_len = packet.decoded.packet_len;
        let confidence = &packet.service.confidence;
        let direction = &packet.direction;
        let service_cap = self.service_cap();

        upsert_service(
            &mut self.current_services,
            key.clone(),
            None,
            confidence,
            direction,
            packet_len,
        );
        upsert_service(
            &mut self.global_services,
            key,
            service_cap,
            confidence,
            direction,
            packet_len,
        );
    }
}

fn attach_tcp_stats(
    flows: &mut HashMap<FlowKey, FlowCounters>,
    tcp_connections: &HashMap<TcpConnectionKey, TcpConnectionStats>,
    flow_key: &FlowKey,
    tcp_key: &TcpConnectionKey,
) {
    let Some(stats) = tcp_connections.get(tcp_key).cloned() else {
        return;
    };
    if let Some(counters) = flows.get_mut(flow_key) {
        counters.tcp_stats = Some(stats);
    }
}

fn upsert_flow(
    map: &mut HashMap<FlowKey, FlowCounters>,
    key: FlowKey,
    cap: Option<usize>,
    timestamp: PacketTimestamp,
    direction: &TrafficDirection,
    packet_len: u32,
    sni: Option<&str>,
) {
    upsert_by_bytes(
        map,
        key,
        cap,
        |value| value.bytes,
        |counters| {
            counters.record(direction, timestamp, packet_len);
            if counters.sni.is_none()
                && let Some(sni) = sni
            {
                counters.sni = Some(sni.to_string());
            }
        },
        || {
            let mut counters = FlowCounters::new(timestamp);
            counters.record(direction, timestamp, packet_len);
            if let Some(sni) = sni {
                counters.sni = Some(sni.to_string());
            }
            counters
        },
    );
}

fn upsert_host(
    map: &mut HashMap<IpAddr, HostCounters>,
    ip: IpAddr,
    cap: Option<usize>,
    timestamp: PacketTimestamp,
    apply: impl Fn(&mut HostCounters),
) {
    upsert_by_bytes(
        map,
        ip,
        cap,
        |value| value.bytes,
        &apply,
        || {
            let mut host = HostCounters::new(timestamp);
            apply(&mut host);
            host
        },
    );
}

fn upsert_service(
    map: &mut HashMap<ServiceKey, ServiceCounters>,
    key: ServiceKey,
    cap: Option<usize>,
    confidence: &ServiceConfidence,
    direction: &TrafficDirection,
    packet_len: u32,
) {
    upsert_by_bytes(
        map,
        key,
        cap,
        |value| value.bytes,
        |counters| counters.record(direction, packet_len, confidence),
        || {
            let mut counters = ServiceCounters::new(confidence.clone());
            counters.record(direction, packet_len, confidence);
            counters
        },
    );
}

fn tcp_key_for_packet(packet: &ClassifiedPacket) -> Option<TcpConnectionKey> {
    if packet.decoded.transport_protocol != TransportProtocol::Tcp {
        return None;
    }

    let (Some(source_port), Some(destination_port)) =
        (packet.decoded.source_port, packet.decoded.destination_port)
    else {
        return None;
    };

    Some(TcpConnectionKey::new(
        packet.decoded.source_ip,
        source_port,
        packet.decoded.destination_ip,
        destination_port,
    ))
}

fn sort_connections(
    flows: &HashMap<FlowKey, FlowCounters>,
    limit: Option<usize>,
) -> Vec<ConnectionSummary> {
    let items = flows
        .iter()
        .map(|(key, counters)| ConnectionSummary {
            key: key.clone(),
            counters: counters.clone(),
        })
        .collect();
    rank_by_traffic(
        items,
        |row| row.counters.bytes,
        |row| row.counters.packets,
        |left, right| {
            left.key
                .source_ip
                .cmp(&right.key.source_ip)
                .then_with(|| left.key.destination_ip.cmp(&right.key.destination_ip))
                .then_with(|| left.key.source_port.cmp(&right.key.source_port))
                .then_with(|| left.key.destination_port.cmp(&right.key.destination_port))
                .then_with(|| left.key.protocol.cmp(&right.key.protocol))
        },
        limit,
    )
}

fn sort_hosts(hosts: &HashMap<IpAddr, HostCounters>, limit: Option<usize>) -> Vec<HostSummary> {
    let items = hosts
        .iter()
        .map(|(host, counters)| HostSummary {
            host: *host,
            counters: counters.clone(),
        })
        .collect();
    rank_by_traffic(
        items,
        |row| row.counters.bytes,
        |row| row.counters.packets,
        |left, right| left.host.cmp(&right.host),
        limit,
    )
}

fn sort_services(
    services: &HashMap<ServiceKey, ServiceCounters>,
    limit: Option<usize>,
) -> Vec<ServiceSummary> {
    let items = services
        .iter()
        .map(|(key, counters)| ServiceSummary {
            service: ServiceLabel {
                name: key.name.clone(),
                transport: key.transport.clone(),
                confidence: counters.confidence.clone(),
            },
            counters: counters.clone(),
        })
        .collect();
    rank_by_traffic(
        items,
        |row| row.counters.bytes,
        |row| row.counters.packets,
        |left, right| {
            left.service
                .name
                .cmp(&right.service.name)
                .then_with(|| left.service.transport.cmp(&right.service.transport))
        },
        limit,
    )
}

fn sort_tcp_connections(
    connections: &HashMap<TcpConnectionKey, TcpConnectionStats>,
    limit: Option<usize>,
) -> Vec<TcpConnectionSummary> {
    let items = connections
        .iter()
        .map(|(key, stats)| TcpConnectionSummary {
            key: *key,
            stats: stats.clone(),
        })
        .collect();
    rank_by_traffic(
        items,
        |row| row.stats.bytes,
        |row| row.stats.packets,
        |left, right| {
            left.key
                .endpoint_a
                .ip
                .cmp(&right.key.endpoint_a.ip)
                .then_with(|| left.key.endpoint_a.port.cmp(&right.key.endpoint_a.port))
                .then_with(|| left.key.endpoint_b.ip.cmp(&right.key.endpoint_b.ip))
                .then_with(|| left.key.endpoint_b.port.cmp(&right.key.endpoint_b.port))
        },
        limit,
    )
}

#[cfg(test)]
mod tests {
    use std::net::{IpAddr, Ipv4Addr};

    use super::*;

    struct PacketSpec {
        second: i64,
        source_ip: IpAddr,
        destination_ip: IpAddr,
        source_port: u16,
        destination_port: u16,
        packet_len: u32,
        direction: TrafficDirection,
        service_name: &'static str,
        transport: TransportProtocol,
    }

    fn aggregator() -> FlowAggregator {
        FlowAggregator::new(AggregatorConfig::forensic(
            "test-capture",
            PacketTimestamp::tick(0),
        ))
    }

    fn resident_aggregator(max_flows: usize) -> FlowAggregator {
        FlowAggregator::new(AggregatorConfig {
            capture_id: "resident-capture".to_string(),
            started_at: PacketTimestamp::tick(0),
            mode: AggregatorMode::Resident,
            resident_bounds: ResidentBounds {
                max_flows,
                max_hosts: max_flows,
                max_tcp_connections: max_flows,
                max_services: max_flows.max(8),
                summary_limit: max_flows,
            },
            local_ips: Vec::new(),
        })
    }

    fn aggregator_with_local(local: IpAddr) -> FlowAggregator {
        FlowAggregator::new(
            AggregatorConfig::forensic("test-capture", PacketTimestamp::tick(0))
                .with_local_ips(vec![local]),
        )
    }

    fn classified_packet(spec: PacketSpec) -> ClassifiedPacket {
        ClassifiedPacket {
            decoded: DecodedPacket {
                timestamp_sec: spec.second,
                timestamp_usec: 0,
                source_ip: spec.source_ip,
                destination_ip: spec.destination_ip,
                source_port: Some(spec.source_port),
                destination_port: Some(spec.destination_port),
                transport_protocol: spec.transport.clone(),
                tcp_flags: None,
                packet_len: spec.packet_len,
                payload_len: 0,
                sni: None,
                arp_operation: None,
            },
            direction: spec.direction,
            service: ServiceLabel {
                name: spec.service_name.to_string(),
                transport: spec.transport,
                confidence: ServiceConfidence::High,
            },
        }
    }

    #[test]
    fn offline_time_gap_emits_compressed_gap() {
        let mut aggregator = aggregator();

        let first = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
            source_port: 50000,
            destination_port: 443,
            packet_len: 100,
            direction: TrafficDirection::Outbound,
            service_name: "https",
            transport: TransportProtocol::Tcp,
        });
        let second = classified_packet(PacketSpec {
            second: 3,
            source_ip: IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
            source_port: 53,
            destination_port: 53000,
            packet_len: 80,
            direction: TrafficDirection::Inbound,
            service_name: "dns",
            transport: TransportProtocol::Udp,
        });

        let first_advance = aggregator.observe_offline_packet_time(PacketTimestamp::tick(1), 0);
        assert!(first_advance.tick_snapshots.is_empty());
        assert!(first_advance.gaps.is_empty());
        aggregator.record_observed_packet(PacketTimestamp::tick(1), first.decoded.packet_len);
        aggregator.record_classified_packet(&first);

        let flushed = aggregator.observe_offline_packet_time(PacketTimestamp::tick(3), 0);
        aggregator.record_observed_packet(PacketTimestamp::tick(3), second.decoded.packet_len);
        aggregator.record_classified_packet(&second);
        let result = aggregator.finish(PacketTimestamp::tick(3), 0);

        assert_eq!(flushed.tick_snapshots.len(), 1);
        assert_eq!(flushed.tick_snapshots[0].sequence, 1);
        assert_eq!(
            flushed.tick_snapshots[0].timestamp,
            PacketTimestamp::tick(1)
        );
        assert_eq!(flushed.tick_snapshots[0].totals.packets, 1);
        assert_eq!(
            flushed.gaps,
            vec![OfflineGap {
                after: PacketTimestamp::tick(1),
                seconds: 1,
            }]
        );
        assert_eq!(result.tick_snapshots.len(), 1);
        assert_eq!(result.tick_snapshots[0].sequence, 2);
        assert_eq!(result.tick_snapshots[0].timestamp, PacketTimestamp::tick(3));
        assert_eq!(result.tick_snapshots[0].totals.packets, 1);
    }

    #[test]
    fn same_input_produces_stable_output() {
        let packets = vec![
            classified_packet(PacketSpec {
                second: 1,
                source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
                destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
                source_port: 50000,
                destination_port: 443,
                packet_len: 100,
                direction: TrafficDirection::Outbound,
                service_name: "https",
                transport: TransportProtocol::Tcp,
            }),
            classified_packet(PacketSpec {
                second: 2,
                source_ip: IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
                destination_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
                source_port: 53,
                destination_port: 53000,
                packet_len: 80,
                direction: TrafficDirection::Inbound,
                service_name: "dns",
                transport: TransportProtocol::Udp,
            }),
        ];

        let run =
            || {
                let mut aggregator = aggregator();
                let mut emitted = Vec::new();
                for packet in &packets {
                    emitted.extend(aggregator.observe_packet_time(
                        PacketTimestamp::new(packet.decoded.timestamp_sec, 0),
                        0,
                    ));
                    aggregator.record_observed_packet(
                        PacketTimestamp::new(packet.decoded.timestamp_sec, 0),
                        packet.decoded.packet_len,
                    );
                    aggregator.record_classified_packet(packet);
                }
                let result = aggregator.finish(PacketTimestamp::tick(2), 0);
                emitted.extend(result.tick_snapshots.clone());
                (emitted, result.final_snapshot)
            };

        let first = run();
        let second = run();
        assert_eq!(first, second);
    }

    #[test]
    fn rankings_are_sorted_deterministically() {
        let mut aggregator = aggregator();
        let slow = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(9, 9, 9, 9)),
            source_port: 50000,
            destination_port: 443,
            packet_len: 60,
            direction: TrafficDirection::Outbound,
            service_name: "https",
            transport: TransportProtocol::Tcp,
        });
        let fast = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
            source_port: 50001,
            destination_port: 53,
            packet_len: 120,
            direction: TrafficDirection::Outbound,
            service_name: "dns",
            transport: TransportProtocol::Udp,
        });

        assert!(
            aggregator
                .observe_packet_time(PacketTimestamp::tick(1), 0)
                .is_empty()
        );
        aggregator.record_observed_packet(PacketTimestamp::tick(1), slow.decoded.packet_len);
        aggregator.record_classified_packet(&slow);
        aggregator.record_observed_packet(PacketTimestamp::tick(1), fast.decoded.packet_len);
        aggregator.record_classified_packet(&fast);
        let result = aggregator.finish(PacketTimestamp::tick(1), 0);

        assert_eq!(
            result.final_snapshot.aggregate_summary.top_connections[0]
                .counters
                .bytes,
            120
        );
        assert_eq!(
            result.final_snapshot.aggregate_summary.top_services[0]
                .service
                .name,
            "dns"
        );
        assert_eq!(
            result.final_snapshot.aggregate_summary.top_hosts[0].host,
            IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1))
        );
    }

    #[test]
    fn resident_mode_soft_caps_global_flows_and_keeps_heavier_entries() {
        let mut aggregator = resident_aggregator(2);
        assert_eq!(aggregator.mode(), AggregatorMode::Resident);

        let light_a = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
            source_port: 50000,
            destination_port: 443,
            packet_len: 10,
            direction: TrafficDirection::Outbound,
            service_name: "https",
            transport: TransportProtocol::Tcp,
        });
        let light_b = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 2)),
            source_port: 50001,
            destination_port: 443,
            packet_len: 20,
            direction: TrafficDirection::Outbound,
            service_name: "https",
            transport: TransportProtocol::Tcp,
        });
        let heavy = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 3)),
            source_port: 50002,
            destination_port: 443,
            packet_len: 100,
            direction: TrafficDirection::Outbound,
            service_name: "https",
            transport: TransportProtocol::Tcp,
        });
        let tiny = classified_packet(PacketSpec {
            second: 1,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 4)),
            source_port: 50003,
            destination_port: 443,
            packet_len: 5,
            direction: TrafficDirection::Outbound,
            service_name: "https",
            transport: TransportProtocol::Tcp,
        });

        assert!(
            aggregator
                .observe_packet_time(PacketTimestamp::tick(1), 0)
                .is_empty()
        );
        for packet in [&light_a, &light_b, &heavy, &tiny] {
            aggregator.record_observed_packet(PacketTimestamp::tick(1), packet.decoded.packet_len);
            aggregator.record_classified_packet(packet);
        }

        assert_eq!(aggregator.global_flow_count(), 2);
        let result = aggregator.finish(PacketTimestamp::tick(1), 0);
        // Totals remain exact even when flow maps are capped.
        assert_eq!(result.final_snapshot.totals.packets, 4);
        assert_eq!(result.final_snapshot.totals.bytes, 135);

        let top_bytes: Vec<u64> = result
            .final_snapshot
            .aggregate_summary
            .top_connections
            .iter()
            .map(|row| row.counters.bytes)
            .collect();
        assert!(top_bytes.contains(&100));
        assert!(top_bytes.contains(&20));
        assert!(!top_bytes.contains(&10));
        assert!(!top_bytes.contains(&5));
    }

    #[test]
    fn forensic_mode_keeps_all_flows() {
        let mut aggregator = aggregator();
        for index in 0..5 {
            let packet = classified_packet(PacketSpec {
                second: 1,
                source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
                destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, index + 1)),
                source_port: 50000 + u16::from(index),
                destination_port: 443,
                packet_len: 10 + u32::from(index),
                direction: TrafficDirection::Outbound,
                service_name: "https",
                transport: TransportProtocol::Tcp,
            });
            assert!(
                aggregator
                    .observe_packet_time(PacketTimestamp::tick(1), 0)
                    .is_empty()
            );
            aggregator.record_observed_packet(PacketTimestamp::tick(1), packet.decoded.packet_len);
            aggregator.record_classified_packet(&packet);
        }
        assert_eq!(aggregator.global_flow_count(), 5);
        let result = aggregator.finish(PacketTimestamp::tick(1), 0);
        assert_eq!(
            result
                .final_snapshot
                .aggregate_summary
                .top_connections
                .len(),
            5
        );
    }

    #[test]
    fn reverse_path_packets_merge_with_local_oriented_key() {
        let local = IpAddr::V4(Ipv4Addr::new(10, 77, 4, 25));
        let remote = IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8));
        let mut aggregator = aggregator_with_local(local);

        let outbound = classified_packet(PacketSpec {
            second: 1,
            source_ip: local,
            destination_ip: remote,
            source_port: 50_123,
            destination_port: 53,
            packet_len: 60,
            direction: TrafficDirection::Outbound,
            service_name: "dns",
            transport: TransportProtocol::Udp,
        });
        let inbound = classified_packet(PacketSpec {
            second: 1,
            source_ip: remote,
            destination_ip: local,
            source_port: 53,
            destination_port: 50_123,
            packet_len: 120,
            direction: TrafficDirection::Inbound,
            service_name: "dns",
            transport: TransportProtocol::Udp,
        });

        assert!(
            aggregator
                .observe_packet_time(PacketTimestamp::tick(1), 0)
                .is_empty()
        );
        aggregator.record_observed_packet(PacketTimestamp::tick(1), outbound.decoded.packet_len);
        aggregator.record_classified_packet(&outbound);
        aggregator.record_observed_packet(PacketTimestamp::tick(1), inbound.decoded.packet_len);
        aggregator.record_classified_packet(&inbound);

        assert_eq!(aggregator.global_flow_count(), 1);
        let result = aggregator.finish(PacketTimestamp::tick(1), 0);
        let connections = &result.final_snapshot.aggregate_summary.top_connections;
        assert_eq!(connections.len(), 1);
        let row = &connections[0];
        assert_eq!(row.key.source_ip, local);
        assert_eq!(row.key.destination_ip, remote);
        assert_eq!(row.key.source_port, Some(50_123));
        assert_eq!(row.key.destination_port, Some(53));
        assert_eq!(row.counters.packets, 2);
        assert_eq!(row.counters.bytes, 180);
        assert_eq!(row.counters.bytes_out, 60);
        assert_eq!(row.counters.bytes_in, 120);
    }

    #[test]
    fn offline_private_side_orients_without_explicit_local_ips() {
        let private = IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10));
        let public = IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8));
        let mut aggregator = aggregator();

        let reply = classified_packet(PacketSpec {
            second: 1,
            source_ip: public,
            destination_ip: private,
            source_port: 53,
            destination_port: 53_000,
            packet_len: 80,
            direction: TrafficDirection::Inbound,
            service_name: "dns",
            transport: TransportProtocol::Udp,
        });
        assert!(
            aggregator
                .observe_packet_time(PacketTimestamp::tick(1), 0)
                .is_empty()
        );
        aggregator.record_observed_packet(PacketTimestamp::tick(1), reply.decoded.packet_len);
        aggregator.record_classified_packet(&reply);
        let result = aggregator.finish(PacketTimestamp::tick(1), 0);
        let row = &result.final_snapshot.aggregate_summary.top_connections[0];
        assert_eq!(row.key.source_ip, private);
        assert_eq!(row.key.destination_ip, public);
        assert_eq!(row.key.source_port, Some(53_000));
        assert_eq!(row.key.destination_port, Some(53));
    }
}
