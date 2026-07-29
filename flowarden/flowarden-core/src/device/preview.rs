use std::{
    sync::mpsc,
    thread,
    time::{Duration, Instant},
};

use pcap::{Capture, Device};
use serde::Serialize;

use crate::prelude::*;

#[derive(Clone, Debug, Serialize)]
pub struct DevicePreviewSummary {
    pub name: String,
    pub packets_seen: u64,
    pub bytes_seen: u64,
    pub unsupported: bool,
    pub error: Option<String>,
}

pub fn preview_devices(duration: Duration) -> Result<Vec<DevicePreviewSummary>> {
    let devices = Device::list()
        .or_err(
            ErrorType::DeviceListError,
            "Failed to list capture devices for preview",
        )
        .map_err(|e| e.into_network())?;
    let expected = devices.len();
    let (tx, rx) = mpsc::channel();

    for device in devices {
        let tx = tx.clone();
        thread::spawn(move || {
            let _ = tx.send(preview_device(device, duration));
        });
    }
    drop(tx);

    let mut previews = Vec::with_capacity(expected);
    for _ in 0..expected {
        let preview = rx
            .recv()
            .or_err(
                ErrorType::InternalError,
                "Failed to receive device preview result",
            )
            .map_err(|e| e.into_in())?;
        previews.push(preview);
    }
    previews.sort_by(|left, right| left.name.cmp(&right.name));
    Ok(previews)
}

fn preview_device(device: Device, duration: Duration) -> DevicePreviewSummary {
    let name = device.name.clone();

    let mut capture = match Capture::from_device(device.clone()).and_then(|inactive| {
        inactive
            .promisc(false)
            .buffer_size(256_000)
            .snaplen(128)
            .immediate_mode(false)
            .timeout(150)
            .open()
    }) {
        Ok(capture) => capture,
        Err(err) => {
            return DevicePreviewSummary {
                name,
                packets_seen: 0,
                bytes_seen: 0,
                unsupported: false,
                error: Some(err.to_string()),
            };
        }
    };

    let link_type = LinkTypeEx::from_pcap_link_type(capture.get_datalink());
    if !link_type.is_supported() {
        return DevicePreviewSummary {
            name,
            packets_seen: 0,
            bytes_seen: 0,
            unsupported: true,
            error: Some(link_type.full_print_on_one_line()),
        };
    }

    let started = Instant::now();
    let mut packets_seen = 0_u64;
    let mut bytes_seen = 0_u64;

    while started.elapsed() < duration {
        match capture.next_packet() {
            Ok(packet) => {
                packets_seen += 1;
                bytes_seen += u64::from(packet.header.len);
            }
            Err(pcap::Error::TimeoutExpired) => {}
            Err(err) => {
                return DevicePreviewSummary {
                    name,
                    packets_seen,
                    bytes_seen,
                    unsupported: false,
                    error: Some(err.to_string()),
                };
            }
        }
    }

    DevicePreviewSummary {
        name,
        packets_seen,
        bytes_seen,
        unsupported: false,
        error: None,
    }
}
