//! Offline pcapng reader with mixed-interface link-type handling.

use std::{
    fs::File,
    io::{BufReader, Read, Seek},
    path::Path,
};

use pcap::Error as PcapError;

use crate::prelude::*;

use super::context::CapturePacket;

pub(crate) fn is_mixed_interface_pcapng_error(err: &PcapError) -> bool {
    err.to_string()
        .contains("an interface has a type 0 different from the type of the first interface")
}

pub(crate) fn pcapng_has_mixed_interface_link_types(path: &Path) -> Result<bool> {
    let mut header = [0_u8; 4];
    let mut file = File::open(path)
        .or_err_with(ErrorType::FileReadError, || {
            format!("Failed to open capture file `{}`", path.display())
        })
        .map_err(|e| e.into_network())?;
    match file.read_exact(&mut header) {
        Ok(()) => {}
        Err(err) if err.kind() == std::io::ErrorKind::UnexpectedEof => return Ok(false),
        Err(err) => {
            return Error::because(
                ErrorType::FileReadError,
                "Failed to read capture file header",
                err,
            )
            .into_network()
            .into_err();
        }
    }
    if header != [0x0A, 0x0D, 0x0D, 0x0A] {
        return Ok(false);
    }

    let mut capture = PcapNgCapture::from_file(path)?;
    let mut link_types = Vec::new();
    while let Some((block_type, body)) = capture.read_block()? {
        match block_type {
            0x0A0D0D0A => capture.parse_section_header_body(&body)?,
            0x00000001 if body.len() >= 2 => {
                let link_type = capture.endian.read_u16([body[0], body[1]]);
                link_types.push(i32::from(link_type));
            }
            _ => {}
        }
    }

    link_types.sort_unstable();
    link_types.dedup();
    Ok(link_types.len() > 1)
}

pub struct PcapNgCapture {
    reader: BufReader<File>,
    interfaces: Vec<PcapNgInterface>,
    endian: Endian,
    section_end: Option<u64>,
}

#[derive(Clone, Copy)]
enum Endian {
    Little,
    Big,
}

impl Endian {
    fn read_u16(self, bytes: [u8; 2]) -> u16 {
        match self {
            Self::Little => u16::from_le_bytes(bytes),
            Self::Big => u16::from_be_bytes(bytes),
        }
    }

    fn read_u32(self, bytes: [u8; 4]) -> u32 {
        match self {
            Self::Little => u32::from_le_bytes(bytes),
            Self::Big => u32::from_be_bytes(bytes),
        }
    }

    fn read_i64(self, bytes: [u8; 8]) -> i64 {
        match self {
            Self::Little => i64::from_le_bytes(bytes),
            Self::Big => i64::from_be_bytes(bytes),
        }
    }
}

#[derive(Clone, Copy)]
struct PcapNgInterface {
    link_type: LinkTypeEx,
    ts_resolution: TimestampResolution,
}

#[derive(Clone, Copy)]
enum TimestampResolution {
    Decimal(u8),
    Binary(u8),
}

impl Default for TimestampResolution {
    fn default() -> Self {
        Self::Decimal(6)
    }
}

impl TimestampResolution {
    fn timestamp(self, value: u64) -> (i64, i64) {
        let units_per_second = match self {
            Self::Decimal(power) => 10_u64.saturating_pow(u32::from(power)),
            Self::Binary(power) => 1_u64.checked_shl(u32::from(power)).unwrap_or(1),
        }
        .max(1);
        let seconds = value / units_per_second;
        let remainder = value % units_per_second;
        let microseconds =
            (u128::from(remainder) * 1_000_000_u128 / u128::from(units_per_second)) as i64;
        (seconds as i64, microseconds)
    }
}

impl PcapNgCapture {
    pub fn from_file(path: &Path) -> Result<Self> {
        let file = File::open(path)
            .or_err_with(ErrorType::FileReadError, || {
                format!("Failed to open pcapng file `{}`", path.display())
            })
            .map_err(|e| e.into_network())?;
        let mut capture = Self {
            reader: BufReader::new(file),
            interfaces: Vec::new(),
            endian: Endian::Little,
            section_end: None,
        };
        capture.read_section_header()?;
        Ok(capture)
    }

