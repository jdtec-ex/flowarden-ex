# Flowarden 第二阶段编码顺序与文件落位

## 1. 文档目的

本文在 `flowarden_phase2_backlog.md` 基础上进一步下钻，回答三个直接面向实现的问题：

1. 第二阶段应该按什么编码顺序推进
2. 每个 backlog 项会落到哪些文件
3. 哪些任务适合合并成一次提交，哪些应严格分开

本文针对第二阶段预期的仓库结构：

- `./flowarden/flowarden-core`
- `./flowarden/flowarden`
- `./flowarden/flowarden-error`
- `./flowarden-ui`

不假设第二阶段会一次性引入复杂多项目拆分。

当前执行说明：

1. 第二阶段任务状态已正式回退到 `M2-002`。
2. `M2-001` 保留已完成。
3. `M2-003` 及之后的任务即使已有代码，也只作为存量实现参考，必须在 `M2-002` 重新通过评审后再逐项恢复推进。

---

## 2. 当前文件基线

### 代码仓库当前已有

#### Rust workspace

- `flowarden/Cargo.toml`
- `flowarden/flowarden-core/...`
- `flowarden/flowarden/...`
- `flowarden/flowarden-error/...`

#### UI 工程

- `flowarden-ui` 已建立，但当前仅能作为后续重新验收的代码基线

结论：

> 第二阶段前半程主要是“补工程壳和通信边界”，后半程才是“补页面与交互”。

---

## 3. 第二阶段建议编码顺序

建议按 5 个实现波次推进，而不是严格按 9 个 backlog 单点逐个提交。

### Wave 1：UI 工程与 resident core 边界

对应 backlog：

- `M2-001`
- `M2-002`
- `M2-003`

### Wave 2：Shell 与全局状态

对应 backlog：

- `M2-004`

### Wave 3：入口页与主工作面

对应 backlog：

- `M2-005`
- `M2-006`

### Wave 4：明细与诊断

对应 backlog：

- `M2-007`
- `M2-008`

### Wave 5：封板

对应 backlog：

- `M2-009`

### 可插入 Wave：建议保留项

对应 backlog：

- `M2-101`

如果主线顺利，建议插在 Wave 3 结束后。

补充说明：

- 在当前回退口径下，重新执行时必须先完成并评审 `M2-002`，然后才允许恢复 `M2-003+` 的推进。

---

## 4. 文件落位总览

## 4.1 `flowarden-ui` 预计新增文件

建议新增：

```text
flowarden-ui/global.json
flowarden-ui/Flowarden.Ui.sln
flowarden-ui/src/Flowarden.Ui/Flowarden.Ui.csproj
flowarden-ui/src/Flowarden.Ui/App.axaml
flowarden-ui/src/Flowarden.Ui/App.axaml.cs
flowarden-ui/src/Flowarden.Ui/Program.cs
flowarden-ui/src/Flowarden.Ui/Views/AppShellView.axaml
flowarden-ui/src/Flowarden.Ui/Views/AppShellView.axaml.cs
flowarden-ui/src/Flowarden.Ui/Views/SourcePageView.axaml
flowarden-ui/src/Flowarden.Ui/Views/OverviewPageView.axaml
flowarden-ui/src/Flowarden.Ui/Views/InspectPageView.axaml
flowarden-ui/src/Flowarden.Ui/Views/SettingsPageView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/AppRailView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/AppHeaderView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/HeroTrafficChartView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/StatusCardsRowView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/DestinationWorkbenchView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/InspectFilterBarView.axaml
flowarden-ui/src/Flowarden.Ui/Views/Components/InspectResultsTableView.axaml
flowarden-ui/src/Flowarden.Ui/ViewModels/AppShellViewModel.cs
flowarden-ui/src/Flowarden.Ui/ViewModels/SourcePageViewModel.cs
flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs
flowarden-ui/src/Flowarden.Ui/ViewModels/InspectPageViewModel.cs
flowarden-ui/src/Flowarden.Ui/ViewModels/SettingsPageViewModel.cs
flowarden-ui/src/Flowarden.Ui/Services/CoreLauncherService.cs
flowarden-ui/src/Flowarden.Ui/Services/CoreHealthService.cs
flowarden-ui/src/Flowarden.Ui/Services/DiscoveryClient.cs
flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs
flowarden-ui/src/Flowarden.Ui/State/AppSessionState.cs
flowarden-ui/src/Flowarden.Ui/State/CaptureSessionState.cs
flowarden-ui/src/Flowarden.Ui/Styles/Theme.axaml
flowarden-ui/src/Flowarden.Ui/Styles/Controls.axaml
```

