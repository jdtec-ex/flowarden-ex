use crate::APPLICATION_NAME;
use crate::deserialize_or_default;
use crate::prelude::*;
use serde::{Deserialize, Serialize};

pub static CONF: std::sync::LazyLock<Config> = std::sync::LazyLock::new(Config::load);

#[derive(Serialize, Deserialize, Default, Clone, PartialEq, Debug)]
#[serde(default)]
pub struct Config {
    #[serde(deserialize_with = "deserialize_or_default")]
    pub capture_source_picklist: CaptureSourcePicklist,
}

impl Config {
    pub(crate) const FILE_NAME: &'static str = "config";

    fn load() -> Self {
        confy::load::<Config>(APPLICATION_NAME, Self::FILE_NAME).unwrap_or_else(|_| {
            let _ = Config::default().store();
            Config::default()
        })
    }

    pub fn store(&self) -> Result<()> {
        confy::store(APPLICATION_NAME, Self::FILE_NAME, self)
            .or_err(ErrorType::InternalError, "Failed to store config")
    }
}
