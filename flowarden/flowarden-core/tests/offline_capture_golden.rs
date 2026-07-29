use std::{
    fs,
    path::{Path, PathBuf},
};

use flowarden_core::{
    capture::{CaptureRuntime, CaptureSource, PcapImport, RuntimeConfig},
    flow::PacketTimestamp,
};

fn fixture_path(name: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("fixtures")
        .join(name)
}

fn golden_path(name: &str) -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .join("tests")
        .join("golden")
        .join(name)
}

fn normalize_capture_ids(report: &mut flowarden_core::capture::RuntimeReport, capture_id: &str) {
    for snapshot in &mut report.tick_snapshots {
        snapshot.capture_id = capture_id.to_string();
    }
    report.final_snapshot.capture_id = capture_id.to_string();
}

#[test]
fn offline_fixture_matches_golden_json() {
    let fixture = fixture_path("offline_mixed_ethernet.pcap");
    let runtime = CaptureRuntime::new(
        CaptureSource::File(PcapImport::new(fixture)),
        RuntimeConfig::forensic(),
    );

    let report = runtime.run().expect("offline capture should succeed");
    let mut report = report;
    normalize_capture_ids(
        &mut report,
        "file:tests/fixtures/offline_mixed_ethernet.pcap",
    );
    let actual = serde_json::to_string_pretty(&serde_json::json!({
        "tick_snapshots": report.tick_snapshots,
        "offline_gaps": report.offline_gaps,
        "final_snapshot": report.final_snapshot,
    }))
    .expect("golden output should serialize");
    let expected = fs::read_to_string(golden_path("offline_mixed_ethernet.json"))
        .expect("golden file should be readable");

    assert_eq!(actual.trim_end(), expected.trim_end());
}

#[test]
fn offline_fixture_reports_expected_core_stats() {
    let fixture = fixture_path("offline_mixed_ethernet.pcap");
    let runtime = CaptureRuntime::new(
        CaptureSource::File(PcapImport::new(fixture)),
        RuntimeConfig::forensic(),
    );

    let report = runtime.run().expect("offline capture should succeed");

    assert_eq!(report.stats.packets_seen, 3);
    assert_eq!(report.stats.bytes_seen, 109);
    assert_eq!(report.stats.packets_decoded, 2);
    assert_eq!(report.stats.packets_decode_failed, 1);
    assert_eq!(report.timed_out_ticks, 0);
    assert!(!report.stopped_by_request);

    assert_eq!(report.tick_snapshots.len(), 2);
    assert_eq!(
        report
            .tick_snapshots
            .iter()
            .map(|snapshot| snapshot.timestamp)
            .collect::<Vec<_>>(),
        vec![PacketTimestamp::tick(1), PacketTimestamp::tick(3)]
    );
    assert_eq!(report.offline_gaps.len(), 1);
    assert_eq!(report.offline_gaps[0].after, PacketTimestamp::tick(1));
    assert_eq!(report.offline_gaps[0].seconds, 1);

    assert_eq!(report.final_snapshot.totals.packets, 3);
    assert_eq!(report.final_snapshot.totals.bytes, 109);
    assert_eq!(
        report.final_snapshot.last_packet_timestamp,
        Some(PacketTimestamp::tick(3))
    );
    assert_eq!(
        report
            .final_snapshot
            .aggregate_summary
            .top_connections
            .len(),
        2
    );
    assert_eq!(report.final_snapshot.aggregate_summary.top_hosts.len(), 3);
    assert_eq!(
        report.final_snapshot.aggregate_summary.top_services.len(),
        2
    );
    assert_eq!(
        report.final_snapshot.aggregate_summary.top_services[0]
            .service
            .name,
        "https"
    );
    assert_eq!(
        report.final_snapshot.aggregate_summary.top_services[1]
            .service
            .name,
        "dns"
    );
}

#[test]
fn offline_fixture_bpf_filter_preserves_reproducible_stats() {
    let fixture = fixture_path("offline_mixed_ethernet.pcap");
    let runtime = CaptureRuntime::new(
        CaptureSource::File(PcapImport::new(fixture)),
        RuntimeConfig::forensic().with_bpf(Some("tcp".to_string())),
    );

    let report = runtime.run().expect("offline capture should succeed");

    assert_eq!(report.stats.packets_seen, 1);
    assert_eq!(report.stats.bytes_seen, 59);
    assert_eq!(report.stats.packets_decoded, 1);
    assert_eq!(report.stats.packets_decode_failed, 0);
    assert_eq!(report.tick_snapshots.len(), 1);
    assert_eq!(report.final_snapshot.totals.packets, 1);
    assert_eq!(report.final_snapshot.totals.bytes, 59);
    assert_eq!(
        report
            .final_snapshot
            .aggregate_summary
            .top_connections
            .len(),
        1
    );
    assert_eq!(
        report.final_snapshot.aggregate_summary.top_services[0]
            .service
            .name,
        "https"
    );
}
