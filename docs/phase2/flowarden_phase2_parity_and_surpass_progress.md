# Flowarden 补齐与反超 — 落地进度

## 1. 记录规则

1. 按 `flowarden_phase2_parity_and_surpass_backlog.md` 顺序推进。  
2. 功能完成但 **§1.1 质量门禁**未过，不得标 `completed`。  
3. 每项记录：实现要点、测试证据、未纳入项、下一步。  

总纲：`flowarden_phase2_parity_and_surpass_plan.md`

---

## 2. 总览

| 任务 | 状态 | 备注 |
| --- | --- | --- |
| M2E-001 Control plane 真闭环 | `completed` | Start/Stop/Pause/Resume + UI 控制条 |
| M2E-002 统一 LiveProjection 运行态 | `completed` | Flows + TCP 均来自同一 live stream |
| M2E-003 Stream 健壮性 | `completed` | 有限重连 + latest 降级 + stale |
| M2E-004 Core failure recovery | `completed` | health watch + offline + Reconnect（不自动恢复 capture） |
| M2E-005 运行态 UI 语义收口 | `completed` | UserRunState + paused/stale/offline 映射；主路径无 gRPC 裸 Status |
| M2E-101 进程归属 | `completed` | async `listeners` lookup + Inspect/Overview 展示 |
| M2E-102 rDNS | `completed` | **机会性 PTR**（非主机语义主解）；Top Hosts 有 PTR 则显示 |
| M2E-103 观察列表/黑名单 | `completed` | Settings 可编辑并持久化 |
| M2E-104 通知/Signals | `completed` | Signals 页 + 三类语义检测（UI feed） |
| M2E-104+ Signals UX 1～4 | `completed` | 未读角标 / pivot Inspect / toast+声音 / 多实体名单 |
| M2E-105 地理增强 | `completed` | Country 已在 projection/UI；本轮确认 |
| M2E-106 Destination Map | `completed` | region markers + map state ready/empty |
| M2E-107 配置持久化 | `completed` | `~/Library/Application Support/Flowarden/preferences.json`（macOS AppData） |
| M2X-006 TLS SNI | `completed` | Light DPI：ClientHello SNI → connection/host projection + UI |
| M2X-001 BehaviorSignal | `completed` | core 检测 + projection.signals + SetSignalPolicy；UI 优先读 core |
| M2X-002 Live/Offline 对称 | `completed` | offline=`finding` 稳定；live=`active/updated`+cooldown；start 清会话 |
| M2X-003 Analyst pivot（最小） | `completed` | Signal 行点击 → Inspect 按 host/service/process/sni 过滤 |
| M2X-004 CLI/UI 契约同源 | `completed` | SNI 进 CLI JSON；字段对照文档；UI-only 标注 |
| M2X-005 诊断台 | `completed` | 质量指标 + restarts + process lookup + **Export JSON** |
| M2X-007 Replay forensics | `completed` | timeline marker + Overview focus + Open Inspect |
| L2 Milestone C | `completed` | plan §8.8 出口自检通过（见 §5.9） |
| L2 回归清单 | `completed` | `flowarden_phase2_l2_regression_checklist.md` + Export 含 signals |

---

## 3. 执行日志

### 2026-07-25 — 启动落地

- 对照代码事实：
  - Start/Stop/SetSource 已存在；Pause/Resume 为 unimplemented。
  - LiveProjection + StreamOverview 已部分存在；TCP 仍独立 query。
  - 无 runtime health watch / stream 重连。
- 决策：本轮先打通 **L0 全套（M2E-001～005）**。

---

### M2E-001 Control plane 真闭环 — `completed`

#### 实现要点

1. **Rust core**  
   - `CaptureRuntime` 增加 `pause_handle`；paused 时冻结聚合（live BPF pause / offline sleep）。  
   - `CaptureType::resume(Option<&str>)` 按启动时 BPF 恢复。  
2. **ControlService**  
   - 实现 `PauseCapture` / `ResumeCapture`。  
   - `CaptureStatus::Paused`；overview `capture_status` 联动。  
   - Start 时若已 active（running/paused/stopping）拒绝。  
3. **UI**  
   - `ControlClient.Pause/Resume`。  
   - Source 控制条：Pause / Resume / Stop / Start。  
   - Shell Capture 状态含 Paused；paused 时保持 stream 订阅。  

