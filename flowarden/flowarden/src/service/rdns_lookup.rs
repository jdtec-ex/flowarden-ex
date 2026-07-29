//! Async reverse-DNS enrichment with per-lookup timeout and overview refresh.
//!
//! System PTR (`getnameinfo`) can hang for many seconds on some networks. Without a
//! timeout, a single worker stalls and Top Hosts never receives hostnames.

use std::{
    collections::{HashMap, HashSet},
    net::IpAddr,
    sync::{
        Arc, Mutex,
        mpsc::{Receiver, SyncSender, TrySendError, sync_channel},
    },
    thread::JoinHandle,
    time::{Duration, Instant},
};

const CACHE_TTL: Duration = Duration::from_secs(300);
const NEGATIVE_TTL: Duration = Duration::from_secs(45);
const REQUEST_QUEUE_CAP: usize = 256;
const LOOKUP_TIMEOUT: Duration = Duration::from_millis(1_200);
const WORKER_COUNT: usize = 3;

#[derive(Clone, Debug)]
struct CacheEntry {
    name: Option<String>,
    instant: Instant,
}

pub(crate) struct RdnsLookup {
    request_tx: SyncSender<IpAddr>,
    cache: Arc<Mutex<HashMap<IpAddr, CacheEntry>>>,
    pending: Arc<Mutex<HashSet<IpAddr>>>,
    _workers: Vec<JoinHandle<()>>,
}

impl RdnsLookup {
    pub(crate) fn spawn(on_updated: Option<Arc<dyn Fn() + Send + Sync>>) -> Self {
        let (request_tx, request_rx) = sync_channel(REQUEST_QUEUE_CAP);
        let request_rx = Arc::new(Mutex::new(request_rx));
        let cache = Arc::new(Mutex::new(HashMap::new()));
        let pending = Arc::new(Mutex::new(HashSet::new()));

        let mut workers = Vec::with_capacity(WORKER_COUNT);
        for index in 0..WORKER_COUNT {
            let request_rx = Arc::clone(&request_rx);
            let cache = Arc::clone(&cache);
            let pending = Arc::clone(&pending);
            let on_updated = on_updated.clone();
            let worker = std::thread::Builder::new()
                .name(format!("flowarden-rdns-{index}"))
                .spawn(move || worker_loop(request_rx, cache, pending, on_updated))
                .expect("failed to spawn rdns lookup worker");
            workers.push(worker);
        }

        Self {
            request_tx,
            cache,
            pending,
            _workers: workers,
        }
    }

    pub(crate) fn resolve(&self, ip: IpAddr) -> Option<String> {
        if ip.is_loopback() || ip.is_unspecified() || ip.is_multicast() {
            return None;
        }
        // Private ranges rarely have useful public PTR records.
        if is_non_public(ip) {
            return None;
        }

        if let Ok(cache) = self.cache.lock()
            && let Some(entry) = cache.get(&ip)
        {
            let ttl = if entry.name.is_some() {
                CACHE_TTL
            } else {
                NEGATIVE_TTL
            };
            if entry.instant.elapsed() < ttl {
                return entry.name.clone();
            }
        }

        self.schedule(ip);
        None
    }

    fn schedule(&self, ip: IpAddr) {
        if let Ok(mut pending) = self.pending.lock()
            && !pending.insert(ip)
        {
            return;
        }

        match self.request_tx.try_send(ip) {
            Ok(()) => {}
            Err(TrySendError::Full(_)) | Err(TrySendError::Disconnected(_)) => {
                if let Ok(mut pending) = self.pending.lock() {
                    pending.remove(&ip);
                }
            }
        }
    }
}

fn worker_loop(
    request_rx: Arc<Mutex<Receiver<IpAddr>>>,
    cache: Arc<Mutex<HashMap<IpAddr, CacheEntry>>>,
    pending: Arc<Mutex<HashSet<IpAddr>>>,
    on_updated: Option<Arc<dyn Fn() + Send + Sync>>,
) {
    loop {
        let ip = {
            let Ok(guard) = request_rx.lock() else {
                break;
            };
            match guard.recv() {
                Ok(ip) => ip,
                Err(_) => break,
            }
        };

        let name = lookup_addr_with_timeout(ip, LOOKUP_TIMEOUT);
        let found = name.is_some();

        if let Ok(mut cache) = cache.lock() {
            cache.insert(
                ip,
                CacheEntry {
                    name,
                    instant: Instant::now(),
                },
            );
            if cache.len() > 8_192 {
                let cutoff = Instant::now() - CACHE_TTL;
                cache.retain(|_, entry| entry.instant >= cutoff);
            }
        }
        if let Ok(mut pending) = pending.lock() {
            pending.remove(&ip);
        }

        if found && let Some(refresh) = on_updated.as_ref() {
            refresh();
        }
    }
}

fn lookup_addr_with_timeout(ip: IpAddr, timeout: Duration) -> Option<String> {
    let (tx, rx) = std::sync::mpsc::channel();
    let join = std::thread::Builder::new()
        .name("flowarden-rdns-one".to_string())
        .spawn(move || {
            let result = dns_lookup::lookup_addr(&ip)
                .ok()
                .map(|value| value.trim().trim_end_matches('.').to_string())
                .filter(|value| !value.is_empty() && *value != ip.to_string());
            let _ = tx.send(result);
        })
        .ok()?;

    let result: Option<String> = rx.recv_timeout(timeout).unwrap_or_default();
    // Detach the lookup thread on timeout; it will exit when getnameinfo returns.
    drop(join);
    result
}

fn is_non_public(ip: IpAddr) -> bool {
    match ip {
        IpAddr::V4(v4) => v4.is_private() || v4.is_link_local() || v4.is_broadcast(),
        IpAddr::V6(v6) => v6.is_unique_local() || v6.is_unicast_link_local(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::net::{IpAddr, Ipv4Addr};

    #[test]
    fn private_ips_are_skipped() {
        let lookup = RdnsLookup::spawn(None);
        assert!(
            lookup
                .resolve(IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)))
                .is_none()
        );
    }
}
