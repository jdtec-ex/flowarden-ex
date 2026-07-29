# GitHub listing copy (paste pack)

Use these strings on https://github.com/jdtec-ex/flowarden-ex — **Settings → General → About**, and when drafting a **Release**.

---

## About → Description

**Primary (≤350 chars, recommended):**

```text
Desktop network traffic monitor: live capture & pcap replay, ranked hosts/services/connections, destination map, process attribution, TLS SNI, and policy signals. Rust resident core + Avalonia UI over gRPC, with a first-class CLI.
```

**Shorter alternative:**

```text
Network traffic monitor for the desktop — Rust core, Avalonia UI, live/pcap, SNI & process context, Inspect filters, and CLI projections.
```

---

## About → Website

Leave empty unless you have a landing page.  
Optional later: docs site or release download page.

---

## About → Topics

Add as individual topic chips (order roughly by priority):

```text
network-monitoring
packet-capture
pcap
traffic-analysis
network-security
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

**Do not spam** more than ~15. Prefer exact topic slugs above.

---

## About → checkboxes

- [x] Releases  
- [x] Packages (optional)  
- [x] Deployments (optional)  
- [ ] Wiki (optional; README is enough for v0.1)

**Pin** this repo on the `jdtec-ex` organization / personal profile if possible.

---

## Social blurb (Reddit / X / Show HN)

**One-liner:**

```text
Flowarden: desktop network monitor with a Rust analysis core, Avalonia UI, and CLI that share the same projections — live + pcap, SNI, process context, Inspect filters.
```

**Short post (EN):**

```text
I open-sourced Flowarden — a desktop network traffic monitor inspired by Sniffnet, rebuilt with a different architecture:

• Rust resident core (capture → aggregate → projection)
• Avalonia UI over local gRPC
• First-class CLI with the same contract
• Live + pcap, destination map, process icons, TLS SNI
• Inspect filters, pivots, and policy-driven signals

Not a Sniffnet fork — same problem space, clearer core/UI split and room for deeper DPI later.

https://github.com/jdtec-ex/flowarden-ex
```

**Short post (ZH):**

```text
开源了 Flowarden：桌面网络流量监控。

Rust 常驻分析核心 + Avalonia UI（本地 gRPC）+ 同源 CLI。
支持 live / pcap、目的地图、进程归属、TLS SNI、Inspect 过滤与策略信号。

灵感来自 Sniffnet，但不是 fork：强调 Core/UI 分离与可扩展 DPI 路径。

https://github.com/jdtec-ex/flowarden-ex
```

---

## Release `v0.1.0` — title

```text
v0.1.0 — First public release
```

---

## Release `v0.1.0` — body (paste into GitHub Release)

```markdown
# Flowarden v0.1.0

First public release of **Flowarden** — a desktop network traffic monitor with a **Rust resident core**, an **Avalonia** UI over local gRPC, and a **CLI** that shares the same projection contract.

Inspired by [Sniffnet](https://github.com/GyulyVGC/sniffnet); not a fork. Same problem space, different architecture.

## Highlights

### Capture & analysis
- Live capture from a selected interface, or offline **pcap / pcapng** replay
- Capture-time **BPF** (applied when a session starts)
- Shared live/offline pipeline: decode, direction, service labels, per-second aggregation
- **ARP**, TCP/UDP/ICMP, TLS ClientHello **SNI**, local **process** name/PID (async)
- Resident mode with **bounded memory** for long-running sessions
- Optional raw pcap write-out during capture

### Desktop console
- **Source** — interfaces, preview samples, start / pause / resume / stop
- **Overview** — in/out throughput, destination map & regions, top hosts / services / connections
- **Inspect** — search + structured filters, removable chips, flows & TCP tables
- **Signals** — threshold / watched / known-bad policy, unread badge, toast & sound, pivot to Inspect
- **Settings** — Top N, policy, diagnostics export, UI density
- **Thumbnail** always-on-top monitoring window
- Process icons on connection rankings when the OS can resolve them

### Cross-page workflow
- One-click pivot from Overview rankings (hosts, services, connections, **regions / map markers**) into Inspect
- Offline findings can focus the timeline and open Inspect with matching filters
- Country filtering on Inspect via host geography from the live projection

### CLI
- `devices` / `capture` with **table** or **JSON** output
- Enrichment aligned with the UI where applicable (hosts, connections, SNI, findings)

## Architecture (why this shape)

```text
Avalonia UI  ←— gRPC —→  flowarden resident core
                              ↓
                        flowarden-core
CLI uses the same core without the UI.
```

UI consumes **projections** only. Capture BPF and Inspect filters are intentionally separate.

## Build

```bash
# Core + CLI
cd flowarden && cargo build --release

# Desktop UI
cd flowarden-ui && dotnet build Flowarden.Ui.sln -c Release
```

See the [README](https://github.com/jdtec-ex/flowarden-ex#readme) for full run instructions and screenshots.

## Notes

- GeoLite2 country/ASN databases may ship under `flowarden/flowarden/resources/DB/` for offline enrichment. UI does **not** surface ASN labels by product choice. MaxMind license/attribution apply if you redistribute those files.
- Process attribution is **heuristic** (local port → process), same class of approach as other desktop monitors.
- **DPI roadmap:** light DPI today (TLS SNI); broader application-layer parsing is planned without turning Flowarden into a full IDS.

## Thanks

GyulyVGC and the Sniffnet community for proving this class of desktop monitor works well — and for the inspiration behind the monitoring experience.
```

---

## Create the release (commands)

After this file is on `main`:

```bash
cd /Users/wangli/workspace/coding/flowarden
git tag -a v0.1.0 -m "v0.1.0 — First public release"
git push origin v0.1.0
```

Then GitHub → **Releases → Draft a new release** → choose tag `v0.1.0` → paste **title** and **body** above → **Publish release**.

Or with GitHub CLI (if installed later):

```bash
gh release create v0.1.0 --title "v0.1.0 — First public release" --notes-file .github/GITHUB_LISTING.md
```

(Prefer copying only the Release body section into `--notes` for a clean release page.)