#### 质量验收

- `cargo test -p flowarden-core -p flowarden` 通过（含 `pause_handle_is_shared_and_independent_of_stop`）。  
- `cargo clippy ... -D warnings` 通过。  
- `dotnet build Flowarden.Ui.sln` 通过。  
- UI 沿用现有 `tools-button` / `mode-button` / `danger-button`。  

#### 未纳入

- 全局顶栏（非 Source 页）快捷 Pause/Stop。  
- Pause 时 UI 图表动画的额外冻结特效（数据已冻结）。  

---

### M2E-002 统一 LiveProjection — `completed`

#### 实现要点

1. `projection.proto`：`OverviewSnapshotResponse.top_tcp_connections`。  
2. core convert 填充 TCP 切片（与 top_n 同源）。  
3. UI `OverviewSnapshotDto.TopTcpConnections` + `ProjectionClient` 统一 Map。  
4. Inspect TCP 模式改为从 live overview 本地过滤，不再 live 路径二次 `GetTcpConnectionsPage`。  
5. 策略冻结：**唯一 live 源 = StreamOverview → LiveProjectionState**；query 仅用于冷启动 / stop 后拉取。  

#### 质量验收

- Rust test + clippy 通过。  
- UI build 通过。  

#### 未纳入

- Inspect ApplyFilters 冷路径仍可走 gRPC query（与 live 互补，非双 stream）。  

---

### M2E-003 Stream 健壮性 — `completed`

#### 实现要点

1. `ConsumeOverviewStreamAsync`：断流后退避重连，最多 5 次。  
2. 超限后标记 `_projectionStale`，降级 `GetLatestOverview` 一次。  
3. 用户文案：`Live projection interrupted...` / stale 提示。  

#### 质量验收

- UI build 通过。  
- 逻辑无无界重连。  

#### 未纳入

- 可配置重连参数（硬编码上限可接受）。  
- 单元测试覆盖 C# 重连状态机（后续可补）。  

---

### M2E-004 Core failure recovery — `completed`

#### 实现要点

1. `BeginHealthWatch` 周期 `GetHealth`。  
2. 失联 → `MarkCoreOffline`（stale + 停 stream + Core Offline）。  
3. Settings「Reconnect」→ shell `ReconnectCoreAsync`（EnsureConnected / 可 relaunch）。  
4. **默认不自动恢复 capture**，需用户再次 Start。  

#### 质量验收

- UI build 通过。  
- 安全默认：重连后文案明确要求重新 Start。  

#### 未纳入

- 页面级 stale banner 组件（目前 header supporting text）。  
- 自动检测 launched process HasExited 与 health 并行（health 已覆盖主路径）。  

---

### M2E-005 运行态 UI 语义收口 — `completed`

#### 实现要点

1. Shell `UserRunState`：`loading/ready/running/paused/stopping/offline/stale/failed`。  
2. Capture/Core 状态点与 pause/stale/offline 对齐。  
3. ControlClient 继续映射 gRPC 异常为用户可读文案。  
4. Source Pause/Resume 控件与现有 Cosmos 按钮体系一致。  

#### 质量验收

- 主控制路径不直出 `Status(StatusCode=...)`。  
- build 通过。  

#### 未纳入 / 后续抛光

- 全页统一 empty/loading 骨架组件库化。  
- UserRunState 尚未全部绑定到独立 badge 控件（字段已就绪）。  

---

## 4. 本轮质量门禁汇总

| 检查 | 结果 |
| --- | --- |
| `cargo test -p flowarden-core -p flowarden` | 通过 |
| `cargo clippy -p flowarden-core -p flowarden --all-targets -- -D warnings` | 通过 |
| `dotnet build Flowarden.Ui.sln` | 通过（0 warning） |

---

## 5. L1 落地记录（2026-07-25 续）

### M2E-101 进程归属 — `completed`（2026-07-25 修复可见性）

- `service/process_lookup.rs`：有界队列 + TTL 缓存 + worker 线程调用 `listeners::get_process_by_port`。  
- Projection `ConnectionRow` 增加 `process_name` / `process_pid` / `process_inferred`。  
- Inspect 表新增 Process 列；Overview Top Connections 显示进程摘要。  
- 热路径不阻塞：lookup 失败只显示 `—`。  
- **修复**：候选本地端口（避免误查远端 443）；查到结果后 `overview_tx` 重推，UI 无需干等下一 tick。  

