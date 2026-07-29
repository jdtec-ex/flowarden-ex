//! Light DPI policy: SNI extraction bounds (no stream reassembly).
//!
//! Snaplen is applied at capture open; SNI parsing may further cap the
//! TCP payload window so hot-path work stays bounded.

/// Default live capture snaplen (headers + typical TLS ClientHello).
pub const DEFAULT_LIVE_SNAPLEN: i32 = 512;

/// Snaplen when writing full packets to pcap out.
pub const FULL_PCAP_SNAPLEN: i32 = 65_535;

/// Max TCP payload bytes inspected for SNI (after headers).
pub const DEFAULT_SNI_MAX_PAYLOAD: usize = 512;

/// Minimum allowed live snaplen (Ethernet + IP + TCP headers).
pub const MIN_LIVE_SNAPLEN: i32 = 96;

/// Maximum allowed live snaplen for monitoring mode (not full dump).
pub const MAX_LIVE_SNAPLEN: i32 = 2048;

/// Runtime options for light DPI (SNI) extraction.
#[derive(Clone, Debug, PartialEq, Eq)]
pub struct LightDpiOptions {
    /// When false, skip SNI parsing entirely.
    pub sni_enabled: bool,
    /// Cap TCP payload bytes scanned for ClientHello SNI.
    pub sni_max_payload: usize,
}

impl Default for LightDpiOptions {
    fn default() -> Self {
        Self {
            sni_enabled: true,
            sni_max_payload: DEFAULT_SNI_MAX_PAYLOAD,
        }
    }
}

impl LightDpiOptions {
    pub fn disabled() -> Self {
        Self {
            sni_enabled: false,
            sni_max_payload: 0,
        }
    }

    pub fn with_sni_enabled(mut self, enabled: bool) -> Self {
        self.sni_enabled = enabled;
        self
    }

    pub fn with_sni_max_payload(mut self, max: usize) -> Self {
        self.sni_max_payload = max.clamp(0, 16_384);
        self
    }
}

/// Normalize monitoring snaplen; `None` → default live snaplen.
pub fn normalize_live_snaplen(snaplen: Option<i32>) -> i32 {
    match snaplen {
        None => DEFAULT_LIVE_SNAPLEN,
        Some(value) => value.clamp(MIN_LIVE_SNAPLEN, MAX_LIVE_SNAPLEN),
    }
}

/// Resolve snaplen for a capture open: full when dumping pcap, else live policy.
pub fn resolve_capture_snaplen(pcap_out: bool, configured: Option<i32>) -> i32 {
    if pcap_out {
        FULL_PCAP_SNAPLEN
    } else {
        normalize_live_snaplen(configured)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn live_snaplen_defaults_and_clamps() {
        assert_eq!(normalize_live_snaplen(None), DEFAULT_LIVE_SNAPLEN);
        assert_eq!(normalize_live_snaplen(Some(10)), MIN_LIVE_SNAPLEN);
        assert_eq!(normalize_live_snaplen(Some(99999)), MAX_LIVE_SNAPLEN);
        assert_eq!(normalize_live_snaplen(Some(1024)), 1024);
    }

    #[test]
    fn pcap_out_uses_full_snaplen() {
        assert_eq!(
            resolve_capture_snaplen(true, Some(128)),
            FULL_PCAP_SNAPLEN
        );
        assert_eq!(
            resolve_capture_snaplen(false, Some(128)),
            128
        );
    }
}
