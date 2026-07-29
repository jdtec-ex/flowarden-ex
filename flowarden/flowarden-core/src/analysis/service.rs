use crate::prelude::*;

pub fn classify_packet(packet: DecodedPacket, context: &DirectionContext) -> ClassifiedPacket {
    let direction = classify_direction(&packet, context);
    let service = classify_service(&packet, &direction);
    ClassifiedPacket {
        decoded: packet,
        direction,
        service,
    }
}

pub fn classify_service(packet: &DecodedPacket, direction: &TrafficDirection) -> ServiceLabel {
    // Prefer ARP request/reply labels when operation is known.
    if matches!(packet.transport_protocol, TransportProtocol::Arp) {
        let name = match packet.arp_operation {
            Some(1) => "arp-request",
            Some(2) => "arp-reply",
            _ => "arp",
        };
        return ServiceLabel {
            name: name.to_string(),
            transport: TransportProtocol::Arp,
            confidence: match direction {
                TrafficDirection::Unknown => ServiceConfidence::Medium,
                _ => ServiceConfidence::High,
            },
        };
    }

    let transport = packet.transport_protocol.clone();

    if let Some(service_name) = protocol_service_name(&transport) {
        return ServiceLabel {
            name: service_name.to_string(),
            transport,
            confidence: match direction {
                TrafficDirection::Unknown => ServiceConfidence::Medium,
                _ => ServiceConfidence::High,
            },
        };
    }

    let chosen_port = match direction {
        TrafficDirection::Outbound => packet.destination_port.or(packet.source_port),
        TrafficDirection::Inbound => packet.source_port.or(packet.destination_port),
        TrafficDirection::Local | TrafficDirection::Unknown => {
            choose_service_port(packet.source_port, packet.destination_port)
        }
    };

    if let Some(port) = chosen_port
        && let Some(service_name) = service_name_for(&transport, port)
    {
        let confidence = match direction {
            TrafficDirection::Unknown => ServiceConfidence::Medium,
            TrafficDirection::Local => ServiceConfidence::Low,
            TrafficDirection::Inbound | TrafficDirection::Outbound => ServiceConfidence::High,
        };
        return ServiceLabel {
            name: service_name.to_string(),
            transport,
            confidence,
        };
    }

    ServiceLabel {
        name: "unknown".to_string(),
        transport,
        confidence: ServiceConfidence::Low,
    }
}

fn choose_service_port(source_port: Option<u16>, destination_port: Option<u16>) -> Option<u16> {
    match (source_port, destination_port) {
        (Some(src), Some(dst)) => {
            let src_well_known = is_well_known_port(src);
            let dst_well_known = is_well_known_port(dst);
            match (src_well_known, dst_well_known) {
                (true, false) => Some(src),
                (false, true) => Some(dst),
                (true, true) => Some(src.min(dst)),
                (false, false) => Some(src.min(dst)),
            }
        }
        (Some(src), None) => Some(src),
        (None, Some(dst)) => Some(dst),
        (None, None) => None,
    }
}

fn is_well_known_port(port: u16) -> bool {
    port <= 1024
}

fn service_name_for(transport: &TransportProtocol, port: u16) -> Option<&'static str> {
    match transport {
        TransportProtocol::Tcp => match port {
            20 | 21 => Some("ftp"),
            22 => Some("ssh"),
            25 => Some("smtp"),
            53 => Some("dns"),
            80 => Some("http"),
            110 => Some("pop3"),
            143 => Some("imap"),
            443 => Some("https"),
            465 => Some("smtps"),
            587 => Some("submission"),
            853 => Some("dns-over-tls"),
            _ => None,
        },
        TransportProtocol::Udp => match port {
            53 => Some("dns"),
            67 | 68 => Some("dhcp"),
            123 => Some("ntp"),
            443 => Some("quic"),
            5353 => Some("mdns"),
            _ => None,
        },
        TransportProtocol::Icmp => Some("icmp"),
        TransportProtocol::Icmpv6 => Some("icmpv6"),
        TransportProtocol::Arp => Some("arp"),
        TransportProtocol::Other(_) => None,
    }
}

fn protocol_service_name(transport: &TransportProtocol) -> Option<&'static str> {
    match transport {
        TransportProtocol::Icmp => Some("icmp"),
        TransportProtocol::Icmpv6 => Some("icmpv6"),
        // ARP uses operation-specific names in classify_service.
        TransportProtocol::Arp => None,
        _ => None,
    }
}

#[cfg(test)]
mod tests {
    use std::{
        fs,
        net::{IpAddr, Ipv4Addr},
        path::PathBuf,
        time::{SystemTime, UNIX_EPOCH},
    };

    use super::*;
    use crate::capture::{CaptureSource, CaptureType, PcapImport};
    use crate::prelude::decode_packet;
    use etherparse::PacketBuilder;
    use pcap::{Capture, Linktype, Packet, PacketHeader};

    fn decoded_packet(
        source_port: Option<u16>,
        destination_port: Option<u16>,
        transport_protocol: TransportProtocol,
    ) -> DecodedPacket {
        DecodedPacket {
            timestamp_sec: 0,
            timestamp_usec: 0,
            source_ip: IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
            source_port,
            destination_port,
            transport_protocol,
            tcp_flags: None,
            packet_len: 60,
            sni: None,
            arp_operation: None,
        }
    }

