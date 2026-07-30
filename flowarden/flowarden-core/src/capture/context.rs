use crate::prelude::*;

use super::pcapng::{
    PcapNgCapture, is_mixed_interface_pcapng_error, pcapng_has_mixed_interface_link_types,
};
use flowarden_error::Context;
use log::error;
use pcap::{Capture, Error as PcapError, Packet, Savefile, Stat};
use std::path::Path;

pub enum CaptureType {
    Live(Capture<pcap::Active>),
    Offline(Capture<pcap::Offline>),
    PcapNg(PcapNgCapture),
}

pub struct CapturePacket {
    pub timestamp_sec: i64,
    pub timestamp_usec: i64,
    pub captured_len: u32,
    pub original_len: u32,
    pub link_type: LinkTypeEx,
    pub data: Box<[u8]>,
}

impl CaptureType {
    pub fn next_packet(&mut self) -> Result<Option<CapturePacket>> {
        match self {
            Self::Live(on) => {
                let link_type = LinkTypeEx::from_pcap_link_type(on.get_datalink());
                match on.next_packet() {
                    Ok(packet) => Ok(Some(capture_packet_from_pcap(&packet, link_type))),
                    Err(PcapError::TimeoutExpired) => Ok(None),
                    Err(err) => Error::because(
                        ErrorType::PacketReadError,
                        "Failed to read packet from live capture",
                        err,
                    )
                    .into_network()
                    .into_err(),
                }
            }
            Self::Offline(off) => {
                let link_type = LinkTypeEx::from_pcap_link_type(off.get_datalink());
                match off.next_packet() {
                    Ok(packet) => Ok(Some(capture_packet_from_pcap(&packet, link_type))),
                    Err(PcapError::NoMorePackets) => Ok(None),
                    Err(err) => Error::because(
                        ErrorType::PacketReadError,
                        "Failed to read packet from offline capture",
                        err,
                    )
                    .into_network()
                    .into_err(),
                }
            }
            Self::PcapNg(reader) => reader.next_packet(),
        }
    }

    pub fn stats(&mut self) -> Result<Stat> {
        match self {
            Self::Live(on) => on.stats(),
            Self::Offline(off) => off.stats(),
            Self::PcapNg(_) => {
                return Error::explain(
                    ErrorType::PacketReadError,
                    "Capture statistics are not available for pcapng fallback reader",
                )
                .into_network()
                .into_err();
            }
        }
        .or_err(
            ErrorType::PacketReadError,
            "Failed to read packet from capture",
        )
    }

    pub fn link_type(&self) -> LinkTypeEx {
        match self {
            Self::Live(on) => LinkTypeEx::from_pcap_link_type(on.get_datalink()),
            Self::Offline(off) => LinkTypeEx::from_pcap_link_type(off.get_datalink()),
            Self::PcapNg(reader) => reader.link_type(),
        }
    }

    pub fn from_source(source: &CaptureSource, pcap_out_path: Option<&Path>) -> Result<Self> {
        Self::from_source_with_snaplen(source, pcap_out_path, None)
    }

    /// Open capture with optional monitoring snaplen override (`None` → default live snaplen).
    /// When `pcap_out_path` is set, full snaplen is always used so dumps are complete.
    pub fn from_source_with_snaplen(
        source: &CaptureSource,
        pcap_out_path: Option<&Path>,
        snaplen: Option<i32>,
    ) -> Result<Self> {
        match source {
            CaptureSource::Device(device) => {
                let resolved = resolve_capture_snaplen(pcap_out_path.is_some(), snaplen);
                Capture::from_device(device.to_pcap_device())
                    .or_err(
                        ErrorType::CaptureStartError,
                        "Failed to create capture from device",
                    )
                    .map_err(|e| e.into_network())
                    .and_then(|inactive| {
                        inactive
                            .promisc(false)
                            .buffer_size(2_000_000) // 2MB buffer -> 10k packets of 200 bytes
                            .snaplen(resolved)
                            .immediate_mode(false)
                            .timeout(150) // ensure UI is updated even if no packets are captured
                            .open()
                            .or_err(
                                ErrorType::CaptureStartError,
                                "Failed to open capture from device",
                            )
                            .map_err(|e| e.into_network())
                    })
                    .map(Self::Live)
                    .err_context(|| {
                        format!("while opening live capture for `{}`", device.get_name())
                    })
            }
            CaptureSource::File(file) => {
                if pcapng_has_mixed_interface_link_types(file.path())? {
                    if pcap_out_path.is_some() {
                        return Error::explain(
                            ErrorType::CaptureStartError,
                            "pcapng files with mixed interface link types cannot be exported to classic pcap",
                        )
                        .into_network()
                        .into_err();
                    }
                    return PcapNgCapture::from_file(file.path()).map(Self::PcapNg);
                }
                match Capture::from_file(file.path()) {
                    Ok(capture) => Ok(Self::Offline(capture)),
                    Err(err) if is_mixed_interface_pcapng_error(&err) => {
                        PcapNgCapture::from_file(file.path()).map(Self::PcapNg)
                    }
                    Err(err) => Error::because(
                        ErrorType::CaptureStartError,
                        "Failed to create capture from file",
                        err,
                    )
                    .into_network()
                    .into_err(),
                }
            }
        }
    }

