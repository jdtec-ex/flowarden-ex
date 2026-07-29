# Flowarden

Flowarden is a headless traffic monitor implemented in Rust.

Phase 1 delivers a CLI-first capture and analysis pipeline that can:

- list capture devices
- run live capture
- replay offline `pcap`
- decode and classify packets
- aggregate traffic by second
- render table or JSON output

This phase intentionally does not include `iced` UI, Avalonia UI, multilingual support, payload deep parsing, or session reconstruction.

## Phase 1 Scope

Phase 1 is the contract baseline for later UI work.

It keeps these Sniffnet-inspired core capabilities:

- shared live/offline analysis pipeline
- offline replay driven by `pcap` timestamps
- direction classification with offline fallback
- service classification that does not degenerate to destination-port-only
- stable per-second snapshots and final aggregates
- BPF support
- explicit unsupported link-type handling

It explicitly defers these capabilities:

- Avalonia UI
- payload deep parsing
- session-level reconstruction
- channel/gRPC-based runtime communication

## Build

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo build
```

## Usage

### List Devices

```bash
cargo run -p flowarden -- devices
cargo run -p flowarden -- devices --format json
```

Preview all devices before selecting one for capture:

```bash
cargo run -p flowarden -- devices --preview 2
```

This preview scans all available devices for a short duration and reports per-device packet/byte counts.
Formal capture still uses exactly one selected source.

### Live Capture

```bash
cargo run -p flowarden -- capture --device en0 --duration 5
```

With BPF:

```bash
cargo run -p flowarden -- capture --device en0 --duration 5 --bpf "tcp"
```

Save raw packets to `pcap` while capturing:

```bash
cargo run -p flowarden -- capture --device en0 --duration 5 --pcap-out ./capture.pcap
```

Notes:

- macOS live capture usually requires access to `/dev/bpf*`
- `Ctrl+C` requests graceful stop
- `--output` is for rendered analysis output
- `--pcap-out` is for raw packet savefile output

### Offline Replay

```bash
cargo run -p flowarden -- capture --read ./sample.pcap
```

With JSON output:

```bash
cargo run -p flowarden -- capture --read ./sample.pcap --format json
```

Write JSON to file:

```bash
cargo run -p flowarden -- capture --read ./sample.pcap --format json --output ./out.json
```

### Capture Command Options

```text
flowarden capture [OPTIONS]

    --device <DEVICE>
    --read <READ>
    --bpf <BPF>
    --format <FORMAT>      [default: table] [possible values: table, json]
    --output <OUTPUT>
    --pcap-out <PCAP_OUT>
    --duration <DURATION>
    --top <TOP>            [default: 10]
```

## Output Contract

Phase 1 exposes two output forms:

- `table`: human-readable final summary
- `json`: stable machine-readable snapshot contract

The JSON contract includes:

- `tick_snapshots`
- `final_snapshot`
- `totals`
- `top_connections`
- `top_hosts`
- `top_services`

This output model is the intended starting point for Phase 2 UI integration.

## Quality Gates

Phase 1 seal criteria:

```bash
cargo fmt --all -- --check
cargo clippy -q --all-targets --all-features -- -D warnings
cargo test -q
```

## Regression Assets

Key fixed assets for repeatable review:

- `flowarden-core/tests/fixtures/offline_mixed_ethernet.pcap`
- `flowarden-core/tests/golden/offline_mixed_ethernet.json`
- `flowarden-core/tests/offline_capture_golden.rs`

## License

Licensed under the Apache License, Version 2.0.
