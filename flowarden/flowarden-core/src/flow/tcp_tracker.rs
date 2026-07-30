use std::{cmp::Ordering, collections::HashMap, net::IpAddr};

use serde::Serialize;

use crate::prelude::*;

const TCP_FLAG_FIN: u8 = 0x01;
const TCP_FLAG_SYN: u8 = 0x02;
const TCP_FLAG_RST: u8 = 0x04;
const TCP_FLAG_ACK: u8 = 0x10;

#[derive(Clone, Debug, Serialize, PartialEq, Eq, PartialOrd, Ord)]
pub enum TcpConnectionState {
    Init,
    SynAck,
    Established,
    Fin,
    Rst,
}

#[derive(Clone, Copy, Debug, Serialize, PartialEq, Eq, Hash)]
pub struct EndpointId {
    pub ip: IpAddr,
    pub port: u16,
}

impl EndpointId {
    pub fn new(ip: IpAddr, port: u16) -> Self {
        Self { ip, port }
    }
}

impl Ord for EndpointId {
    fn cmp(&self, other: &Self) -> Ordering {
        self.ip
            .to_string()
            .cmp(&other.ip.to_string())
            .then_with(|| self.port.cmp(&other.port))
    }
}

impl PartialOrd for EndpointId {
    fn partial_cmp(&self, other: &Self) -> Option<Ordering> {
        Some(self.cmp(other))
    }
}

#[derive(Clone, Copy, Debug, Serialize, PartialEq, Eq, Hash)]
pub struct TcpConnectionKey {
    pub endpoint_a: EndpointId,
    pub endpoint_b: EndpointId,
}

impl TcpConnectionKey {
    pub fn new(
        source_ip: IpAddr,
        source_port: u16,
        destination_ip: IpAddr,
        destination_port: u16,
    ) -> Self {
        let source = EndpointId::new(source_ip, source_port);
        let destination = EndpointId::new(destination_ip, destination_port);
        if source <= destination {
            Self {
                endpoint_a: source,
                endpoint_b: destination,
            }
        } else {
            Self {
                endpoint_a: destination,
                endpoint_b: source,
            }
        }
    }
}

#[derive(Clone, Debug, Serialize, PartialEq, Eq)]
pub struct TcpConnectionStats {
    pub syn_count: u64,
    pub fin_count: u64,
    pub rst_count: u64,
    pub packets: u64,
    pub bytes: u64,
    /// Bytes attributed to transport payload (payload_len > 0).
    pub payload_bytes: u64,
    pub state: TcpConnectionState,
    pub initiator: Option<EndpointId>,
    pub first_seen: PacketTimestamp,
    pub last_seen: PacketTimestamp,
    /// Last time a packet with non-empty transport payload was observed.
    pub last_payload_seen: Option<PacketTimestamp>,
}

impl TcpConnectionStats {
    pub fn new(timestamp: PacketTimestamp) -> Self {
        Self {
            syn_count: 0,
            fin_count: 0,
            rst_count: 0,
            packets: 0,
            bytes: 0,
            payload_bytes: 0,
            state: TcpConnectionState::Established,
            initiator: None,
            first_seen: timestamp,
            last_seen: timestamp,
            last_payload_seen: None,
        }
    }

    pub fn observe_packet(
        &mut self,
        source: EndpointId,
        timestamp: PacketTimestamp,
        flags: u8,
        packet_len: u32,
        payload_len: u32,
    ) {
        self.last_seen = timestamp;
        self.packets += 1;
        self.bytes += u64::from(packet_len);
        if payload_len > 0 {
            self.payload_bytes += u64::from(payload_len);
            self.last_payload_seen = Some(timestamp);
        }

        let has_syn = flags & TCP_FLAG_SYN != 0;
        let has_ack = flags & TCP_FLAG_ACK != 0;
        let has_fin = flags & TCP_FLAG_FIN != 0;
        let has_rst = flags & TCP_FLAG_RST != 0;

        if has_syn {
            self.syn_count += 1;
        }
        if has_fin {
            self.fin_count += 1;
        }
        if has_rst {
            self.rst_count += 1;
        }

        if has_rst {
            self.state = TcpConnectionState::Rst;
            return;
        }

        if has_fin {
            self.state = TcpConnectionState::Fin;
            return;
        }

        if has_syn && !has_ack {
            self.initiator = Some(source);
            if self.state != TcpConnectionState::Rst && self.state != TcpConnectionState::Fin {
                self.state = TcpConnectionState::Init;
            }
            return;
        }

        if has_syn && has_ack {
            self.state = TcpConnectionState::SynAck;
            return;
        }

        self.state = TcpConnectionState::Established;
    }
}

