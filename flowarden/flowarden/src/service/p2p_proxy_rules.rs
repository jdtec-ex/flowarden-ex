//! Built-in heuristics for unauthorized P2P / proxy detection (ports, process names, SNI).

/// Well-known proxy / anonymity ports (destination or source).
pub(crate) fn is_proxy_port(port: u16) -> bool {
    matches!(
        port,
        1080 | 1081 | 3128 | 8118 | 8123 | 8888 | 9050 | 9051 | 9150 | 9222 | 7890 | 7891 | 10808
    ) || (8080..=8082).contains(&port)
}

/// Common BitTorrent / DHT style ports (narrow ranges to limit false positives).
pub(crate) fn is_p2p_port(port: u16) -> bool {
    (6881..=6889).contains(&port) || matches!(port, 51413 | 6771 | 6969)
}

pub(crate) fn process_looks_like_p2p_or_proxy(name: &str) -> bool {
    let n = name.to_ascii_lowercase();
    const NEEDLES: &[&str] = &[
        "transmission",
        "utorrent",
        "qbittorrent",
        "deluge",
        "rtorrent",
        "aria2",
        "v2ray",
        "xray",
        "clash",
        "sing-box",
        "singbox",
        "trojan",
        "shadowsocks",
        "ss-local",
        "proxifier",
        "tor",
        "privoxy",
        "openvpn",
        "wireguard",
        "tailscale", // VPN; still "proxy-class" tooling
    ];
    NEEDLES.iter().any(|needle| n.contains(needle))
}

pub(crate) fn sni_looks_like_proxy_infra(sni: &str) -> bool {
    let s = sni.to_ascii_lowercase();
    // Keep this list tight — CDN false positives are expensive.
    const NEEDLES: &[&str] = &[
        "cloudflare-relay",
        "torproject.org",
        "onion.",
    ];
    NEEDLES.iter().any(|needle| s.contains(needle))
}

#[allow(dead_code)] // reserved for service-label matching when label is available on rows
pub(crate) fn service_looks_like_p2p(service: &str) -> bool {
    let s = service.to_ascii_lowercase();
    s.contains("bittorrent") || s.contains("torrent") || s == "dht"
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn proxy_and_p2p_ports() {
        assert!(is_proxy_port(9050));
        assert!(is_p2p_port(6881));
        assert!(!is_proxy_port(443));
    }

    #[test]
    fn process_names() {
        assert!(process_looks_like_p2p_or_proxy("Clash for Windows"));
        assert!(!process_looks_like_p2p_or_proxy("Safari"));
    }
}
