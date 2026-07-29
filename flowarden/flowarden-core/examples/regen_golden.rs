use std::{fs, path::PathBuf};
use flowarden_core::{
    capture::{CaptureRuntime, CaptureSource, PcapImport, RuntimeConfig},
};

fn main() {
    let fixture = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests/fixtures/offline_mixed_ethernet.pcap");
    let golden = PathBuf::from(env!("CARGO_MANIFEST_DIR"))
        .join("tests/golden/offline_mixed_ethernet.json");
    let runtime = CaptureRuntime::new(
        CaptureSource::File(PcapImport::new(fixture)),
        RuntimeConfig::forensic(),
    );
    let mut report = runtime.run().expect("run");
    let capture_id = "file:tests/fixtures/offline_mixed_ethernet.pcap";
    for snapshot in &mut report.tick_snapshots {
        snapshot.capture_id = capture_id.to_string();
    }
    report.final_snapshot.capture_id = capture_id.to_string();
    let actual = serde_json::to_string_pretty(&serde_json::json!({
        "tick_snapshots": report.tick_snapshots,
        "offline_gaps": report.offline_gaps,
        "final_snapshot": report.final_snapshot,
    }))
    .expect("serialize");
    fs::write(&golden, format!("{}\n", actual.trim_end())).expect("write golden");
    println!("wrote {}", golden.display());
}