### M2E-102 rDNS — `completed`（定性：机会性 PTR，非最佳主解）

- `service/rdns_lookup.rs`：`dns-lookup` 异步 worker（超时 + 多 worker）。  
- `HostRow.hostname`；Top Hosts **有 PTR 时**前置显示，否则 **IP · 国家**。  
- **产品决策（已确认）**：rDNS **不是** Top Hosts 主机语义的最佳解。  
  - 主解应是 **TLS SNI / 应用域名**（Phase3 / M2X-006）。  
  - 稳定底是 **Country[+ASN] + IP**。  
  - rDNS 仅作公网基础设施 PTR 补充（与 Sniffnet 同角色）。  
- **验收**：不要求「Top Hosts 大多显示业务主机名」；无 PTR 属正常。  

### M2E-105 / M2E-106 — `completed`

- Country 已在 host/destination projection。  
- Destination map：`state=ready|empty|error`；UI 已有 equal-earth 标记与 Top Regions。  

### M2E-103 / M2E-104 / M2E-107 — `completed`

- `UserPreferencesStore` 持久化 TopN、阈值、watched/known-bad、shutdown 偏好。  
- Settings 可编辑 watchlist 与阈值。  
- Signals 页 + 三类语义（阈值 / watched / known-bad）基于 live overview 本地检测。  

### 质量门禁

- `cargo test` / `clippy -D warnings`：通过  
- `dotnet build`：通过  

### 手测建议（L1）

1. 启动 live capture，Inspect 中 Process 列应逐步补全本机进程名。  
2. Top Hosts：有 PTR 时出现 hostname；多数 CDN 可能仍是 `IP · 国家`（预期内）。  
3. Overview 地图/Top Regions 在有公网目的地时非空。  
4. Settings 配置 watched host 与较低阈值 → Signals 页出现条目；重启 UI 配置仍在。  

## 5.1 M2X-006 TLS SNI — `completed`（2026-07-25）

### 实现

1. `analysis/tls_sni.rs`：单包 ClientHello SNI 解析 + 单测  
2. `decoder`：TCP payload 形如 `0x16` 时提取 SNI  
3. `FlowCounters` / `HostCounters` 挂载 `sni`（首见保留）  
4. live snaplen **200 → 512**（便于看到 ClientHello）  
5. gRPC：`ConnectionRow.sni`、`HostRow.sni`  
6. UI：Inspect **SNI** 列；Top Hosts 展示优先 **SNI > rDNS > IP·国家**

### 质量

- `cargo test` / `clippy -D warnings` 通过  
- golden 已更新 `sni` 字段  
- `dotnet build` 通过  

### 手测

1. 完全退出 UI 再启（强制新 core）  
2. 浏览 HTTPS 站点后：Inspect 应出现 SNI（如 `example.com`）  
3. Top Hosts 对应 IP 优先显示 SNI 域名  

### 边界

- 需看到 ClientHello（加密后的流量无 SNI）  
- ECH / 会话复用可能看不到明文 SNI  
- 截断过大的 ClientHello 仍可能失败（snaplen 512 覆盖常见情况）  

## 5.2 M2X-001 BehaviorSignal — `completed`（2026-07-25）

### 实现

1. `service/signals.rs`：三类 Sniffnet 兼容检测  
   - `DataThresholdExceeded`  
   - `WatchedEntityTransmitted`  
   - `KnownBadHostTransmitted`  
2. `Control.SetSignalPolicy`：阈值 + watchlist + known-bad 下发 core  
3. `OverviewSnapshot.signals`：projection 携带信号日志（最多 30）  
4. UI：启动/保存偏好时 `SetSignalPolicy`；Signals 页优先消费 core 信号  

### 质量

- 引擎单测通过  
- `cargo test` / `clippy` / `dotnet build` 通过  

### 手测

1. Settings 设较低阈值（如 `1000`）+ watched host  
2. Start capture → Signals 页出现 core 产生的条目  
3. 重启后偏好仍下发  

## 5.3 Signals UX 1～4 — `completed`（2026-07-25）

