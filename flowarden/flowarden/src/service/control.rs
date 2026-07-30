//! Capture control gRPC service implementation.

use std::{
    collections::HashSet,
    sync::{Arc, Mutex},
    thread::JoinHandle,
};

use flowarden_core::capture::{CaptureRuntime, RuntimeConfig, RuntimeTickObserver};
use tonic::{Request, Response, Status};

use super::{
    constants::PROJECTION_TICK_WINDOW,
    proto::control::{
        ApplyFilterRequest, ControlResponse, GetSyslogConfigRequest, PauseCaptureRequest,
        ResumeCaptureRequest, SetSignalPolicyRequest, SetSourceRequest, SetSyslogConfigRequest,
        ShutdownCoreRequest, StartCaptureRequest, StopCaptureRequest, SyslogConfigResponse,
        control_service_server::ControlService,
    },
    signals::SignalPolicy,
    syslog_export::{SyslogConfig, SyslogProto},
    state::{
        CaptureStatus, RuntimeOverviewObserver, SelectedCaptureSource, ServiceState,
        empty_overview_snapshot_for_meta, local_ips_from_source, overview_error_snapshot,
        overview_meta_for_selected_source, overview_snapshot_from_report,
        running_overview_snapshot_for_meta, with_overview_capture_status,
    },
};

#[derive(Clone)]
pub(crate) struct ControlServiceImpl {
    state: ServiceState,
}

impl ControlServiceImpl {
    pub(crate) fn new(state: ServiceState) -> Self {
        Self { state }
    }
}

