//! BPF helpers that keep resident capture off the control-plane traffic.

use std::net::{IpAddr, SocketAddr};

pub(crate) fn normalize_bpf(user_bpf: Option<&str>) -> Option<String> {
    user_bpf
        .map(str::trim)
        .filter(|value| !value.is_empty())
        .map(ToOwned::to_owned)
}

pub(crate) fn resident_capture_bpf(
    user_bpf: Option<&str>,
    control_bind: SocketAddr,
) -> Option<String> {
    let user_bpf = user_bpf.map(str::trim).filter(|value| !value.is_empty());
    let exclusion = control_plane_exclusion_bpf(control_bind);

    match (user_bpf, exclusion) {
        (Some(user_bpf), Some(exclusion)) => Some(format!("({user_bpf}) and not ({exclusion})")),
        (Some(user_bpf), None) => Some(user_bpf.to_string()),
        (None, Some(exclusion)) => Some(format!("not ({exclusion})")),
        (None, None) => None,
    }
}

pub(crate) fn control_plane_exclusion_bpf(control_bind: SocketAddr) -> Option<String> {
    let port = control_bind.port();
    if port == 0 {
        return None;
    }

    let host_filter = match control_bind.ip() {
        IpAddr::V4(ip) if ip.is_unspecified() => "(host 127.0.0.1 or host ::1)".to_string(),
        IpAddr::V6(ip) if ip.is_unspecified() => "(host 127.0.0.1 or host ::1)".to_string(),
        ip => format!("host {ip}"),
    };

    Some(format!("tcp and {host_filter} and port {port}"))
}
