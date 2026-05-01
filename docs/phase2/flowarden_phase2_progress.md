# Flowarden 第二阶段任务进度

## 1. 记录规则

1. 仅当任务同时满足 backlog 中的“输出”和“验收条件”时，才标记为“已完成”。
2. 若实现偏离既定架构约束，即使代码可运行，也不得标记为“已完成”。
3. 每个任务完成后先记录状态，再等待用户确认是否进入下一个任务。

---

## 2. 当前状态总览

| 任务 | 状态 | 说明 |
| --- | --- | --- |
| `M2-001` | 已完成 | Avalonia 工程骨架与 `net8.0` / `8.0.125` 基线已落地。 |
| `M2-002` | 已完成 | 已用 `proto + tonic + .NET gRPC client` 重做最小 service mode 与本地通信骨架。 |
| `M2-003` | 已完成 | phase2 UI 侧最小 DTO/契约模型已冻结，足以承接 Source / Overview / Inspect / Settings 开发。 |
| `M2-004` | 已完成 | `Left Rail + Top App Bar + Main Workbench` 已落地，页面切换与全局状态点已稳定。 |
| `M2-005` | 已完成 | Source 页面 MVP 已落地，preview 与 formal capture 边界已图形化，并补齐最小 preview gRPC 通道。 |
| `M2-006` | 已完成 | Overview 页面 MVP 已落地，hero/status/destination/detail row 结构已由 `OverviewSnapshotDto` 驱动。 |
| `M2-007` | 已完成 | Inspect 页面 MVP 已落地，过滤条、结果表与 footer summary 已形成稳定工作台。 |
| `M2-008` | 已完成 | Settings 与诊断页 MVP 已落地，运行配置、core 状态和近期错误提示已可见。 |
| `M2-009` | 已完成 | 第二阶段已完成封板、质量门禁、运行说明与验收模板。 |
| `M2-101` | 已完成 | Destination workbench 的 placeholder 模型与 future-state 文案已增强，且未影响第二阶段主线封板。 |

---

## 3. 任务记录

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

- 状态：已完成
- 原偏差说明：
  - 曾落成 loopback HTTP skeleton，不符合 [sniffnet_refactor_avalonia_rust_grpc.md](/Users/wangli/workspace/coding/flowarden/docs/sniffnet_refactor_avalonia_rust_grpc.md) 中已明确的 `Rust Core + Avalonia + gRPC` 架构要求
  - 该偏差已在本任务内修正，不再作为正式通信基线保留
- 修正后完成内容：
  - 建立共享 `proto/flowarden/health.proto`
  - 建立共享 `proto/flowarden/discovery.proto`
  - Rust 侧改为 `tonic` gRPC service mode
  - Avalonia 侧改为 `.NET gRPC client`
  - 最小 RPC 已落地：
    - `GetHealth`
    - `GetVersion`
    - `ListDevices`
- 验证结果：
  - `cargo build -p flowarden` 通过
  - `cargo test -q -p flowarden` 通过
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 端到端探针验证通过：
    - `health.status=ok`
    - `version.service=flowarden-core-service`
    - `devices.count=22`
- 原提交：
  - inner repo `0daff75` `Complete M2-002 local service mode and health skeleton`
  - outer repo `0099fc7` `Complete M2-002 local service mode and health skeleton`
- 修正后提交：
  - inner repo `7b1c4d8` `Rework M2-002 to gRPC service mode skeleton`
  - outer repo `83a8b89` `Rework M2-002 to gRPC ui client skeleton`

### M2-003 phase2 契约模型冻结

- 状态：已完成
- 完成内容：
  - 冻结 `DeviceSummaryDto`
  - 冻结 `DevicePreviewDto`
  - 冻结 `CaptureSessionStateDto`
  - 冻结 `OverviewSnapshotDto`
  - 冻结 `InspectFilterDto`
  - 冻结 `InspectResultDto`
  - 冻结 `CoreErrorDto`
- 结构约束：
  - DTO 命名明确区分 UI 传输模型与 Rust 内部领域模型
  - `DestinationMapPanel` 和 `TopDestinationsPanel` 已保留 placeholder 契约
  - `InspectResultDto` 只承接 phase2 的连接明细和结果摘要，不提前引入 phase3 的 payload / session 字段
  - 当前只冻结 UI 侧最小数据面，不新增 core 侧 proto 扩展
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - UI 项目未直接依赖 Rust 内部结构体
  - phase2 页面所需最小数据面已齐备
- 提交：
  - outer repo `a21b72a` `Complete M2-003 phase 2 dto contract freeze`

### M2-004 App Shell 与全局状态层

- 状态：已完成
- 完成内容：
  - `AppShellView`
  - `AppRailView`
  - `AppHeaderView`
  - `AppShellViewModel`
- 具体落地：
  - 固定 `Left Rail + Top App Bar + Main Workbench`
  - 左 rail 已承载主导航与 `Start Capture` 主 CTA
  - top app bar 已承载 mode 切换、core/capture 状态点、tools 入口
  - main workbench 已作为稳定页面宿主，支持 `Source / Overview / Inspect / Settings` 切换
  - `Overview` 仍保留 `Hero Chart / Status Cards / Top Destinations / Destination Map / Lower Detail Row` 的稳定版位
- 范围边界：
  - 当前任务只落 shell、切页、状态层与壳层样式
  - `Source / Overview / Inspect / Settings` 的业务内容仍分别留给 `M2-005` 到 `M2-008`
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - shell 可启动并稳定切页面
  - 全局状态点可显示 `core / capture` 状态
  - 风格已脱离默认 Avalonia 壳，收敛到 `Cosmos Network System` 的 shell 基线
- 提交：
  - outer repo `a457e25` `Complete M2-004 app shell and global state layer`

