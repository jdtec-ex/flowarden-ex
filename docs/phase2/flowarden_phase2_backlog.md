# Flowarden 第二阶段开发 Backlog

## 1. 文档目的

本文将第二阶段实施方案拆成可执行 backlog，目标是让后续开发可以直接进入排期和实现，而不是继续停留在页面概念和架构原则层。

任务设计遵循以下约束：

1. 固定使用本机 `dotnet` SDK `8.0.125`
2. UI 默认风格固定为 `Cosmos Network System`
3. 继续遵循 `YAGNI`
4. 正式 capture 仍然只允许单 source
5. 错误语义继续统一对齐 `flowarden-error`

对应的编码顺序、文件落位和建议提交边界见：

- `flowarden_phase2_implementation_sequence.md`

---

## 2. 使用方式

每个 backlog 项都包含：

- 编号
- 目标
- 输入
- 输出
- 依赖
- 实现要点
- 验收条件

优先级约定：

- `M2`：第二阶段主线
- `M2.1`：第二阶段建议保留但可后置的小版本项

本文默认所有 `M2-xxx` 都是第二阶段主线任务。

---

## 3. 总体依赖顺序

建议顺序如下：

```text
M2-001 -> M2-002 -> M2-003
               |
               v
        M2-004 -> M2-005 -> M2-006
               |
               v
        M2-007 -> M2-008 -> M2-009

M2-101 可在 M2-006 之后插入
```

说明：

- `M2-001` 到 `M2-003` 是工程基线、resident core 模式和契约骨架。
- `M2-004` 到 `M2-006` 是 shell、source、overview 主线。
- `M2-007` 到 `M2-009` 是 inspect、settings、封板主线。
- `M2-101` 是 destination workbench 的增强预留项，不阻塞第二阶段封板。

---

## 4. Backlog 明细

## M2-001 UI 工程骨架与 SDK 基线

### 目标

建立 `flowarden-ui` 的最小 Avalonia 工程，并固定本机 `.NET` 基线。

### 输入

- 本机 `dotnet --version = 8.0.125`
- 第二阶段开发计划中的目录建议

### 输出

- `flowarden-ui/`
- `Flowarden.Ui.sln`
- `src/Flowarden.Ui/`
- `global.json` 或等价 SDK 约束

### 依赖

- 无

### 实现要点

1. 目标框架固定为 `net8.0`。
2. 只建立一个桌面主项目，不提前拆多项目方案。
3. 首次工程即接入 Avalonia 基础样式与启动入口。
4. README 或 runbook 中明确构建命令。

### 验收条件

1. `dotnet build` 可在本机 `8.0.125` 下通过。
2. 窗口可启动。
3. 工程结构可承接后续 `Views / ViewModels / Services / State / Styles`。

---

## M2-002 resident core 模式与本地 gRPC 骨架

### 目标

让 Rust core 可以以本地 `flowarden core` 常驻模式运行，并建立 UI 可连接的最小通信边界。

### 输入

- 第一阶段稳定的 `flowarden-core`
- 第二阶段契约设计建议

### 输出

- resident core mode
- health/version 接口
- control / discovery / projection 骨架

### 依赖

- `M2-001`

### 实现要点

1. 不直接把抓包逻辑塞进 Avalonia 进程。
2. 先实现最小 health / version / discovery。
3. UI 启动时可检测 core 是否在线。
4. 所有错误仍可映射回统一语义。

### 验收条件

1. UI 可判断 core 是否在线。
2. UI 可拉起 core 或连接已运行 core。
3. 错误路径清楚且不崩窗口。

---

## M2-003 phase2 契约模型冻结

### 目标

冻结第二阶段 UI 与 core 之间的跨进程模型，避免后面边写 UI 边改协议。

### 输入

- 第一阶段 `tick_snapshots` / `final_snapshot`
- 第二阶段 UI 设计中的区块职责表

### 输出

- `DeviceSummaryDto`
- `DevicePreviewDto`
- `CaptureSessionStateDto`
- `OverviewSnapshotDto`
- `InspectFilterDto`
- `InspectResultDto`
- `CoreErrorDto`

