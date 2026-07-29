//! Async local-process attribution for resident live captures.
//!
//! Lookup is off the capture hot path: projection only submits keys; a worker
//! fills a TTL cache. When the cache updates, an optional refresh callback
//! re-emits the current overview so UI sees enrichment without waiting forever.

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

use flowarden_core::{
    analysis::TransportProtocol,
    flow::{ConnectionSummary, FlowKey},
};
use listeners::Protocol as ListenersProtocol;

const CACHE_TTL: Duration = Duration::from_secs(60);
const NEGATIVE_TTL: Duration = Duration::from_secs(3);
const REQUEST_QUEUE_CAP: usize = 256;

#[derive(Clone, Debug, Default, PartialEq, Eq)]
pub(crate) struct ProcessInfo {
    pub(crate) name: String,
    pub(crate) pid: u32,
    /// Absolute path when the platform provides it (may be empty).
    pub(crate) path: String,
    /// Best-effort macOS bundle id derived from path (may be empty).
    pub(crate) bundle_id: String,
}

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
struct LookupKey {
    port: u16,
    protocol: ListenersProtocol,
}

#[derive(Clone, Debug)]
struct CacheEntry {
    process: Option<ProcessInfo>,
    instant: Instant,
}

pub(crate) struct ProcessLookup {
    request_tx: SyncSender<LookupKey>,
    cache: Arc<Mutex<HashMap<LookupKey, CacheEntry>>>,
    pending: Arc<Mutex<HashSet<LookupKey>>>,
    _worker: JoinHandle<()>,
}

impl ProcessLookup {
    pub(crate) fn spawn(on_updated: Option<Arc<dyn Fn() + Send + Sync>>) -> Self {
        let (request_tx, request_rx) = sync_channel::<LookupKey>(REQUEST_QUEUE_CAP);
        let cache = Arc::new(Mutex::new(HashMap::new()));
        let pending = Arc::new(Mutex::new(HashSet::new()));
        let cache_for_worker = Arc::clone(&cache);
        let pending_for_worker = Arc::clone(&pending);

        let worker = std::thread::Builder::new()
            .name("flowarden-process-lookup".to_string())
            .spawn(move || {
                worker_loop(request_rx, cache_for_worker, pending_for_worker, on_updated)
            })
            .expect("failed to spawn process lookup worker");

        Self {
            request_tx,
            cache,
            pending,
            _worker: worker,
        }
    }

    /// Pending OS lookups not yet resolved (diagnostic).
    pub(crate) fn pending_count(&self) -> usize {
        self.pending.lock().map(|set| set.len()).unwrap_or(0)
    }

    /// Approximate filled cache entries (diagnostic).
    pub(crate) fn cache_size(&self) -> usize {
        self.cache.lock().map(|map| map.len()).unwrap_or(0)
    }

    /// Returns a cache hit when present; otherwise schedules async lookup(s).
    pub(crate) fn resolve(
        &self,
        connection: &ConnectionSummary,
        local_ips: &HashSet<IpAddr>,
    ) -> Option<ProcessInfo> {
        let protocol = match connection.key.protocol {
            TransportProtocol::Tcp => ListenersProtocol::TCP,
            TransportProtocol::Udp => ListenersProtocol::UDP,
            _ => return None,
        };

        let ports = candidate_local_ports(&connection.key, local_ips, connection);
        if ports.is_empty() {
            return None;
        }

        let mut best: Option<ProcessInfo> = None;
        let mut to_schedule = Vec::new();

        if let Ok(cache) = self.cache.lock() {
            for port in ports {
                let key = LookupKey { port, protocol };
                if let Some(entry) = cache.get(&key) {
                    let ttl = if entry.process.is_some() {
                        CACHE_TTL
                    } else {
                        NEGATIVE_TTL
                    };
                    if entry.instant.elapsed() < ttl {
                        if let Some(process) = entry.process.clone() {
                            best = Some(process);
                            break;
                        }
                        // Negative hit for this port — try the other candidate.
                        continue;
                    }
                }
                to_schedule.push(key);
            }
        } else {
            to_schedule.extend(ports.into_iter().map(|port| LookupKey { port, protocol }));
        }

        for key in to_schedule {
            self.schedule(key);
        }

        best
    }

    fn schedule(&self, key: LookupKey) {
        if key.port == 0 {
            return;
        }
        if let Ok(mut pending) = self.pending.lock()
            && !pending.insert(key)
        {
            return;
        }

        match self.request_tx.try_send(key) {
            Ok(()) => {}
            Err(TrySendError::Full(_)) | Err(TrySendError::Disconnected(_)) => {
                if let Ok(mut pending) = self.pending.lock() {
                    pending.remove(&key);
                }
            }
        }
    }
}

