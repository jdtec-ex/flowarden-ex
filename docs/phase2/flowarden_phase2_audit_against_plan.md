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
   - 各页面的样本驱动 MVP
3. 当前未完成的是：
   - UI 与 Rust core 的真实运行时接线
   - control plane
   - projection stream / latest projection
   - 页面从运行中的 core 拉取真实数据
4. 因此，phase2 当前应被定性为：

> `阶段二 UI 壳层与契约样本 MVP 已完成，但与初始总方案一致的运行闭环未完成。`

---

## 3. 对照矩阵

| 初始要求 | 来源文档 | 当前代码事实 | 结论 |
| --- | --- | --- | --- |
| UI 能拉起或连接本地 core service | `flowarden_phase2_development_plan.md` 2 / 8 / 10 | UI 中虽有 `CoreLauncherService`、`CoreHealthService`，但 `App.axaml.cs` 与 `AppShellViewModel` 未接入真实启动与连接流程 | 未完成 |
| UI 能列出设备、显示多 device preview，并选择单一 source 正式抓包 | `flowarden_phase2_development_plan.md` 2 / 5.1 / 8 / 10 | `SourcePageViewModel` 使用 `CreateSeedDevices()`；`RefreshPreview()` 只更新时间；`StartFormalCapture()` 只改本地状态 | 未完成 |
| UI 能完成 `Start / Stop / Pause / Resume` | `flowarden_phased_development_plan.md` 7.2 / 7.5；`flowarden_phase2_development_plan.md` 2 / 6.1 / 10 | Rust 无 `ControlService`；UI 无真实控制接线；shell 的 `Start Capture` 只是导航 | 未完成 |
| UI 能实时展示 `tick_snapshots` 和 `final_snapshot` | `flowarden_phase2_development_plan.md` 2 / 8 / 10 | `OverviewPageViewModel` 使用 `CreateSeedSnapshot()`；`ProjectionClient` 仅返回 placeholder | 未完成 |
| Overview / Inspect 页面数据与 CLI 输出一致 | `flowarden_phase2_development_plan.md` 2 / 8 / 10；`flowarden_phase2_backlog.md` M2-006 / M2-007 | Overview 与 Inspect 都由本地样本驱动，不是运行中的 phase1 输出投影 | 未完成 |
| Settings 显示最小运行参数与 core 状态 | `flowarden_phase2_development_plan.md` 5.1 / 8；`flowarden_phase2_backlog.md` M2-008 | `SettingsPageViewModel` 使用本地样本 `RuntimeState / CoreHealth / Diagnostics` | 未完成 |
| UI 与 core 通过稳定契约通信，UI 不直接依赖 Rust 内部结构体 | `flowarden_phase2_development_plan.md` 2 / 6.3 | DTO 存在，UI 未直接引用 Rust 内部结构体 | 已完成 |
| 本地 gRPC / IPC 基线固定为双进程 | `flowarden_phased_development_plan.md` 7.3；`flowarden_phase2_development_plan.md` 4 | 已有 `tonic` gRPC server 与 .NET gRPC client skeleton | 部分完成 |
| 多 device 只用于 preview，正式 capture 保持单 source | `flowarden_phase2_development_plan.md` 3.2 / 11；`flowarden_phase2_backlog.md` M2-005 | UI 文案和页面结构守住了这个边界 | 已完成 |
| `Destination Map` 预留区域必须存在 | `flowarden_phase2_development_plan.md` 3.3 / 5.1；`flowarden_phase2_backlog.md` M2-006 / M2-101 | Overview 中 reserved panel 与 future-state 已存在 | 已完成 |

---

## 4. 代码证据

### 4.1 Source 仍是样本驱动

文件：

- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/SourcePageViewModel.cs`

证据：

1. `CreateSeedDevices()` 构造固定设备和 preview 数据。
2. `RefreshPreview()` 仅更新 `LastPreviewLabel`。
3. `StartFormalCapture()` 仅把 `CaptureStatus` 改成 `"armed"`。

### 4.2 Overview 仍是样本驱动

文件：

- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs`
- `../../flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs`

证据：

1. `OverviewPageViewModel` 直接调用 `CreateSeedSnapshot()`。
2. `ProjectionClient` 只有 `GetPlaceholderOverviewAsync()`。

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

### 4.6 UI 应用未真实接入 launcher / health

文件：

- `../../flowarden-ui/src/Flowarden.Ui/App.axaml.cs`
- `../../flowarden-ui/src/Flowarden.Ui/ViewModels/AppShellViewModel.cs`
- `../../flowarden-ui/src/Flowarden.Ui/Services/CoreLauncherService.cs`
- `../../flowarden-ui/src/Flowarden.Ui/Services/CoreHealthService.cs`

证据：

1. `App.axaml.cs` 直接 new `AppShellViewModel()`，没有 service 注入。
2. `AppShellViewModel` 自身不持有 gRPC client / launcher。
3. `CoreLauncherService` 存在，但没有被应用启动路径使用。

---

## 5. 对任务状态的直接影响

按 backlog 验收口径，应修正为：

| 任务 | 修正后状态 |
| --- | --- |
| `M2-001` | 已完成 |
| `M2-002` | 进行中 |
| `M2-003` | 已完成 |
| `M2-004` | 已完成 |
| `M2-005` | 进行中 |
| `M2-006` | 进行中 |
| `M2-007` | 进行中 |
| `M2-008` | 进行中 |
| `M2-009` | 进行中 |
| `M2-101` | 已完成 |

---

## 6. 当前最小修正方向

如果要让 phase2 真正对齐初始总方案，后续至少要补齐：

1. 应用启动时的 core 检查、拉起、连接管理
2. Source 页真实 `ListDevices / ListDevicePreviews`
3. `ControlService`：
   - `StartCapture`
   - `StopCapture`
   - `PauseCapture`
   - `ResumeCapture`
4. `ProjectionService`：
   - `StreamOverview`
   - `GetLatestOverview`
   - `GetInspectPage`
5. Settings 页真实 runtime / health / version / diagnostics
6. core 异常退出后的 UI 可恢复状态

---

## 7. 审计后的正式表述

审计后，推荐统一使用以下表述：

> 第二阶段当前已完成 UI 壳层、契约模型和样本驱动 MVP，并建立了最小 gRPC skeleton；但 UI 与 Rust core 的真实运行闭环尚未完成，因此不能按初始总方案口径宣告 phase2 已封板。
