# Flowarden 第二阶段任务进度

## 1. 记录规则

1. 仅当任务同时满足 backlog 中的“输出”和“验收条件”时，才标记为“已完成”。
2. 若实现偏离既定架构约束，即使代码可运行，也不得标记为“已完成”。
3. 阶段状态必须同时对齐以下文档：
   - `../flowarden_phased_development_plan.md`
   - `flowarden_phase2_development_plan.md`
   - `flowarden_phase2_backlog.md`
4. 页面壳层、样本驱动 MVP、真实运行闭环必须分开记录，禁止混写成“已完成”。
5. 每个任务完成后先记录状态，再等待用户确认是否进入下一个任务。

---

## 2. 审计结论

本文件已按初始总方案和 phase2 计划重新审计。

结论如下：

1. 第二阶段当前不能标记为“已封板完成”。
2. 当前已经成立的是：
   - Avalonia 工程骨架
   - gRPC 最小 skeleton
   - UI shell
   - Source / Overview / Inspect / Settings 的样本驱动 MVP
3. 当前尚未成立的是：
   - UI 启动后真实拉起或连接 core 的运行闭环
   - Source 页面真实 `ListDevices / ListDevicePreviews` 接线
   - `Start / Stop / Pause / Resume`
   - Overview 对 `tick_snapshots / final_snapshot` 的真实投影接线
   - Inspect 对后端 query / projection 的真实接线
   - Settings 对 runtime / health / version / diagnostics 的真实接线
4. 因此，第二阶段当前应被描述为：

> `gRPC skeleton + 样本驱动 UI MVP 已完成，但与初始总方案一致的真实运行闭环尚未完成。`

详细对照见：

- `flowarden_phase2_audit_against_plan.md`

---

## 3. 当前状态总览

| 任务 | 真实状态 | 说明 |
| --- | --- | --- |
| `M2-001` | 已完成 | Avalonia 工程骨架与 `net8.0` / `8.0.125` 基线已落地。 |
| `M2-002` | 进行中 | `proto + tonic + .NET gRPC client` 最小 skeleton 已落地，但 UI 应用本身尚未真实使用 launcher / health 连接闭环。 |
| `M2-002` | 进行中 | `flowarden core` resident mode 已改名并可常驻运行，UI 启动链已接入真实探活/拉起，但仍缺 UI 内更完整的运行态反馈与后续 control/projection 接线。 |
| `M2-003` | 已完成 | phase2 最小 DTO/契约模型已冻结，且未直接依赖 Rust 内部结构体。 |
| `M2-004` | 已完成 | `Left Rail + Top App Bar + Main Workbench` 壳层已落地并可稳定切页。 |
| `M2-005` | 进行中 | Source 页面结构与文案边界已完成，但设备与 preview 仍是样本驱动，不是运行中 core 的真实结果。 |
| `M2-006` | 进行中 | Overview 页面结构已完成，但未接入 `tick_snapshots / final_snapshot` 的真实 projection。 |
| `M2-007` | 进行中 | Inspect 页面结构已完成，但过滤与结果仍作用于本地样本，不是后端 query / projection。 |
| `M2-008` | 进行中 | Settings 页面结构已完成，但 runtime / core / diagnostics 仍由本地样本填充。 |
| `M2-009` | 进行中 | runbook、模板和质量门禁文件已存在，但“从启动到抓包到关闭流程完整”的封板条件未满足。 |
| `M2-101` | 已完成 | Destination workbench 的 reserved / future-state UI 壳增强已完成，但它不代表 phase2 主线已封板。 |

---

## 4. 任务记录

### M2-001 UI 工程骨架与 SDK 基线

- 状态：已完成
- 完成依据：
  - `flowarden-ui/`、`Flowarden.Ui.sln`、`src/Flowarden.Ui/` 已建立
  - `global.json` 固定 `8.0.125`
  - `TargetFramework = net8.0`
  - 窗口可启动，`dotnet build` 通过
- 提交：
  - outer repo `9315b9a` `Complete M2-001 Avalonia UI scaffold and sdk baseline`

### M2-002 core service mode 与本地 IPC 骨架