fn worker_loop(
    request_rx: Receiver<LookupKey>,
    cache: Arc<Mutex<HashMap<LookupKey, CacheEntry>>>,
    pending: Arc<Mutex<HashSet<LookupKey>>>,
    on_updated: Option<Arc<dyn Fn() + Send + Sync>>,
) {
    while let Ok(key) = request_rx.recv() {
        let process = listeners::get_process_by_port(key.port, key.protocol)
            .ok()
            .map(|proc| {
                let path = proc.path;
                let bundle_id = derive_bundle_id(&path);
                ProcessInfo {
                    name: proc.name,
                    pid: proc.pid,
                    path,
                    bundle_id,
                }
            });
        let found = process.is_some();

        if let Ok(mut cache) = cache.lock() {
            cache.insert(
                key,
                CacheEntry {
                    process,
                    instant: Instant::now(),
                },
            );
            if cache.len() > 8_192 {
                let cutoff = Instant::now() - CACHE_TTL;
                cache.retain(|_, entry| entry.instant >= cutoff);
            }
        }

        if let Ok(mut pending) = pending.lock() {
            pending.remove(&key);
        }

        // Re-emit projection so UI picks up cache hits without waiting for a new tick.
        if found && let Some(refresh) = on_updated.as_ref() {
            refresh();
        }
    }
}

/// Prefer the local side of the flow; if local IPs are unknown/mismatched, try both ports.
/// Trying both is important: selecting the remote service port (e.g. 443) always fails process lookup.
fn candidate_local_ports(
    key: &FlowKey,
    local_ips: &HashSet<IpAddr>,
    connection: &ConnectionSummary,
) -> Vec<u16> {
    let mut ports = Vec::with_capacity(2);

    let push = |ports: &mut Vec<u16>, port: Option<u16>| {
        if let Some(port) = port
            && port != 0
            && !ports.contains(&port)
        {
            ports.push(port);
        }
    };

    if !local_ips.is_empty() {
        if local_ips.contains(&key.source_ip) {
            push(&mut ports, key.source_port);
        }
        if local_ips.contains(&key.destination_ip) {
            push(&mut ports, key.destination_port);
        }
        if !ports.is_empty() {
            return ports;
        }
    }

    // Fallback when address list does not match packet IPs (VPN/bridge/alias).
    // Prefer the side that looks local by direction counters, then try the other.
    if connection.counters.bytes_out >= connection.counters.bytes_in {
        push(&mut ports, key.source_port);
        push(&mut ports, key.destination_port);
    } else {
        push(&mut ports, key.destination_port);
        push(&mut ports, key.source_port);
    }

    // Prefer ephemeral-looking ports when both present (remote is often well-known).
    ports.sort_by_key(|port| if *port >= 1024 { 0u8 } else { 1u8 });
    ports
}

/// Best-effort extraction of a macOS bundle id from an absolute path into a `.app` bundle.
fn derive_bundle_id(path: &str) -> String {
    // Typical: /Applications/Foo.app/Contents/MacOS/Foo → leave empty (bundle id needs Info.plist).
    // We surface the .app path leaf as a weak identity key for UI icon lookup when path is enough.
    // Keep this conservative: only emit something when path contains ".app/".
    let Some(app_idx) = path.find(".app/") else {
        if path.ends_with(".app") {
            return path
                .rsplit('/')
                .next()
                .unwrap_or_default()
                .trim_end_matches(".app")
                .to_string();
        }
        return String::new();
    };
    let app_prefix = &path[..=app_idx + 3]; // include ".app"
    app_prefix
        .rsplit('/')
        .next()
        .unwrap_or_default()
        .trim_end_matches(".app")
        .to_string()
}

#[cfg(test)]
mod tests {
    use super::*;
    use flowarden_core::{
        analysis::TransportProtocol,
        flow::{FlowCounters, FlowKey, PacketTimestamp},
    };
    use std::net::{IpAddr, Ipv4Addr};

    fn sample_connection(source_port: u16, dest_port: u16) -> ConnectionSummary {
        ConnectionSummary {
            key: FlowKey {
                source_ip: IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)),
                destination_ip: IpAddr::V4(Ipv4Addr::new(1, 1, 1, 1)),
                source_port: Some(source_port),
                destination_port: Some(dest_port),
                protocol: TransportProtocol::Tcp,
            },
            counters: FlowCounters {
                packets: 2,
                bytes: 200,
                packets_in: 0,
                packets_out: 2,
                bytes_in: 0,
                bytes_out: 200,
                first_seen: PacketTimestamp::tick(1),
                last_seen: PacketTimestamp::tick(1),
                tcp_stats: None,
                sni: None,
            },
        }
    }

    #[test]
    fn derive_bundle_id_from_app_path() {
        assert_eq!(
            derive_bundle_id("/Applications/Safari.app/Contents/MacOS/Safari"),
            "Safari"
        );
        assert_eq!(derive_bundle_id("/usr/bin/ssh"), "");
    }

    #[test]
    fn candidates_prefer_local_ip_side() {
        let connection = sample_connection(55_555, 443);
        let mut local_ips = HashSet::new();
        local_ips.insert(IpAddr::V4(Ipv4Addr::new(192, 168, 1, 10)));
        assert_eq!(
            candidate_local_ports(&connection.key, &local_ips, &connection),
            vec![55_555]
        );
    }

    #[test]
    fn candidates_try_ephemeral_first_when_local_ips_miss() {
        let connection = sample_connection(55_555, 443);
        let local_ips = HashSet::new();
        assert_eq!(
            candidate_local_ports(&connection.key, &local_ips, &connection),
            vec![55_555, 443]
        );
    }
}