## 4.2 Rust 侧预计新增或修改文件

建议新增或修改：

```text
flowarden/flowarden/src/service.rs
flowarden/flowarden/src/ipc.rs
flowarden/flowarden/src/main.rs
flowarden/flowarden/Cargo.toml
flowarden/flowarden-core/src/lib.rs
flowarden/flowarden-core/src/projection/mod.rs
flowarden/flowarden-core/src/projection/snapshot.rs
flowarden/flowarden-core/src/projection/summary.rs
```

如果第二阶段采用 proto / gRPC 方案，还应预留：

```text
flowarden/proto/flowarden/common.proto
flowarden/proto/flowarden/control.proto
flowarden/proto/flowarden/discovery.proto
flowarden/proto/flowarden/projection.proto
flowarden/proto/flowarden/health.proto
```

## 4.3 文档侧预计新增文件

建议新增：

```text
docs/phase2/flowarden_phase2_progress.md
docs/phase2/flowarden_phase2_runbook.md
docs/phase2/flowarden_phase2_acceptance_template.md
```

---

## 5. 各 backlog 项的文件级落位

## M2-001 UI 工程骨架与 SDK 基线

### 主要新增文件

- `flowarden-ui/global.json`
- `flowarden-ui/Flowarden.Ui.sln`
- `flowarden-ui/src/Flowarden.Ui/Flowarden.Ui.csproj`
- `flowarden-ui/src/Flowarden.Ui/App.axaml`
- `flowarden-ui/src/Flowarden.Ui/Program.cs`

### 目标落点

1. 建立 `net8.0` + Avalonia 最小工程。
2. 固定 `8.0.125` SDK 约束。
3. 首次跑通 `dotnet build` / `dotnet run`。

### 建议提交边界

建议单独一个提交完成。

原因：

- 这是第二阶段后续所有页面和服务接线的工程基底。

---

## M2-002 resident core 模式与本地 gRPC 骨架

### 主要修改文件

- `flowarden/flowarden/src/main.rs`
- `flowarden/flowarden/Cargo.toml`

### 可能新增文件

- `flowarden/flowarden/src/service.rs`
- `flowarden/flowarden/src/ipc.rs`
- `flowarden/proto/...`

### 目标落点

1. 让 `flowarden` 支持 `core` resident mode。
2. 建立最小 health / version / discovery 通道。
3. 让 UI 可检查 core 是否在线。

### 建议提交边界

建议与 `M2-003` 分开提交。

原因：

- resident mode 和契约模型是两类变化，拆开更容易 review。

---

## M2-003 phase2 契约模型冻结

### 主要新增文件

- `flowarden/proto/...` 或对应契约定义文件
- `flowarden-ui/src/Flowarden.Ui/Models/...`

### 目标落点

1. 冻结 Dto。
2. 把 `Destination Map` 的 placeholder 契约一并预留。
3. 让 UI client 先有稳定输入面。

### 建议提交边界

建议单独提交。

---

## M2-004 App Shell 与全局状态层

### 主要新增文件

- `flowarden-ui/src/Flowarden.Ui/Views/AppShellView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/AppRailView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/AppHeaderView.axaml`
- `flowarden-ui/src/Flowarden.Ui/ViewModels/AppShellViewModel.cs`
- `flowarden-ui/src/Flowarden.Ui/State/AppSessionState.cs`

### 目标落点

1. 落 `Left Rail + Top App Bar + Main Workbench`。
2. 跑通页面切换和全局状态展示。
3. 接入 `Cosmos Network System` 主题变量。

### 建议提交边界

建议单独提交。

原因：

- shell 一旦稳定，后续页面只是在内容区填充。

---

## M2-005 Source 页面 MVP

### 主要新增文件