#[tonic::async_trait]
impl ControlService for ControlServiceImpl {
    async fn start_capture(
        &self,
        _request: Request<StartCaptureRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let (selected_source, bpf, already_active) = {
            let state = self
                .state
                .control
                .lock()
                .map_err(|_| Status::internal("Failed to lock capture control state"))?;
            let selected_source = state.selected_source.clone().ok_or_else(|| {
                Status::failed_precondition("No source selected for resident capture")
            })?;
            let already_active = state.capture_status.is_active();
            (selected_source, state.active_bpf.clone(), already_active)
        };

        if already_active {
            return Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is already active".to_string(),
            }));
        }

        let source = selected_source
            .resolve()
            .map_err(|err| Status::internal(format!("Failed to resolve source: {err}")))?;
        let local_ips = local_ips_from_source(&source);
        let effective_bpf = selected_source.effective_bpf(bpf.as_deref(), self.state.control_bind);
        let overview_meta =
            overview_meta_for_selected_source(&selected_source, bpf.as_deref(), local_ips);
        let tick_observer: Option<Arc<dyn RuntimeTickObserver>> = match selected_source {
            SelectedCaptureSource::Live { .. } => Some(Arc::new(RuntimeOverviewObserver {
                overview_tx: self.state.overview_tx.clone(),
                meta: overview_meta.clone(),
            })),
            SelectedCaptureSource::Offline { .. } => None,
        };
        let runtime = CaptureRuntime::new(
            source,
            RuntimeConfig::resident(tick_observer, PROJECTION_TICK_WINDOW).with_bpf(effective_bpf),
        );
        let stop_handle = runtime.stop_handle();
        let pause_handle = runtime.pause_handle();
        let state = self.state.clone();
        let thread_meta = overview_meta.clone();
        let capture_thread_slot = Arc::new(Mutex::new(None::<JoinHandle<()>>));
        let capture_thread_slot_for_spawn = Arc::clone(&capture_thread_slot);
        {
            let mut runtime_state = self
                .state
                .control
                .lock()
                .map_err(|_| Status::internal("Failed to lock capture control state"))?;
            runtime_state.capture_status = CaptureStatus::Running;
            runtime_state.stop_requested = Some(stop_handle);
            runtime_state.pause_requested = Some(pause_handle);
            runtime_state.capture_thread = Some(Arc::clone(&capture_thread_slot));

            // Fresh capture session: clear prior live signals / offline findings.
            if let Ok(mut engine) = self.state.signals.lock() {
                engine.reset_session();
            }

            self.state
                .overview_tx
                .send_replace(running_overview_snapshot_for_meta(&overview_meta));

            let thread = std::thread::spawn(move || {
                let snapshot = match runtime.run() {
                    Ok(report) => overview_snapshot_from_report(report, &thread_meta),
                    Err(err) => overview_error_snapshot(err.to_string(), &thread_meta),
                };
                state.overview_tx.send_replace(snapshot);

                if let Ok(mut control) = state.control.lock() {
                    control.capture_status = CaptureStatus::Idle;
                    control.stop_requested = None;
                    control.pause_requested = None;
                    control.capture_thread = None;
                }

                if let Ok(mut thread_slot) = capture_thread_slot_for_spawn.lock() {
                    *thread_slot = None;
                }
            });
            let mut thread_slot = capture_thread_slot
                .lock()
                .map_err(|_| Status::internal("Failed to lock resident capture thread"))?;
            *thread_slot = Some(thread);
        }

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: "Resident capture started".to_string(),
        }))
    }

    async fn stop_capture(
        &self,
        _request: Request<StopCaptureRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let capture_thread = {
            let mut state = self
                .state
                .control
                .lock()
                .map_err(|_| Status::internal("Failed to lock capture control state"))?;

            let Some(stop_requested) = state.stop_requested.as_ref() else {
                return Ok(Response::new(ControlResponse {
                    accepted: false,
                    message: "Resident capture is not running".to_string(),
                }));
            };

            stop_requested.store(true, std::sync::atomic::Ordering::Relaxed);
            if let Some(pause_requested) = state.pause_requested.as_ref() {
                pause_requested.store(false, std::sync::atomic::Ordering::Relaxed);
            }
            state.capture_status = CaptureStatus::Stopping;
            state.capture_thread.clone()
        };

        let Some(capture_thread) = capture_thread else {
            return Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is not running".to_string(),
            }));
        };

        let join_handle = {
            let mut thread_slot = capture_thread
                .lock()
                .map_err(|_| Status::internal("Failed to lock resident capture thread"))?;
            thread_slot.take()
        };

        if let Some(handle) = join_handle {
            tokio::task::spawn_blocking(move || {
                let _ = handle.join();
            })
            .await
            .map_err(|_| Status::internal("Failed to wait for resident capture shutdown"))?;
        }

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: "Resident capture stopped".to_string(),
        }))
    }

    async fn pause_capture(
        &self,
        _request: Request<PauseCaptureRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let mut state = self
            .state
            .control
            .lock()
            .map_err(|_| Status::internal("Failed to lock capture control state"))?;

        match state.capture_status {
            CaptureStatus::Running => {
                let Some(pause_requested) = state.pause_requested.as_ref() else {
                    return Ok(Response::new(ControlResponse {
                        accepted: false,
                        message: "Resident capture pause handle is unavailable".to_string(),
                    }));
                };
                pause_requested.store(true, std::sync::atomic::Ordering::Relaxed);
                state.capture_status = CaptureStatus::Paused;
                let current = self.state.overview_tx.borrow().clone();
                self.state
                    .overview_tx
                    .send_replace(with_overview_capture_status(current, CaptureStatus::Paused));
                Ok(Response::new(ControlResponse {
                    accepted: true,
                    message: "Resident capture paused".to_string(),
                }))
            }
            CaptureStatus::Paused => Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is already paused".to_string(),
            })),
            CaptureStatus::Stopping => Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is stopping".to_string(),
            })),
            CaptureStatus::Idle => Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is not running".to_string(),
            })),
        }
    }

    async fn resume_capture(
        &self,
        _request: Request<ResumeCaptureRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let mut state = self
            .state
            .control
            .lock()
            .map_err(|_| Status::internal("Failed to lock capture control state"))?;

        match state.capture_status {
            CaptureStatus::Paused => {
                let Some(pause_requested) = state.pause_requested.as_ref() else {
                    return Ok(Response::new(ControlResponse {
                        accepted: false,
                        message: "Resident capture pause handle is unavailable".to_string(),
                    }));
                };
                pause_requested.store(false, std::sync::atomic::Ordering::Relaxed);
                state.capture_status = CaptureStatus::Running;
                let current = self.state.overview_tx.borrow().clone();
                self.state
                    .overview_tx
                    .send_replace(with_overview_capture_status(
                        current,
                        CaptureStatus::Running,
                    ));
                Ok(Response::new(ControlResponse {
                    accepted: true,
                    message: "Resident capture resumed".to_string(),
                }))
            }
            CaptureStatus::Running => Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is already running".to_string(),
            })),
            CaptureStatus::Stopping => Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is stopping".to_string(),
            })),
            CaptureStatus::Idle => Ok(Response::new(ControlResponse {
                accepted: false,
                message: "Resident capture is not running".to_string(),
            })),
        }
    }

    async fn set_source(
        &self,
        request: Request<SetSourceRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let request = request.into_inner();
        let source = request
            .source
            .ok_or_else(|| Status::invalid_argument("source must be set"))?;
        let selected_source = SelectedCaptureSource::from_proto(source)?;
        let source_label = selected_source.source_label();

        let active_bpf = {
            let mut state = self
                .state
                .control
                .lock()
                .map_err(|_| Status::internal("Failed to lock capture control state"))?;
            state.selected_source = Some(selected_source.clone());
            state.active_bpf.clone()
        };
        let overview_meta = overview_meta_for_selected_source(
            &selected_source,
            active_bpf.as_deref(),
            HashSet::new(),
        );
        self.state
            .overview_tx
            .send_replace(empty_overview_snapshot_for_meta(&overview_meta));

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: format!("Selected {source_label}"),
        }))
    }

    async fn apply_filter(
        &self,
        request: Request<ApplyFilterRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let bpf = request.into_inner().bpf.trim().to_string();
        let mut state = self
            .state
            .control
            .lock()
            .map_err(|_| Status::internal("Failed to lock capture control state"))?;
        state.active_bpf = if bpf.is_empty() {
            None
        } else {
            Some(bpf.clone())
        };

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: if bpf.is_empty() {
                "Cleared active filter".to_string()
            } else {
                format!("Applied filter `{bpf}`")
            },
        }))
    }

    async fn set_signal_policy(
        &self,
        request: Request<SetSignalPolicyRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let req = request.into_inner();
        let mut watched_tokens = req.watched_hosts;
        watched_tokens.extend(req.watched_services.into_iter().map(|s| format!("service:{s}")));
        watched_tokens.extend(req.watched_processes.into_iter().map(|s| format!("process:{s}")));
        let mut bad_tokens = req.known_bad_hosts;
        bad_tokens.extend(req.known_bad_services.into_iter().map(|s| format!("service:{s}")));
        bad_tokens.extend(req.known_bad_processes.into_iter().map(|s| format!("process:{s}")));

        let watched = super::signals::parse_entity_patterns(
            watched_tokens
                .into_iter()
                .map(|s| s.trim().to_string())
                .filter(|s| !s.is_empty()),
        );
        let bad = super::signals::parse_entity_patterns(
            bad_tokens
                .into_iter()
                .map(|s| s.trim().to_string())
                .filter(|s| !s.is_empty()),
        );

        let defaults = SignalPolicy::default();
        let policy = SignalPolicy {
            data_threshold_bytes: req.data_threshold_bytes,
            watched_hosts: watched.hosts,
            known_bad_hosts: bad.hosts,
            watched_services: watched.services,
            watched_processes: watched.processes,
            known_bad_services: bad.services,
            known_bad_processes: bad.processes,
            dpi_exfil_enabled: if req.dpi_exfil_min_bytes > 0 || req.dpi_exfil_enabled {
                req.dpi_exfil_enabled || defaults.dpi_exfil_enabled
            } else {
                defaults.dpi_exfil_enabled
            },
            dpi_exfil_min_bytes: if req.dpi_exfil_min_bytes > 0 {
                req.dpi_exfil_min_bytes
            } else {
                defaults.dpi_exfil_min_bytes
            },
            dpi_exfil_ratio: if req.dpi_exfil_ratio > 0.0 {
                req.dpi_exfil_ratio
            } else {
                defaults.dpi_exfil_ratio
            },
            dpi_idle_enabled: req.dpi_idle_enabled || defaults.dpi_idle_enabled,
            dpi_idle_min_age_secs: if req.dpi_idle_min_age_secs > 0 {
                req.dpi_idle_min_age_secs
            } else {
                defaults.dpi_idle_min_age_secs
            },
            dpi_idle_silence_secs: if req.dpi_idle_silence_secs > 0 {
                req.dpi_idle_silence_secs
            } else {
                defaults.dpi_idle_silence_secs
            },
            dpi_idle_max_bytes: if req.dpi_idle_max_bytes > 0 {
                req.dpi_idle_max_bytes
            } else {
                defaults.dpi_idle_max_bytes
            },
            dpi_p2p_enabled: req.dpi_p2p_enabled || defaults.dpi_p2p_enabled,
            dpi_p2p_allow: req.dpi_p2p_allow,
        };
        let threshold = policy.data_threshold_bytes;
        let w_host = policy.watched_hosts.len();
        let w_svc = policy.watched_services.len();
        let w_proc = policy.watched_processes.len();
        let mut engine = self
            .state
            .signals
            .lock()
            .map_err(|_| Status::internal("Failed to lock signal engine"))?;
        engine.set_policy(policy);

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: format!(
                "Signal policy applied (threshold={threshold}, hosts={w_host}, services={w_svc}, processes={w_proc})"
            ),
        }))
    }

    async fn get_syslog_config(
        &self,
        _request: Request<GetSyslogConfigRequest>,
    ) -> std::result::Result<Response<SyslogConfigResponse>, Status> {
        let syslog = self
            .state
            .syslog
            .lock()
            .map_err(|_| Status::internal("Failed to lock syslog exporter"))?;
        let cfg = syslog.snapshot_config();
        Ok(Response::new(SyslogConfigResponse {
            enabled: cfg.enabled,
            target: cfg
                .target
                .map(|a| a.to_string())
                .unwrap_or_default(),
            proto: cfg.proto.as_str().to_string(),
            facility: "local0".into(),
            tag: cfg.tag,
            emit_signals: cfg.emit_signals,
            emit_flows: cfg.emit_flows,
            flow_min_bytes: cfg.flow_min_bytes,
            flow_delta_bytes: cfg.flow_delta_bytes,
            flow_interval_secs: cfg.flow_interval_secs,
            dropped_messages: syslog.dropped(),
            last_error: syslog.last_error(),
        }))
    }

    async fn set_syslog_config(
        &self,
        request: Request<SetSyslogConfigRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let req = request.into_inner();
        let target = req.target.trim();
        let (target_addr, enabled) = if req.enabled && !target.is_empty() {
            let addr = super::syslog_export::parse_syslog_target(target).map_err(|e| {
                Status::invalid_argument(format!("syslog target must be HOST:PORT ({e})"))
            })?;
            (Some(addr), true)
        } else {
            (None, false)
        };
        let cfg = SyslogConfig {
            enabled,
            target: target_addr,
            proto: SyslogProto::parse(&req.proto),
            facility: 16,
            tag: if req.tag.trim().is_empty() {
                "flowarden".into()
            } else {
                req.tag.trim().to_string()
            },
            emit_signals: req.emit_signals,
            emit_flows: req.emit_flows,
            flow_min_bytes: if req.flow_min_bytes > 0 {
                req.flow_min_bytes
            } else {
                10_000
            },
            flow_delta_bytes: if req.flow_delta_bytes > 0 {
                req.flow_delta_bytes
            } else {
                1_000_000
            },
            flow_interval_secs: if req.flow_interval_secs > 0 {
                req.flow_interval_secs
            } else {
                60
            },
        };

        {
            let mut syslog = self
                .state
                .syslog
                .lock()
                .map_err(|_| Status::internal("Failed to lock syslog exporter"))?;
            syslog.reconfigure(cfg.clone());
            if cfg.enabled {
                syslog.ensure_worker();
            }
        }

        // Re-export signals already in the engine so enabling syslog after a finding
        // still delivers (overview stream may not fire until the next capture tick).
        let flushed = if cfg.enabled && cfg.emit_signals {
            let rows = self
                .state
                .signals
                .lock()
                .map_err(|_| Status::internal("Failed to lock signal engine"))?
                .list_proto();
            let mut syslog = self
                .state
                .syslog
                .lock()
                .map_err(|_| Status::internal("Failed to lock syslog exporter"))?;
            let mut n = 0u32;
            for row in rows {
                let summary = if row.detail.trim().is_empty() {
                    row.summary.clone()
                } else {
                    format!("{} — {}", row.summary, row.detail)
                };
                syslog.submit_signal(super::syslog_export::SignalSyslogPayload {
                    id: row.id,
                    kind: row.kind,
                    severity: row.severity,
                    mode: row.mode,
                    status: row.status,
                    subject: row.subject,
                    summary,
                    confidence: row.confidence,
                    pivot_kind: row.pivot_kind,
                    pivot_value: row.pivot_value,
                });
                n += 1;
            }
            n
        } else {
            0
        };

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: if cfg.enabled {
                format!(
                    "Syslog enabled → {} ({}); flushed {flushed} signal(s)",
                    cfg.target
                        .map(|a| a.to_string())
                        .unwrap_or_default(),
                    cfg.proto.as_str()
                )
            } else {
                "Syslog disabled".into()
            },
        }))
    }

    async fn shutdown_core(
        &self,
        _request: Request<ShutdownCoreRequest>,
    ) -> std::result::Result<Response<ControlResponse>, Status> {
        let capture_thread = {
            let mut state = self
                .state
                .control
                .lock()
                .map_err(|_| Status::internal("Failed to lock capture control state"))?;

            if let Some(stop_requested) = state.stop_requested.as_ref() {
                stop_requested.store(true, std::sync::atomic::Ordering::Relaxed);
                state.capture_status = CaptureStatus::Stopping;
            }

            state.capture_thread.clone()
        };

        if let Some(capture_thread) = capture_thread {
            let join_handle = {
                let mut thread_slot = capture_thread
                    .lock()
                    .map_err(|_| Status::internal("Failed to lock resident capture thread"))?;
                thread_slot.take()
            };

            if let Some(handle) = join_handle {
                tokio::task::spawn_blocking(move || {
                    let _ = handle.join();
                })
                .await
                .map_err(|_| Status::internal("Failed to wait for resident capture shutdown"))?;
            }
        }

        let _ = self.state.shutdown_tx.send(true);

        Ok(Response::new(ControlResponse {
            accepted: true,
            message: "Resident core shutdown requested".to_string(),
        }))
    }
}