pub fn update_tcp_tracker(
    tracker: &mut HashMap<TcpConnectionKey, TcpConnectionStats>,
    packet: &ClassifiedPacket,
    timestamp: PacketTimestamp,
    cap: Option<usize>,
) {
    let Some(flags) = packet.decoded.tcp_flags else {
        return;
    };
    if packet.decoded.transport_protocol != TransportProtocol::Tcp {
        return;
    }

    let (Some(source_port), Some(destination_port)) =
        (packet.decoded.source_port, packet.decoded.destination_port)
    else {
        return;
    };

    let key = TcpConnectionKey::new(
        packet.decoded.source_ip,
        source_port,
        packet.decoded.destination_ip,
        destination_port,
    );
    let source = EndpointId::new(packet.decoded.source_ip, source_port);
    let packet_len = packet.decoded.packet_len;
    let payload_len = packet.decoded.payload_len;

    crate::flow::upsert_by_bytes(
        tracker,
        key,
        cap,
        |stats| stats.bytes,
        |stats| stats.observe_packet(source, timestamp, flags, packet_len, payload_len),
        || {
            let mut stats = TcpConnectionStats::new(timestamp);
            stats.observe_packet(source, timestamp, flags, packet_len, payload_len);
            stats
        },
    );
}

#[cfg(test)]
mod tests {
    use std::net::{IpAddr, Ipv4Addr};

    use super::*;

    fn endpoint(ip: [u8; 4], port: u16) -> EndpointId {
        EndpointId::new(IpAddr::V4(Ipv4Addr::from(ip)), port)
    }

    #[test]
    fn key_normalizes_bidirectional_endpoints() {
        let forward = TcpConnectionKey::new(
            IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            50000,
            IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
            443,
        );
        let reverse = TcpConnectionKey::new(
            IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
            443,
            IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            50000,
        );

        assert_eq!(forward, reverse);
    }

    #[test]
    fn state_machine_tracks_handshake_and_terminal_flags() {
        let mut stats = TcpConnectionStats::new(PacketTimestamp::tick(1));
        let initiator = endpoint([10, 0, 0, 1], 50000);

        stats.observe_packet(initiator, PacketTimestamp::tick(1), TCP_FLAG_SYN, 60, 0);
        assert_eq!(stats.syn_count, 1);
        assert_eq!(stats.packets, 1);
        assert_eq!(stats.bytes, 60);
        assert_eq!(stats.state, TcpConnectionState::Init);
        assert_eq!(stats.initiator, Some(initiator));
        assert!(stats.last_payload_seen.is_none());

        stats.observe_packet(
            endpoint([1, 1, 1, 1], 443),
            PacketTimestamp::tick(2),
            TCP_FLAG_SYN | TCP_FLAG_ACK,
            60,
            0,
        );
        assert_eq!(stats.syn_count, 2);
        assert_eq!(stats.packets, 2);
        assert_eq!(stats.bytes, 120);
        assert_eq!(stats.state, TcpConnectionState::SynAck);

        stats.observe_packet(initiator, PacketTimestamp::tick(3), TCP_FLAG_ACK, 52, 0);
        assert_eq!(stats.state, TcpConnectionState::Established);

        stats.observe_packet(
            initiator,
            PacketTimestamp::tick(4),
            TCP_FLAG_ACK,
            200,
            148,
        );
        assert_eq!(stats.payload_bytes, 148);
        assert_eq!(stats.last_payload_seen.map(|t| t.seconds), Some(4));

        stats.observe_packet(
            initiator,
            PacketTimestamp::tick(5),
            TCP_FLAG_FIN | TCP_FLAG_ACK,
            52,
            0,
        );
        assert_eq!(stats.fin_count, 1);
        assert_eq!(stats.state, TcpConnectionState::Fin);

        stats.observe_packet(
            endpoint([1, 1, 1, 1], 443),
            PacketTimestamp::tick(6),
            TCP_FLAG_RST | TCP_FLAG_ACK,
            52,
            0,
        );
        assert_eq!(stats.rst_count, 1);
        assert_eq!(stats.state, TcpConnectionState::Rst);
    }

    #[test]
    fn midstream_ack_defaults_to_established() {
        let mut stats = TcpConnectionStats::new(PacketTimestamp::tick(10));
        stats.observe_packet(
            endpoint([10, 0, 0, 1], 50000),
            PacketTimestamp::tick(10),
            TCP_FLAG_ACK,
            128,
            76,
        );
        assert_eq!(stats.state, TcpConnectionState::Established);
        assert_eq!(stats.packets, 1);
        assert_eq!(stats.bytes, 128);
        assert_eq!(stats.payload_bytes, 76);
        assert!(stats.last_payload_seen.is_some());
    }
}
