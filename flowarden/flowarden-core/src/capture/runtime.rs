use std::{
    collections::VecDeque,
    path::PathBuf,
    sync::{
        Arc,
        atomic::{AtomicBool, Ordering},
    },
    time::{Duration, Instant},
};

use crate::prelude::*;

#[derive(Clone, Debug)]
pub enum RuntimeMode {
    Live,
    Offline,
}

/// How finished/report tick history is retained during a capture run.
#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub enum TickHistoryMode {
    /// Keep every emitted tick for the final report (CLI / forensic).
    #[default]
    Full,
    /// Keep only the most recent N ticks for live resident captures.
    Windowed(usize),
}

#[derive(Clone)]
pub struct RuntimeConfig {
    pub bpf: Option<String>,
    pub pcap_output_path: Option<PathBuf>,
    pub duration_limit: Option<Duration>,
    pub tick_observer: Option<Arc<dyn RuntimeTickObserver>>,
    pub tick_observer_window: Option<usize>,
    pub tick_history_mode: TickHistoryMode,
    pub aggregator_mode: AggregatorMode,
    pub resident_bounds: ResidentBounds,
    /// Live capture snaplen override (`None` → default Light DPI snaplen).
    pub snaplen: Option<i32>,
    /// SNI extraction bounds for Light DPI.
    pub light_dpi: LightDpiOptions,
}

impl RuntimeConfig {
    /// CLI / golden path: full aggregation and full tick history.
    pub fn forensic() -> Self {
        Self {
            bpf: None,
            pcap_output_path: None,
            duration_limit: None,
            tick_observer: None,
            tick_observer_window: None,
            tick_history_mode: TickHistoryMode::Full,
            aggregator_mode: AggregatorMode::Forensic,
            resident_bounds: ResidentBounds::default(),
            snaplen: None,
            light_dpi: LightDpiOptions::default(),
        }
    }

    /// Resident core live path: soft-capped maps and windowed ticks.
    pub fn resident(
        tick_observer: Option<Arc<dyn RuntimeTickObserver>>,
        tick_window: usize,
    ) -> Self {
        Self {
            bpf: None,
            pcap_output_path: None,
            duration_limit: None,
            tick_observer,
            tick_observer_window: Some(tick_window),
            tick_history_mode: TickHistoryMode::Windowed(tick_window),
            aggregator_mode: AggregatorMode::Resident,
            resident_bounds: ResidentBounds::default(),
            snaplen: None,
            light_dpi: LightDpiOptions::default(),
        }
    }

    pub fn with_bpf(mut self, bpf: Option<String>) -> Self {
        self.bpf = bpf;
        self
    }

    pub fn with_pcap_output_path(mut self, path: Option<PathBuf>) -> Self {
        self.pcap_output_path = path;
        self
    }

    pub fn with_duration_limit(mut self, limit: Option<Duration>) -> Self {
        self.duration_limit = limit;
        self
    }

    pub fn with_snaplen(mut self, snaplen: Option<i32>) -> Self {
        self.snaplen = snaplen;
        self
    }

    pub fn with_light_dpi(mut self, light_dpi: LightDpiOptions) -> Self {
        self.light_dpi = light_dpi;
        self
    }
}

#[derive(Clone, Debug, Default)]
pub struct RuntimeStats {
    pub packets_seen: u64,
    pub bytes_seen: u64,
    pub packets_decoded: u64,
    pub packets_decode_failed: u64,
}

#[derive(Clone, Debug)]
pub struct RuntimeReport {
    pub mode: RuntimeMode,
    pub link_type: LinkTypeEx,
    pub stats: RuntimeStats,
    pub timed_out_ticks: u64,
    pub stopped_by_request: bool,
    pub tick_snapshots: Vec<TickSnapshot>,
    pub offline_gaps: Vec<OfflineGap>,
    pub final_snapshot: FinalSnapshot,
}

#[derive(Clone, Debug)]
pub struct RuntimeProgressSnapshot {
    pub tick_snapshots: Vec<TickSnapshot>,
    pub offline_gaps: Vec<OfflineGap>,
    pub final_snapshot: FinalSnapshot,
}

pub struct CaptureRuntime {
    source: CaptureSource,
    config: RuntimeConfig,
    stop_requested: Arc<AtomicBool>,
    pause_requested: Arc<AtomicBool>,
}