### 依赖

- `M2-002`

### 实现要点

1. Dto 命名明确区分领域模型与传输模型。
2. 只冻结 phase2 实际用到的字段。
3. `DestinationMapPanel` 和 `TopDestinationsPanel` 的 placeholder 契约也要预留。

### 验收条件

1. UI 不直接依赖 Rust 内部结构体。
2. phase2 页面所需最小数据面齐备。
3. 契约可稳定支撑后续 UI 开发，不需要反复返工。

---

## M2-004 App Shell 与全局状态层

### 目标

落地 `Cosmos Network System` 的 shell 结构，让页面切换、运行状态和主动作先稳定。

### 输入

- `flowarden_phase2_ui_design.md`
- shell 线框和视图树建议

### 输出

- `AppShellView`
- `AppRailView`
- `AppHeaderView`
- `AppShellViewModel`

### 依赖

- `M2-001`
- `M2-003`

### 实现要点

1. 固定 `Left Rail + Top App Bar + Main Workbench`。
2. 左 rail 放主导航和 `Start Capture` 主 CTA。
3. top app bar 放模式切换、状态点、工具入口。
4. 先把页面宿主和切换机制立稳，不先写复杂业务。

### 验收条件

1. shell 可启动并稳定切页面。
2. 全局状态点可显示 core / capture 状态。
3. 风格已收敛到 `Cosmos Network System`，不是默认 Avalonia 样式。

---

## M2-005 Source 页面 MVP

### 目标

把 phase1 的多 device preview 与单 source 选择图形化。

### 输入

- discovery / preview 契约
- Source 页面线框

### 输出

- `SourcePageView`
- `SourceDeviceListView`
- `SourcePreviewWorkbenchView`
- `SourcePageViewModel`

### 依赖

- `M2-003`
- `M2-004`

### 实现要点

1. 清楚区分 preview 与 formal capture。
2. 支持 live source 与 offline file import 入口。
3. 设备权限错误、unsupported、无设备等场景要可视化。
4. 页面是“左列表 + 右详情”工作台，不做卡片拼贴风。

### 验收条件

1. 所有 device preview 可展示。
2. 用户只能选一个 source 进入正式 capture。
3. preview 与 formal capture 文案严格区分。

---

## M2-006 Overview 页面 MVP

### 目标

把第一阶段聚合结果放入新的 `hero chart + status cards + destination workbench + lower detail row` 结构中。

### 输入

- `OverviewSnapshotDto`
- Overview 页面线框

### 输出

- `HeroTrafficChartView`
- `StatusCardsRowView`
- `DestinationWorkbenchView`
- `TopHostsView`
- `TopServicesView`
- `TopConnectionsView`

### 依赖

- `M2-003`
- `M2-004`

### 实现要点

1. 顶部 hero chart 是第一视觉焦点。
2. 中间四张状态卡必须可一眼读数。
3. `Destination Map` 和 `Top Destinations` 必须成对留位。
4. `Top hosts / Top services / Top connections` 不能因新版布局被挤掉。

### 验收条件

1. 页面数据与 phase1 CLI 口径一致。
2. live/offline 两种模式都可显示。
3. `Destination Map` 区域在布局中稳定存在，即使当前只显示 placeholder。

---

## M2-007 Inspect 页面 MVP

### 目标

把 phase1 的连接明细和过滤能力变成可交互的表格工作台。

### 输入

- `InspectFilterDto`
- `InspectResultDto`
- Inspect 页面线框

### 输出

- `InspectPageView`
- `InspectHeaderView`
- `InspectFilterBarView`
- `InspectResultsTableView`
- `InspectFooterSummaryView`

### 依赖

- `M2-003`
- `M2-004`

### 实现要点

1. 过滤条必须保持高可见性。
2. 结果表格是主体，不要让装饰元素挤压高度。
3. 当前结果数、排序状态和结果摘要要持续可见。
4. 不引入 phase3 的 payload / session 字段。

### 验收条件

1. 过滤条件可下发。
2. 表格结果与 phase1 聚合结果一致。
3. Inspect 页不依赖 phase3 会话级数据。

