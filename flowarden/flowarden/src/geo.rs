use std::{collections::HashMap, net::IpAddr};

use maxminddb::{MaxMindDbError, Reader};
use serde::Deserialize;

const COUNTRY_MMDB: &[u8] = include_bytes!("../resources/DB/GeoLite2-Country.mmdb");
const ASN_MMDB: &[u8] = include_bytes!("../resources/DB/GeoLite2-ASN.mmdb");

const GEO_CACHE_CAP: usize = 20_000;

#[derive(Clone, Debug, PartialEq, Eq)]
pub enum CountryKind {
    Country,
    Loopback,
    Local,
    Unknown,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct CountryInfo {
    pub code: String,
    pub name: String,
    pub kind: CountryKind,
}

impl CountryInfo {
    pub fn display_label(&self) -> String {
        match self.kind {
            CountryKind::Country => format!("{} · {}", self.code, self.name),
            CountryKind::Loopback => "Loopback".to_string(),
            CountryKind::Local => "Local".to_string(),
            CountryKind::Unknown => "Unknown".to_string(),
        }
    }
}

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub struct AsnInfo {
    /// 0 means unknown / not applicable.
    pub number: u32,
    pub organization: String,
}

impl AsnInfo {
    pub fn is_known(&self) -> bool {
        self.number != 0
    }

    pub fn display_label(&self) -> String {
        if !self.is_known() {
            return String::new();
        }
        if self.organization.is_empty() {
            format!("AS{}", self.number)
        } else {
            format!("AS{} · {}", self.number, self.organization)
        }
    }
}

pub struct GeoCountryResolver {
    country_reader: Reader<&'static [u8]>,
    asn_reader: Reader<&'static [u8]>,
    country_cache: HashMap<IpAddr, CountryInfo>,
    asn_cache: HashMap<IpAddr, AsnInfo>,
}

impl GeoCountryResolver {
    pub fn new() -> Result<Self, MaxMindDbError> {
        Ok(Self {
            country_reader: Reader::from_source(COUNTRY_MMDB)?,
            asn_reader: Reader::from_source(ASN_MMDB)?,
            country_cache: HashMap::new(),
            asn_cache: HashMap::new(),
        })
    }

    pub fn resolve(&mut self, ip: IpAddr) -> CountryInfo {
        if let Some(cached) = self.country_cache.get(&ip) {
            return cached.clone();
        }

        let resolved = resolve_ip_country(&self.country_reader, ip);
        evict_half_if_full(&mut self.country_cache, GEO_CACHE_CAP);
        self.country_cache.insert(ip, resolved.clone());
        resolved
    }

    pub fn resolve_asn(&mut self, ip: IpAddr) -> AsnInfo {
        if let Some(cached) = self.asn_cache.get(&ip) {
            return cached.clone();
        }

        let resolved = resolve_ip_asn(&self.asn_reader, ip);
        evict_half_if_full(&mut self.asn_cache, GEO_CACHE_CAP);
        self.asn_cache.insert(ip, resolved.clone());
        resolved
    }
}

fn evict_half_if_full<V>(cache: &mut HashMap<IpAddr, V>, cap: usize) {
    if cache.len() < cap {
        return;
    }
    let drop_count = cache.len() / 2;
    let victims: Vec<IpAddr> = cache.keys().copied().take(drop_count).collect();
    for victim in victims {
        cache.remove(&victim);
    }
}

#[derive(Clone, Debug, Deserialize)]
struct CountryRecord {
    country: Option<CountryInner>,
}

#[derive(Clone, Debug, Deserialize)]
struct CountryInner {
    iso_code: Option<String>,
    names: Option<HashMap<String, String>>,
}

#[derive(Clone, Debug, Deserialize)]
struct AsnRecord {
    #[serde(rename = "autonomous_system_number")]
    number: Option<u32>,
    #[serde(rename = "autonomous_system_organization")]
    organization: Option<String>,
}

fn resolve_ip_country(reader: &Reader<&'static [u8]>, ip: IpAddr) -> CountryInfo {
    if ip.is_loopback() {
        return CountryInfo {
            code: "LO".to_string(),
            name: "Loopback".to_string(),
            kind: CountryKind::Loopback,
        };
    }

    if is_local_ip(&ip) {
        return CountryInfo {
            code: "LOCAL".to_string(),
            name: "Local".to_string(),
            kind: CountryKind::Local,
        };
    }

    let record: Option<CountryRecord> = reader
        .lookup(ip)
        .ok()
        .and_then(|lookup| lookup.decode().ok().flatten());

    if let Some(record) = record
        && let Some(country) = record.country
        && let Some(code) = country.iso_code
    {
        let name = country
            .names
            .and_then(|mut names| names.remove("en"))
            .unwrap_or_else(|| code.clone());
        return CountryInfo {
            code,
            name,
            kind: CountryKind::Country,
        };
    }

    CountryInfo {
        code: "ZZ".to_string(),
        name: "Unknown".to_string(),
        kind: CountryKind::Unknown,
    }
}

fn resolve_ip_asn(reader: &Reader<&'static [u8]>, ip: IpAddr) -> AsnInfo {
    if ip.is_loopback() || is_local_ip(&ip) {
        return AsnInfo::default();
    }

    let record: Option<AsnRecord> = reader
        .lookup(ip)
        .ok()
        .and_then(|lookup| lookup.decode().ok().flatten());

    match record {
        Some(AsnRecord {
            number: Some(number),
            organization,
        }) if number != 0 => AsnInfo {
            number,
            organization: organization.unwrap_or_default(),
        },
        _ => AsnInfo::default(),
    }
}

fn is_local_ip(ip: &IpAddr) -> bool {
    match ip {
        IpAddr::V4(v4) => v4.is_private() || v4.is_link_local(),
        IpAddr::V6(v6) => v6.is_unique_local() || v6.is_unicast_link_local(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::net::Ipv4Addr;

    #[test]
    fn asn_resolves_public_google_dns() {
        let mut geo = GeoCountryResolver::new().expect("mmdb load");
        let asn = geo.resolve_asn(IpAddr::V4(Ipv4Addr::new(8, 8, 8, 8)));
        assert_eq!(asn.number, 15169);
        assert!(
            asn.organization.to_ascii_lowercase().contains("google"),
            "org={}",
            asn.organization
        );
        assert!(asn.display_label().starts_with("AS15169"));
    }

    #[test]
    fn asn_skips_private_and_loopback() {
        let mut geo = GeoCountryResolver::new().expect("mmdb load");
        assert!(!geo
            .resolve_asn(IpAddr::V4(Ipv4Addr::new(192, 168, 1, 1)))
            .is_known());
        assert!(!geo
            .resolve_asn(IpAddr::V4(Ipv4Addr::new(127, 0, 0, 1)))
            .is_known());
    }
}