pub trait RuntimeTickObserver: Send + Sync {
    fn observe_progress(&self, snapshot: RuntimeProgressSnapshot);
}

#[derive(Default)]
struct ProgressTickWindow {
    limit: Option<usize>,
    ticks: VecDeque<TickSnapshot>,
}

impl ProgressTickWindow {
    fn new(limit: Option<usize>) -> Self {
        Self {
            limit,
            ticks: VecDeque::new(),
        }
    }

    fn push_many(&mut self, snapshots: &[TickSnapshot]) {
        for snapshot in snapshots {
            self.ticks.push_back(snapshot.clone());
            if let Some(limit) = self.limit {
                while self.ticks.len() > limit {
                    self.ticks.pop_front();
                }
            }
        }
    }

    fn to_vec(&self) -> Vec<TickSnapshot> {
        self.ticks.iter().cloned().collect()
    }
}

impl CaptureRuntime {
    pub fn new(source: CaptureSource, config: RuntimeConfig) -> Self {
        Self {
            source,
            config,
            stop_requested: Arc::new(AtomicBool::new(false)),
            pause_requested: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn stop_handle(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.stop_requested)
    }

    pub fn pause_handle(&self) -> Arc<AtomicBool> {
        Arc::clone(&self.pause_requested)
    }

    pub fn run(&self) -> Result<RuntimeReport> {
        let mut capture = CaptureType::from_source_with_snaplen(
            &self.source,
            self.config.pcap_output_path.as_deref(),
            self.config.snaplen,
        )?;
        if let Some(bpf) = &self.config.bpf
            && !bpf.trim().is_empty()
        {
            capture
                .set_bpf(bpf)
                .map_err(|e| e.more_context(format!("while applying BPF `{bpf}`")))?;
        }
        let light_dpi = self.config.light_dpi.clone();

        let link_type = capture.link_type();
        if !link_type.is_supported() {
            return Error::explain(
                ErrorType::UnsupportedLinkType,
                link_type.full_print_on_one_line(),
            )
            .into_network()
            .into_err();
        }

        let mode = if matches!(self.source, CaptureSource::Device(_)) {
            RuntimeMode::Live
        } else {
            RuntimeMode::Offline
        };
        let resume_bpf = self.config.bpf.as_deref();
        let mut capture_paused = false;
        let direction_context = DirectionContext::from_source(&self.source);
        let mut aggregator = FlowAggregator::new(
            AggregatorConfig {
                capture_id: capture_id(&self.source),
                started_at: aggregator_started_at(&mode)?,
                mode: self.config.aggregator_mode,
                resident_bounds: self.config.resident_bounds,
                local_ips: Vec::new(),
            }
            .with_local_ips(direction_context.local_ips().to_vec()),
        );

        let started = Instant::now();
        let mut timed_out_ticks = 0_u64;
        let mut stats = RuntimeStats::default();
        let mut stopped_by_request = false;
        let mut offline_gaps = Vec::new();
        let report_tick_limit = effective_report_tick_limit(&self.config.tick_history_mode, &mode);
        let mut report_tick_window = ProgressTickWindow::new(report_tick_limit);
        let mut progress_tick_window = ProgressTickWindow::new(self.config.tick_observer_window);
        let mut savefile = capture.create_savefile(self.config.pcap_output_path.as_deref())?;

        loop {
            if self.stop_requested.load(Ordering::Relaxed) {
                stopped_by_request = true;
                break;
            }

            if let Some(limit) = self.config.duration_limit
                && started.elapsed() >= limit
            {
                break;
            }

            let want_pause = self.pause_requested.load(Ordering::Relaxed);
            if want_pause != capture_paused {
                if want_pause {
                    capture.pause();
                } else {
                    capture.resume(resume_bpf);
                }
                capture_paused = want_pause;
            }

            if capture_paused {
                // Freeze aggregation while paused. Live devices keep the BPF
                // pause filter; offline waits without consuming pcap time.
                std::thread::sleep(Duration::from_millis(50));
                continue;
            }

            match capture.next_packet()? {
                Some(packet) => {
                    if let Some(savefile) = savefile.as_mut() {
                        let header = pcap::PacketHeader {
                            ts: libc::timeval {
                                // Narrow i64 capture timestamps back into platform timeval fields.
                                tv_sec: packet.timestamp_sec as _,
                                tv_usec: packet.timestamp_usec as _,
                            },
                            caplen: packet.captured_len,
                            len: packet.original_len,
                        };
                        let pcap_packet = pcap::Packet::new(&header, &packet.data);
                        savefile.write(&pcap_packet);
                    }
                    let timestamp_sec = packet.timestamp_sec;
                    let timestamp_usec = packet.timestamp_usec;
                    let caplen = packet.captured_len;
                    let packet_len = packet.original_len;
                    let packet_link_type = packet.link_type;
                    let data = packet.data;
                    let packet_timestamp = PacketTimestamp::new(timestamp_sec, timestamp_usec);

                    stats.packets_seen += 1;
                    stats.bytes_seen += packet_len as u64;
                    let envelope = PacketEnvelope::new(
                        timestamp_sec,
                        timestamp_usec,
                        caplen,
                        packet_len,
                        packet_link_type,
                        data,
                    );
                    let dropped_packets = capture_dropped_packets(&mut capture)?;
                    let (emitted, emitted_gaps) = if matches!(mode, RuntimeMode::Offline) {
                        let advance = aggregator
                            .observe_offline_packet_time(packet_timestamp, dropped_packets);
                        (advance.tick_snapshots, advance.gaps)
                    } else {
                        (
                            aggregator.observe_packet_time(packet_timestamp, dropped_packets),
                            Vec::new(),
                        )
                    };
                    publish_tick_progress(TickProgressPublish {
                        observer: &self.config.tick_observer,
                        progress_tick_window: &mut progress_tick_window,
                        aggregator: &aggregator,
                        emitted: &emitted,
                        offline_gaps: offline_gaps
                            .iter()
                            .chain(emitted_gaps.iter())
                            .copied()
                            .collect(),
                        ended_at: emitted
                            .last()
                            .map(|snapshot| snapshot.timestamp)
                            .unwrap_or(packet_timestamp),
                        dropped_packets,
                        should_publish: !emitted.is_empty() || !emitted_gaps.is_empty(),
                    });
                    report_tick_window.push_many(&emitted);
                    offline_gaps.extend(emitted_gaps);
                    aggregator.record_observed_packet(packet_timestamp, packet_len);
                    match decode_packet_with_options(&envelope, &light_dpi) {
                        Ok(decoded) => {
                            stats.packets_decoded += 1;
                            let classified = classify_packet(decoded, &direction_context);
                            aggregator.record_classified_packet(&classified);
                        }
                        Err(_) => {
                            stats.packets_decode_failed += 1;
                        }
                    }
                }
                None => {
                    if matches!(mode, RuntimeMode::Offline) {
                        break;
                    }
                    timed_out_ticks += 1;
                    let dropped_packets = capture_dropped_packets(&mut capture)?;
                    let now = PacketTimestamp::now()?;
                    let emitted = aggregator.observe_live_time(now, dropped_packets);
                    publish_tick_progress(TickProgressPublish {
                        observer: &self.config.tick_observer,
                        progress_tick_window: &mut progress_tick_window,
                        aggregator: &aggregator,
                        emitted: &emitted,
                        offline_gaps: Vec::new(),
                        ended_at: emitted
                            .last()
                            .map(|snapshot| snapshot.timestamp)
                            .unwrap_or(now),
                        dropped_packets,
                        should_publish: !emitted.is_empty(),
                    });
                    report_tick_window.push_many(&emitted);
                }
            }
        }

        if let Some(savefile) = savefile.as_mut() {
            savefile
                .flush()
                .or_err(ErrorType::FileWriteError, "Failed to flush pcap savefile")
                .map_err(|e| e.into_network())?;
        }

        let dropped_packets = capture_dropped_packets(&mut capture)?;
        let ended_at = match aggregator.last_packet_timestamp().copied() {
            Some(timestamp) => timestamp,
            None if matches!(mode, RuntimeMode::Offline) => aggregator.started_at(),
            None => PacketTimestamp::now()?,
        };
        let aggregation = aggregator.finish(ended_at, dropped_packets);
        if let Some(observer) = &self.config.tick_observer
            && !aggregation.tick_snapshots.is_empty()
        {
            progress_tick_window.push_many(&aggregation.tick_snapshots);
            observer.observe_progress(RuntimeProgressSnapshot {
                tick_snapshots: progress_tick_window.to_vec(),
                offline_gaps: offline_gaps.clone(),
                final_snapshot: aggregation.final_snapshot.clone(),
            });
        }
        report_tick_window.push_many(&aggregation.tick_snapshots);

        Ok(RuntimeReport {
            mode,
            link_type,
            stats,
            timed_out_ticks,
            stopped_by_request,
            tick_snapshots: report_tick_window.to_vec(),
            offline_gaps,
            final_snapshot: aggregation.final_snapshot,
        })
    }
}

/// Live resident captures window report ticks; offline always keeps full history so
/// compressed timelines remain accurate.
fn effective_report_tick_limit(
    history_mode: &TickHistoryMode,
    mode: &RuntimeMode,
) -> Option<usize> {
    match (history_mode, mode) {
        (TickHistoryMode::Windowed(limit), RuntimeMode::Live) => Some(*limit),
        _ => None,
    }
}

struct TickProgressPublish<'a> {
    observer: &'a Option<Arc<dyn RuntimeTickObserver>>,
    progress_tick_window: &'a mut ProgressTickWindow,
    aggregator: &'a FlowAggregator,
    emitted: &'a [TickSnapshot],
    offline_gaps: Vec<OfflineGap>,
    ended_at: PacketTimestamp,
    dropped_packets: u64,
    should_publish: bool,
}

