# Flowarden 第二阶段审计对照

## 1. 文档目的

本文用于将第二阶段当前代码事实，与以下上游约束逐项对照：

- `../flowarden_phased_development_plan.md`
- `flowarden_phase2_development_plan.md`
- `flowarden_phase2_backlog.md`

目标不是重新设计方案，而是回答一个更严格的问题：

> 当前 phase2 到底哪些承诺已经兑现，哪些还没有。

---

## 2. 审计结论摘要

结论：

1. 第二阶段当前没有达到初始总方案要求的“真实运行闭环”。
2. 当前已经完成的是：
   - Avalonia 工程基线
   - gRPC 最小 skeleton
   - UI shell
   - Source 页的真实 discovery / preview 接线
   - Overview 页的真实 projection 接线
3. 当前未完成的是：
   - UI 与 Rust core 的真实运行时接线
   - control plane
   - projection stream / latest projection
   - 页面从运行中的 core 拉取真实数据
4. 因此，phase2 当前应被定性为：

> `阶段二已完成 UI 壳层、Source/Overview 的真实数据接线以及最小 gRPC skeleton，但 control、inspect、settings 的运行闭环仍未完成。`

---

## 3. 对照矩阵

| 初始要求 | 来源文档 | 当前代码事实 | 结论 |
| --- | --- | --- | --- |
| UI 能拉起或连接本地 core | `flowarden_phase2_development_plan.md` 2 / 8 / 10 | UI 已接入真实探活与拉起 `flowarden core` 的流程；resident core 与本地 gRPC 骨架已成立，后续页面接线后置到 `M2-003+` | 已完成（限 M2-002 骨架范围） |
| UI 能列出设备、显示多 device preview，并选择单一 source 正式抓包 | `flowarden_phase2_development_plan.md` 2 / 5.1 / 8 / 10 | `ListDevices` 与 `ListDevicePreviews` 已接通；`SourcePageViewModel` 通过真实 `DiscoveryClient` 拉取设备与 preview | 已完成 |
| UI 能完成 `Start / Stop / Pause / Resume` | `flowarden_phased_development_plan.md` 7.2 / 7.5；`flowarden_phase2_development_plan.md` 2 / 6.1 / 10 | Rust 无 `ControlService`；UI 无真实控制接线；shell 的 `Start Capture` 只是导航 | 未完成 |
| UI 能实时展示 `tick_snapshots` 和 `final_snapshot` | `flowarden_phase2_development_plan.md` 2 / 8 / 10 | Overview 已接入 `ProjectionService.GetLatestOverview`，可展示稳定 projection snapshot；但尚未提供流式刷新 | 部分完成 |
| Overview / Inspect 页面数据与 CLI 输出一致 | `flowarden_phase2_development_plan.md` 2 / 8 / 10；`flowarden_phase2_backlog.md` M2-006 / M2-007 | Overview 已接入 core 侧稳定 snapshot；Inspect 仍为本地样本驱动 | 部分完成 |
| Settings 显示最小运行参数与 core 状态 | `flowarden_phase2_development_plan.md` 5.1 / 8；`flowarden_phase2_backlog.md` M2-008 | `SettingsPageViewModel` 使用本地样本 `RuntimeState / CoreHealth / Diagnostics` | 未完成 |
| UI 与 core 通过稳定契约通信，UI 不直接依赖 Rust 内部结构体 | `flowarden_phase2_development_plan.md` 2 / 6.3 | DTO 存在，UI 未直接引用 Rust 内部结构体 | 已完成 |
| 本地 gRPC / IPC 基线固定为双进程 | `flowarden_phased_development_plan.md` 7.3；`flowarden_phase2_development_plan.md` 4 | 已有 `tonic` gRPC server 与 .NET gRPC client skeleton，且 `flowarden core` 已可作为常驻进程运行 | 部分完成 |
| 多 device 只用于 preview，正式 capture 保持单 source | `flowarden_phase2_development_plan.md` 3.2 / 11；`flowarden_phase2_backlog.md` M2-005 | UI 文案和页面结构守住了这个边界 | 已完成 |
| `Destination Map` 预留区域必须存在 | `flowarden_phase2_development_plan.md` 3.3 / 5.1；`flowarden_phase2_backlog.md` M2-006 / M2-101 | Overview 中 reserved panel 与 future-state 已存在 | 已完成 |

