use crate::deserialize_or_default;
use serde::{Deserialize, Serialize};

#[derive(Serialize, Deserialize, Clone, PartialEq, Debug, Default)]
#[serde(default)]
pub struct Filters {
    #[serde(deserialize_with = "deserialize_or_default")]
    pub(crate) expanded: bool,
    #[serde(deserialize_with = "deserialize_or_default")]
    pub(crate) bpf: String,
}

impl Filters {
    pub fn toggle(&mut self) {
        self.expanded = !self.expanded;
    }

    pub fn set_bpf(&mut self, bpf: String) {
        self.bpf = bpf;
    }

    pub fn expanded(&self) -> bool {
        self.expanded
    }

    pub fn bpf(&self) -> &str {
        &self.bpf
    }

    pub fn is_some_filter_active(&self) -> bool {
        self.expanded && !self.bpf.trim().is_empty()
    }
}