Sniffnet 式体验补齐（相对纯 feed）：

| # | 能力 | 实现 |
| --- | --- | --- |
| 1 | 未读角标 | Rail `Signals` 旁数字 badge；`SignalFeedState.UnreadCount`；Mark read 清除 |
| 2 | Pivot → Inspect | 行点击 `OpenSignal` → `ApplySignalPivotAsync`；host/service/process/sni 本地保留过滤 |
| 3 | 可选通知/声音 | Settings 勾选；`SignalAlertService` 应用内 toast + OS 声音（afplay/Beep/paplay） |
| 4 | 多实体名单 | Settings 支持 `host` / `service:` / `process:` / `sni:` 前缀；core `parse_entity_patterns` 分桶 |

### 质量

- `cargo test --bin flowarden` 30 passed  
- `cargo clippy --bin flowarden -- -D warnings` 通过  
- `dotnet build` 0 warning / 0 error  

### 手测建议

1. Settings → Watched：`service:https, process:Edge, sni:github.com` + 阈值 1000 → Save lists  
2. 开 Desktop notifications（可选 sound）→ Start capture  
3. Rail Signals 出现未读数字；页内 ● 标记  
4. 点信号行 → 跳转 Inspect 并按 pivot 过滤  
5. Mark read → 角标消失  

## 5.4 M2X-002 Live/Offline 对称 — `completed`（2026-07-25）

### 实现

1. 同一 `SignalEngine` detector：  
   - **live**：`status=active|updated`，实体 cooldown 20s，阈值 cooldown 30s  
   - **offline**：`status=finding`，稳定 finding，重评估只合并 dedupe、不刷屏  
2. `StartCapture` 调用 `reset_session()` 清上一会话信号  
3. Signals 页展示 `live · active` / `offline · finding`  
4. 单测：`offline_finding_is_stable_and_deduped`、`live_entity_respects_cooldown`、`reset_session_clears_findings`

## 5.5 M2X-004 CLI/UI 契约 — `completed`（2026-07-25）

1. CLI `top_hosts_enriched[].sni` / `top_connections_enriched[].sni`  
2. 文档：`docs/phase2/flowarden_cli_ui_field_contract.md`（含 UI-only 列表）  
3. JSON 单测断言 SNI  

## 5.6 M2X-005 诊断台最小 — `completed`（2026-07-25）

Settings Diagnostics 实时指标：

- dropped packets  
- last packet age  
- stream / UserRunState  
- core uptime（health started_at）  

随 Overview 刷新。

### 质量

- `cargo test --bin flowarden` **33 passed**  
- `cargo clippy --bin flowarden -- -D warnings`  
- `dotnet build` 0 error  

## 5.7 M2X-007 Replay forensics（最小）— `completed`（2026-07-25）

### 实现

1. **Offline findings 可浏览**：Signals 页区分 offline finding 摘要文案；mode/status 展示  
2. **Finding → Overview + Inspect pivot**  
   - offline 点击：`Overview.ApplyForensicsFocus` 重排 Top Hosts/Connections  
   - 同时 `Inspect.ApplySignalPivotAsync` 过滤  
3. **Timeline 策略区分**  
   - Overview 图表头：`Timeline · full offline capture` vs `live rolling window`  
   - Hero summary 带 focus 提示  
4. 新 capture starting 时清空 signal feed / forensics focus  

### CLI findings 同源

1. `findings[]` 写入 CLI JSON（同一 `SignalEngine`）  
2. Flags：`--data-threshold` / `--watch` / `--known-bad`  
3. 单测：`json_output_includes_offline_findings_with_policy`  

### 质量

- `cargo test --bin flowarden` **34 passed**  
- `cargo clippy --bin flowarden -- -D warnings`  
- `dotnet build` 0 error  

## 5.8 L2 深化 — timeline marker / 诊断 / Inspect process（2026-07-25）

### Timeline 秒级 marker

1. Offline finding 点击 → 跳转 Overview  
2. 按 `signal.Timestamp` 最近 timeline point 画 **FINDING** 竖线 + 时间标签  
3. 同步重排 Top Hosts/Connections；Inspect 预填 pivot（切换 Inspect 即可看）  

### 诊断扩展