- `flowarden-ui/src/Flowarden.Ui/Views/SourcePageView.axaml`
- `flowarden-ui/src/Flowarden.Ui/ViewModels/SourcePageViewModel.cs`
- `flowarden-ui/src/Flowarden.Ui/Services/DiscoveryClient.cs`

### 可能新增文件

- `flowarden-ui/src/Flowarden.Ui/Views/Components/SourceDeviceListView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/SourcePreviewWorkbenchView.axaml`

### 目标落点

1. 跑通 device list + preview。
2. source 选择和 offline import 入口可见。
3. preview 与 formal capture 文案严格区分。

### 建议提交边界

可与 `M2-006` 分开提交。

---

## M2-006 Overview 页面 MVP

### 主要新增文件

- `flowarden-ui/src/Flowarden.Ui/Views/OverviewPageView.axaml`
- `flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/HeroTrafficChartView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/StatusCardsRowView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/DestinationWorkbenchView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs`

### 目标落点

1. hero chart
2. status cards
3. destination workbench
4. lower detail row

### 建议提交边界

建议单独提交。

原因：

- Overview 是第二阶段主价值页，也是布局最复杂的一页。

---

## M2-007 Inspect 页面 MVP

### 主要新增文件

- `flowarden-ui/src/Flowarden.Ui/Views/InspectPageView.axaml`
- `flowarden-ui/src/Flowarden.Ui/ViewModels/InspectPageViewModel.cs`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/InspectFilterBarView.axaml`
- `flowarden-ui/src/Flowarden.Ui/Views/Components/InspectResultsTableView.axaml`

### 目标落点

1. filter bar
2. results table
3. footer summary

### 建议提交边界

建议单独提交。

---

## M2-008 Settings 与诊断页 MVP

### 主要新增文件

- `flowarden-ui/src/Flowarden.Ui/Views/SettingsPageView.axaml`
- `flowarden-ui/src/Flowarden.Ui/ViewModels/SettingsPageViewModel.cs`
- `flowarden-ui/src/Flowarden.Ui/Services/CoreHealthService.cs`

### 目标落点

1. runtime panel
2. core panel
3. diagnostics panel

### 建议提交边界

可与 `M2-007` 分开提交。

---

## M2-009 第二阶段封板与质量门禁

### 主要新增文件

- `docs/phase2/flowarden_phase2_progress.md`
- `docs/phase2/flowarden_phase2_runbook.md`
- `docs/phase2/flowarden_phase2_acceptance_template.md`

### 可能修改文件

- `flowarden-ui/README.md` 或根 README

### 目标落点

1. 固定 UI 运行说明。
2. 补 phase2 验收模板。
3. 固定构建、运行、质量门禁。

### 建议提交边界

建议单独提交，作为 phase2 封板提交。

---

## M2-101 Destination Workbench 增强预留

### 主要修改文件

- `flowarden-ui/src/Flowarden.Ui/Views/Components/DestinationWorkbenchView.axaml`
- `flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs`

### 目标落点

1. 把 destination 区从“空占位”提升为“有明确 future state 的占位”。
2. 保持地图与排行的结构稳定。

### 建议提交边界

可单独作为阶段二.1提交。

---

## 6. 我建议优先盯紧的文件与任务

第二阶段最需要优先盯住的是：

1. `flowarden-ui/src/Flowarden.Ui/Views/AppShellView.axaml`
   - 决定整个 UI 壳层是否稳定。
2. `flowarden/flowarden/src/service.rs` / `ipc.rs`
   - 决定 UI 是否真的有 core 可连。
3. `OverviewPageView.axaml`
   - 决定 `Destination Map` 和下层 detail row 是否能共存。
4. 契约定义文件
   - 决定 UI 和 core 是否后面会来回返工。

---

## 7. 最终建议

如果你认可这个实现顺序，我建议第二阶段实际编码时按下面方式管理：

1. 每完成一个 `M2-xxx` 都有对应运行截图或构建记录。
2. 不跨波次乱跳实现，先稳工程、再稳边界、再稳页面。
3. `M2-002`、`M2-003`、`M2-004`、`M2-006` 应作为第二阶段里程碑检查点。

这样推进，第二阶段更容易做成一个工程结构清楚、视觉系统统一、能继续承接真实 destination 地图能力与 phase3 深度分析的 Avalonia 桌面前端。
