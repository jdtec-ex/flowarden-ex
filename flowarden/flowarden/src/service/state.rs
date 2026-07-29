//! Shared resident-core runtime state and overview snapshot types.

use std::{
    collections::HashSet,
    net::{IpAddr, SocketAddr},
    path::PathBuf,
    sync::{Arc, Mutex},
    thread::JoinHandle,
};

use flowarden_core::{
    capture::{CaptureSource, RuntimeProgressSnapshot, RuntimeReport},
    flow::{
        AggregateTotals, ConnectionSummary, HostSummary, OfflineGap, PacketTimestamp,
        ServiceSummary, TcpConnectionSummary, TickSnapshot,
    },
};
use flowarden_error::Result;
use tokio::sync::watch;
use tonic::Status;

use super::{
    bpf::{normalize_bpf, resident_capture_bpf},
    constants::{PROJECTION_MAX_TOP_N, PROJECTION_TICK_WINDOW},
    process_lookup::ProcessLookup,
    proto::{
        control::{CaptureSourceMode, CaptureSourceSpec},
        projection::ProjectionMode,
    },
    rdns_lookup::RdnsLookup,
    signals::SignalEngine,
};
use crate::geo::GeoCountryResolver;

#[derive(Clone)]
pub(crate) struct ServiceState {
    pub(crate) started_at_unix_seconds: u64,
    pub(crate) control_bind: SocketAddr,
    pub(crate) control: Arc<Mutex<CaptureControlState>>,
    pub(crate) geo: Arc<Mutex<GeoCountryResolver>>,
    pub(crate) process_lookup: Arc<ProcessLookup>,
    pub(crate) rdns_lookup: Arc<RdnsLookup>,
    pub(crate) signals: Arc<Mutex<SignalEngine>>,
    pub(crate) shutdown_tx: watch::Sender<bool>,
    pub(crate) overview_tx: watch::Sender<OverviewRuntimeSnapshot>,
}

#[derive(Clone, Debug)]
pub(crate) struct OverviewRuntimeSnapshot {
    pub(crate) capture_id: String,
    pub(crate) mode: ProjectionMode,
    pub(crate) source_label: String,
    pub(crate) filter_label: String,
    pub(crate) metric_mode: String,
    pub(crate) capture_status: String,
    pub(crate) local_ips: HashSet<IpAddr>,
    pub(crate) tick_snapshots: Vec<TickSnapshot>,
    pub(crate) offline_gaps: Vec<OfflineGap>,
    pub(crate) top_connections: Vec<ConnectionSummary>,
    pub(crate) top_hosts: Vec<HostSummary>,
    pub(crate) top_services: Vec<ServiceSummary>,
    pub(crate) tcp_connections: Vec<TcpConnectionSummary>,
    pub(crate) totals: AggregateTotals,
    pub(crate) dropped_packets: u64,
    pub(crate) last_packet_timestamp: Option<PacketTimestamp>,
    pub(crate) error_message: Option<String>,
}

#[derive(Clone, Debug)]
pub(crate) struct RuntimeOverviewMeta {
    pub(crate) capture_id: String,
    pub(crate) error_capture_id: String,
    pub(crate) mode: ProjectionMode,
    pub(crate) source_label: String,
    pub(crate) filter_label: String,
    pub(crate) metric_mode: String,
    pub(crate) local_ips: HashSet<IpAddr>,
}

pub(crate) struct RuntimeOverviewObserver {
    pub(crate) overview_tx: watch::Sender<OverviewRuntimeSnapshot>,
    pub(crate) meta: RuntimeOverviewMeta,
}

#[derive(Default)]
pub(crate) struct CaptureControlState {
    pub(crate) selected_source: Option<SelectedCaptureSource>,
    pub(crate) active_bpf: Option<String>,
    pub(crate) capture_status: CaptureStatus,
    pub(crate) stop_requested: Option<Arc<std::sync::atomic::AtomicBool>>,
    pub(crate) pause_requested: Option<Arc<std::sync::atomic::AtomicBool>>,
    pub(crate) capture_thread: Option<Arc<Mutex<Option<JoinHandle<()>>>>>,
}

#[derive(Clone, Debug)]
pub(crate) enum SelectedCaptureSource {
    Live { device_name: String },
    Offline { file_path: PathBuf },
}

#[derive(Clone, Copy, Debug, Default, PartialEq, Eq)]
pub(crate) enum CaptureStatus {
    #[default]
    Idle,
    Running,
    Paused,
    Stopping,
}

impl CaptureStatus {
    pub(crate) fn as_overview_label(self) -> &'static str {
        match self {
            Self::Idle => "idle",
            Self::Running => "running",
            Self::Paused => "paused",
            Self::Stopping => "stopping",
        }
    }

    pub(crate) fn is_active(self) -> bool {
        matches!(self, Self::Running | Self::Paused | Self::Stopping)
    }
}