### M2-005 Source 页面 MVP

- 状态：已完成
- 完成内容：
  - `SourcePageView`
  - `SourceDeviceListView`
  - `SourcePreviewWorkbenchView`
  - `SourcePageViewModel`
- 具体落地：
  - Source 页已经进入 `AppShell` 的正式页面切换路径
  - 设备列表、选中设备详情、preview 指标、offline import 入口、formal capture footer 已成型
  - preview 与 formal capture 文案已明确区分
  - 为满足页面数据面，`M2-005` 同步补齐了 discovery gRPC 的 `ListDevicePreviews`
- 当前实现边界：
  - UI 当前用稳定种子数据驱动 Source MVP 展示，不把 core 连接生命周期和自动刷新提前塞进本任务
  - 但 gRPC preview 通道已经存在，后续可在不改页面契约的情况下接入真实调用
  - 正式 capture 仍保持单 source 语义，没有被 preview 多设备行为污染
- 验证结果：
  - `cargo build -p flowarden` 通过
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - Source 页具备设备 preview 展示、单 source 选择和 offline import 入口
- 提交：
  - inner repo `f712ab5` `Complete M2-005 preview discovery gRPC path`
  - outer repo `8e9ab88` `Complete M2-005 source page mvp`

### M2-006 Overview 页面 MVP

- 状态：已完成
- 完成内容：
  - `HeroTrafficChartView`
  - `StatusCardsRowView`
  - `DestinationWorkbenchView`
  - `TopHostsView`
  - `TopServicesView`
  - `TopConnectionsView`
- 具体落地：
  - Overview 已从壳层占位切换成正式页面
  - `hero chart + status cards + destination workbench + lower detail row` 结构已成立
  - `Destination Map` 与 `Top Destinations` 保持成对存在
  - `Top hosts / Top services / Top connections` 没有因新版布局丢失
- 当前实现边界：
  - 当前页面由稳定样本 `OverviewSnapshotDto` 驱动
  - 尚未接入实时 projection stream
  - 但字段口径严格限制在 phase1 已有聚合输出，没有提前引入 phase3 数据
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 页面具备 live/offline 显示口径
  - `Destination Map` 区域在布局中稳定存在，即使当前是 placeholder
- 提交：
  - outer repo `d7cad13` `Complete M2-006 overview page mvp`

### M2-007 Inspect 页面 MVP

- 状态：已完成
- 完成内容：
  - `InspectPageView`
  - `InspectHeaderView`
  - `InspectFilterBarView`
  - `InspectResultsTableView`
  - `InspectFooterSummaryView`
- 具体落地：
  - Inspect 页已从壳层占位切换成正式页面
  - 过滤条保持高可见性
  - 结果表格成为主体区域
  - footer 持续显示当前结果数、汇总字节数、汇总包数和排序口径
  - `ConnectionRowDto` 已补齐 `service` 与 `direction` 列，满足 phase2 inspect MVP 表格需求
- 当前实现边界：
  - 当前过滤在本地稳定样本结果集上真实生效
  - 尚未接入后端 inspect projection/query 通道
  - 未引入 phase3 的 payload / session 级字段
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 过滤条件可下发并影响当前结果集
  - 表格仅依赖 phase2 可用聚合字段
- 提交：
  - outer repo `307822b` `Complete M2-007 inspect page mvp`

### M2-008 Settings 与诊断页 MVP

- 状态：已完成
- 完成内容：
  - `SettingsPageView`
  - `SettingsRuntimePanelView`
  - `SettingsCorePanelView`
  - `SettingsDiagnosticsPanelView`
- 具体落地：
  - Settings 页已从壳层占位切换成正式页面
  - runtime 面板可展示当前 source、BPF、tick interval、top N
  - core 面板可展示 endpoint、process state、health、core version、UI version
  - diagnostics 面板可展示错误日志入口和近期错误提示
- 当前实现边界：
  - 当前页面由稳定运行态样本驱动
  - 未扩展为大而全设置中心
  - 诊断面板聚焦 core / permission / filter 相关提示，不提前引入 phase3 诊断需求
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 当前 source、BPF、tick interval、top N 可见
  - core endpoint / process state / version 可见
  - 错误日志入口与近期错误提示可见
- 提交：
  - outer repo `272cbc9` `Complete M2-008 settings and diagnostics mvp`

### M2-009 第二阶段封板与质量门禁

- 状态：已完成
- 完成内容：
  - 第二阶段封板版本
  - 第二阶段运行说明
  - 第二阶段验收记录模板
- 具体落地：
  - 补充 phase2 runbook
  - 补充 phase2 acceptance template
  - 补齐质量门禁记录
- 质量门禁结果：
  - `dotnet format flowarden-ui/Flowarden.Ui.sln --verify-no-changes` 通过
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - `cargo test -q -p flowarden` 通过
- 封板边界：
  - 第二阶段 UI 已可独立评审
  - 后续 phase3 继续接真实地图能力和 session 详情，不需要推倒当前 UI 壳层
- 提交：
  - outer repo `2618230` `Complete M2-009 phase 2 quality gates and docs`

### M2-101 Destination Workbench 增强预留

- 状态：已完成
- 完成内容：
  - 更明确的 destination placeholder model
  - 地图区域空态 / future state 文案
  - 更稳定的 destination ranking 辅助说明
- 具体落地：
  - `Destination Map` 区域不再只是空白框
  - 用户可以明确理解该区域未来承载 destination 分布、区域热点、组织叠层等能力
  - `Destination Map + Top Destinations` 的组合关系更明确
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 地图区域已具备空态、reserved state、future state 的说明
  - 不影响第二阶段主线封板结论
- 提交：
  - outer repo `fda2e27` `Complete M2-101 destination workbench reserve enhancement`
