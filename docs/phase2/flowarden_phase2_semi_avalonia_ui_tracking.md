# Flowarden Phase2 Technical Forensic Console UI 改造跟踪表

## 1. 文档目的

本文用于跟踪 Flowarden Avalonia desktop UI 的新一轮视觉与布局改造。

本轮设计基线改为 `docs/phase2/stitch_flowarden_network_monitoring_*` 四个 Stitch 原型目录。此前下载的 `docs/phase2/flowarden_*.png` 不再作为实现基线，仅保留为历史参考。

本专项只跟踪 UI 视觉系统、页面布局和控件风格，不替代既有 phase2 backlog 状态。既有 `M2-001` 到 `M2-009`、`M2-101` 仍以 `flowarden_phase2_progress.md` 为准。

## 2. 新原型输入

| 页面 | 原型目录 | 核心文件 | 页面定位 | 采纳重点 |
| --- | --- | --- | --- | --- |
| Overview | `stitch_flowarden_network_monitoring_1` | `screen.png`、`code.html`、`DESIGN.md` | Console Dashboard | 左 rail、48px top app bar、KPI row、Traffic Throughput、大图表、Geospatial Routing、Top Regions、Top Hosts/Services/Connections。 |
| Source | `stitch_flowarden_network_monitoring_2` | `screen.png`、`DESIGN.md` | Source Selection | Interfaces 列表、eth0 Configuration、Hardware Details、Traffic Summary、底部 Capture Stop / Start Live Capture 操作区。 |
| Inspect | `stitch_flowarden_network_monitoring_3` | `screen.png`、`code.html`、`DESIGN.md` | Inspect Workbench | 顶部 filter-first bar、active filter chips、密集连接表格、底部结果汇总。 |
| Settings | `stitch_flowarden_network_monitoring_4` | `screen.png`、`code.html`、`DESIGN.md` | Settings & Diagnostics | Runtime Status、Core Connection、Capture Defaults、Diagnostics 四面板。 |

## 3. 总体设计方向

本轮采用 `Technical Forensic Console` 方向：高密度、低装饰、细边框、低圆角、强数据对齐，面向网络观测和取证分析。

与上一版设计相比，本轮明确改变：

1. 不再追求大胶囊、玻璃感或卡片堆叠。
2. 不使用柔和渐变、阴影或营销式 dashboard。
3. 页面布局更接近工业监控台和取证 console。
4. 数字、IP、端口、包数等数据优先使用等宽字体。
5. 控件高度压缩到 32-36px，表格行高以 32px 为基准。
6. 面板以 1px 边框和 tonal stacking 区分层级。

## 4. 原型适配规则

1. 保留现有产品主导航：`Overview / Source / Inspect / Settings`。
2. 全局 shell 以四张新原型的共同结构为准：176px 左 rail + 48px top app bar + 24px page padding。
3. 统一顶部标题为当前页面语义，不机械照搬所有图里的 `Console Dashboard`。
4. 不扩展 phase2 数据边界，不新增 payload/session 深度解析。
5. 不为了匹配原型伪造后端不存在的数据；缺失数据必须用现有 DTO、占位态或后置任务表达。
6. Source 最新原型已撤下 `Promiscuous Mode / Snapshot Length / Buffer Size / BPF Filter` 首屏配置区，本轮 Source 以 `Hardware Details + Traffic Summary + Capture Stop / Start Live Capture` 为准。
7. Overview 原型中的 `Geospatial Routing` 仍对应 phase2 的 destination reserved workbench，不实现真实地图能力。
8. Settings 原型中的 daemon 日志可先映射为 diagnostics summary；完整 log viewer 不纳入首批。
9. Semi.Avalonia 作为控件底座可以继续使用，但最终外观以本轮 `Technical Forensic Console` token 为准，不套 Semi 默认风格。

## 5. 视觉 Token 决策