---

## 4. 代码证据

### 4.1 Source 已接通真实 discovery / preview

文件：

- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/SourcePageViewModel.cs`

证据：

1. `SourcePageViewModel` 通过 `DiscoveryClient.GetDevicesAsync()` 拉取设备。
2. `SourcePageViewModel` 通过 `DiscoveryClient.GetDevicePreviewsAsync(2)` 拉取 preview。
3. `RefreshPreview()` 在运行时会重新触发 discovery / preview 刷新。

### 4.2 Overview 已接通真实 projection

文件：

- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs`
- `../../flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs`
- `../../flowarden/flowarden/src/service.rs`

证据：

1. `OverviewPageViewModel` 运行时从 `ProjectionClient.GetLatestOverviewAsync()` 拉取 snapshot。
2. `ProjectionClient` 通过 `GetLatestOverview` 真实连接 Rust `ProjectionService`。
3. Rust `ProjectionService.GetLatestOverview` 返回稳定 overview snapshot。

### 4.3 Inspect 仍是样本驱动

文件：

- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/InspectPageViewModel.cs`

证据：

1. `_allRows = CreateSeedRows()`。
2. `ApplyFilters()` 只过滤本地 `_allRows`。

### 4.4 Settings 仍是样本驱动

文件：

- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/SettingsPageViewModel.cs`

证据：

1. `RuntimeState` 本地构造。
2. `CoreHealth` 本地构造。
3. `Diagnostics` 本地构造。

### 4.5 gRPC 仅到最小 skeleton

文件：

- `../../flowarden/flowarden/src/service.rs`
- `../../flowarden/proto/flowarden/health.proto`
- `../../flowarden/proto/flowarden/discovery.proto`

证据：

1. 当前仅有：
   - `HealthService`
   - `DiscoveryService`
2. 当前不存在：
   - `ControlService`
   - `ProjectionService`
   - `GetRuntimeStatus`

### 4.6 UI 应用已接入最小 launcher / health，但闭环仍未完成

文件：

- `../../flowarden-ui/src/Flowarden.Ui/App.axaml.cs`
- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/AppShellViewModel.cs`
- `../../flowarden-ui/src/Flowarden.Ui/Services/CoreLauncherService.cs`
- `../../flowarden-ui/src/Flowarden.Ui/Services/CoreHealthService.cs`

证据：

1. `App.axaml.cs` 已创建 `CoreHealthService`、`CoreLauncherService`、`CoreConnectionCoordinator`，并启动初始化连接流程。
2. `AppShellViewModel` 已接入 `InitializeCoreConnectionAsync(...)`。
3. 但这仍只覆盖最小探活 / 拉起，不代表后续 control / projection 已闭环。

---

## 5. 对任务状态的直接影响

按 backlog 验收口径，应修正为：

| 任务 | 修正后状态 |
| --- | --- |
| `M2-001` | 已完成 |
| `M2-002` | 已完成（限 resident core + gRPC 骨架范围） |
| `M2-003` | 已完成（限最小 DTO/契约冻结范围） |
| `M2-004` | 已完成 |
| `M2-005` | 已完成 |
| `M2-006` | 已完成 |
| `M2-007` | 未重新验收 |
| `M2-008` | 未重新验收 |
| `M2-009` | 未重新验收 |
| `M2-101` | 未重新验收 |

---

## 6. 当前最小修正方向

如果要让 phase2 真正对齐初始总方案，后续至少要补齐：

1. `ControlService`：
   - `StartCapture`
   - `StopCapture`
   - `PauseCapture`
   - `ResumeCapture`
2. `ProjectionService`：
   - `StreamOverview`
   - `GetLatestOverview`
   - `GetInspectPage`
3. Source 页真实 `ListDevices / ListDevicePreviews`
4. Settings 页真实 runtime / health / version / diagnostics
5. core 异常退出后的 UI 可恢复状态

---

## 7. 审计后的正式表述

审计后，推荐统一使用以下表述：

> 第二阶段当前已完成 UI 壳层、Source/Overview 的真实数据接线以及最小 gRPC skeleton；但 control、Inspect、Settings 的运行闭环尚未完成，因此不能按初始总方案口径宣告 phase2 已封板。
