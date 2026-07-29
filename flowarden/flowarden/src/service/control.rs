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
        ApplyFilterRequest, ControlResponse, PauseCaptureRequest, ResumeCaptureRequest,
        SetSignalPolicyRequest, SetSourceRequest, ShutdownCoreRequest, StartCaptureRequest,
        StopCaptureRequest, control_service_server::ControlService,
    },
    signals::SignalPolicy,
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

        let policy = SignalPolicy {
            data_threshold_bytes: req.data_threshold_bytes,
            watched_hosts: watched.hosts,
            known_bad_hosts: bad.hosts,
            watched_services: watched.services,
            watched_processes: watched.processes,
            known_bad_services: bad.services,
            known_bad_processes: bad.processes,
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