| 类别 | Token | 值 / 规则 |
| --- | --- | --- |
| App background | `TfcBackground` | `#0F1117` 或贴近原型的深黑中性底色。 |
| Shell surface | `TfcSurface` | `#141218` / `#181B23`，用于 rail、top bar、主面板。 |
| Panel surface | `TfcPanel` | `#181B23`。 |
| Raised surface | `TfcRaised` | `#20242E`。 |
| Border | `TfcBorder` | `#2B303B`，所有主要结构使用 1px 边框。 |
| Floating border | `TfcBorderHigh` | `#3F444E`，用于 popover、tooltip、浮层。 |
| Text primary | `TfcText` | `#E6E0E9` / `#E7EAF0`。 |
| Text muted | `TfcMutedText` | `#CBC4D2` 或更低对比度的说明文本。 |
| Primary accent | `TfcPrimary` | 以原型紫 `#CFBCFF` 为主按钮和选中态。 |
| Traffic cyan | `TfcTrafficCyan` | `#00F0FF`，用于 inbound/traffic/链接数据。 |
| Traffic purple | `TfcTrafficPurple` | `#B026FF`，用于 outbound/secondary series。 |
| Health green | `TfcGood` | `#00FF66` / `#10B981`。 |
| Warning amber | `TfcWarning` | amber，仅用于 warning。 |
| Error red | `TfcError` | red，仅用于错误和 stop。 |
| Radius | `TfcRadius` | 默认 4-6px；外层面板最多 8px；禁止大胶囊。 |
| Control height | `TfcControlHeight` | 32px 常规，36px 强操作。 |
| Table row height | `TfcTableRowHeight` | 32px。 |
| Page padding | `TfcPagePadding` | 24px。 |
| Section gap | `TfcGap` | 16px。 |

## 6. 字体策略

| 用途 | 决策 |
| --- | --- |
| UI 文本 | 继续使用现有 Inter 或评估 Geist；不因视觉改造阻塞实现。 |
| 数据文本 | 引入等宽数据样式，优先 JetBrains Mono；若字体资源暂不接入，先用系统 monospace fallback。 |
| 标题 | 24px / 600，贴近原型 h1。 |
| 面板标题 | 16-20px / 600。 |
| 正文 | 13-14px。 |
| 表头 / 标签 | 11px uppercase / 600，适度 letter spacing。 |

## 7. 状态规则

| 状态 | 含义 |
| --- | --- |
| 未开始 | 尚未进入实现。 |
| 进行中 | 已开始实现，但未完成自测。 |
| 待验收 | 代码已完成，等待人工确认视觉与交互。 |
| 已完成 | 代码、自测、人工验收均通过。 |
| 需返工 | 已有实现，但与原型在字体、布局、比例、密度或视觉语言上差距较大，不能进入验收。 |
| 阻塞 | 因依赖、版本或设计决策无法推进。 |
| 后置 | 明确不纳入当前改造批次。 |

## 8. 阶段总览

| 阶段 | 范围 | 状态 | 目标 |
| --- | --- | --- | --- |
| `TFC-G0` | 原型评审与方案冻结 | 已完成 | 用四个新 Stitch 目录替换旧设计基线。 |
| `TFC-G1` | 全局 token 与 shell | 进行中 | 已按 Source 新原型收敛 rail 宽度、Source top bar 操作区和深紫 token；其余页面仍需继续复核。 |
| `TFC-G2` | Overview 落地 | 进行中 | 已完成 KPI、Traffic Throughput 骨架、Top Regions 和三榜单密度重排；仍需最终对照固定窗口和真实投影数据复核。 |
| `TFC-G3` | Source 落地 | 待验收 | 已按最新 Source 原型重排为 Interfaces + Configuration，撤下旧 Activity/Capture Preferences 结构；追加 Capture Stop，并将底部按钮区优化为状态摘要 + 固定宽度操作组；设备列表先加载接口清单，随后刷新 preview 并回填 Traffic Summary；修正 UI launcher 使用 `core --bind`，避免 core 未启动导致 Source 空白。 |
| `TFC-G4` | Inspect 落地 | 待验收 | 已按 prototype 3 压缩 filter bar、active filter chips、结果表和 footer 密度，完成构建和 `inspect_after8.png` 截图验证；真实数据为空时仍保持空表，不伪造连接行。 |
| `TFC-G5` | Settings 落地 | 待验收 | 已按 prototype 4 收敛为首屏 2x2 面板，压缩标题区、panel padding、输入/按钮高度和 diagnostics 日志密度，完成构建和 `settings_after4.png` 截图验证。 |
| `TFC-G6` | 验证与文档收口 | 进行中 | 构建和 Source/Inspect/Settings 截图已完成；剩余 Overview 终态和固定窗口尺寸复核。 |