fn publish_tick_progress(args: TickProgressPublish<'_>) {
    let Some(observer) = args.observer else {
        return;
    };
    if !args.should_publish {
        return;
    }

    args.progress_tick_window.push_many(args.emitted);
    let progress = args.aggregator.runtime_progress(
        args.progress_tick_window.to_vec(),
        args.ended_at,
        args.dropped_packets,
    );
    observer.observe_progress(RuntimeProgressSnapshot {
        tick_snapshots: progress.tick_snapshots,
        offline_gaps: args.offline_gaps,
        final_snapshot: progress.final_snapshot,
    });
}

fn capture_id(source: &CaptureSource) -> String {
    match source {
        CaptureSource::Device(device) => format!("live:{}", device.get_name()),
        CaptureSource::File(file) => format!("file:{}", file.path.display()),
    }
}

fn aggregator_started_at(mode: &RuntimeMode) -> Result<PacketTimestamp> {
    match mode {
        RuntimeMode::Live => PacketTimestamp::now(),
        RuntimeMode::Offline => Ok(PacketTimestamp::tick(0)),
    }
}

fn capture_dropped_packets(capture: &mut CaptureType) -> Result<u64> {
    match capture {
        CaptureType::Live(_) => {
            let stats = capture.stats()?;
            Ok(u64::from(stats.dropped) + u64::from(stats.if_dropped))
        }
        CaptureType::Offline(_) | CaptureType::PcapNg(_) => Ok(0),
    }
}

