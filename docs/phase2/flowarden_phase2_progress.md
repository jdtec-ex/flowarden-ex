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

1. 第二阶段当前不能标记为“已封板完成”。
2. 第二阶段任务状态已正式回退到 `M2-002` 重新开始推进。
3. 当前已经成立的是：
   - Avalonia 工程骨架
   - gRPC 最小 skeleton
   - UI shell
   - Source / Overview / Inspect / Settings 的样本驱动 MVP
4. 这些已存在实现只作为代码基线，不自动视为当前任务已完成。
5. 当前尚未成立的是：
   - UI 启动后真实拉起或连接 core 的运行闭环
   - Source 页面真实 `ListDevices / ListDevicePreviews` 接线
   - `Start / Stop / Pause / Resume`
   - Overview 对 `tick_snapshots / final_snapshot` 的真实投影接线
   - Inspect 对后端 query / projection 的真实接线
   - Settings 对 runtime / health / version / diagnostics 的真实接线
6. 因此，第二阶段当前应被描述为：

> `gRPC skeleton + 样本驱动 UI MVP 已建立，但与初始总方案一致的真实运行闭环尚未完成。`

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
| `M2-005` | 未重新验收 | 现有 Source 页面实现只保留为代码基线，不计当前完成。 |
| `M2-006` | 已完成 | Overview 页面已接入真实 ProjectionService，数据由 core 侧稳定 snapshot 提供，并与 shell 的 live/replay mode 联动。 |
| `M2-007` | 未重新验收 | 现有 Inspect 页面实现只保留为代码基线，不计当前完成。 |
| `M2-008` | 未重新验收 | 现有 Settings 页面实现只保留为代码基线，不计当前完成。 |
| `M2-009` | 未重新验收 | 现有封板文档与门禁只保留为代码基线，不计当前完成。 |
| `M2-101` | 未重新验收 | 现有 destination workbench 增强只保留为代码基线，不计当前完成。 |

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

- 状态：未重新验收
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

- 状态：未重新验收
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

- 状态：未重新验收
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

- 状态：未重新验收
- 完成内容：
  - 更明确的 destination placeholder model
  - 地图区域空态 / reserved state / future state 文案
  - 更稳定的 destination ranking 辅助说明
- 说明：
  - 本任务只评价 destination workbench 的 UI reserved shell
  - 它不代表 phase2 主线已经形成真实运行闭环
- 当前口径：
  - 现有实现仅作为存量代码基线
  - 必须在主线恢复推进后再单独按 backlog 重新验收
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
