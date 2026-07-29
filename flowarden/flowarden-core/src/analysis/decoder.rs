use std::net::IpAddr;

use etherparse::{
    EtherType, LaxPacketHeaders, LinuxSllHeaderSlice, LinuxSllProtocolType, NetHeaders,
    TransportHeader, ether_type,
};

use crate::prelude::*;

fn payload_for_link_type(link_type: LinkTypeEx, data: &[u8]) -> Result<(Option<EtherType>, &[u8])> {
    match link_type {
        LinkTypeEx::Ethernet(_) => Ok((None, data)),
        LinkTypeEx::RawIp(_) => {
            let version = data.first().map(|first| first >> 4).unwrap_or_default();
            match version {
                4 => Ok((Some(ether_type::IPV4), data)),
                6 => Ok((Some(ether_type::IPV6), data)),
                _ => packet_decode_error("Raw IP packet does not start with IPv4 or IPv6"),
            }
        }
        LinkTypeEx::IPv4(_) => Ok((Some(ether_type::IPV4), data)),
        LinkTypeEx::IPv6(_) => Ok((Some(ether_type::IPV6), data)),
        LinkTypeEx::Null(_) => {
            if data.len() < 4 {
                packet_decode_error("Loop/Null packet too short for family header")
            } else {
                let family = u32::from_le_bytes([data[0], data[1], data[2], data[3]]);
                match family {
                    2 => Ok((Some(ether_type::IPV4), &data[4..])),
                    24 | 28 | 30 => Ok((Some(ether_type::IPV6), &data[4..])),
                    _ => Error::explain(
                        ErrorType::PacketDecodeError,
                        format!("Unsupported loopback family value: {family}"),
                    )
                    .into_network()
                    .into_err(),
                }
            }
        }
        LinkTypeEx::Loop(_) => {
            if data.len() < 4 {
                packet_decode_error("Loop/Null packet too short for family header")
            } else {
                let family = u32::from_be_bytes([data[0], data[1], data[2], data[3]]);
                match family {
                    2 => Ok((Some(ether_type::IPV4), &data[4..])),
                    24 | 28 | 30 => Ok((Some(ether_type::IPV6), &data[4..])),
                    _ => Error::explain(
                        ErrorType::PacketDecodeError,
                        format!("Unsupported loopback family value: {family}"),
                    )
                    .into_network()
                    .into_err(),
                }
            }
        }
        LinkTypeEx::LinuxSll(_) => {
            if data.len() < 16 {
                packet_decode_error("Linux SLL packet too short for cooked header")
            } else {
                let header = LinuxSllHeaderSlice::from_slice(data)
                    .or_err(
                        ErrorType::PacketDecodeError,
                        "Failed to decode Linux SLL header",
                    )
                    .map_err(|e| e.into_network())?;
                match header.protocol_type() {
                    LinuxSllProtocolType::EtherType(protocol_type) => {
                        Ok((Some(protocol_type), &data[16..]))
                    }
                    other => Error::explain(
                        ErrorType::PacketDecodeError,
                        format!("Unsupported Linux SLL protocol type: {other:?}"),
                    )
                    .into_network()
                    .into_err(),
                }
            }
        }
        LinkTypeEx::LinuxSll2(_) => {
            if data.len() < 20 {
                packet_decode_error("Linux SLL2 packet too short for cooked header")
            } else {
                let protocol_type = EtherType(u16::from_be_bytes([data[0], data[1]]));
                Ok((Some(protocol_type), &data[20..]))
            }
        }
        LinkTypeEx::Unsupported(_) | LinkTypeEx::MixedPcapNg | LinkTypeEx::NotYetAssigned => {
            Error::explain(
                ErrorType::UnsupportedLinkType,
                link_type.full_print_on_one_line(),
            )
            .into_network()
            .into_err()
        }
    }
}

fn ip_pair(ip: &NetHeaders) -> Result<(IpAddr, IpAddr)> {
    match ip {
        NetHeaders::Ipv4(header, _) => Ok((
            IpAddr::V4(header.source.into()),
            IpAddr::V4(header.destination.into()),
        )),
        NetHeaders::Ipv6(header, _) => Ok((
            IpAddr::V6(header.source.into()),
            IpAddr::V6(header.destination.into()),
        )),
        NetHeaders::Arp(_) => packet_decode_error("ip_pair does not apply to ARP headers"),
    }
}

