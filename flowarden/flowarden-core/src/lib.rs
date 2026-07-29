use serde::{Deserialize, Deserializer};

pub const APPLICATION_NAME: &str = "flowarden";
pub mod analysis;
pub mod capture;
pub mod config;
pub mod device;
pub mod filters;
pub mod flow;

pub mod prelude {
    pub use crate::analysis::*;
    pub use crate::capture::*;
    pub use crate::config::*;
    pub use crate::device::*;
    pub use crate::filters::*;
    pub use crate::flow::*;
    pub use flowarden_error::{ErrorType::*, *};
}

pub(crate) fn deserialize_or_default<'de, T, D>(deserializer: D) -> Result<T, D::Error>
where
    T: Deserialize<'de> + Default,
    D: Deserializer<'de>,
{
    Ok(T::deserialize(deserializer).unwrap_or_default())
}
