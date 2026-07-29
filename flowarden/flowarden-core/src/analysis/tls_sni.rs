//! Lightweight TLS ClientHello SNI extraction (no stream reassembly).
//!
//! Parses a single TCP payload that begins with a TLS handshake record.
//! Incomplete / truncated payloads return `None` without error.

use super::light_dpi::LightDpiOptions;

/// Extract Server Name Indication from a TLS ClientHello payload, if present.
pub fn extract_sni_from_tcp_payload(payload: &[u8]) -> Option<String> {
    extract_sni_from_tcp_payload_with_options(payload, &LightDpiOptions::default())
}

/// Extract SNI respecting light-DPI enable/max-payload policy.
pub fn extract_sni_from_tcp_payload_with_options(
    payload: &[u8],
    options: &LightDpiOptions,
) -> Option<String> {
    if !options.sni_enabled || options.sni_max_payload == 0 {
        return None;
    }
    let window = payload.len().min(options.sni_max_payload);
    extract_sni_from_tcp_payload_raw(&payload[..window])
}

fn extract_sni_from_tcp_payload_raw(payload: &[u8]) -> Option<String> {
    // TLS record: type(1)=0x16 handshake, version(2), length(2)
    if payload.len() < 5 {
        return None;
    }
    if payload[0] != 0x16 {
        return None;
    }
    let record_len = u16::from_be_bytes([payload[3], payload[4]]) as usize;
    if payload.len() < 5 + record_len.min(4) {
        // Need at least handshake header inside the record.
    }
    let handshake = payload.get(5..)?;
    // Handshake: type(1)=0x01 ClientHello, length(3)
    if handshake.first().copied()? != 0x01 {
        return None;
    }
    if handshake.len() < 4 {
        return None;
    }
    let hs_len = u32::from_be_bytes([0, handshake[1], handshake[2], handshake[3]]) as usize;
    let body = handshake.get(4..4 + hs_len.min(handshake.len().saturating_sub(4)))?;
    parse_client_hello_body(body)
}

fn parse_client_hello_body(mut body: &[u8]) -> Option<String> {
    // legacy_version(2) + random(32)
    if body.len() < 34 {
        return None;
    }
    body = &body[34..];

    // session_id
    let sid_len = *body.first()? as usize;
    body = body.get(1 + sid_len..)?;

    // cipher_suites
    if body.len() < 2 {
        return None;
    }
    let cs_len = u16::from_be_bytes([body[0], body[1]]) as usize;
    body = body.get(2 + cs_len..)?;

    // compression_methods
    let comp_len = *body.first()? as usize;
    body = body.get(1 + comp_len..)?;

    // extensions
    if body.len() < 2 {
        return None;
    }
    let ext_total = u16::from_be_bytes([body[0], body[1]]) as usize;
    let mut ext = body.get(2..2 + ext_total.min(body.len().saturating_sub(2)))?;

    while ext.len() >= 4 {
        let ext_type = u16::from_be_bytes([ext[0], ext[1]]);
        let ext_len = u16::from_be_bytes([ext[2], ext[3]]) as usize;
        let ext_data = ext.get(4..4 + ext_len)?;
        if ext_type == 0x0000 {
            return parse_sni_extension(ext_data);
        }
        ext = ext.get(4 + ext_len..)?;
    }
    None
}

fn parse_sni_extension(data: &[u8]) -> Option<String> {
    // server_name_list length(2)
    if data.len() < 2 {
        return None;
    }
    let list_len = u16::from_be_bytes([data[0], data[1]]) as usize;
    let mut list = data.get(2..2 + list_len.min(data.len().saturating_sub(2)))?;

    while list.len() >= 3 {
        let name_type = list[0];
        let name_len = u16::from_be_bytes([list[1], list[2]]) as usize;
        let name_bytes = list.get(3..3 + name_len)?;
        if name_type == 0 {
            // host_name
            let name = std::str::from_utf8(name_bytes).ok()?.trim();
            if !name.is_empty() && name.len() <= 253 {
                return Some(name.to_string());
            }
        }
        list = list.get(3 + name_len..)?;
    }
    None
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Minimal synthetic ClientHello with SNI=example.com
    fn client_hello_with_sni(sni: &str) -> Vec<u8> {
        let sni_bytes = sni.as_bytes();
        // SNI extension body: list_len(2) + type(1) + name_len(2) + name
        let mut sni_ext_body = Vec::new();
        let name_entry_len = 1 + 2 + sni_bytes.len();
        sni_ext_body.extend_from_slice(&(name_entry_len as u16).to_be_bytes());
        sni_ext_body.push(0); // host_name
        sni_ext_body.extend_from_slice(&(sni_bytes.len() as u16).to_be_bytes());
        sni_ext_body.extend_from_slice(sni_bytes);

        let mut extensions = Vec::new();
        extensions.extend_from_slice(&0u16.to_be_bytes()); // type SNI
        extensions.extend_from_slice(&(sni_ext_body.len() as u16).to_be_bytes());
        extensions.extend_from_slice(&sni_ext_body);

        let mut body = Vec::new();
        body.extend_from_slice(&0x0303u16.to_be_bytes()); // version
        body.extend_from_slice(&[0u8; 32]); // random
        body.push(0); // session id len
        body.extend_from_slice(&2u16.to_be_bytes()); // cipher suites len
        body.extend_from_slice(&0x1301u16.to_be_bytes()); // one suite
        body.push(1); // compression methods len
        body.push(0); // null
        body.extend_from_slice(&(extensions.len() as u16).to_be_bytes());
        body.extend_from_slice(&extensions);

        let mut handshake = Vec::new();
        handshake.push(0x01); // ClientHello
        let hs_len = body.len() as u32;
        handshake.push(((hs_len >> 16) & 0xff) as u8);
        handshake.push(((hs_len >> 8) & 0xff) as u8);
        handshake.push((hs_len & 0xff) as u8);
        handshake.extend_from_slice(&body);

        let mut record = Vec::new();
        record.push(0x16); // handshake
        record.extend_from_slice(&0x0301u16.to_be_bytes());
        record.extend_from_slice(&(handshake.len() as u16).to_be_bytes());
        record.extend_from_slice(&handshake);
        record
    }

    #[test]
    fn extracts_example_com() {
        let payload = client_hello_with_sni("example.com");
        assert_eq!(
            extract_sni_from_tcp_payload(&payload).as_deref(),
            Some("example.com")
        );
    }

    #[test]
    fn rejects_non_tls() {
        assert!(extract_sni_from_tcp_payload(&[1, 2, 3, 4, 5]).is_none());
    }

    #[test]
    fn respects_disabled_and_max_payload() {
        let payload = client_hello_with_sni("example.com");
        assert!(
            extract_sni_from_tcp_payload_with_options(&payload, &LightDpiOptions::disabled())
                .is_none()
        );
        // Too small window → incomplete ClientHello → None
        let tiny = LightDpiOptions::default().with_sni_max_payload(8);
        assert!(extract_sni_from_tcp_payload_with_options(&payload, &tiny).is_none());
        let enough = LightDpiOptions::default().with_sni_max_payload(payload.len());
        assert_eq!(
            extract_sni_from_tcp_payload_with_options(&payload, &enough).as_deref(),
            Some("example.com")
        );
    }
}
