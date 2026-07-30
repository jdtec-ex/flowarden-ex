use std::net::IpAddr;

use crate::prelude::*;

#[derive(Clone, Debug, Default)]
pub struct DirectionContext {
    local_ips: Vec<IpAddr>,
    offline_fallback: bool,
}

impl DirectionContext {
    pub fn new(local_ips: Vec<IpAddr>, offline_fallback: bool) -> Self {
        Self {
            local_ips,
            offline_fallback,
        }
    }

    pub fn from_source(source: &CaptureSource) -> Self {
        match source {
            CaptureSource::Device(device) => {
                let local_ips = device
                    .get_addresses()
                    .iter()
                    .map(|address| address.addr)
                    .collect();
                Self::new(local_ips, false)
            }
            CaptureSource::File(_) => Self::new(Vec::new(), true),
        }
    }

    pub fn local_ips(&self) -> &[IpAddr] {
        &self.local_ips
    }
}

fn is_unspecified(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(ipv4) => ipv4.is_unspecified(),
        IpAddr::V6(ipv6) => ipv6.is_unspecified(),
    }
}

fn is_loopback(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(ipv4) => ipv4.is_loopback(),
        IpAddr::V6(ipv6) => ipv6.is_loopback(),
    }
}

pub fn classify_direction(packet: &DecodedPacket, context: &DirectionContext) -> TrafficDirection {
    let src = packet.source_ip;
    let dst = packet.destination_ip;

    if is_loopback(src) && is_loopback(dst) {
        return TrafficDirection::Local;
    }

    if is_unspecified(src) || is_unspecified(dst) {
        return TrafficDirection::Unknown;
    }

    let src_is_local = context.local_ips.contains(&src);
    let dst_is_local = context.local_ips.contains(&dst);

    match (src_is_local, dst_is_local) {
        (true, true) => TrafficDirection::Local,
        (true, false) => TrafficDirection::Outbound,
        (false, true) => TrafficDirection::Inbound,
        (false, false) => {
            if context.offline_fallback {
                if is_private_or_local(src) && !is_private_or_local(dst) {
                    TrafficDirection::Outbound
                } else if !is_private_or_local(src) && is_private_or_local(dst) {
                    TrafficDirection::Inbound
                } else {
                    TrafficDirection::Unknown
                }
            } else {
                TrafficDirection::Unknown
            }
        }
    }
}

fn is_private_or_local(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(ipv4) => ipv4.is_private() || ipv4.is_loopback() || ipv4.is_link_local(),
        IpAddr::V6(ipv6) => {
            ipv6.is_loopback() || ipv6.is_unique_local() || ipv6.is_unicast_link_local()
        }
    }
}

#[cfg(test)]
mod tests {
    use std::net::{IpAddr, Ipv4Addr, Ipv6Addr};

    use super::*;

    fn decoded_packet(src: IpAddr, dst: IpAddr) -> DecodedPacket {
        DecodedPacket {
            timestamp_sec: 0,
            timestamp_usec: 0,
            source_ip: src,
            destination_ip: dst,
            source_port: Some(1),
            destination_port: Some(2),
            transport_protocol: TransportProtocol::Tcp,
            tcp_flags: Some(0x10),
            packet_len: 60,
            payload_len: 0,
            sni: None,
            arp_operation: None,
        }
    }

    #[test]
    fn marks_loopback_pair_as_local() {
        let packet = decoded_packet(
            IpAddr::V4(Ipv4Addr::LOCALHOST),
            IpAddr::V4(Ipv4Addr::LOCALHOST),
        );
        let direction = classify_direction(&packet, &DirectionContext::default());
        assert_eq!(direction, TrafficDirection::Local);
    }

    #[test]
    fn marks_unspecified_as_unknown() {
        let packet = decoded_packet(
            IpAddr::V4(Ipv4Addr::UNSPECIFIED),
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
        );
        let direction = classify_direction(&packet, &DirectionContext::default());
        assert_eq!(direction, TrafficDirection::Unknown);
    }

    #[test]
    fn marks_live_local_to_remote_as_outbound() {
        let packet = decoded_packet(
            IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
        );
        let context =
            DirectionContext::new(vec![IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10))], false);
        let direction = classify_direction(&packet, &context);
        assert_eq!(direction, TrafficDirection::Outbound);
    }

    #[test]
    fn marks_live_remote_to_local_as_inbound() {
        let packet = decoded_packet(
            IpAddr::V6(Ipv6Addr::new(0x2606, 0x4700, 0, 0, 0, 0, 0, 2)),
            IpAddr::V6(Ipv6Addr::new(0xfd00, 0, 0, 0, 0, 0, 0, 1)),
        );
        let context = DirectionContext::new(
            vec![IpAddr::V6(Ipv6Addr::new(0xfd00, 0, 0, 0, 0, 0, 0, 1))],
            false,
        );
        let direction = classify_direction(&packet, &context);
        assert_eq!(direction, TrafficDirection::Inbound);
    }

    #[test]
    fn uses_offline_private_to_public_fallback() {
        let packet = decoded_packet(
            IpAddr::V4(Ipv4Addr::new(10, 0, 0, 8)),
            IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
        );
        let direction = classify_direction(&packet, &DirectionContext::new(Vec::new(), true));
        assert_eq!(direction, TrafficDirection::Outbound);
    }

    #[test]
    fn uses_offline_public_to_private_fallback() {
        let packet = decoded_packet(
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
            IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
        );
        let direction = classify_direction(&packet, &DirectionContext::new(Vec::new(), true));
        assert_eq!(direction, TrafficDirection::Inbound);
    }

    #[test]
    fn offline_public_to_public_stays_unknown() {
        let packet = decoded_packet(
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
            IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
        );
        let direction = classify_direction(&packet, &DirectionContext::new(Vec::new(), true));
        assert_eq!(direction, TrafficDirection::Unknown);
    }
}