## 9. 开发跟踪表

| ID | 模块 | 任务 | 状态 | 原型依据 | 输出物 | 验收条件 |
| --- | --- | --- | --- | --- | --- | --- |
| `TFC-001` | 原型 | 废弃上一版 `flowarden_*.png` 基线，确认新 Stitch 目录为唯一实现基线 | 已完成 | 四个 `stitch_flowarden_network_monitoring_*` 目录 | 本文件 | 文档明确旧基线不再使用，新基线路径完整。 |
| `TFC-002` | 依赖 | 确认 Semi.Avalonia 兼容版本与接入方式 | 已完成 | 全局 | `.csproj` 版本决策 | 不升级 Avalonia 12；若使用 Semi，选 Avalonia `11.3.12` 兼容版本。 |
| `TFC-003` | 主题 | 建立 Technical Forensic Console token | 进行中 | `DESIGN.md`、四张 screen | `Styles/Theme.axaml` | 已按 Source 新原型收敛 `#141218/#0f0d13/#1d1b20/#494551/#cfbcff`，Source/Inspect/Settings 已完成本轮截图确认，Overview 仍需终态复核。 |
| `TFC-004` | 字体 | 建立 UI 字体和数据等宽字体样式 | 进行中 | `DESIGN.md` | `Styles/Controls.axaml` 或字体资源 | 已切换 Geist fallback 和数据等宽样式，Source 已验证；字体资源是否实际安装仍需最终确认。 |
| `TFC-005` | 控件 | 收敛 Button/Input/Select/Switch/Chip/Status 基础样式 | 进行中 | Source、Inspect、Settings | `Styles/Controls.axaml` | Source/Inspect/Settings 的按钮、chip、输入和状态标签已收敛到 32-36px 和 4-6px radius；Overview 仍需固定尺寸复核。 |
| `TFC-006` | 控件 | 建立 panel/header/table/list/progress reusable classes | 进行中 | Overview、Inspect | `Styles/Controls.axaml` | Inspect table/header/footer 已恢复紧凑 panel、32px 行高和 1px 分割线；Overview 相关 panel 仍需终态复核。 |
| `TFC-007` | Shell | 重做左侧 rail | 进行中 | 四张 screen | `AppShellView.axaml`、`AppRailView.axaml` | rail 已从 220px 收敛到 180px，Source 选中行铺满修正；其余页面比例待复核。 |
| `TFC-008` | Shell | 重做 top app bar | 进行中 | 四张 screen | `AppHeaderView.axaml` | Source 页已切换为刷新、最近刷新、导入离线和 sensors 操作区；其他页面仍保留状态区，需后续统一。 |
| `TFC-009` | Shell | 重做主工作区容器 | 需返工 | 四张 screen | `AppShellView.axaml` | 当前运行截图包含窗口外部干扰且页面尺度偏大；需固定内容画布、24px padding、原型主区比例。 |
| `TFC-010` | Overview | 实现 KPI row | 待验收 | prototype 1 | `OverviewPageView.axaml` / component | 已重排为 5 个 72px KPI panel，数字使用等宽字体，状态小标签来自当前真实数据状态。 |
| `TFC-011` | Overview | 重做 Traffic Throughput panel | 待验收 | prototype 1 | `HeroTrafficChartView.axaml` / `.cs`、`OverviewPageViewModel.cs` | 已恢复图表 header、legend、网格和坐标骨架；`Current Throughput` 改为随鼠标移动的 hover tooltip，按最近 timeline 点显示 IN/OUT；无 timeline 时保留空态，不伪造折线数据。 |
| `TFC-012` | Overview | 重做 destination reserved 区 | 进行中 | prototype 1 | `DestinationWorkbenchView.axaml` | 已压缩 Geospatial Routing 与 Top Regions 比例并加入区域空态；地图仍按 phase2 reserved visual 表达，不实现真实地图。 |
| `TFC-013` | Overview | 重做底部 Top Hosts/Services/Connections | 待验收 | prototype 1 | `TopHostsView`、`TopServicesView`、`TopConnectionsView`、`OverviewPageViewModel.cs` | 已改为 32px 行高、等宽数据、真实数据占比条和右对齐数值；Top Hosts/Top Connections 尽量显示 `IP(所属)`，无归属信息时不伪造；无数据时显示紧凑空态。 |
| `TFC-014` | Source | 重做 Source 顶部栏 | 待验收 | prototype 2 最新 `screen.png` | `AppHeaderView.axaml`、`SourcePageView.axaml` | 已去掉页内重复面包屑，刷新/最近刷新/导入离线移动到 48px top app bar 右侧。 |
| `TFC-015` | Source | 重做 Interfaces 列表 | 待验收 | prototype 2 最新 `screen.png` + 用户反馈 | `SourceDeviceListView.axaml`、`SourceDeviceItemViewModel.cs` | 已重排接口列表、selected 紫色边、状态点、RX/TX 小块和等宽数字；启动时先填充 device inventory，再刷新 preview，避免页面显示 0 device 且保持 Traffic Summary 有 preview 数据。 |
| `TFC-016` | Source | 重做 Configuration header 和 Hardware Details | 待验收 | prototype 2 最新 `screen.png` | `SourcePreviewWorkbenchView.axaml`、`SourcePageViewModel.cs` | 已还原 `* Configuration` 标题、selected tag 和 2x2 硬件详情块；背景改为中性数据面板，避免与主按钮同色。 |
| `TFC-017` | Source | 实现 Traffic Summary 区 | 待验收 | prototype 2 最新 `screen.png` + 用户反馈 | `SourcePreviewWorkbenchView.axaml`、`SourcePageViewModel.cs` | 已按新原型改为 Packets/Average Rate/Bytes/Errors 四项统计，数值使用中性数据文本配色；启动加载恢复 preview 刷新，Traffic Summary 使用 selected device preview 数据回填。 |
| `TFC-018` | Source | 同步撤下 Capture Preferences 首屏区 | 待验收 | prototype 2 最新 `screen.png` | `SourcePreviewWorkbenchView.axaml` | 最新 Source 原型不再展示 Capture Preferences，本轮已从首屏移除，后续若恢复需另开设置/偏好任务。 |
| `TFC-019` | Source | 重做底部 command bar | 待验收 | prototype 2 最新 `screen.png` | `SourcePreviewWorkbenchView.axaml` | 已改为右侧配置面板底部的 `Capture Stop` + `Start Live Capture` 固定宽度操作组，左侧增加 capture 状态摘要；Start 保持主按钮，Stop 按运行状态启用。 |
| `TFC-020` | Inspect | 重做 filter-first 工具条 | 待验收 | prototype 3 | `InspectFilterBarView.axaml` | 已压缩为 32px 控件、12px panel padding 和单行 filter controls；Search、Last 5m、Clear、Apply 均不再被截断。 |
| `TFC-021` | Inspect | 重做 active filters | 待验收 | prototype 3 | `InspectFilterBarView.axaml` / VM 如需 | 已改为紧凑 rectangular chips 和 13px 空态文本；无 active filters 时不伪造默认 chip。 |
| `TFC-022` | Inspect | 评估 DataGrid vs 手写 table | 已完成 | prototype 3 | 技术结论 | 明确列宽、排序、滚动、虚拟化和样式可控性。 |
| `TFC-023` | Inspect | 重做 connection table | 待验收 | prototype 3 | `InspectResultsTableView.axaml`、`TcpConnectionsResultsTableView.axaml`、row DTO | 已恢复 32px row、紧凑列宽、proto tag、方向符号和短格式 bytes/packets；运行时无投影数据时保留空表。 |
| `TFC-024` | Inspect | 重做 footer summary | 待验收 | prototype 3 | `InspectFooterSummaryView.axaml` | 已改为贴合表格底部的 32px fixed footer，左侧 filter 文案、右侧三项统计按原型对齐。 |
| `TFC-025` | Settings | 重做 Settings 页面标题区 | 待验收 | prototype 4 | `SettingsPageView.axaml` | 已改为 24px 标题、13px 说明和 16px 主间距，撤掉页面级 ScrollViewer 引发的大尺度滚动。 |
| `TFC-026` | Settings | 重做 Runtime Status panel | 待验收 | prototype 4 | Settings components | 已压缩 status tags、两列字段、label/data 字号和 panel padding。 |
| `TFC-027` | Settings | 重做 Core Connection panel | 待验收 | prototype 4 | Settings components | 已按原型比例压缩 core version/health/endpoint/reconnect 区，输入和按钮统一为 32px。 |
| `TFC-028` | Settings | 重做 Capture Defaults panel | 待验收 | prototype 4 + 用户反馈 | Settings components | 已删除 `Auto-start capture on launch` 开关；启动 UI 后固定自动启动 capture，面板保留现有 Top N 绑定和 retention 展示。 |
| `TFC-029` | Settings | 重做 Diagnostics panel | 待验收 | prototype 4 | Settings components | 已还原 Export 按钮、等宽日志块和 INFO/WARN 紧凑行距。 |
| `TFC-030` | 适配 | 桌面尺寸验证 | 进行中 | 全局 | 布局修正 | Source/Inspect/Settings 已完成当前窗口截图；仍需固定 1440x900、1600x1200、1920x1080 三档重验。 |
| `TFC-031` | 验证 | 构建与基础运行 | 已完成 | 全局 | build/run 记录 | `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过；本机 CLI `flowarden devices --format json` 可列出 22 个接口；`flowarden core --bind 127.0.0.1:39232` 可正常监听。 |
| `TFC-032` | 验收 | 对照新原型截图验收 | 进行中 | 四张 `screen.png` | `docs/phase2/tfc_runtime_screenshots/*_after*.png` | Source/Inspect/Settings 已进入待验收截图；Overview 和固定尺寸截图仍需收口。 |
| `TFC-033` | 文档 | 更新 UI 设计和差距文档 | 进行中 | 全局 | 文档更新 | 本跟踪表已同步最新落地和截图记录；`flowarden_phase2_ui_design.md`、`flowarden_phase2_ui_gap_analysis.md` 仍待按同口径收口。 |

## 10. 推荐实施顺序

| 批次 | 范围 | 任务 |
| --- | --- | --- |
| Batch R1 | Foundation Refit | `TFC-003` 到 `TFC-009`，先修字体、token、控件高度、rail/top bar 和全局比例；Source 新原型相关部分已先行落地。 |
| Batch R2 | Overview + Inspect | `TFC-010` 到 `TFC-013`、`TFC-020` 到 `TFC-024`，先修最依赖密度和表格的页面。 |
| Batch R3 | Source + Settings | `TFC-014` 到 `TFC-019`、`TFC-025` 到 `TFC-029`；Source 与 Settings 已进入待验收。 |
| Batch R4 | Verification | `TFC-030` 到 `TFC-033`，固定窗口尺寸截图，对照四张 `screen.png` 做差距收敛。 |

## 11. 页面验收口径

### 11.1 Overview

1. 左 rail 和 top app bar 与原型一致，页面不再使用旧大圆角 workbench 外壳。
2. KPI row 五项横向排列，数据用等宽风格。
3. Traffic Throughput 是页面主视觉，具备网格、双折线、legend 和 current throughput。
4. Geospatial Routing 明确是 destination reserved visual，不伪装为真实地图能力。
5. Top Hosts、Top Services、Top Connections 三面板行高、边框、进度条和数值对齐一致。

### 11.2 Source

1. Interfaces 左列宽度接近 400px，selected 设备使用 3px 主色边。
2. 右侧 Configuration panel 包含 header、Hardware Details、Traffic Summary。
3. RX/TX packets、MAC、IP、MTU 等数据使用等宽样式。
4. 底部 command bar 左侧展示 capture 状态摘要，右侧使用固定宽度 `Capture Stop` 与 `Start Live Capture` 操作组；Start 为主动作，Stop 仅在 capture running 时启用。
5. 不伪造不存在的 MAC/MTU/speed 或 timeline 数据；缺失值保持 `not reported` 或现有 preview 状态。

### 11.3 Inspect

1. 顶部 filter bar 是页面主交互区，控件高度和边框贴近原型。
2. active filters 使用矩形 chip，不使用大胶囊。
3. connection table 具备高密度、等宽数据、固定表头视觉和右对齐数字。
4. footer summary 横向排布清晰，能表达过滤状态和总量。

### 11.4 Settings

1. 页面使用两列四面板结构。
2. Runtime/Core/Capture/Diagnostics 面板均使用同一种 panel language。
3. 状态标签使用小矩形或小色点，不使用大 pill。
4. Diagnostics 日志块等宽显示，INFO/WARN 颜色语义清楚。

## 12. 风险记录

| 风险 | 影响 | 应对 |
| --- | --- | --- |
| 新原型明显偏紫色主强调，与之前 cyan 主色冲突 | 主题不统一 | 本轮主选中态采用原型 primary purple，traffic 数据仍用 cyan/purple 双序列。 |
| 原型包含当前 DTO 不支持的字段 | 可能扩大业务范围 | 只落地 UI 结构；无数据字段用现有状态、空态或后置任务表达。 |
| Source 最新原型撤下 Activity/Capture Preferences | 旧跟踪项容易误导实现 | 跟踪项已改为 Traffic Summary 和 Capture Stop / Start Live Capture 操作区；Capture Preferences 后续如需恢复另立任务。 |
| Settings 默认项涉及持久化配置 | UI 改造牵动设置系统 | 用户已明确 Auto-start 不做可配置项，改为 UI 启动后固定自动启动 capture；本轮不新增持久化设置。 |
| Semi.Avalonia 默认风格与 forensic console 冲突 | 画面被默认控件风格稀释 | Semi 只作为控件底座，最终样式由 TFC token 覆盖。 |
| 大量 XAML 重排影响现有绑定 | 回归风险 | 按页面分批改造，每批执行 `dotnet build` 和四页手动打开验证。 |

## 13. 当前结论

当前专项已完成首轮 TFC 结构落地，但不能视为全局视觉完成。Source 已按用户更新后的 `stitch_flowarden_network_monitoring_2/screen.png` 完成第二轮重排，并根据最新反馈补入 Capture Stop、修正 Hardware Details 与 Traffic Summary 配色、优化底部按钮区布局，进入待验收；Inspect 已完成 filter bar、active filters、结果表和 footer 的第二轮密度重排，进入待验收；Settings 已完成标题区和四面板首屏 2x2 紧凑重排，进入待验收；Overview 已完成 KPI、图表骨架、Top Regions 和三榜单密度返工，仍需最终固定尺寸复核。

已通过 `dotnet build flowarden-ui/Flowarden.Ui.sln` 验证当前代码可构建。为绕开 macOS 辅助功能点击限制，已增加 `FLOWARDEN_UI_INITIAL_PAGE=source|inspect|settings` 启动页验证入口；默认启动仍为 Overview。

本轮运行截图记录：

| 页面 | 截图 |
| --- | --- |
| Overview | `docs/phase2/tfc_runtime_screenshots/overview_after4.png` |
| Source | `docs/phase2/tfc_runtime_screenshots/source_after7.png` |
| Inspect | `docs/phase2/tfc_runtime_screenshots/inspect_after8.png` |
| Settings | `docs/phase2/tfc_runtime_screenshots/settings_after4.png` |

当前状态：Source 待验收；Inspect 待验收；Settings 待验收；Overview 进行中并已有局部待验收项。下一轮优先顺序：继续统一 Geist/JetBrains Mono 字体策略、32px 控件高度、4-6px radius、panel padding 和 shell 比例；然后固定 1440x900、1600x1200、1920x1080 三档截图复核。