- 状态：进行中
- 已成立：
  - `flowarden core` 作为单一可执行程序内的 resident mode 入口
  - 共享 `proto/flowarden/health.proto`
  - 共享 `proto/flowarden/discovery.proto`
  - Rust 侧 `tonic` gRPC service mode
  - `.NET gRPC client`
  - 最小 RPC：
    - `GetHealth`
    - `GetVersion`
    - `ListDevices`
    - `ListDevicePreviews`
- 审计证据：
  - Rust 仅暴露 `HealthService` 和 `DiscoveryService`
  - 代码位置：`../../flowarden/flowarden/src/service.rs`
  - `ControlService`、`ProjectionService`、`GetRuntimeStatus` 尚不存在
  - UI 已能探活并在需要时拉起 `flowarden core`，但 UI 内仍缺少后续更完整的运行态反馈与控制面接线
- 未满足的验收项：
  - UI 可拉起 core 或连接已运行 core
  - UI 内部可判断 core 是否在线并更新真实状态
  - 错误路径在 UI 内真实闭环
- 已保留提交：
  - inner repo `7b1c4d8` `Rework M2-002 to gRPC service mode skeleton`
  - outer repo `83a8b89` `Rework M2-002 to gRPC ui client skeleton`

### M2-003 phase2 契约模型冻结

- 状态：已完成
- 完成内容：
  - `DeviceSummaryDto`
  - `DevicePreviewDto`
  - `CaptureSessionStateDto`
  - `OverviewSnapshotDto`
  - `InspectFilterDto`
  - `InspectResultDto`
  - `CoreErrorDto`
- 结构约束：
  - DTO 与 Rust 内部领域模型分离
  - `Destination Map` placeholder 契约已预留
  - 未提前引入 phase3 payload / session 字段
- 提交：
  - outer repo `a21b72a` `Complete M2-003 phase 2 dto contract freeze`

### M2-004 App Shell 与全局状态层

- 状态：已完成
- 完成内容：
  - `AppShellView`
  - `AppRailView`
  - `AppHeaderView`
  - `AppShellViewModel`
- 已成立：
  - `Left Rail + Top App Bar + Main Workbench`
  - `Source / Overview / Inspect / Settings` 稳定切页
  - `Cosmos Network System` shell 基线
  - `Start Capture` 已收敛为导航到 `Source`
  - 无多余 `Docs / Quit`
- 边界说明：
  - `core / capture` 状态点当前只是壳层状态位，不代表真实运行态
- 提交：
  - outer repo `a457e25` `Complete M2-004 app shell and global state layer`
  - outer repo `1ab36b9` `Tighten shell actions and remove extra rail items`

### M2-005 Source 页面 MVP

- 状态：进行中
- 已成立：
  - `SourcePageView`
  - `SourceDeviceListView`
  - `SourcePreviewWorkbenchView`
  - `SourcePageViewModel`
  - 页面结构为“左列表 + 右详情”
  - preview / formal capture / offline import 文案边界清楚
  - 正式 capture 仍保持单 source 语义
- 审计证据：
  - `SourcePageViewModel` 使用 `CreateSeedDevices()`
  - `RefreshPreview()` 只更新时间标签
  - `StartFormalCapture()` 只改本地 `CaptureStatus`
  - 代码位置：`../../flowarden-ui/src/Flowarden.Ui/ViewModels/SourcePageViewModel.cs`
- 未满足的验收项：
  - 所有 device preview 来自真实 gRPC 调用，而不是本地样本
  - 无权限、unsupported、无设备等场景来自真实运行时
  - 用户选择 source 后能进入真实 formal capture 控制闭环
- 已保留提交：
  - inner repo `f712ab5` `Complete M2-005 preview discovery gRPC path`
  - outer repo `8e9ab88` `Complete M2-005 source page mvp`

### M2-006 Overview 页面 MVP

- 状态：进行中
- 已成立：
  - `HeroTrafficChartView`
  - `StatusCardsRowView`
  - `DestinationWorkbenchView`
  - `TopHostsView`
  - `TopServicesView`
  - `TopConnectionsView`
  - `Destination Map + Top Destinations` 稳定留位
