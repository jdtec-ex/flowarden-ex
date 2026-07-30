# GitHub listing copy (paste pack)

Strings for https://github.com/jdtec-ex/flowarden-ex — **Settings → General → About**, social posts, and **Releases**.

Keep in sync with the root [README](../README.md) when product surface changes (signals, syslog, DPI).

---

## About → Description

**Primary (≤350 chars, recommended):**

```text
Desktop network traffic monitor: live capture & pcap, ranked hosts/services/connections, destination map, process attribution, TLS SNI. Behavior signals (threshold, watchlists, exfil/idle/P2P heuristics) and RFC5424+CEF syslog for signals & flows. Rust core + Avalonia UI over gRPC, plus CLI.
```

**Shorter alternative:**

```text
Network traffic monitor — Rust core, Avalonia UI, live/pcap, SNI & process context, behavior signals, CEF syslog, Inspect filters, CLI.
```

---

## About → Website

Optional: point to the latest release downloads:

```text
https://github.com/jdtec-ex/flowarden-ex/releases
```

Leave empty if you prefer users to use the Releases tab only.

---

## About → Topics

Add as individual topic chips (order roughly by priority):

```text
network-monitoring
packet-capture
pcap
traffic-analysis
network-security
syslog
cef
dpi
rust
avalonia
dotnet
desktop-app
grpc
cli
tls-sni
process-monitoring
sniffnet
open-source
```

GitHub caps topics (~20). Prefer the list above; drop lower-priority tags if needed rather than inventing synonyms.

---

## About → checkboxes

- [x] Releases  
- [x] Packages (optional)  
- [x] Deployments (optional)  
- [ ] Wiki (optional; README is enough for Public Beta)

**Pin** this repo on the `jdtec-ex` organization / personal profile if possible.

---

## Social blurb (Reddit / X / Show HN)

**One-liner:**

```text
Flowarden: desktop network monitor — Rust analysis core, Avalonia UI, CLI. Live + pcap, SNI, process context, behavior signals, and CEF syslog for SIEM-style export.
```

**Short post (EN):**

```text
I open-sourced Flowarden — a desktop network traffic monitor inspired by Sniffnet, rebuilt with a different architecture:

• Rust resident core (capture → aggregate → projection + signals)
• Avalonia UI over local gRPC
• First-class CLI with the same contract
• Live + pcap, destination map, process icons, TLS SNI
• Inspect filters, pivots, and policy-driven signals
• Behavior detectors: data threshold, watch / known-bad, large unidirectional transfer, long-idle TCP, P2P/proxy heuristics
• Syslog export: RFC5424 envelope + CEF body (signals and Inspect-style flows); Settings UI or core CLI flags

Not a Sniffnet fork — same problem space, clearer core/UI split, SIEM-friendly export, room for deeper DPI later.

https://github.com/jdtec-ex/flowarden-ex
```

**Short post (ZH):**

```text
开源了 Flowarden：桌面网络流量监控。

Rust 常驻分析核心 + Avalonia UI（本地 gRPC）+ 同源 CLI。
支持 live / pcap、目的地图、进程归属、TLS SNI、Inspect 过滤。

策略与行为信号：阈值、关注/黑名单、单向大体量、长空闲 TCP、P2P/代理启发式。
Syslog 外发：RFC5424 + CEF（signal 与 flow 同一套字段习惯），Settings 或 core 参数配置。

灵感来自 Sniffnet，但不是 fork：强调 Core/UI 分离、可接 SIEM、可扩展 DPI。

https://github.com/jdtec-ex/flowarden-ex
```

---

## Release title format

```text
Flowarden 0.1.0
Flowarden 0.1.1
…
```

Git tags: `0.1.0`, `0.1.1`, … (no `v` prefix preferred).

---

## Release `Flowarden 0.1.0` — body

Default release body (also set in `.github/workflows/release.yml`):

```text
First Public Release of Flowarden.
```

Longer notes (optional paste if you want more detail):