---

## M2-008 Settings 与诊断页 MVP

### 目标

收敛第二阶段最小配置、版本、连接与错误信息页面。

### 输入

- health/version 契约
- error state
- Settings 页面线框

### 输出

- `SettingsPageView`
- `SettingsRuntimePanelView`
- `SettingsCorePanelView`
- `SettingsDiagnosticsPanelView`

### 依赖

- `M2-003`
- `M2-004`

### 实现要点

1. 只放最小运行配置，不做大而全设置中心。
2. 诊断面板优先承载 core 错误和权限问题提示。
3. 页面也要走 glass panel，不回退成普通表单页。

### 验收条件

1. 当前 source、BPF、tick interval、top N 可展示。
2. core endpoint / process state / version 可展示。
3. 错误日志入口与近期错误提示可见。

---

## M2-009 第二阶段封板与质量门禁

### 目标

把第二阶段从“窗口能跑”提升到“可评审、可重复运行、可继续承接 phase3”的状态。

### 输入

- 前置全部任务产物

### 输出

- 第二阶段封板版本
- 运行说明
- 验收记录模板

### 依赖

- `M2-006`
- `M2-007`
- `M2-008`

### 实现要点

1. 固定 UI 工程质量门禁。
2. 运行说明至少写清：
   - 如何构建 UI
   - 如何启动 UI
   - 如何连接或拉起 core
   - 如何完成 source 选择和正式 capture
3. 明确已保留和明确后置的能力，尤其是 `Destination Map` 的 placeholder 语义。

### 验收条件

1. 第二阶段可被独立评审和重复验收。
2. 文档、产物、运行说明齐备。
3. phase3 继续接地图真实能力或 session 详情时，不需要推倒 UI 壳层。

---

## M2-101 Destination Workbench 增强预留

### 目标

为 `Destination Map + Top Destinations` 建立比 placeholder 更稳定的展示壳，但不阻塞第二阶段主线封板。

### 输入

- `DestinationMapPanel`
- `TopDestinationsPanel`

### 输出

- 更明确的 destination placeholder model
- 地图区域空态 / 加载态 / future state 文案

### 依赖

- `M2-006`

### 实现要点

1. 不要求真实地图引擎。
2. 但要让用户明确知道这块区域未来承载什么。
3. 保证地图区和 destinations 排行的结构稳定。

### 验收条件

1. `Destination Map` 不再只是空白框。
2. 用户能理解该区域是预留给 destination 分布视图的。
3. 不影响第二阶段主线封板。

---

## 5. 建议排期分组

如果按最小风险排期，建议分 4 组推进。

### 组 A：工程与边界

- `M2-001`
- `M2-002`
- `M2-003`

### 组 B：UI 壳层与入口

- `M2-004`
- `M2-005`

### 组 C：主工作面

- `M2-006`
- `M2-007`
- `M2-008`

### 组 D：封板

- `M2-009`

### 组 E：阶段二.1

- `M2-101`

---

## 6. 我建议优先盯紧的任务

从风险角度，第二阶段最需要盯住的是：

1. `M2-002`
   - resident core mode 和 UI 拉起/连接逻辑不稳，后续页面都会悬空。
2. `M2-003`
   - 契约模型不先冻结，UI 和 core 会来回返工。
3. `M2-004`
   - shell 一旦定歪，后续所有页面都要重排。
4. `M2-006`
   - Overview 是第二阶段主价值页，也是 `Destination Map` 预留的关键落点。

---

## 7. 最终建议

如果你认可这个 backlog，我建议第二阶段执行时按下面方式管理：

1. 每完成一个 `M2-xxx` 都有对应运行记录或 UI 截图证据。
2. 不跨编号乱跳实现，先稳工程和边界，再稳页面。
3. `M2-002`、`M2-003`、`M2-004`、`M2-006` 应作为第二阶段里程碑检查点。

这样推进，第二阶段更容易做成一个结构清楚、视觉统一、后续能直接承接真实地图能力和 phase3 深度分析的 Avalonia 前端。
