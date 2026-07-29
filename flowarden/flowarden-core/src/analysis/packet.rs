use std::net::IpAddr;

use serde::Serialize;
use std::hash::Hash;

use crate::prelude::*;

#[derive(Clone, Debug)]
pub struct PacketEnvelope {
    pub timestamp_sec: i64,
    pub timestamp_usec: i64,
    pub captured_len: u32,
    pub original_len: u32,
    pub link_type: LinkTypeEx,
    pub data: Box<[u8]>,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq, PartialOrd, Ord, Hash)]
pub enum TransportProtocol {
    Tcp,
    Udp,
    Icmp,
    Icmpv6,
    /// Address Resolution Protocol (Ethernet/IPv4 common case).
    Arp,
    Other(u8),
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub enum TrafficDirection {
    Inbound,
    Outbound,
    Local,
    Unknown,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub enum ServiceConfidence {
    High,
    Medium,
    Low,
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct ServiceLabel {
    pub name: String,
    pub transport: TransportProtocol,
    pub confidence: ServiceConfidence,
}

#[derive(Clone, Debug, Serialize)]
pub struct DecodedPacket {
    pub timestamp_sec: i64,
    pub timestamp_usec: i64,
    pub source_ip: IpAddr,
    pub destination_ip: IpAddr,
    pub source_port: Option<u16>,
    pub destination_port: Option<u16>,
    pub transport_protocol: TransportProtocol,
    pub tcp_flags: Option<u8>,
    pub packet_len: u32,
    /// TLS ClientHello SNI when extracted from this packet's TCP payload.
    pub sni: Option<String>,
    /// ARP operation code when `transport_protocol == Arp` (1=request, 2=reply).
    pub arp_operation: Option<u16>,
}

#[derive(Clone, Debug, Serialize)]
pub struct ClassifiedPacket {
    pub decoded: DecodedPacket,
    pub direction: TrafficDirection,
    pub service: ServiceLabel,
}

impl PacketEnvelope {
    pub fn new(
        timestamp_sec: i64,
        timestamp_usec: i64,
        captured_len: u32,
        original_len: u32,
        link_type: LinkTypeEx,
        data: Box<[u8]>,
    ) -> Self {
        Self {
            timestamp_sec,
            timestamp_usec,
            captured_len,
            original_len,
            link_type,
            data,
        }
    }
}

pub fn packet_decode_error<T, C: Into<String>>(context: C) -> Result<T> {
    Error::explain(ErrorType::PacketDecodeError, context.into())
        .into_network()
        .into_err()
}
