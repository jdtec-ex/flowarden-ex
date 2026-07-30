//! Resident core gRPC service: health, discovery, control, and projection.

mod bpf;
mod constants;
mod control;
mod convert;
mod discovery;
mod health;
mod p2p_proxy_rules;
mod process_lookup;
mod projection;
pub mod proto;
mod rdns_lookup;
mod signals;
mod state;
mod syslog_export;
mod timeline;

#[cfg(test)]
mod e2e_syslog;

pub use signals::{CliFinding, evaluate_cli_findings};

#[cfg(test)]
mod tests;

use std::{
    net::SocketAddr,
    sync::{Arc, Mutex},
    time::{SystemTime, UNIX_EPOCH},
};

use flowarden_error::{ErrorType, OrErr, Result};
use tokio::sync::watch;
use tonic::transport::Server;

use crate::geo::GeoCountryResolver;
use control::ControlServiceImpl;
use discovery::DiscoveryServiceImpl;
use health::HealthServiceImpl;
use process_lookup::ProcessLookup;
use projection::ProjectionServiceImpl;
use proto::{
    control::control_service_server::ControlServiceServer,
    discovery::discovery_service_server::DiscoveryServiceServer,
    health::health_service_server::HealthServiceServer,
    projection::projection_service_server::ProjectionServiceServer,
};
use rdns_lookup::RdnsLookup;
use signals::SignalEngine;
use state::{CaptureControlState, CaptureStatus, ServiceState, empty_runtime_overview_snapshot};

#[derive(Debug, Clone)]
pub struct CoreServiceOptions {
    pub bind: SocketAddr,
    /// When set (CLI), overrides env-based syslog bootstrap. Empty/None → try env, else disabled.
    pub syslog_target: Option<String>,
    pub syslog_proto: String,
    pub syslog_emit_signals: bool,
    pub syslog_emit_flows: bool,
}

impl Default for CoreServiceOptions {
    fn default() -> Self {
        Self {
            bind: SocketAddr::from(([127, 0, 0, 1], 39_091)),
            syslog_target: None,
            syslog_proto: "udp".into(),
            syslog_emit_signals: true,
            syslog_emit_flows: true,
        }
    }
}

pub async fn run_core_service(options: CoreServiceOptions) -> Result<()> {
    let started_at_unix_seconds = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .or_err(
            ErrorType::InternalError,
            "System clock is before UNIX_EPOCH",
        )
        .map_err(|e| e.into_in())?
        .as_secs();

    let (shutdown_tx, shutdown_rx) = watch::channel(false);
    let initial_overview = empty_runtime_overview_snapshot();
    let (overview_tx, _) = watch::channel(initial_overview.clone());
    // When process/rDNS caches fill, re-broadcast the current overview so UI
    // receives enrichment without waiting for the next capture tick.
    let overview_refresh_tx = overview_tx.clone();
    let enrichment_refresh: Arc<dyn Fn() + Send + Sync> = Arc::new(move || {
        let current = overview_refresh_tx.borrow().clone();
        let _ = overview_refresh_tx.send_replace(current);
    });
    let syslog_cfg = if options
        .syslog_target
        .as_deref()
        .map(str::trim)
        .is_some_and(|s| !s.is_empty())
    {
        syslog_export::SyslogConfig::from_target_str(
            options.syslog_target.as_deref(),
            &options.syslog_proto,
            options.syslog_emit_signals,
            options.syslog_emit_flows,
        )
    } else {
        // CLI target empty: allow env bootstrap; still default disabled.
        let mut cfg = syslog_export::SyslogConfig::from_env();
        if cfg.target.is_none() {
            cfg.emit_signals = options.syslog_emit_signals;
            cfg.emit_flows = options.syslog_emit_flows;
            cfg.proto = syslog_export::SyslogProto::parse(&options.syslog_proto);
        }
        cfg
    };
    let state = ServiceState {
        started_at_unix_seconds,
        control_bind: options.bind,
        control: Arc::new(Mutex::new(CaptureControlState {
            selected_source: None,
            active_bpf: None,
            capture_status: CaptureStatus::Idle,
            stop_requested: None,
            pause_requested: None,
            capture_thread: None,
        })),
        geo: Arc::new(Mutex::new(
            GeoCountryResolver::new()
                .or_err(
                    ErrorType::InternalError,
                    "Failed to initialize bundled country MMDB resolver",
                )
                .map_err(|e| e.into_in())?,
        )),
        process_lookup: Arc::new(ProcessLookup::spawn(Some(Arc::clone(&enrichment_refresh)))),
        rdns_lookup: Arc::new(RdnsLookup::spawn(Some(enrichment_refresh))),
        signals: Arc::new(Mutex::new(SignalEngine::default())),
        syslog: Arc::new(Mutex::new(syslog_export::SyslogExporter::start(syslog_cfg))),
        shutdown_tx,
        overview_tx,
    };

    Server::builder()
        .add_service(HealthServiceServer::new(HealthServiceImpl::new(
            state.clone(),
        )))
        .add_service(DiscoveryServiceServer::new(DiscoveryServiceImpl))
        .add_service(ControlServiceServer::new(ControlServiceImpl::new(
            state.clone(),
        )))
        .add_service(ProjectionServiceServer::new(ProjectionServiceImpl::new(
            state,
        )))
        .serve_with_shutdown(options.bind, shutdown_signal(shutdown_rx))
        .await
        .or_err(
            ErrorType::CaptureStopError,
            "Flowarden core process exited unexpectedly",
        )
        .map_err(|e| e.into_network())
}

async fn shutdown_signal(mut shutdown_rx: watch::Receiver<bool>) {
    let ctrl_c = async {
        let _ = tokio::signal::ctrl_c().await;
    };

    #[cfg(unix)]
    let terminate = async {
        use tokio::signal::unix::{SignalKind, signal};

        if let Ok(mut terminate) = signal(SignalKind::terminate()) {
            let _ = terminate.recv().await;
        }
    };

    #[cfg(not(unix))]
    let terminate = std::future::pending::<()>();

    tokio::select! {
        _ = ctrl_c => {}
        _ = terminate => {}
        changed = shutdown_rx.changed() => {
            if changed.is_ok() && *shutdown_rx.borrow() {}
        }
    }
}