    pub fn create_savefile(&mut self, pcap_out_path: Option<&Path>) -> Result<Option<Savefile>> {
        let Some(path) = pcap_out_path else {
            return Ok(None);
        };

        match self {
            Self::Live(capture) => capture
                .savefile(path)
                .or_err_with(ErrorType::FileCreateError, || {
                    format!("Failed to create pcap savefile `{}`", path.display())
                })
                .map(Some)
                .map_err(|e| e.into_network()),
            Self::Offline(capture) => capture
                .savefile(path)
                .or_err_with(ErrorType::FileCreateError, || {
                    format!("Failed to create pcap savefile `{}`", path.display())
                })
                .map(Some)
                .map_err(|e| e.into_network()),
            Self::PcapNg(_) => Error::explain(
                ErrorType::FileCreateError,
                "pcapng fallback reader cannot create classic pcap savefile",
            )
            .into_network()
            .into_err(),
        }
    }

    pub fn set_bpf(&mut self, bpf: &str) -> Result<()> {
        match self {
            Self::Live(cap) => cap
                .filter(bpf, true)
                .or_err(ErrorType::FilterApplyError, "Failed to set BPF filter")
                .map_err(|e| e.into_network()),
            Self::Offline(cap) => cap
                .filter(bpf, true)
                .or_err(ErrorType::FilterApplyError, "Failed to set BPF filter")
                .map_err(|e| e.into_network()),
            Self::PcapNg(_) => Error::explain(
                ErrorType::FilterApplyError,
                "BPF filtering is not supported by the pcapng fallback reader",
            )
            .into_network()
            .into_err(),
        }
    }

    pub fn pause(&mut self) {
        if let Self::Live(cap) = self
            && let Some(err) = cap.filter("less 2", true).err()
        {
            error!("Failed to set BPF filter to pause capture: {}", err);
        }
    }

    /// Restore live capture filtering after [`Self::pause`].
    ///
    /// `bpf` should be the filter that was active before pause (including any
    /// resident control-port exclusion). Empty / `None` clears to accept-all.
    pub fn resume(&mut self, bpf: Option<&str>) {
        let Self::Live(cap) = self else {
            return;
        };

        let filter = bpf.map(str::trim).filter(|value| !value.is_empty());
        match filter {
            Some(value) => {
                if let Err(err) = cap.filter(value, true) {
                    error!(
                        "Failed to set BPF filter to resume capture (`{value}`): {err}; falling back to accept-all"
                    );
                    if let Err(fallback_err) = cap.filter("", true) {
                        error!(
                            "Failed to clear BPF filter after resume failure: {fallback_err}; trying greater 0"
                        );
                        let _ = cap.filter("greater 0", true);
                    }
                }
            }
            None => {
                if let Err(err) = cap.filter("", true) {
                    error!("Failed to clear BPF filter on resume: {err}; trying greater 0");
                    let _ = cap.filter("greater 0", true);
                }
            }
        }
    }
}

fn capture_packet_from_pcap(packet: &Packet<'_>, link_type: LinkTypeEx) -> CapturePacket {
    CapturePacket {
        timestamp_sec: packet.header.ts.tv_sec,
        // timeval.tv_usec is i32 on some platforms (e.g. macOS) and i64 on others (e.g. Linux).
        timestamp_usec: tv_usec_as_i64(packet.header.ts.tv_usec),
        captured_len: packet.header.caplen,
        original_len: packet.header.len,
        link_type,
        data: packet.data.into(),
    }
}

/// Widen libc `tv_usec` to `i64` without platform-specific casts in the call site.
fn tv_usec_as_i64(value: impl Into<i64>) -> i64 {
    value.into()
}