fn ipv4_from_arp_addr(bytes: &[u8]) -> Result<std::net::Ipv4Addr> {
    if bytes.len() != 4 {
        return packet_decode_error(format!(
            "ARP protocol address length {} is not IPv4",
            bytes.len()
        ));
    }
    Ok(std::net::Ipv4Addr::new(bytes[0], bytes[1], bytes[2], bytes[3]))
}

fn tcp_flags(header: &etherparse::TcpHeader) -> u8 {
    let mut flags = 0_u8;
    if header.fin {
        flags |= 0x01;
    }
    if header.syn {
        flags |= 0x02;
    }
    if header.rst {
        flags |= 0x04;
    }
    if header.psh {
        flags |= 0x08;
    }
    if header.ack {
        flags |= 0x10;
    }
    if header.urg {
        flags |= 0x20;
    }
    if header.ece {
        flags |= 0x40;
    }
    if header.cwr {
        flags |= 0x80;
    }
    flags
}

pub fn decode_packet(envelope: &PacketEnvelope) -> Result<DecodedPacket> {
    decode_packet_with_options(envelope, &LightDpiOptions::default())
}

pub fn decode_packet_with_options(
    envelope: &PacketEnvelope,
    options: &LightDpiOptions,
) -> Result<DecodedPacket> {
    let (start_after_ether_type, payload) =
        payload_for_link_type(envelope.link_type, &envelope.data)?;
    let headers = match start_after_ether_type {
        None => LaxPacketHeaders::from_ethernet(payload)
            .or_err(
                ErrorType::PacketDecodeError,
                "Failed to decode Ethernet packet headers",
            )
            .map_err(|e| e.into_network())?,
        Some(ether_type) => LaxPacketHeaders::from_ether_type(ether_type, payload),
    };

    let ip = headers.net.ok_or_else(|| {
        Error::explain(
            ErrorType::PacketDecodeError,
            "Packet does not contain network headers",
        )
        .into_network()
    })?;

    // Phase 3 wave 1: ARP is a first-class L3 protocol (no L4 ports).
    if let NetHeaders::Arp(arp) = ip {
        let source_ip = IpAddr::V4(ipv4_from_arp_addr(arp.sender_protocol_addr())?);
        let destination_ip = IpAddr::V4(ipv4_from_arp_addr(arp.target_protocol_addr())?);
        return Ok(DecodedPacket {
            timestamp_sec: envelope.timestamp_sec,
            timestamp_usec: envelope.timestamp_usec,
            source_ip,
            destination_ip,
            source_port: None,
            destination_port: None,
            transport_protocol: TransportProtocol::Arp,
            tcp_flags: None,
            packet_len: envelope.original_len,
            sni: None,
            arp_operation: Some(arp.operation.0),
        });
    }

    let (source_ip, destination_ip) = ip_pair(&ip)?;

    let (source_port, destination_port, transport_protocol, tcp_flags) = match &headers.transport {
        Some(TransportHeader::Tcp(header)) => (
            Some(header.source_port),
            Some(header.destination_port),
            TransportProtocol::Tcp,
            Some(tcp_flags(header)),
        ),
        Some(TransportHeader::Udp(header)) => (
            Some(header.source_port),
            Some(header.destination_port),
            TransportProtocol::Udp,
            None,
        ),
        Some(TransportHeader::Icmpv4(_)) => (None, None, TransportProtocol::Icmp, None),
        Some(TransportHeader::Icmpv6(_)) => (None, None, TransportProtocol::Icmpv6, None),
        None => (None, None, TransportProtocol::Other(0), None),
    };

    // Light DPI: only attempt SNI on TCP payloads that look like TLS handshakes.
    let sni = if matches!(transport_protocol, TransportProtocol::Tcp) {
        let payload = headers.payload.slice();
        if payload.first() == Some(&0x16) {
            extract_sni_from_tcp_payload_with_options(payload, options)
        } else {
            None
        }
    } else {
        None
    };

    Ok(DecodedPacket {
        timestamp_sec: envelope.timestamp_sec,
        timestamp_usec: envelope.timestamp_usec,
        source_ip,
        destination_ip,
        source_port,
        destination_port,
        transport_protocol,
        tcp_flags,
        packet_len: envelope.original_len,
        sni,
        arp_operation: None,
    })
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
    use crate::capture::CaptureType;
    use crate::device::LinkTypeEx;
    use crate::{
        capture::{CaptureRuntime, CaptureSource, PcapImport, RuntimeConfig},
        prelude::TransportProtocol,
    };
    use etherparse::{LinuxSllPacketType, PacketBuilder};
    use pcap::{Capture, Linktype, Packet, PacketHeader};

    fn temp_pcap_path(name: &str) -> PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("flowarden-{name}-{unique}.pcap"))
    }

    fn build_valid_tcp_packet() -> Vec<u8> {
        let builder = PacketBuilder::ethernet2(
            [0x10, 0x11, 0x12, 0x13, 0x14, 0x15],
            [0x20, 0x21, 0x22, 0x23, 0x24, 0x25],
        )
        .ipv4([192, 168, 10, 2], [93, 184, 216, 34], 64)
        .tcp(49152, 443, 1000, 4096)
        .syn()
        .ack(2000);

        let payload = [1_u8, 2, 3, 4, 5];
        let mut packet = Vec::with_capacity(builder.size(payload.len()));
        builder.write(&mut packet, &payload).unwrap();
        packet
    }

    fn build_ipv4_udp_payload() -> Vec<u8> {
        let builder = PacketBuilder::ipv4([10, 0, 0, 1], [8, 8, 8, 8], 64).udp(5353, 53);
        let payload = [9_u8, 8, 7, 6];
        let mut packet = Vec::with_capacity(builder.size(payload.len()));
        builder.write(&mut packet, &payload).unwrap();
        packet
    }

    fn build_ipv6_udp_payload() -> Vec<u8> {
        let builder = PacketBuilder::ipv6(
            [0x20, 0x01, 0x0d, 0xb8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1],
            [0x26, 0x06, 0x47, 0x00, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 2],
            64,
        )
        .udp(5353, 853);
        let payload = [6_u8, 6, 6, 6];
        let mut packet = Vec::with_capacity(builder.size(payload.len()));
        builder.write(&mut packet, &payload).unwrap();
        packet
    }

    fn build_linux_sll_udp_packet() -> Vec<u8> {
        let builder = PacketBuilder::linux_sll(
            LinuxSllPacketType::HOST,
            6,
            [0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0, 0],
        )
        .ipv4([172, 16, 0, 2], [1, 1, 1, 1], 64)
        .udp(60000, 123);

        let payload = [1_u8, 3, 3, 7];
        let mut packet = Vec::with_capacity(builder.size(payload.len()));
        builder.write(&mut packet, &payload).unwrap();
        packet
    }

    fn build_linux_sll2_udp_packet() -> Vec<u8> {
        let mut packet = Vec::new();
        packet.extend_from_slice(&0x0800_u16.to_be_bytes());
        packet.extend_from_slice(&0_u16.to_be_bytes());
        packet.extend_from_slice(&1_u32.to_be_bytes());
        packet.extend_from_slice(&1_u16.to_be_bytes());
        packet.push(0);
        packet.push(6);
        packet.extend_from_slice(&[0x40, 0x41, 0x42, 0x43, 0x44, 0x45, 0, 0]);
        packet.extend_from_slice(&build_ipv4_udp_payload());
        packet
    }

    fn write_decode_test_pcap(path: &PathBuf) {
        let dead = Capture::dead(Linktype::ETHERNET).unwrap();
        let mut savefile = dead.savefile(path).unwrap();

        let valid = build_valid_tcp_packet();
        let valid_header = PacketHeader {
            ts: libc::timeval {
                tv_sec: 10,
                tv_usec: 500,
            },
            caplen: valid.len() as u32,
            len: valid.len() as u32,
        };
        let valid_packet = Packet::new(&valid_header, &valid);
        savefile.write(&valid_packet);

        let malformed = [0_u8, 1, 2, 3, 4, 5];
        let malformed_header = PacketHeader {
            ts: libc::timeval {
                tv_sec: 11,
                tv_usec: 0,
            },
            caplen: malformed.len() as u32,
            len: malformed.len() as u32,
        };
        let malformed_packet = Packet::new(&malformed_header, &malformed);
        savefile.write(&malformed_packet);
        savefile.flush().unwrap();
    }

    fn build_arp_packet() -> Vec<u8> {
        let mut packet = Vec::with_capacity(42);
        packet.extend_from_slice(&[0xff, 0xff, 0xff, 0xff, 0xff, 0xff]);
        packet.extend_from_slice(&[0x10, 0x11, 0x12, 0x13, 0x14, 0x15]);
        packet.extend_from_slice(&0x0806_u16.to_be_bytes());
        packet.extend_from_slice(&0x0001_u16.to_be_bytes());
        packet.extend_from_slice(&0x0800_u16.to_be_bytes());
        packet.push(6);
        packet.push(4);
        packet.extend_from_slice(&0x0001_u16.to_be_bytes());
        packet.extend_from_slice(&[0x10, 0x11, 0x12, 0x13, 0x14, 0x15]);
        packet.extend_from_slice(&[192, 168, 1, 10]);
        packet.extend_from_slice(&[0, 0, 0, 0, 0, 0]);
        packet.extend_from_slice(&[192, 168, 1, 1]);
        packet
    }

    fn write_arp_then_tcp_test_pcap(path: &PathBuf) {
        let dead = Capture::dead(Linktype::ETHERNET).unwrap();
        let mut savefile = dead.savefile(path).unwrap();

        let arp = build_arp_packet();
        let arp_header = PacketHeader {
            ts: libc::timeval {
                tv_sec: 20,
                tv_usec: 0,
            },
            caplen: arp.len() as u32,
            len: arp.len() as u32,
        };
        savefile.write(&Packet::new(&arp_header, &arp));

        let tcp = build_valid_tcp_packet();
        let tcp_header = PacketHeader {
            ts: libc::timeval {
                tv_sec: 21,
                tv_usec: 0,
            },
            caplen: tcp.len() as u32,
            len: tcp.len() as u32,
        };
        savefile.write(&Packet::new(&tcp_header, &tcp));
        savefile.flush().unwrap();
    }

    #[test]
    fn rejects_short_linux_sll_packet() {
        let packet = PacketEnvelope::new(
            0,
            0,
            8,
            8,
            LinkTypeEx::LinuxSll(Linktype::LINUX_SLL),
            vec![0; 8].into(),
        );
        let err = decode_packet(&packet).unwrap_err();
        assert_eq!(err.reason_str(), "PacketDecodeError");
    }

    #[test]
    fn rejects_unsupported_link_type() {
        let packet = PacketEnvelope::new(
            0,
            0,
            0,
            0,
            LinkTypeEx::Unsupported(Linktype(999)),
            vec![].into(),
        );
        let err = decode_packet(&packet).unwrap_err();
        assert_eq!(err.reason_str(), "UnsupportedLinkType");
    }

    #[test]
    fn decodes_arp_packet_without_panicking() {
        let raw = build_arp_packet();
        let packet = PacketEnvelope::new(
            20,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::Ethernet(Linktype::ETHERNET),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Arp));
        assert_eq!(decoded.arp_operation, Some(1));
    }

    #[test]
    fn decodes_ethernet_ipv4_tcp_packet() {
        let raw = build_valid_tcp_packet();
        let packet = PacketEnvelope::new(
            10,
            500,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::Ethernet(Linktype::ETHERNET),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.timestamp_sec, 10);
        assert_eq!(decoded.timestamp_usec, 500);
        assert_eq!(
            decoded.source_ip,
            IpAddr::V4(Ipv4Addr::new(192, 168, 10, 2))
        );
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(93, 184, 216, 34))
        );
        assert_eq!(decoded.source_port, Some(49152));
        assert_eq!(decoded.destination_port, Some(443));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Tcp));
        assert_eq!(decoded.tcp_flags, Some(0x12));
    }

    #[test]
    fn decodes_null_ipv4_udp_packet() {
        let mut raw = 2_u32.to_le_bytes().to_vec();
        raw.extend_from_slice(&build_ipv4_udp_payload());

        let packet = PacketEnvelope::new(
            1,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::Null(Linktype::NULL),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)));
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(53));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
        assert_eq!(decoded.tcp_flags, None);
    }

    #[test]
    fn decodes_loop_ipv4_udp_packet() {
        let mut raw = 2_u32.to_be_bytes().to_vec();
        raw.extend_from_slice(&build_ipv4_udp_payload());

        let packet = PacketEnvelope::new(
            1,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::Loop(Linktype::LOOP),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)));
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(53));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_raw_ipv4_udp_packet() {
        let raw = build_ipv4_udp_payload();
        let packet = PacketEnvelope::new(
            2,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::RawIp(Linktype(12)),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)));
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(53));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_raw_ipv6_udp_packet() {
        let raw = build_ipv6_udp_payload();
        let packet = PacketEnvelope::new(
            2,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::RawIp(Linktype(12)),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, "2001:db8::1".parse::<IpAddr>().unwrap());
        assert_eq!(
            decoded.destination_ip,
            "2606:4700::2".parse::<IpAddr>().unwrap()
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(853));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_ipv4_link_type_udp_packet() {
        let raw = build_ipv4_udp_payload();
        let packet = PacketEnvelope::new(
            2,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::IPv4(Linktype::IPV4),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)));
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(53));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_ipv6_link_type_udp_packet() {
        let raw = build_ipv6_udp_payload();
        let packet = PacketEnvelope::new(
            2,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::IPv6(Linktype::IPV6),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, "2001:db8::1".parse::<IpAddr>().unwrap());
        assert_eq!(
            decoded.destination_ip,
            "2606:4700::2".parse::<IpAddr>().unwrap()
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(853));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_linux_sll_udp_packet() {
        let raw = build_linux_sll_udp_packet();
        let packet = PacketEnvelope::new(
            3,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::LinuxSll(Linktype::LINUX_SLL),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, IpAddr::V4(Ipv4Addr::new(172, 16, 0, 2)));
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1))
        );
        assert_eq!(decoded.source_port, Some(60000));
        assert_eq!(decoded.destination_port, Some(123));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_linux_sll2_udp_packet() {
        let raw = build_linux_sll2_udp_packet();
        let packet = PacketEnvelope::new(
            4,
            0,
            raw.len() as u32,
            raw.len() as u32,
            LinkTypeEx::LinuxSll2(Linktype::LINUX_SLL2),
            raw.into(),
        );

        let decoded = decode_packet(&packet).unwrap();
        assert_eq!(decoded.source_ip, IpAddr::V4(Ipv4Addr::new(10, 0, 0, 1)));
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8))
        );
        assert_eq!(decoded.source_port, Some(5353));
        assert_eq!(decoded.destination_port, Some(53));
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Udp));
    }

    #[test]
    fn decodes_first_packet_from_sample_pcap() {
        let path = temp_pcap_path("decoder-sample-pcap");
        write_decode_test_pcap(&path);

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
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Tcp));
        assert_eq!(decoded.source_port, Some(49152));
        assert_eq!(decoded.destination_port, Some(443));

        let _ = fs::remove_file(path);
    }

    #[test]
    fn runtime_continues_after_malformed_packet() {
        let path = temp_pcap_path("decoder-runtime");
        write_decode_test_pcap(&path);

        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(path.clone())),
            RuntimeConfig::forensic(),
        );

        let report = runtime.run().unwrap();
        assert_eq!(report.stats.packets_seen, 2);
        assert_eq!(report.stats.packets_decoded, 1);
        assert_eq!(report.stats.packets_decode_failed, 1);

        let _ = fs::remove_file(path);
    }

    #[test]
    fn decode_arp_request_extracts_sender_and_target_ip() {
        let path = temp_pcap_path("decoder-arp-only");
        let dead = Capture::dead(Linktype::ETHERNET).unwrap();
        let mut savefile = dead.savefile(&path).unwrap();
        let arp = build_arp_packet();
        let header = PacketHeader {
            ts: libc::timeval {
                tv_sec: 1,
                tv_usec: 0,
            },
            caplen: arp.len() as u32,
            len: arp.len() as u32,
        };
        savefile.write(&Packet::new(&header, &arp));
        savefile.flush().unwrap();

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
        assert!(matches!(decoded.transport_protocol, TransportProtocol::Arp));
        assert_eq!(decoded.arp_operation, Some(1));
        assert_eq!(
            decoded.source_ip,
            IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10))
        );
        assert_eq!(
            decoded.destination_ip,
            IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1))
        );

        let _ = fs::remove_file(path);
    }

    #[test]
    fn runtime_decodes_arp_then_tcp() {
        let path = temp_pcap_path("decoder-runtime-arp");
        write_arp_then_tcp_test_pcap(&path);

        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(path.clone())),
            RuntimeConfig::forensic(),
        );

        let report = runtime.run().unwrap();
        assert_eq!(report.stats.packets_seen, 2);
        assert_eq!(report.stats.packets_decoded, 2);
        assert_eq!(report.stats.packets_decode_failed, 0);
        assert!(
            report
                .final_snapshot
                .aggregate_summary
                .top_services
                .iter()
                .any(|s| s.service.name == "arp" || s.service.name == "arp-request")
        );

        let _ = fs::remove_file(path);
    }
}