    pub fn link_type(&self) -> LinkTypeEx {
        let mut unique = self
            .interfaces
            .iter()
            .copied()
            .map(|interface| link_type_identity(interface.link_type))
            .collect::<Vec<_>>();
        unique.sort_unstable();
        unique.dedup();
        if unique.len() == 1 {
            self.interfaces
                .first()
                .map(|interface| interface.link_type)
                .unwrap_or(LinkTypeEx::NotYetAssigned)
        } else {
            LinkTypeEx::MixedPcapNg
        }
    }

    pub fn next_packet(&mut self) -> Result<Option<CapturePacket>> {
        loop {
            let Some((block_type, body)) = self.read_block()? else {
                return Ok(None);
            };

            match block_type {
                0x0A0D0D0A => self.parse_section_header_body(&body)?,
                0x00000001 => self.parse_interface_description(&body)?,
                0x00000006 => {
                    if let Some(packet) = self.parse_enhanced_packet(&body)? {
                        return Ok(Some(packet));
                    }
                }
                _ => {}
            }
        }
    }

    fn read_section_header(&mut self) -> Result<()> {
        let Some((block_type, body)) = self.read_block_with_endian(None)? else {
            return Error::explain(ErrorType::PacketReadError, "Empty pcapng file")
                .into_network()
                .into_err();
        };
        if block_type != 0x0A0D0D0A {
            return Error::explain(
                ErrorType::PacketReadError,
                "pcapng fallback reader expected a section header block",
            )
            .into_network()
            .into_err();
        }
        self.parse_section_header_body(&body)
    }

    fn read_block(&mut self) -> Result<Option<(u32, Vec<u8>)>> {
        self.read_block_with_endian(Some(self.endian))
    }

    fn read_block_with_endian(&mut self, endian: Option<Endian>) -> Result<Option<(u32, Vec<u8>)>> {
        let start = self
            .reader
            .stream_position()
            .or_err(
                ErrorType::PacketReadError,
                "Failed to read pcapng stream position",
            )
            .map_err(|e| e.into_network())?;
        if self.section_end.is_some_and(|end| start >= end) {
            self.section_end = None;
        }

        let mut header = [0_u8; 8];
        match self.reader.read_exact(&mut header) {
            Ok(()) => {}
            Err(err) if err.kind() == std::io::ErrorKind::UnexpectedEof => return Ok(None),
            Err(err) => {
                return Error::because(
                    ErrorType::PacketReadError,
                    "Failed to read pcapng block header",
                    err,
                )
                .into_network()
                .into_err();
            }
        }

        let endian_for_len = endian.unwrap_or(Endian::Little);
        let block_type = endian_for_len.read_u32([header[0], header[1], header[2], header[3]]);
        let block_len =
            endian_for_len.read_u32([header[4], header[5], header[6], header[7]]) as usize;
        if block_len < 12 {
            return Error::explain(
                ErrorType::PacketReadError,
                format!("Invalid pcapng block length: {block_len}"),
            )
            .into_network()
            .into_err();
        }

        let body_len = block_len - 12;
        let mut body = vec![0_u8; body_len];
        self.reader
            .read_exact(&mut body)
            .or_err(
                ErrorType::PacketReadError,
                "Failed to read pcapng block body",
            )
            .map_err(|e| e.into_network())?;
        let mut trailer = [0_u8; 4];
        self.reader
            .read_exact(&mut trailer)
            .or_err(
                ErrorType::PacketReadError,
                "Failed to read pcapng block trailer",
            )
            .map_err(|e| e.into_network())?;

        Ok(Some((block_type, body)))
    }

    fn parse_section_header_body(&mut self, body: &[u8]) -> Result<()> {
        if body.len() < 16 {
            return Error::explain(
                ErrorType::PacketReadError,
                "pcapng section header too short",
            )
            .into_network()
            .into_err();
        }

        self.endian = match [body[0], body[1], body[2], body[3]] {
            [0x4D, 0x3C, 0x2B, 0x1A] => Endian::Little,
            [0x1A, 0x2B, 0x3C, 0x4D] => Endian::Big,
            _ => {
                return Error::explain(
                    ErrorType::PacketReadError,
                    "Invalid pcapng byte-order magic",
                )
                .into_network()
                .into_err();
            }
        };

        self.interfaces.clear();
        let section_len = self.endian.read_i64([
            body[8], body[9], body[10], body[11], body[12], body[13], body[14], body[15],
        ]);
        if section_len >= 0 {
            let current = self.reader.stream_position().or_err(
                ErrorType::PacketReadError,
                "Failed to read pcapng stream position",
            )?;
            self.section_end = Some(current + section_len as u64);
        } else {
            self.section_end = None;
        }
        Ok(())
    }

