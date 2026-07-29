use std::path::{Path, PathBuf};

use serde::{Deserialize, Serialize};

use crate::prelude::*;

#[derive(Clone, Eq, PartialEq, Debug, Copy, Default, Serialize, Deserialize)]
pub enum CaptureSourcePicklist {
    #[default]
    Device,
    File,
}
#[derive(Clone, Debug)]
pub struct PcapImport {
    pub path: PathBuf,
    pub link_type: LinkTypeEx,
}

impl PcapImport {
    pub fn new(path: PathBuf) -> Self {
        Self {
            path,
            link_type: LinkTypeEx::default(),
        }
    }

    pub fn path(&self) -> &Path {
        &self.path
    }
}

#[derive(Clone, Debug)]
pub enum CaptureSource {
    Device(DeviceEx),
    File(PcapImport),
}

impl CaptureSource {
    pub fn from_device_name(name: &str) -> Result<Self> {
        get_device(name)
            .map(Self::Device)
            .map_err(|e| e.more_context(format!("while resolving capture device `{name}`")))
    }

    pub fn from_file_path(path: PathBuf) -> Result<Self> {
        std::fs::metadata(&path)
            .or_err_with(ErrorType::FileReadError, || {
                format!("Failed to access capture file `{}`", path.display())
            })
            .map_err(|e| e.into_cli())?;

        Ok(Self::File(PcapImport::new(path)))
    }
}