impl SelectedCaptureSource {
    pub(crate) fn from_proto(source: CaptureSourceSpec) -> std::result::Result<Self, Status> {
        let mode =
            CaptureSourceMode::try_from(source.mode).unwrap_or(CaptureSourceMode::Unspecified);

        match mode {
            CaptureSourceMode::Live => {
                let device_name = source.device_name.trim().to_string();
                if device_name.is_empty() {
                    return Err(Status::invalid_argument(
                        "source.device_name must not be empty for live capture",
                    ));
                }

                Ok(Self::Live { device_name })
            }
            CaptureSourceMode::Offline => {
                let file_path = source.file_path.trim().to_string();
                if file_path.is_empty() {
                    return Err(Status::invalid_argument(
                        "source.file_path must not be empty for offline capture",
                    ));
                }

                Ok(Self::Offline {
                    file_path: PathBuf::from(file_path),
                })
            }
            CaptureSourceMode::Unspecified => Err(Status::invalid_argument(
                "source.mode must be live or offline",
            )),
        }
    }

    pub(crate) fn resolve(&self) -> Result<CaptureSource> {
        match self {
            Self::Live { device_name } => CaptureSource::from_device_name(device_name),
            Self::Offline { file_path } => CaptureSource::from_file_path(file_path.clone()),
        }
    }

    pub(crate) fn source_label(&self) -> String {
        match self {
            Self::Live { device_name } => format!("Live source · {device_name}"),
            Self::Offline { file_path } => format!("Offline source · {}", file_path.display()),
        }
    }

    pub(crate) fn capture_id(&self) -> String {
        match self {
            Self::Live { device_name } => format!("live:{device_name}"),
            Self::Offline { file_path } => format!("file:{}", file_path.display()),
        }
    }

    pub(crate) fn error_capture_id(&self) -> String {
        match self {
            Self::Live { device_name } => format!("live:error:{device_name}"),
            Self::Offline { file_path } => format!("file:error:{}", file_path.display()),
        }
    }

    pub(crate) fn projection_mode(&self) -> ProjectionMode {
        match self {
            Self::Live { .. } => ProjectionMode::Live,
            Self::Offline { .. } => ProjectionMode::Offline,
        }
    }

    pub(crate) fn effective_bpf(
        &self,
        user_bpf: Option<&str>,
        control_bind: SocketAddr,
    ) -> Option<String> {
        match self {
            Self::Live { .. } => resident_capture_bpf(user_bpf, control_bind),
            Self::Offline { .. } => normalize_bpf(user_bpf),
        }
    }
}

impl flowarden_core::capture::RuntimeTickObserver for RuntimeOverviewObserver {
    fn observe_progress(&self, snapshot: RuntimeProgressSnapshot) {
        self.overview_tx
            .send_replace(overview_snapshot_from_progress(snapshot, &self.meta));
    }
}

pub(crate) fn empty_runtime_overview_snapshot() -> OverviewRuntimeSnapshot {
    OverviewRuntimeSnapshot {
        capture_id: "live:inactive".to_string(),
        mode: ProjectionMode::Live,
        source_label: "Live source · not started".to_string(),
        filter_label: "Filter · none".to_string(),
        metric_mode: "bytes".to_string(),
        capture_status: "idle".to_string(),
        local_ips: HashSet::new(),
        tick_snapshots: Vec::new(),
        offline_gaps: Vec::new(),
        top_connections: Vec::new(),
        top_hosts: Vec::new(),
        top_services: Vec::new(),
        tcp_connections: Vec::new(),
        totals: AggregateTotals::default(),
        dropped_packets: 0,
        last_packet_timestamp: None,
        error_message: None,
    }
}

pub(crate) fn local_ips_from_source(source: &CaptureSource) -> HashSet<IpAddr> {
    match source {
        CaptureSource::Device(device) => device
            .get_addresses()
            .iter()
            .map(|address| address.addr)
            .collect(),
        CaptureSource::File(_) => HashSet::new(),
    }
}

pub(crate) fn filter_label(bpf: Option<&str>) -> String {
    bpf.filter(|value| !value.trim().is_empty())
        .map(|value| format!("Filter · {value}"))
        .unwrap_or_else(|| "Filter · none".to_string())
}

pub(crate) fn overview_meta_for_selected_source(
    selected_source: &SelectedCaptureSource,
    bpf: Option<&str>,
    local_ips: HashSet<IpAddr>,
) -> RuntimeOverviewMeta {
    RuntimeOverviewMeta {
        capture_id: selected_source.capture_id(),
        error_capture_id: selected_source.error_capture_id(),
        mode: selected_source.projection_mode(),
        source_label: selected_source.source_label(),
        filter_label: filter_label(bpf),
        metric_mode: "bytes".to_string(),
        local_ips,
    }
}