    fn parse_interface_description(&mut self, body: &[u8]) -> Result<()> {
        if body.len() < 8 {
            return Error::explain(
                ErrorType::PacketReadError,
                "pcapng interface description block too short",
            )
            .into_network()
            .into_err();
        }
        let link_type = self.endian.read_u16([body[0], body[1]]);
        let options = &body[8..];
        let ts_resolution = parse_ts_resolution(options, self.endian)?;
        self.interfaces.push(PcapNgInterface {
            link_type: LinkTypeEx::from_pcap_link_type(pcap::Linktype(i32::from(link_type))),
            ts_resolution,
        });
        Ok(())
    }

    fn parse_enhanced_packet(&self, body: &[u8]) -> Result<Option<CapturePacket>> {
        if body.len() < 20 {
            return Error::explain(
                ErrorType::PacketReadError,
                "pcapng enhanced packet block too short",
            )
            .into_network()
            .into_err();
        }
        let interface_id = self.endian.read_u32([body[0], body[1], body[2], body[3]]) as usize;
        let Some(interface) = self.interfaces.get(interface_id).copied() else {
            return Ok(None);
        };
        let timestamp_high = self.endian.read_u32([body[4], body[5], body[6], body[7]]);
        let timestamp_low = self.endian.read_u32([body[8], body[9], body[10], body[11]]);
        let captured_len = self
            .endian
            .read_u32([body[12], body[13], body[14], body[15]]);
        let original_len = self
            .endian
            .read_u32([body[16], body[17], body[18], body[19]]);
        let captured_len_usize = captured_len as usize;
        if body.len() < 20 + captured_len_usize {
            return Error::explain(
                ErrorType::PacketReadError,
                "pcapng enhanced packet data is truncated",
            )
            .into_network()
            .into_err();
        }
        let timestamp_value = (u64::from(timestamp_high) << 32) | u64::from(timestamp_low);
        let (timestamp_sec, timestamp_usec) = interface.ts_resolution.timestamp(timestamp_value);

        Ok(Some(CapturePacket {
            timestamp_sec,
            timestamp_usec,
            captured_len,
            original_len,
            link_type: interface.link_type,
            data: body[20..20 + captured_len_usize].into(),
        }))
    }
}

fn parse_ts_resolution(body: &[u8], endian: Endian) -> Result<TimestampResolution> {
    let mut offset = 0_usize;
    while offset + 4 <= body.len() {
        let code = endian.read_u16([body[offset], body[offset + 1]]);
        let len = endian.read_u16([body[offset + 2], body[offset + 3]]) as usize;
        offset += 4;
        if code == 0 {
            break;
        }
        if offset + len > body.len() {
            return Error::explain(
                ErrorType::PacketReadError,
                "pcapng option length exceeds block body",
            )
            .into_network()
            .into_err();
        }
        if code == 9 && len >= 1 {
            let value = body[offset];
            return if value & 0x80 == 0 {
                Ok(TimestampResolution::Decimal(value))
            } else {
                Ok(TimestampResolution::Binary(value & 0x7F))
            };
        }
        offset += align32(len);
    }
    Ok(TimestampResolution::default())
}

fn align32(value: usize) -> usize {
    (value + 3) & !3
}

fn link_type_identity(link_type: LinkTypeEx) -> i32 {
    match link_type {
        LinkTypeEx::Null(link_type)
        | LinkTypeEx::Ethernet(link_type)
        | LinkTypeEx::RawIp(link_type)
        | LinkTypeEx::Loop(link_type)
        | LinkTypeEx::IPv4(link_type)
        | LinkTypeEx::IPv6(link_type)
        | LinkTypeEx::LinuxSll(link_type)
        | LinkTypeEx::LinuxSll2(link_type)
        | LinkTypeEx::Unsupported(link_type) => link_type.0,
        LinkTypeEx::MixedPcapNg => -2,
        LinkTypeEx::NotYetAssigned => -1,
    }
}
