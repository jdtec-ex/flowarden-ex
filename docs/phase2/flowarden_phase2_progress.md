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
6. 若阶段执行被回退，则回退点之后的任务一律改为“未重新验收”，直到按新基线重新通过评审。

---

## 2. 审计结论

本文件已按初始总方案和 phase2 计划重新审计。

结论如下：

1. 第二阶段 backlog 主线当前已收口到 `M2-009`。
2. 当前已经成立的是：
   - Avalonia 工程骨架
   - gRPC 最小 skeleton
   - UI shell
   - Source / Overview / Inspect / Settings 的主线数据接线
3. 当前仍明确后置的是：
   - `Start / Stop / Pause / Resume`
   - Overview 实时 projection stream
   - core 异常退出后的恢复路径
4. 因此，第二阶段当前应被描述为：

> `phase2 backlog 主线已完成，但 control、实时 projection stream 与异常恢复仍作为后续增强项保留。`

详细对照见：

- `flowarden_phase2_audit_against_plan.md`

---

## 3. 当前状态总览

| 任务 | 真实状态 | 说明 |
| --- | --- | --- |
| `M2-001` | 已完成 | Avalonia 工程骨架与 `net8.0` / `8.0.125` 基线已落地。 |
| `M2-002` | 已完成 | `flowarden core` resident mode、UI 探活/拉起、以及 `health/discovery/control/projection` gRPC 骨架已到位。 |
| `M2-003` | 已完成 | phase2 最小 DTO/契约模型已冻结，并已按 `YAGNI` 删去未使用字段。 |
| `M2-004` | 已完成 | App Shell 与全局状态层已按 backlog 口径完成，作为 phase2 后续页面与运行闭环的稳定壳层基线。 |
| `M2-005` | 已完成 | Source 页面已通过真实 `DiscoveryClient` 接入设备列表与 preview，并保留单 source formal capture 边界。 |
| `M2-006` | 已完成 | Overview 页面已接入真实 ProjectionService，数据由 core 侧稳定 snapshot 提供，并与 shell 的 live/replay mode 联动。 |
| `M2-007` | 已完成 | Inspect 页面已接入真实 `ProjectionService.GetInspectPage`，过滤条件可下发到 core。 |
| `M2-008` | 已完成 | Settings 页面已接入真实 health/discovery/error 状态组合，展示最小运行态与诊断信息。 |
| `M2-009` | 已完成 | phase2 运行说明、验收模板和质量门禁已按当前真实状态更新并可重复执行。 |
| `M2-101` | 已完成 | Destination workbench 的 reserved shell、future-state 文案与 `Top Destinations` 成对结构已稳定落地。 |

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

### M2-002 resident core 模式与本地 gRPC 骨架

- 状态：已完成
- 已成立：
  - `flowarden core` 作为单一可执行程序内的 resident mode 入口
  - 共享 `proto/flowarden/health.proto`
  - 共享 `proto/flowarden/discovery.proto`
  - 共享 `proto/flowarden/control.proto`
  - 共享 `proto/flowarden/projection.proto`
  - Rust 侧 `tonic` gRPC 常驻模式 host
  - `.NET gRPC client`
  - 最小 RPC：
    - `GetHealth`
    - `GetVersion`
    - `ListDevices`
    - `ListDevicePreviews` skeleton
    - `StartCapture / StopCapture / PauseCapture / ResumeCapture / SetSource / ApplyFilter` 骨架
    - `GetLatestOverview / GetInspectPage` 骨架
- 审计证据：
  - Rust 当前已暴露 `HealthService`、`DiscoveryService`、`ControlService`、`ProjectionService`
  - 代码位置：`../../flowarden/flowarden/src/service.rs`
  - UI 已能探活并在需要时拉起 `flowarden core`
  - launcher 异常路径已映射为 `CoreErrorDto`，不会打断窗口初始化
  - `ListDevicePreviews` 当前明确保留为 skeleton，真实 preview 采样推迟到 `M2-005`
- 验收结果：
  - `cargo test -q -p flowarden` 通过
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 本任务完成的是 resident core 与本地 gRPC 骨架，不包含 `M2-003+` 的真实页面数据接线
- 已保留提交：
  - inner repo `7b1c4d8` `Rework M2-002 to gRPC service mode skeleton`
  - outer repo `83a8b89` `Rework M2-002 to gRPC ui client skeleton`
  - inner repo `e6351e6` `Refine resident core mode under flowarden core`
  - outer repo `557fd73` `Connect UI startup to resident core and update phase2 docs`

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
- 收紧说明：
  - 删除了当前未使用且未接通的冗余字段
  - `M2-003` 仅冻结 phase2 最小 DTO 集，不包含 `M2-004+` 的真实页面数据接线
- 验收结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - UI `Models/` 不直接依赖 Rust 内部结构体或 tonic 生成类型
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
- 当前口径：
  - App Shell、全局状态层、页面切换与主题基线已按 backlog 口径完成
  - 后续页面与运行闭环直接建立在该壳层之上
- 验收结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
- 提交：
  - outer repo `a457e25` `Complete M2-004 app shell and global state layer`
  - outer repo `1ab36b9` `Tighten shell actions and remove extra rail items`

### M2-005 Source 页面 MVP

