use pcap::{Address, Device, DeviceFlags};
use serde::Serialize;

use crate::prelude::*;
mod link_type;
mod preview;

pub use link_type::*;
pub use preview::*;

#[derive(Clone, Debug)]
pub struct DeviceEx {
    name: String,
    desc: Option<String>,
    addresses: Vec<Address>,
    link_type: LinkTypeEx,
}

#[derive(Clone, Debug, Serialize)]
pub struct DeviceAddressSummary {
    pub addr: String,
    pub netmask: Option<String>,
    pub broadcast_addr: Option<String>,
    pub dst_addr: Option<String>,
}

#[derive(Clone, Debug, Serialize)]
pub struct DeviceSummary {
    pub name: String,
    pub desc: Option<String>,
    pub addresses: Vec<DeviceAddressSummary>,
}

impl DeviceEx {
    pub fn to_pcap_device(&self) -> Device {
        Device {
            name: self.name.clone(),
            desc: self.desc.clone(),
            addresses: self.addresses.clone(),
            flags: DeviceFlags::empty(),
        }
    }

    pub fn from_pcap_device(device: Device) -> Self {
        DeviceEx {
            name: device.name,
            desc: device.desc,
            addresses: device.addresses,
            link_type: LinkTypeEx::default(),
        }
    }

    pub fn get_name(&self) -> &String {
        &self.name
    }

    pub fn get_desc(&self) -> Option<&String> {
        self.desc.as_ref()
    }

    pub fn get_addresses(&self) -> &Vec<Address> {
        &self.addresses
    }

    pub fn set_addresses(&mut self, addresses: Vec<Address>) {
        self.addresses = addresses;
    }

    pub fn get_link_type(&self) -> LinkTypeEx {
        self.link_type
    }

    pub fn set_link_type(&mut self, link_type: LinkTypeEx) {
        self.link_type = link_type;
    }

    pub fn to_summary(&self) -> DeviceSummary {
        DeviceSummary {
            name: self.name.clone(),
            desc: self.desc.clone(),
            addresses: self
                .addresses
                .iter()
                .map(|address| DeviceAddressSummary {
                    addr: address.addr.to_string(),
                    netmask: address.netmask.map(|ip| ip.to_string()),
                    broadcast_addr: address.broadcast_addr.map(|ip| ip.to_string()),
                    dst_addr: address.dst_addr.map(|ip| ip.to_string()),
                })
                .collect(),
        }
    }
}

pub fn list_devices() -> Result<Vec<DeviceSummary>> {
    Device::list()
        .or_err(ErrorType::DeviceListError, "Failed to list capture devices")
        .map_err(|e| e.into_network())
        .map(|devices| {
            devices
                .into_iter()
                .map(DeviceEx::from_pcap_device)
                .map(|device| device.to_summary())
                .collect()
        })
}

pub fn get_device(name: &str) -> Result<DeviceEx> {
    Device::list()
        .or_err(ErrorType::DeviceListError, "Failed to list capture devices")
        .map_err(|e| e.into_network())
        .and_then(|devices| {
            devices
                .into_iter()
                .find(|device| device.name == name)
                .map(DeviceEx::from_pcap_device)
                .or_err_with(ErrorType::DeviceNotFound, || {
                    format!("Capture device not found: {name}")
                })
                .map_err(|e| e.into_network())
        })
}