```markdown
# Flowarden 0.1.0

First Public Release of Flowarden.

**Public Beta.** Desktop network traffic monitor with a **Rust resident core**, an **Avalonia** UI over local gRPC, and a **CLI** that shares the same projection contract.

Inspired by [Sniffnet](https://github.com/GyulyVGC/sniffnet); not a fork. Same problem space, different architecture.

## Highlights

### Capture & analysis
- Live capture from a selected interface, or offline **pcap / pcapng** replay
- Capture-time **BPF** (applied when a session starts)
- Shared live/offline pipeline: decode, direction, service labels, per-second aggregation
- **ARP**, TCP/UDP/ICMP, TLS ClientHello **SNI**, local **process** name/PID (async)
- Resident mode with **bounded memory** for long-running sessions
- Optional raw pcap write-out during capture

### Behavior signals
Produced in the core while capture runs (or when offline replay finishes). Shown on the **Signals** page and in overview projection.

| Kind | Role |
| --- | --- |
| `DataThresholdExceeded` | Session byte total above a configured ceiling |
| `WatchedEntityTransmitted` | Host / service / process match on the watch list |
| `KnownBadHostTransmitted` | Same matching against the known-bad list |
| `UnidirectionalLargeTransfer` | Large, mostly outbound connection (exfil-style heuristic) |
| `LongIdleTcpConnection` | Long-lived established TCP with little recent payload |
| `UnauthorizedP2pOrProxy` | Port / process / SNI heuristics for P2P or proxy tooling |

Policy: Settings (threshold, watched / known-bad) or CLI (`--data-threshold`, `--watch`, `--known-bad`). Detectors use cooldowns so the feed does not spam. Details and how to exercise each kind: [README → Behavior signals](https://github.com/jdtec-ex/flowarden-ex#behavior-signals).

### Syslog export
- **RFC5424** transport + **CEF** message body (common SIEM / traffic-log shape)
- **Signals** (`cat=signal`) and **Inspect-style flow** summaries (`cat=traffic`)
- CEF dictionary keys where applicable: `src`, `dst`, `spt`, `dpt`, `proto`, `in`, `out`, `act`, `app`, …
- Configure via Settings (**Syslog Export** → Enable / target / protocol / emit flags) or resident core flags (`--syslog-target`, `--syslog-proto`, …)
- Live config also via control `GetSyslogConfig` / `SetSyslogConfig`

### Desktop console
- **Source** — interfaces, preview samples, start / pause / resume / stop
- **Overview** — in/out throughput, destination map & regions, top hosts / services / connections
- **Inspect** — search + structured filters, removable chips, flows & TCP tables
- **Signals** — policy + behavior detectors, unread badge, toast & sound, pivot to Inspect
- **Settings** — Top N, signal policy, **syslog**, diagnostics export, UI density
- **Thumbnail** always-on-top monitoring window
- Process icons on connection rankings when the OS can resolve them

### Cross-page workflow
- One-click pivot from Overview rankings (hosts, services, connections, **regions / map markers**) into Inspect
- Offline findings can focus the timeline and open Inspect with matching filters
- Country filtering on Inspect via host geography from the live projection

### CLI
- `devices` / `capture` with **table** or **JSON** output
- Enrichment aligned with the UI where applicable (hosts, connections, SNI, findings)
- `core --bind …` with optional `--syslog-target` for resident mode

## Architecture (why this shape)

```text
Avalonia UI  ←— gRPC —→  flowarden resident core
                              ↓
                        flowarden-core
CLI uses the same core without the UI.
```

UI consumes **projections** only. Capture BPF and Inspect filters are intentionally separate. Syslog is emitted from the **core**, not the UI process.

## Build

```bash
# Core + CLI
cd flowarden && cargo build --release

# Desktop UI
cd flowarden-ui && dotnet build Flowarden.Ui.sln -c Release
```

See the [README](https://github.com/jdtec-ex/flowarden-ex#readme) for run instructions, signal testing notes, and screenshots.

## Notes

- GeoLite2 country/ASN databases may ship under `flowarden/flowarden/resources/DB/` for offline enrichment. UI does **not** surface ASN labels by product choice. MaxMind license/attribution apply if you redistribute those files.
- Process attribution is **heuristic** (local port → process), same class of approach as other desktop monitors.
- Signal / DPI defaults are conservative (e.g. large exfil thresholds, hour-scale idle). Lower them for lab tests.
- **DPI roadmap:** light DPI today (TLS SNI + behavior heuristics); richer application-layer parsing is planned without turning Flowarden into a full IDS.

## Thanks

GyulyVGC and the Sniffnet community for proving this class of desktop monitor works well — and for the inspiration behind the monitoring experience.
```

---

## Create the release (commands)

Release packages are built by **`.github/workflows/release.yml`** when an `0.1.*` tag is pushed. The GitHub Release title is **`Flowarden 0.1.x`**.

Assets:

- `flowarden-linux-x64.tar.gz`
- `flowarden-macos-arm64.tar.gz`
- `flowarden-windows-x64.zip`

Each archive is a **full portable bundle** (self-contained Avalonia UI + `flowarden` core).

```bash
cd /Users/wangli/workspace/coding/flowarden
# main green first
git tag -a 0.1.0 -m "Flowarden 0.1.0"
git push origin 0.1.0
# Wait for Actions → "Release" workflow → assets appear on the Releases page
```

Users download from:

```text
https://github.com/jdtec-ex/flowarden-ex/releases
```

Optional: paste the **Release body** section above into the release notes (the workflow also generates notes from commits). Manual `gh release create` is only needed if you skip the workflow.
