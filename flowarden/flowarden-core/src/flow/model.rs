use std::{
    net::IpAddr,
    time::{SystemTime, UNIX_EPOCH},
};

use serde::Serialize;

use crate::prelude::*;

#[derive(Clone, Copy, Debug, Serialize, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub struct PacketTimestamp {
    pub seconds: i64,
    pub microseconds: u32,
}

impl PacketTimestamp {
    pub fn new(seconds: i64, microseconds: i64) -> Self {
        Self {
            seconds,
            microseconds: microseconds.max(0) as u32,
        }
    }

    pub fn tick(second: i64) -> Self {
        Self {
            seconds: second,
            microseconds: 0,
        }
    }

    pub fn now() -> Result<Self> {
        let duration = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .or_err(
                ErrorType::InternalError,
                "System clock is before UNIX_EPOCH while creating packet timestamp",
            )
            .map_err(|e| e.into_in())?;
        Ok(Self {
            seconds: duration.as_secs() as i64,
            microseconds: duration.subsec_micros(),
        })
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq, Hash)]
pub struct FlowKey {
    pub source_ip: IpAddr,
    pub destination_ip: IpAddr,
    pub source_port: Option<u16>,
    pub destination_port: Option<u16>,
    pub protocol: TransportProtocol,
}

impl FlowKey {
    /// Wire-order key from a packet (packet source → packet destination).
    pub fn from_wire(packet: &ClassifiedPacket) -> Self {
        Self {
            source_ip: packet.decoded.source_ip,
            destination_ip: packet.decoded.destination_ip,
            source_port: packet.decoded.source_port,
            destination_port: packet.decoded.destination_port,
            protocol: packet.decoded.transport_protocol.clone(),
        }
    }

    /// Session-oriented key: prefer local endpoint as `source_*` so reverse-path
    /// packets merge into one flow. When local membership is unknown, private /
    /// link-local / loopback is treated as the local side (offline fallback).
    /// When neither side is local, endpoints are ordered canonically so A↔B still merges.
    pub fn from_packet_oriented(packet: &ClassifiedPacket, local_ips: &[IpAddr]) -> Self {
        Self::from_wire(packet).oriented(local_ips)
    }

    pub fn swapped(self) -> Self {
        Self {
            source_ip: self.destination_ip,
            destination_ip: self.source_ip,
            source_port: self.destination_port,
            destination_port: self.source_port,
            protocol: self.protocol,
        }
    }

    pub fn oriented(self, local_ips: &[IpAddr]) -> Self {
        let src_local = endpoint_is_local_side(self.source_ip, local_ips);
        let dst_local = endpoint_is_local_side(self.destination_ip, local_ips);
        match (src_local, dst_local) {
            (true, false) => self,
            (false, true) => self.swapped(),
            // Both local (loopback/LAN) or neither: stable order merges reverse pairs.
            _ if should_swap_for_canonical(&self) => self.swapped(),
            _ => self,
        }
    }
}

impl From<&ClassifiedPacket> for FlowKey {
    fn from(packet: &ClassifiedPacket) -> Self {
        // Default remains wire order for call sites that do not pass local IPs.
        Self::from_wire(packet)
    }
}

fn endpoint_is_local_side(ip: IpAddr, local_ips: &[IpAddr]) -> bool {
    if !local_ips.is_empty() {
        return local_ips.contains(&ip);
    }
    match ip {
        IpAddr::V4(v4) => v4.is_private() || v4.is_loopback() || v4.is_link_local(),
        IpAddr::V6(v6) => v6.is_loopback() || v6.is_unique_local() || v6.is_unicast_link_local(),
    }
}

fn should_swap_for_canonical(key: &FlowKey) -> bool {
    use std::cmp::Ordering;
    match key.source_ip.cmp(&key.destination_ip) {
        Ordering::Greater => true,
        Ordering::Less => false,
        Ordering::Equal => key.source_port.cmp(&key.destination_port) == Ordering::Greater,
    }
}

#[derive(Clone, Debug, Default, Serialize, PartialEq, Eq)]
pub struct AggregateTotals {
    pub packets: u64,
    pub bytes: u64,
}