#[cfg(test)]
mod tests {
    use std::{
        fs,
        path::PathBuf,
        sync::atomic::Ordering,
        time::{SystemTime, UNIX_EPOCH},
    };

    use pcap::{Capture, Linktype, Packet, PacketHeader};

    use super::*;

    fn temp_pcap_path(name: &str) -> PathBuf {
        let unique = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .unwrap()
            .as_nanos();
        std::env::temp_dir().join(format!("flowarden-{name}-{unique}.pcap"))
    }

    fn write_test_pcap(path: &PathBuf) {
        let dead = Capture::dead(Linktype::ETHERNET).unwrap();
        let mut savefile = dead.savefile(path).unwrap();

        let header1 = PacketHeader {
            ts: libc::timeval {
                tv_sec: 1,
                tv_usec: 0,
            },
            caplen: 4,
            len: 4,
        };
        let data1 = [0_u8, 1, 2, 3];
        let packet1 = Packet::new(&header1, &data1);
        savefile.write(&packet1);

        let header2 = PacketHeader {
            ts: libc::timeval {
                tv_sec: 2,
                tv_usec: 0,
            },
            caplen: 6,
            len: 6,
        };
        let data2 = [10_u8, 11, 12, 13, 14, 15];
        let packet2 = Packet::new(&header2, &data2);
        savefile.write(&packet2);
        savefile.flush().unwrap();
    }