- 审计证据：
  - `OverviewPageViewModel` 使用 `CreateSeedSnapshot()`
  - `ProjectionClient` 仍返回 placeholder
  - 代码位置：
    - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs`
    - `../../flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs`
- 未满足的验收项：
  - 页面数据与 phase1 CLI 同步口径一致
  - live / offline 真实运行态显示
  - `tick_snapshots / final_snapshot` 的真实 projection 接线
- 已保留提交：
  - outer repo `d7cad13` `Complete M2-006 overview page mvp`

### M2-007 Inspect 页面 MVP

- 状态：进行中
- 已成立：
  - `InspectPageView`
  - `InspectHeaderView`
  - `InspectFilterBarView`
  - `InspectResultsTableView`
  - `InspectFooterSummaryView`
  - 过滤条、结果表、footer summary 结构稳定
- 审计证据：
  - `InspectPageViewModel` 使用 `CreateSeedRows()`
  - `ApplyFilters()` 仅过滤本地 `_allRows`
  - 代码位置：`../../flowarden-ui/src/Flowarden.Ui/ViewModels/InspectPageViewModel.cs`
- 未满足的验收项：
  - 过滤条件可真实下发到 core
  - 表格结果与 phase1 聚合结果真实一致
  - Inspect 页由后端 query / projection 驱动
- 已保留提交：
  - outer repo `307822b` `Complete M2-007 inspect page mvp`

### M2-008 Settings 与诊断页 MVP

- 状态：进行中
- 已成立：
  - `SettingsPageView`
  - `SettingsRuntimePanelView`
  - `SettingsCorePanelView`
  - `SettingsDiagnosticsPanelView`
  - 运行配置、core 状态、近期错误的版位与样式壳层已存在
- 审计证据：
  - `SettingsPageViewModel` 直接构造本地 `RuntimeState`、`CoreHealth`、`Diagnostics`
  - `CoreHealthService` 存在，但未被页面接入
  - 代码位置：
    - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/SettingsPageViewModel.cs`
    - `../../flowarden-ui/src/Flowarden.Ui/Services/CoreHealthService.cs`
- 未满足的验收项：
  - 当前 source / BPF / tick interval / top N 来自真实运行态
  - core endpoint / process state / version 来自真实 core
  - 近期错误提示来自真实跨进程错误语义
- 已保留提交：
  - outer repo `272cbc9` `Complete M2-008 settings and diagnostics mvp`

### M2-009 第二阶段封板与质量门禁

- 状态：进行中
- 已成立：
  - phase2 runbook
  - phase2 acceptance template
  - `dotnet format` / `dotnet build` / `cargo test -q -p flowarden`
- 未满足的验收项：
  - 从启动到抓包到关闭流程完整
  - UI 与 core 可重复运行并形成真实闭环
  - 无明显资源失控的真实长流程验证
- 结论：
  - 文档和质量门禁文件存在，不等于 phase2 已封板
  - 在 `M2-002 / M2-005 / M2-006 / M2-007 / M2-008` 未收口前，不得标记 phase2 主线完成
- 已保留提交：
  - outer repo `2618230` `Complete M2-009 phase 2 quality gates and docs`

### M2-101 Destination Workbench 增强预留

- 状态：已完成
- 完成内容：
  - 更明确的 destination placeholder model
  - 地图区域空态 / reserved state / future state 文案
  - 更稳定的 destination ranking 辅助说明
- 说明：
  - 本任务只评价 destination workbench 的 UI reserved shell
  - 它不代表 phase2 主线已经形成真实运行闭环
- 提交：
  - outer repo `fda2e27` `Complete M2-101 destination workbench reserve enhancement`

---

## 5. 当前未完成闭环项

按初始总方案和 phase2 计划，当前至少还有以下缺口：

1. UI 启动时真实检查 core、拉起 core 或连接已运行 core
2. Source 页真实 `ListDevices / ListDevicePreviews` 接线
3. `ControlService` 与 `Start / Stop / Pause / Resume`
4. `ProjectionService` 与 `tick_snapshots / final_snapshot`
5. Inspect 的真实 query / projection 接线
6. Settings 对 runtime / health / version / diagnostics 的真实接线
7. core 异常退出时，UI 真实进入可恢复状态

这些缺口在收口前，phase2 只能表述为：

> `样本驱动 UI MVP + gRPC skeleton`

不能表述为：

> `与初始总方案一致的第二阶段已完成`