impl AggregateTotals {
    pub(crate) fn record(&mut self, packet_len: u32) {
        self.packets += 1;
        self.bytes += u64::from(packet_len);
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct FlowCounters {
    pub packets: u64,
    pub bytes: u64,
    pub packets_in: u64,
    pub packets_out: u64,
    pub bytes_in: u64,
    pub bytes_out: u64,
    pub first_seen: PacketTimestamp,
    pub last_seen: PacketTimestamp,
    pub tcp_stats: Option<TcpConnectionStats>,
    /// First observed TLS SNI on this flow (if any).
    pub sni: Option<String>,
}

impl FlowCounters {
    pub(crate) fn new(timestamp: PacketTimestamp) -> Self {
        Self {
            packets: 0,
            bytes: 0,
            packets_in: 0,
            packets_out: 0,
            bytes_in: 0,
            bytes_out: 0,
            first_seen: timestamp,
            last_seen: timestamp,
            tcp_stats: None,
            sni: None,
        }
    }

    pub(crate) fn record(
        &mut self,
        direction: &TrafficDirection,
        timestamp: PacketTimestamp,
        packet_len: u32,
    ) {
        self.packets += 1;
        self.bytes += u64::from(packet_len);
        self.last_seen = timestamp;
        let len = u64::from(packet_len);
        match direction {
            TrafficDirection::Inbound => {
                self.packets_in += 1;
                self.bytes_in += len;
            }
            TrafficDirection::Outbound => {
                self.packets_out += 1;
                self.bytes_out += len;
            }
            TrafficDirection::Local | TrafficDirection::Unknown => {}
        }
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct HostCounters {
    pub packets: u64,
    pub bytes: u64,
    pub packets_in: u64,
    pub packets_out: u64,
    pub bytes_in: u64,
    pub bytes_out: u64,
    pub first_seen: PacketTimestamp,
    pub last_seen: PacketTimestamp,
    /// Best-effort application hostname (TLS SNI) associated with this host IP.
    pub sni: Option<String>,
}

impl HostCounters {
    pub(crate) fn new(timestamp: PacketTimestamp) -> Self {
        Self {
            packets: 0,
            bytes: 0,
            packets_in: 0,
            packets_out: 0,
            bytes_in: 0,
            bytes_out: 0,
            first_seen: timestamp,
            last_seen: timestamp,
            sni: None,
        }
    }

    pub(crate) fn record_inbound(&mut self, timestamp: PacketTimestamp, packet_len: u32) {
        self.packets += 1;
        self.bytes += u64::from(packet_len);
        self.packets_in += 1;
        self.bytes_in += u64::from(packet_len);
        self.last_seen = timestamp;
    }

    pub(crate) fn record_outbound(&mut self, timestamp: PacketTimestamp, packet_len: u32) {
        self.packets += 1;
        self.bytes += u64::from(packet_len);
        self.packets_out += 1;
        self.bytes_out += u64::from(packet_len);
        self.last_seen = timestamp;
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct ServiceCounters {
    pub packets: u64,
    pub bytes: u64,
    pub packets_in: u64,
    pub packets_out: u64,
    pub bytes_in: u64,
    pub bytes_out: u64,
    pub confidence: ServiceConfidence,
}

impl ServiceCounters {
    pub(crate) fn new(confidence: ServiceConfidence) -> Self {
        Self {
            packets: 0,
            bytes: 0,
            packets_in: 0,
            packets_out: 0,
            bytes_in: 0,
            bytes_out: 0,
            confidence,
        }
    }

    pub(crate) fn record(
        &mut self,
        direction: &TrafficDirection,
        packet_len: u32,
        confidence: &ServiceConfidence,
    ) {
        self.packets += 1;
        self.bytes += u64::from(packet_len);
        self.confidence = max_confidence(&self.confidence, confidence);
        let len = u64::from(packet_len);
        match direction {
            TrafficDirection::Inbound => {
                self.packets_in += 1;
                self.bytes_in += len;
            }
            TrafficDirection::Outbound => {
                self.packets_out += 1;
                self.bytes_out += len;
            }
            TrafficDirection::Local | TrafficDirection::Unknown => {}
        }
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq, Hash)]
pub(crate) struct ServiceKey {
    pub(crate) name: String,
    pub(crate) transport: TransportProtocol,
}

fn max_confidence(left: &ServiceConfidence, right: &ServiceConfidence) -> ServiceConfidence {
    match (left, right) {
        (ServiceConfidence::High, _) | (_, ServiceConfidence::High) => ServiceConfidence::High,
        (ServiceConfidence::Medium, _) | (_, ServiceConfidence::Medium) => {
            ServiceConfidence::Medium
        }
        _ => ServiceConfidence::Low,
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct ConnectionSummary {
    pub key: FlowKey,
    pub counters: FlowCounters,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct TcpConnectionSummary {
    pub key: TcpConnectionKey,
    pub stats: TcpConnectionStats,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct HostSummary {
    pub host: IpAddr,
    pub counters: HostCounters,
}

impl HostSummary {
    pub fn sni(&self) -> Option<&str> {
        self.counters.sni.as_deref()
    }
}

impl ConnectionSummary {
    pub fn sni(&self) -> Option<&str> {
        self.counters.sni.as_deref()
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct ServiceSummary {
    pub service: ServiceLabel,
    pub counters: ServiceCounters,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct TickSnapshot {
    pub capture_id: String,
    pub sequence: u64,
    pub timestamp: PacketTimestamp,
    pub totals: AggregateTotals,
    pub dropped_packets: u64,
    pub last_packet_timestamp: Option<PacketTimestamp>,
    pub top_connections: Vec<ConnectionSummary>,
    pub top_hosts: Vec<HostSummary>,
    pub top_services: Vec<ServiceSummary>,
}

#[derive(Clone, Copy, Debug, Serialize, PartialEq, Eq)]
pub struct OfflineGap {
    pub after: PacketTimestamp,
    pub seconds: u32,
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct OfflineTickAdvance {
    pub tick_snapshots: Vec<TickSnapshot>,
    pub gaps: Vec<OfflineGap>,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct AggregateSummary {
    pub top_connections: Vec<ConnectionSummary>,
    pub tcp_connections: Vec<TcpConnectionSummary>,
    pub top_hosts: Vec<HostSummary>,
    pub top_services: Vec<ServiceSummary>,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct FinalSnapshot {
    pub capture_id: String,
    pub started_at: PacketTimestamp,
    pub ended_at: PacketTimestamp,
    pub totals: AggregateTotals,
    pub dropped_packets: u64,
    pub last_packet_timestamp: Option<PacketTimestamp>,
    pub aggregate_summary: AggregateSummary,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AggregationResult {
    pub tick_snapshots: Vec<TickSnapshot>,
    pub final_snapshot: FinalSnapshot,
}