1. `process_lookup_pending` / `process_lookup_cache_size` 进 projection  
2. Settings：PROCESS LOOKUP `q=… · cache=…`  
3. CORE RESTARTS：UI 侧 Reconnect 成功 relaunch 计数  

### Inspect process/sni 过滤

`MatchesFilter` 识别 `Bpf` 中的 `process:` / `sni:` 前缀（pivot 与手填均生效）。

### 质量

- `cargo test` 34 passed · clippy clean · `dotnet build` ok  

## 5.9 L2 收尾 — Export / 里程碑自检（2026-07-25）

### Diagnostics Export

1. Settings → Diagnostics → **Export** 写出 JSON 快照（核心健康、capture 质量、偏好计数、诊断条目）  
2. 优先系统 Save 对话框；失败回落 `~/Library/Application Support/Flowarden/exports/`  

### Forensics → Inspect

Overview 在有 forensics focus 时显示 **Open Inspect**（Inspect 已预填 pivot）。

### Milestone C / L2 Surpass 出口自检

| # | 条件（plan §8.8） | 状态 |
| --- | --- | --- |
| 1 | BehaviorSignal 实时+离线可演示 | ✅ M2X-001/002/007 |
| 2 | CLI JSON 与 UI 增强字段同源可测 | ✅ M2X-004 + findings/sni 单测 |
| 3 | Analyst pivot 工作流可用 | ✅ Signal→Inspect / offline→Overview+Inspect |
| 4 | SNI Light DPI 在 Inspect 可见 | ✅ M2X-006 |
| 5 | Signals/forensics/诊断融入 Cosmos shell | ✅ 同 rail/token |
| 6 | 质量门禁 + 自动化证据 | ✅ cargo test 34 / clippy / dotnet build |

**宣布：Milestone C / L2 Surpass Slice 成立**（4/4 主张 + 门禁 5–6）。

### 仍属边界外 / 后续

- CLI process findings（无 OS lookup）  
- Inspect 绝对时间窗 scrub 控件  
- Phase3：ARP / 会话状态机 / HTTP·DNS 全解析 / 流重组  

## 5.10 收口增强 — Export signals + 回归清单（2026-07-25）

1. Diagnostics Export JSON 增加 `signals[]` 当前 feed 快照  
2. 手测清单：`docs/phase2/flowarden_phase2_l2_regression_checklist.md`  
3. CLI smoke（fixture）：`offline_mixed_ethernet.pcap` + `--data-threshold 1 --watch service:https` → 2 findings  

## 6. 下一步（Phase2 收口后）

1. 按 `flowarden_phase2_l2_regression_checklist.md` 手测签署  
2. **Phase3 已启动**：ARP 波次 1 见 `docs/phase3/flowarden_phase3_progress.md`  
3. 边界外：CLI process findings、Inspect 时间窗 scrub、Export 含 full tick dump  




---

## 7. 关键改动路径（索引）

| 区域 | 路径 |
| --- | --- |
| Runtime pause | `flowarden/flowarden-core/src/capture/runtime.rs` |
| ControlService | `flowarden/flowarden/src/service/control.rs` |
| Process lookup | `flowarden/flowarden/src/service/process_lookup.rs` |
| rDNS lookup | `flowarden/flowarden/src/service/rdns_lookup.rs` |
| Projection enrich | `flowarden/flowarden/src/service/convert.rs`, `projection.proto` |
| Signal engine | `flowarden/flowarden/src/service/signals.rs` |
| CLI JSON + findings | `flowarden/flowarden/src/output.rs`, `cli.rs`, `main.rs` |
| CLI/UI 契约 | `docs/phase2/flowarden_cli_ui_field_contract.md` |
| Signals / prefs | `flowarden-ui/.../State/SignalFeedState.cs`, `UserPreferencesStore.cs` |
| Signals UI | `SignalsPageView.axaml`, `SignalsPageViewModel.cs`, `AppRailView.axaml` |
| Replay focus | `OverviewPageViewModel.ApplyForensicsFocus` |
| Diagnostics | `SettingsDiagnosticsPanelView.axaml`, `SettingsPageViewModel` |
| Alert | `flowarden-ui/.../Services/SignalAlertService.cs` |
| Inspect pivot | `InspectPageViewModel.ApplySignalPivotAsync` |
| Inspect process col | `InspectResultsTableView.axaml` |