    #[test]
    fn offline_runtime_reads_complete_file() {
        let path = temp_pcap_path("offline-runtime");
        write_test_pcap(&path);

        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(path.clone())),
            RuntimeConfig::forensic(),
        );

        let report = runtime.run().unwrap();
        assert!(matches!(report.mode, RuntimeMode::Offline));
        assert!(matches!(report.link_type, LinkTypeEx::Ethernet(_)));
        assert_eq!(report.stats.packets_seen, 2);
        assert_eq!(report.stats.bytes_seen, 10);
        assert_eq!(report.stats.packets_decoded, 0);
        assert_eq!(report.stats.packets_decode_failed, 2);
        assert_eq!(report.timed_out_ticks, 0);
        assert!(!report.stopped_by_request);
        assert_eq!(report.tick_snapshots.len(), 2);
        assert_eq!(report.tick_snapshots[0].sequence, 1);
        assert_eq!(report.tick_snapshots[0].timestamp, PacketTimestamp::tick(1));
        assert_eq!(report.tick_snapshots[0].totals.packets, 1);
        assert_eq!(report.tick_snapshots[1].sequence, 2);
        assert_eq!(report.tick_snapshots[1].timestamp, PacketTimestamp::tick(2));
        assert_eq!(report.tick_snapshots[1].totals.packets, 1);
        assert_eq!(report.final_snapshot.totals.packets, 2);
        assert_eq!(report.final_snapshot.totals.bytes, 10);
        assert_eq!(
            report.final_snapshot.last_packet_timestamp,
            Some(PacketTimestamp::tick(2))
        );
        assert_eq!(
            report
                .final_snapshot
                .aggregate_summary
                .top_connections
                .len(),
            0
        );
        assert_eq!(report.final_snapshot.aggregate_summary.top_hosts.len(), 0);
        assert_eq!(
            report.final_snapshot.aggregate_summary.top_services.len(),
            0
        );

        let _ = fs::remove_file(path);
    }

    #[test]
    fn stop_handle_requests_runtime_exit() {
        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(PathBuf::from("/tmp/not-used.pcap"))),
            RuntimeConfig::forensic(),
        );
        let stop = runtime.stop_handle();
        stop.store(true, Ordering::Relaxed);
        assert!(runtime.stop_handle().load(Ordering::Relaxed));
    }

    #[test]
    fn pause_handle_is_shared_and_independent_of_stop() {
        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(PathBuf::from("/tmp/not-used.pcap"))),
            RuntimeConfig::forensic(),
        );
        let pause = runtime.pause_handle();
        assert!(!pause.load(Ordering::Relaxed));
        pause.store(true, Ordering::Relaxed);
        assert!(runtime.pause_handle().load(Ordering::Relaxed));
        assert!(!runtime.stop_handle().load(Ordering::Relaxed));
    }

    #[test]
    fn offline_runtime_applies_bpf_filter() {
        let path = temp_pcap_path("offline-runtime-bpf");
        write_test_pcap(&path);

        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(path.clone())),
            RuntimeConfig::forensic().with_bpf(Some("len >= 6".to_string())),
        );

        let report = runtime.run().unwrap();
        assert_eq!(report.stats.packets_seen, 1);
        assert_eq!(report.stats.bytes_seen, 6);
        assert_eq!(report.stats.packets_decoded, 0);
        assert_eq!(report.stats.packets_decode_failed, 1);
        assert_eq!(report.tick_snapshots.len(), 1);
        assert_eq!(report.tick_snapshots[0].totals.packets, 1);
        assert_eq!(report.final_snapshot.totals.packets, 1);
        assert_eq!(report.final_snapshot.totals.bytes, 6);

        let _ = fs::remove_file(path);
    }

    #[test]
    fn offline_runtime_can_export_replay_to_pcap() {
        let input = temp_pcap_path("offline-runtime-export-input");
        let output = temp_pcap_path("offline-runtime-export-output");
        write_test_pcap(&input);

        let runtime = CaptureRuntime::new(
            CaptureSource::File(PcapImport::new(input.clone())),
            RuntimeConfig::forensic().with_pcap_output_path(Some(output.clone())),
        );

        let report = runtime.run().unwrap();
        assert_eq!(report.stats.packets_seen, 2);
        assert!(output.exists());

        let source = CaptureSource::File(PcapImport::new(output.clone()));
        let mut exported = CaptureType::from_source(&source, None).unwrap();
        assert!(exported.next_packet().unwrap().is_some());
        assert!(exported.next_packet().unwrap().is_some());
        assert!(exported.next_packet().unwrap().is_none());

        let _ = fs::remove_file(input);
        let _ = fs::remove_file(output);
    }
}