    fn temp_pcap_path(name: &str) -> PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("flowarden-{name}-{unique}.pcap"))
    }

    fn build_outbound_https_packet() -> Vec<u8> {
        let builder = PacketBuilder::ethernet2(
            [0x10, 0x11, 0x12, 0x13, 0x14, 0x15],
            [0x20, 0x21, 0x22, 0x23, 0x24, 0x25],
        )
        .ipv4([192, 168, 1, 10], [93, 184, 216, 34], 64)
        .tcp(50123, 443, 1000, 4096)
        .ack(2000);

        let payload = [1_u8, 2, 3];
        let mut packet = Vec::with_capacity(builder.size(payload.len()));
        builder.write(&mut packet, &payload).unwrap();
        packet
    }

    fn write_classification_test_pcap(path: &PathBuf) {
        let dead = Capture::dead(Linktype::ETHERNET).unwrap();
        let mut savefile = dead.savefile(path).unwrap();
        let data = build_outbound_https_packet();
        let header = PacketHeader {
            ts: libc::timeval {
                tv_sec: 12,
                tv_usec: 0,
            },
            caplen: data.len() as u32,
            len: data.len() as u32,
        };
        let packet = Packet::new(&header, &data);
        savefile.write(&packet);
        savefile.flush().unwrap();
    }

    #[test]
    fn outbound_uses_destination_port_for_service() {
        let packet = decoded_packet(Some(54000), Some(443), TransportProtocol::Tcp);
        let service = classify_service(&packet, &TrafficDirection::Outbound);
        assert_eq!(service.name, "https");
        assert_eq!(service.confidence, ServiceConfidence::High);
    }

    #[test]
    fn inbound_uses_source_port_for_service() {
        let packet = decoded_packet(Some(53), Some(53000), TransportProtocol::Udp);
        let service = classify_service(&packet, &TrafficDirection::Inbound);
        assert_eq!(service.name, "dns");
        assert_eq!(service.confidence, ServiceConfidence::High);
    }

    #[test]
    fn unknown_direction_does_not_degenerate_to_destination_only() {
        let packet = decoded_packet(Some(22), Some(54000), TransportProtocol::Tcp);
        let service = classify_service(&packet, &TrafficDirection::Unknown);
        assert_eq!(service.name, "ssh");
        assert_eq!(service.confidence, ServiceConfidence::Medium);
    }

    #[test]
    fn local_direction_returns_low_confidence_for_well_known_service() {
        let packet = decoded_packet(Some(5353), Some(5353), TransportProtocol::Udp);
        let service = classify_service(&packet, &TrafficDirection::Local);
        assert_eq!(service.name, "mdns");
        assert_eq!(service.confidence, ServiceConfidence::Low);
    }

    #[test]
    fn icmp_is_classified_without_ports() {
        let packet = decoded_packet(None, None, TransportProtocol::Icmp);
        let service = classify_service(&packet, &TrafficDirection::Unknown);
        assert_eq!(service.name, "icmp");
        assert_eq!(service.confidence, ServiceConfidence::Medium);
    }

    #[test]
    fn arp_request_service_label() {
        let packet = DecodedPacket {
            timestamp_sec: 0,
            timestamp_usec: 0,
            source_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1)),
            source_port: None,
            destination_port: None,
            transport_protocol: TransportProtocol::Arp,
            tcp_flags: None,
            packet_len: 42,
            sni: None,
            arp_operation: Some(1),
        };
        let label = classify_service(&packet, &TrafficDirection::Local);
        assert_eq!(label.name, "arp-request");
        assert!(matches!(label.transport, TransportProtocol::Arp));
    }

    #[test]
    fn classify_packet_combines_direction_and_service() {
        let packet = DecodedPacket {
            timestamp_sec: 0,
            timestamp_usec: 0,
            source_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
            destination_ip: IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)),
            source_port: Some(50123),
            destination_port: Some(443),
            transport_protocol: TransportProtocol::Tcp,
            tcp_flags: Some(0x18),
            packet_len: 80,
            sni: None,
            arp_operation: None,
        };
        let context =
            DirectionContext::new(vec![IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10))], false);

        let classified = classify_packet(packet, &context);
        assert_eq!(classified.direction, TrafficDirection::Outbound);
        assert_eq!(classified.service.name, "https");
        assert_eq!(classified.service.confidence, ServiceConfidence::High);
        assert_eq!(classified.decoded.destination_port, Some(443));
    }

    #[test]
    fn sample_pcap_packet_classifies_to_outbound_https() {
        let path = temp_pcap_path("service-sample");
        write_classification_test_pcap(&path);

        let source = CaptureSource::File(PcapImport::new(path.clone()));
        let mut capture = CaptureType::from_source(&source, None).unwrap();
        let packet = capture.next_packet().unwrap().unwrap();
        let envelope = PacketEnvelope::new(
            packet.timestamp_sec,
            packet.timestamp_usec,
            packet.captured_len,
            packet.original_len,
            packet.link_type,
            packet.data,
        );
        let decoded = decode_packet(&envelope).unwrap();
        let context =
            DirectionContext::new(vec![IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10))], false);

        let classified = classify_packet(decoded, &context);
        assert_eq!(classified.direction, TrafficDirection::Outbound);
        assert_eq!(classified.service.name, "https");
        assert_eq!(classified.service.confidence, ServiceConfidence::High);

        let _ = fs::remove_file(path);
    }
}