pub(crate) fn empty_overview_snapshot_for_meta(
    meta: &RuntimeOverviewMeta,
) -> OverviewRuntimeSnapshot {
    OverviewRuntimeSnapshot {
        capture_id: meta.capture_id.clone(),
        mode: meta.mode,
        source_label: meta.source_label.clone(),
        filter_label: meta.filter_label.clone(),
        metric_mode: meta.metric_mode.clone(),
        capture_status: "idle".to_string(),
        local_ips: meta.local_ips.clone(),
        tick_snapshots: Vec::new(),
        offline_gaps: Vec::new(),
        top_connections: Vec::new(),
        top_hosts: Vec::new(),
        top_services: Vec::new(),
        tcp_connections: Vec::new(),
        totals: AggregateTotals::default(),
        dropped_packets: 0,
        last_packet_timestamp: None,
        error_message: None,
    }
}

pub(crate) fn running_overview_snapshot_for_meta(
    meta: &RuntimeOverviewMeta,
) -> OverviewRuntimeSnapshot {
    let mut snapshot = empty_overview_snapshot_for_meta(meta);
    snapshot.capture_status = "running".to_string();
    snapshot
}

pub(crate) fn with_overview_capture_status(
    mut snapshot: OverviewRuntimeSnapshot,
    status: CaptureStatus,
) -> OverviewRuntimeSnapshot {
    snapshot.capture_status = status.as_overview_label().to_string();
    snapshot
}

pub(crate) fn overview_snapshot_from_progress(
    progress: RuntimeProgressSnapshot,
    meta: &RuntimeOverviewMeta,
) -> OverviewRuntimeSnapshot {
    OverviewRuntimeSnapshot {
        capture_id: meta.capture_id.clone(),
        mode: meta.mode,
        source_label: meta.source_label.clone(),
        filter_label: meta.filter_label.clone(),
        metric_mode: meta.metric_mode.clone(),
        capture_status: "running".to_string(),
        local_ips: meta.local_ips.clone(),
        tick_snapshots: progress.tick_snapshots,
        offline_gaps: progress.offline_gaps,
        top_connections: progress
            .final_snapshot
            .aggregate_summary
            .top_connections
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        top_hosts: progress
            .final_snapshot
            .aggregate_summary
            .top_hosts
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        top_services: progress
            .final_snapshot
            .aggregate_summary
            .top_services
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        tcp_connections: progress
            .final_snapshot
            .aggregate_summary
            .tcp_connections
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        totals: progress.final_snapshot.totals,
        dropped_packets: progress.final_snapshot.dropped_packets,
        last_packet_timestamp: progress.final_snapshot.last_packet_timestamp,
        error_message: None,
    }
}

pub(crate) fn overview_snapshot_from_report(
    mut report: RuntimeReport,
    meta: &RuntimeOverviewMeta,
) -> OverviewRuntimeSnapshot {
    if matches!(meta.mode, ProjectionMode::Live)
        && report.tick_snapshots.len() > PROJECTION_TICK_WINDOW
    {
        report
            .tick_snapshots
            .drain(0..report.tick_snapshots.len() - PROJECTION_TICK_WINDOW);
    }

    OverviewRuntimeSnapshot {
        capture_id: report.final_snapshot.capture_id,
        mode: meta.mode,
        source_label: meta.source_label.clone(),
        filter_label: meta.filter_label.clone(),
        metric_mode: meta.metric_mode.clone(),
        capture_status: "idle".to_string(),
        local_ips: meta.local_ips.clone(),
        tick_snapshots: report.tick_snapshots,
        offline_gaps: report.offline_gaps,
        top_connections: report
            .final_snapshot
            .aggregate_summary
            .top_connections
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        top_hosts: report
            .final_snapshot
            .aggregate_summary
            .top_hosts
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        top_services: report
            .final_snapshot
            .aggregate_summary
            .top_services
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        tcp_connections: report
            .final_snapshot
            .aggregate_summary
            .tcp_connections
            .into_iter()
            .take(PROJECTION_MAX_TOP_N)
            .collect(),
        totals: report.final_snapshot.totals,
        dropped_packets: report.final_snapshot.dropped_packets,
        last_packet_timestamp: report.final_snapshot.last_packet_timestamp,
        error_message: None,
    }
}

pub(crate) fn overview_error_snapshot(
    error_message: String,
    meta: &RuntimeOverviewMeta,
) -> OverviewRuntimeSnapshot {
    OverviewRuntimeSnapshot {
        capture_id: meta.error_capture_id.clone(),
        mode: meta.mode,
        source_label: meta.source_label.clone(),
        filter_label: meta.filter_label.clone(),
        metric_mode: meta.metric_mode.clone(),
        capture_status: "error".to_string(),
        local_ips: meta.local_ips.clone(),
        tick_snapshots: Vec::new(),
        offline_gaps: Vec::new(),
        top_connections: Vec::new(),
        top_hosts: Vec::new(),
        top_services: Vec::new(),
        tcp_connections: Vec::new(),
        totals: AggregateTotals::default(),
        dropped_packets: 0,
        last_packet_timestamp: None,
        error_message: Some(error_message),
    }
}
