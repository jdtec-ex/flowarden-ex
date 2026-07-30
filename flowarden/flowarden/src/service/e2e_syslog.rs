//! End-to-end: core gRPC + offline pcap → DataThresholdExceeded → UDP syslog.

#[cfg(test)]
mod tests {
    use std::{
        net::UdpSocket,
        path::PathBuf,
        time::{Duration, Instant},
    };

    use tonic::Request;

    use crate::service::{
        CoreServiceOptions, run_core_service,
        proto::{
            control::{
                CaptureSourceMode, CaptureSourceSpec, GetSyslogConfigRequest, SetSignalPolicyRequest,
                SetSourceRequest, SetSyslogConfigRequest, StartCaptureRequest,
                control_service_client::ControlServiceClient,
            },
            projection::{
                GetLatestOverviewRequest, projection_service_client::ProjectionServiceClient,
            },
        },
    };

    fn pcap_path() -> PathBuf {
        PathBuf::from(env!("CARGO_MANIFEST_DIR"))
            .join("../flowarden-core/tests/fixtures/offline_mixed_ethernet.pcap")
    }

    fn free_tcp_port() -> u16 {
        let sock = std::net::TcpListener::bind("127.0.0.1:0").expect("bind free tcp");
        sock.local_addr().unwrap().port()
    }

    #[tokio::test(flavor = "multi_thread", worker_threads = 2)]
    async fn core_offline_threshold_signal_reaches_udp_syslog() {
        let pcap = pcap_path();
        assert!(pcap.is_file(), "missing fixture pcap at {}", pcap.display());

        let listener = UdpSocket::bind("127.0.0.1:0").expect("udp listen");
        listener
            .set_read_timeout(Some(Duration::from_secs(2)))
            .unwrap();
        let syslog_addr = listener.local_addr().unwrap();

        let bind_port = free_tcp_port();
        let bind = format!("127.0.0.1:{bind_port}");
        let bind_addr: std::net::SocketAddr = bind.parse().unwrap();

        let core = tokio::spawn(async move {
            run_core_service(CoreServiceOptions {
                bind: bind_addr,
                syslog_target: Some(syslog_addr.to_string()),
                syslog_proto: "udp".into(),
                syslog_emit_signals: true,
                syslog_emit_flows: false,
            })
            .await
        });

        // Wait for core to accept connections.
        let endpoint = format!("http://{bind}");
        let mut control = None;
        let deadline = Instant::now() + Duration::from_secs(5);
        while Instant::now() < deadline {
            match ControlServiceClient::connect(endpoint.clone()).await {
                Ok(c) => {
                    control = Some(c);
                    break;
                }
                Err(_) => tokio::time::sleep(Duration::from_millis(50)).await,
            }
        }
        let mut control = control.expect("core control not reachable");
        let mut projection = ProjectionServiceClient::connect(endpoint.clone())
            .await
            .expect("projection connect");

        // Confirm syslog is enabled on core from CLI bootstrap.
        let cfg = control
            .get_syslog_config(Request::new(GetSyslogConfigRequest {}))
            .await
            .expect("GetSyslogConfig")
            .into_inner();
        assert!(
            cfg.enabled,
            "expected syslog enabled from CLI target, got {cfg:?}"
        );
        assert!(
            cfg.emit_signals,
            "expected emit_signals=true, got {cfg:?}"
        );

        control
            .set_signal_policy(Request::new(SetSignalPolicyRequest {
                data_threshold_bytes: 1, // fire on any traffic
                ..Default::default()
            }))
            .await
            .expect("SetSignalPolicy");

        control
            .set_source(Request::new(SetSourceRequest {
                source: Some(CaptureSourceSpec {
                    mode: CaptureSourceMode::Offline as i32,
                    device_name: String::new(),
                    file_path: pcap.to_string_lossy().into_owned(),
                }),
            }))
            .await
            .expect("SetSource");

        let start = control
            .start_capture(Request::new(StartCaptureRequest {}))
            .await
            .expect("StartCapture")
            .into_inner();
        assert!(start.accepted, "start declined: {}", start.message);

        // Wait for offline capture to finish and overview to settle.
        let mut saw_signal = false;
        let wait_deadline = Instant::now() + Duration::from_secs(15);
        while Instant::now() < wait_deadline {
            let ov = projection
                .get_latest_overview(Request::new(GetLatestOverviewRequest { top_n: 20 }))
                .await
                .expect("overview")
                .into_inner();
            if ov.signals.iter().any(|s| s.kind == "DataThresholdExceeded") {
                saw_signal = true;
                break;
            }
            // Also try explicit re-apply of syslog to flush if signal already listed.
            tokio::time::sleep(Duration::from_millis(100)).await;
        }
        assert!(
            saw_signal,
            "expected DataThresholdExceeded on overview after offline pcap"
        );

        // Drain UDP until we see the threshold signal line (or timeout).
        let mut buf = [0u8; 8192];
        let mut got = None;
        let udp_deadline = Instant::now() + Duration::from_secs(5);
        while Instant::now() < udp_deadline {
            match listener.recv_from(&mut buf) {
                Ok((n, _)) => {
                    let line = String::from_utf8_lossy(&buf[..n]).into_owned();
                    if line.contains("DataThresholdExceeded") || line.contains("signal") {
                        got = Some(line);
                        break;
                    }
                }
                Err(_) => {
                    // Force another overview conversion (submit path).
                    let _ = projection
                        .get_latest_overview(Request::new(GetLatestOverviewRequest { top_n: 20 }))
                        .await;
                }
            }
        }

        // If still nothing, force SetSyslogConfig flush path.
        if got.is_none() {
            control
                .set_syslog_config(Request::new(SetSyslogConfigRequest {
                    enabled: true,
                    target: syslog_addr.to_string(),
                    proto: "udp".into(),
                    facility: "local0".into(),
                    tag: "flowarden".into(),
                    emit_signals: true,
                    emit_flows: false,
                    flow_min_bytes: 10_000,
                    flow_delta_bytes: 1_000_000,
                    flow_interval_secs: 60,
                }))
                .await
                .expect("SetSyslogConfig flush");
            if let Ok((n, _)) = listener.recv_from(&mut buf) {
                got = Some(String::from_utf8_lossy(&buf[..n]).into_owned());
            }
        }

        let line = got.expect("expected UDP syslog datagram for DataThresholdExceeded");
        assert!(
            line.contains("DataThresholdExceeded") || line.contains("threshold"),
            "unexpected syslog line: {line}"
        );

        // Shutdown core by aborting the task (process exit path not needed in test).
        core.abort();
    }
}