- 状态：已完成
- 完成内容：
  - `SourcePageView`
  - `SourceDeviceListView`
  - `SourcePreviewWorkbenchView`
  - `SourcePageViewModel`
- 已成立：
  - 设备列表与 preview 通过真实 `DiscoveryClient` 拉取
  - `Refresh Preview` 触发真实 discovery / preview 刷新
  - 设备选择、离线导入、正式 capture 边界清楚
  - 页面结构保持“左列表 + 右详情”工作台形态
- 验收结果：
  - `ListDevices` / `ListDevicePreviews` 接线已成立
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
- 代码证据：
  - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/SourcePageViewModel.cs`
  - `../../flowarden-ui/src/Flowarden.Ui/Views/SourcePageView.axaml`
- 提交：
  - inner repo `f712ab5` `Complete M2-005 preview discovery gRPC path`
  - outer repo `8e9ab88` `Complete M2-005 source page mvp`

### M2-006 Overview 页面 MVP

- 状态：已完成
- 已成立：
  - `HeroTrafficChartView`
  - `StatusCardsRowView`
  - `DestinationWorkbenchView`
  - `TopHostsView`
  - `TopServicesView`
  - `TopConnectionsView`
  - `Destination Map + Top Destinations` 稳定留位
- 接线结果：
  - `OverviewPageViewModel` 运行时从 `ProjectionClient` 拉取 overview snapshot
  - Rust `ProjectionService.GetLatestOverview` 返回稳定 snapshot
  - shell 的 `Live / Replay` mode 会联动 Overview 的模式卡
  - 代码位置：
    - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/OverviewPageViewModel.cs`
    - `../../flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs`
    - `../../flowarden/flowarden/src/service.rs`
- 验收结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - `cargo test -q -p flowarden -p flowarden-core -p flowarden-error` 通过
- 已保留提交：
  - outer repo `d7cad13` `Complete M2-006 overview page mvp`

### M2-007 Inspect 页面 MVP

- 状态：已完成
- 已成立：
  - `InspectPageView`
  - `InspectHeaderView`
  - `InspectFilterBarView`
  - `InspectResultsTableView`
  - `InspectFooterSummaryView`
  - `InspectPageViewModel` 运行时通过 `ProjectionClient.GetInspectPageAsync()` 拉取真实后端结果
  - `ApplyFilters()` 会把 `InspectFilterDto` 下发到 Rust `ProjectionService.GetInspectPage`
- 代码证据：
  - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/InspectPageViewModel.cs`
  - `../../flowarden-ui/src/Flowarden.Ui/Services/ProjectionClient.cs`
  - `../../flowarden/flowarden/src/service.rs`
- 验收结果：
  - 过滤条件可下发
  - 结果表由后端 query / projection 驱动
  - 未引入 phase3 payload / session 字段
  - `cargo test -q -p flowarden -p flowarden-core -p flowarden-error` 通过
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
- 提交：
  - outer repo `307822b` `Complete M2-007 inspect page mvp`

### M2-008 Settings 与诊断页 MVP

- 状态：已完成
- 已成立：
  - `SettingsPageView`
  - `SettingsRuntimePanelView`
  - `SettingsCorePanelView`
  - `SettingsDiagnosticsPanelView`
  - `SettingsPageViewModel` 运行时组合真实 `CoreHealthService`、`DiscoveryClient` 和 shell 级错误状态
- 代码证据：
  - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/SettingsPageViewModel.cs`
  - `../../flowarden-ui/src/Flowarden.Ui/ViewModels/AppShellViewModel.cs`
  - `../../flowarden-ui/src/Flowarden.Ui/App.axaml.cs`
- 验收结果：
  - 当前 source / BPF / tick interval / top N 可展示
  - core endpoint / process state / version 可展示
  - 错误日志入口与近期错误提示可见
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
- 提交：
  - outer repo `272cbc9` `Complete M2-008 settings and diagnostics mvp`

### M2-009 第二阶段封板与质量门禁

- 状态：已完成
- 已成立：
  - phase2 runbook
  - phase2 acceptance template
  - 当前真实状态对齐后的 phase2 审计与进度记录
  - `dotnet format` / `dotnet build` / `cargo test -q -p flowarden`
- 验收结果：
  - 第二阶段可被独立评审和重复验收
  - 文档、产物、运行说明齐备
  - phase3 继续接地图真实能力或 session 详情时，不需要推倒 UI 壳层
- 说明：
  - `M2-009` 完成，表示 phase2 的封板文档和质量门禁已收口
  - 不代表后续增强项如 `ControlService`、实时 projection stream、core 异常恢复已经完成
- 提交：
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
- 当前口径：
  - destination workbench 的 reserved shell 已按 backlog 重新验收通过
  - 真实地图能力仍明确后置，不属于 `M2-101` 完成定义
- 提交：
  - outer repo `fda2e27` `Complete M2-101 destination workbench reserve enhancement`

---

## 5. 当前未完成闭环项

按当前 phase2 backlog，主线任务已收口；当前仍明确后置或未实现的增强项如下：

1. `ControlService` 与 `Start / Stop / Pause / Resume`
2. Overview 实时 projection stream
3. core 异常退出时，UI 真实进入可恢复状态

这些项不阻塞 `M2-009` 封板，但在进入后续增强或 phase3 前需要单独规划。
