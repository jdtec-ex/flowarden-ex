# Flowarden 第二阶段 UI 设计稿

## 1. 文档目的

本文用于冻结第二阶段 UI 的视觉方向、页面结构、交互边界和可验收的 MVP 范围。

本稿的参考来源不是抽象“风格偏好”，而是：

- `../ui-images/image-url.md` 中引用的 Sniffnet 页面截图

参考图包括：

1. `overview.png`
2. `inspect.png`
3. `notifications.png`
4. `deep_cosmos.png`
5. `thumbnail.png`

### 1.1 本轮评审先看什么

本轮先不讨论 Stitch 样版，也不先讨论 Avalonia 控件细节，而是先从原参考图抽出两类内容：

1. 这些页面到底在展示哪些领域实体
2. 这些页面到底由哪些界面要素组成

只有这两件事先冻结，后续基于 Stitch 重做 layout 和风格时，才不会把关键监控信息丢掉。

### 1.2 参考图中的领域实体

从 `overview / inspect / notifications / thumbnail` 四类画面看，原图实际在展示的不是“页面组件集合”，而是一组稳定的流量监控实体：

1. `CaptureSource`
   - 当前网卡或当前数据源
   - 例如 `Network adapter: en0`
2. `CaptureState`
   - 当前是否运行、暂停、停止
   - 当前页面是否连接到活跃采集会话
3. `FilterState`
   - 当前生效的过滤条件
   - 例如 `Active filters: none`、国家/域名/ASN/服务筛选
4. `MetricMode`
   - 数据展示口径
   - `bits / bytes / packets`
5. `TrafficSummary`
   - 总接收、总发送、总丢弃
6. `TrafficTimeline`
   - 随时间推进的入站/出站速率序列
7. `HostEntry`
   - 主机或远端地址条目
   - 可能带 hostname、组织名、IP、收藏状态、国家标识
8. `ServiceEntry`
   - 服务条目
   - 例如 `https / domain / zeroconf`
9. `ProgramEntry`
   - 本地程序条目
   - 原图中是 Sniffnet 的增强能力，Flowarden phase2 只记录为“原图存在的实体”，不承诺 MVP 实现
10. `ConnectionEntry`
   - 连接明细条目
   - 包含源地址、目标地址、源端口、目标端口、协议、服务、字节数
11. `NotificationEvent`
   - 监控事件条目
   - 包含事件类型、时间、标题、目标对象、相关统计
12. `FavoriteState`
   - 收藏或关注标记
   - 原图中存在于 host / service / program / notification

### 1.3 参考图中的全局界面要素

原图在所有页面上基本保持了一套稳定的 shell：

1. 顶部主色带
2. 页面级主导航
3. 顶部快速动作区
   - 返回
   - 开始/暂停
   - 布局切换或缩略模式
   - 设置/工具
4. 主内容区深色面板系统
5. 底部状态带
6. 页面标题区
7. 当前页激活态
8. 全局图标语言
9. 全局颜色语义
   - 主强调色
   - 入站色
   - 出站色
   - 异常/警告色

这说明后续重设计时，哪怕换掉具体 layout，也不能丢：

1. 页面层级感
2. 运行状态可见性
3. 全局导航和局部工作区的区分

### 1.4 Overview 画面中的要素

`overview.png` 和 `deep_cosmos.png` 共同呈现了 Overview 页的核心要素：

1. Source summary
   - 当前网卡名
   - 当前过滤条件
2. Metric mode switch
   - `bits`
   - `bytes`
   - `packets`
3. Totals donut / totals summary
   - 总流量
   - incoming
   - outgoing
   - dropped
4. Traffic rate chart
   - 时间轴
   - 入站曲线或面积
   - 出站曲线或面积
   - 图例
5. Top host list
   - 主机名或 IP
   - 组织名
   - 排名值
   - 微型进度条
   - 收藏状态
6. Top service list
   - 服务名
   - 排名值
   - 微型进度条
   - 收藏状态
7. Top program list
   - 程序名
   - 图标
   - 排名值
   - 微型进度条
   - 收藏状态
8. 列表排序控件
9. 列表筛选入口
10. 列表滚动区

其中需要特别注意：

1. Overview 不是“一个总览卡片加若干小卡片”，而是“趋势图 + 三列排行榜”的工作台
2. 榜单每一项都承载了实体名称、数值和轻量比例关系
3. 原图中 `Program` 是一列独立榜单，不是连接明细的一部分

### 1.5 Inspect 画面中的要素

`inspect.png` 展示的是“过滤工作台 + 连接明细表格”，核心要素如下：

1. 页面标题
2. 顶部过滤条
3. 布尔开关类过滤器
4. 标签/Chip 类过滤条件
   - `Country`
   - `Domain`
   - `ASN`
   - `Program`
   - `Service`
5. 可移除的条件标签
6. 明细表格
7. 表头排序
8. 列级过滤入口
9. 结果滚动区
10. 底部汇总条
   - 当前结果总量
   - 当前统计值
11. 结果计数文案

表格字段在原图中非常明确：

1. `Address (source)`
2. `Port (source)`
3. `Address (destination)`
4. `Port (destination)`
5. `Protocol`
6. `Service`
7. `Bytes`

这里要注意两点：

1. Inspect 页在原图中是“连接/流记录视角”，不是主机榜单视角
2. 过滤器是页面一级主角，不是藏在二级弹窗里的附属功能

### 1.6 Notifications 画面中的要素

`notifications.png` 体现的是事件流视角，核心要素如下：

1. 事件卡片列表
2. 事件类型图标
   - 收藏相关
   - 阈值超限
   - 黑名单命中
3. 事件时间戳
4. 事件标题
5. 事件摘要
6. 相关实体
   - 主机
   - 服务
   - 程序
7. 相关统计值
8. 卡片内部的局部榜单
9. 删除或清理动作

这页对 Flowarden phase2 的价值不是“现在就做通知中心”，而是提醒后续 UI 结构里需要给事件卡片预留表达方式。

### 1.7 Thumbnail 画面中的要素

`thumbnail.png` 体现的是“压缩态概览”，它抽出的要素是：

1. 缩略窗口模式
2. 最小总量环形图
3. 最小趋势图
4. 精简 host 列表
5. 精简 service 列表
6. 顶部最少控制按钮

这说明原图并不只是做单一大窗口，而是有“密度压缩后的概览态”。

Flowarden phase2 不把它列为必做页面，但要在设计文档中记录：

1. 它是参考图中存在的显示形态
2. 它证明主数据模型需要支持 full / compact 两种呈现层级

### 1.8 需要落入 Flowarden phase2 MVP 的实体与要素

不是原图里出现的每个实体都要在 phase2 MVP 里实现。

按当前阶段边界，建议分成三组：

第一组：phase2 必须落地

1. `CaptureSource`
2. `CaptureState`
3. `FilterState`
4. `MetricMode`
5. `TrafficSummary`
6. `TrafficTimeline`
7. `HostEntry`
8. `ServiceEntry`
9. `ConnectionEntry`

第二组：phase2 可保留入口，但不要求完整实现

1. `NotificationEvent`
2. `FavoriteState`
3. compact / thumbnail presentation

第三组：原图存在，但 Flowarden phase2 明确后置

1. `ProgramEntry`
2. 进程图标
3. 国家旗帜与组织增强
4. 黑名单与通知系统完整版

这组划分的意义是：

1. 评审时先看“是否把原图真正重要的实体抓全了”
2. 后续用 Stitch 重设计时，再看“哪些实体放在哪个 layout 区域最合适”

---

## 2. 视觉方向结论

从参考图可以提炼出几个必须保留的视觉特征：

1. 高对比主题，不是默认桌面业务系统风格
2. 顶部与底部有强色带，主体内容区为深色容器
3. 页面重心是“监控面板”，不是传统表单页
4. 图表、榜单、过滤区同时存在，信息密度高
5. 色彩有明确角色分工：
   - 主强调色
   - 入站色
   - 出站色
   - 中性背景色

因此，Flowarden 的 phase2 UI 设计结论是：

> 借鉴 Sniffnet 的“监控台”视觉语言和信息密度，但用 Avalonia 重新实现，更强调模块清晰、状态反馈和后续可扩展性。

当前评审已进一步确定：

1. phase2 默认风格固定为 `Cosmos Network System`
2. 后续基于 Stitch 重做 layout 时，视觉基线直接采用 `docs/ui-images/stitch_avalonia_ui_refinement/DESIGN.md`
3. `Destination Map` 必须作为 Overview 的固定预留区域出现，MVP 可先不实现地图内容，但布局上不能省略

---

## 3. 设计原则

### 3.1 保留的部分

1. 顶部主导航
2. 深色主工作区
3. Overview 的“大图表 + 榜单列”
4. Inspect 的“过滤条 + 明细表格”
5. 强调色驱动的状态提示

### 3.2 不直接复制的部分

1. Sniffnet 的 logo、图标语言
2. `iced` 组件结构
3. 底部外链和装饰性内容
4. 过度拟物的卡通表达

### 3.3 phase2 额外强化的部分

1. 连接状态反馈
2. core 进程状态
3. 错误提示与恢复入口
4. source selection 与 preview 的前置流程

---

## 4. 主题方案

## 4.1 默认主题

第二阶段默认主题固定为 `Cosmos Network System`。

颜色角色：

1. `ShellBand`
   - 顶部/底部主带
   - 冷紫到宇宙蓝的高对比渐变
2. `SurfaceBase`
   - 主背景
   - 深宇宙底色
3. `SurfacePanel`
   - 卡片与列表容器
   - 玻璃感深色面板
4. `AccentPrimary`
   - 高亮边框、主按钮、选中项
   - 电紫
5. `TrafficInbound`
   - 入站流量
   - 亮紫
6. `TrafficOutbound`
   - 出站流量
   - 青蓝
7. `Danger`
   - 错误与异常
   - 高对比错误红

默认变量采用 `Cosmos Network System` 的主变量语义：

```text
background:         #11131e
surface-container:  #1d1f2b
surface-glass:      rgba(17, 19, 30, 0.65)
primary:            #d7baff
secondary:          #75d4e8
tertiary:           #ffafd7
on-background:      #e1e1f1
outline-variant:    #4a4451
error:              #ffb4ab
```

## 4.2 风格约束

`Cosmos Network System` 在 phase2 中意味着：

1. 使用 `Space Grotesk + Manrope` 的字体组合
2. 容器优先采用 glass / mica 感表面
3. 图表允许 glow path，但不能影响读数
4. 激活态要明确，但避免噪声过强
5. 所有页面都保持深色监控台的沉浸感

---

## 5. 字体与排版

参考图的文字观感不是默认系统字体堆栈，而是略有工具感的窄体/等宽风格。

Flowarden phase2 建议：

1. 标题与导航使用偏窄、偏技术感的展示字体
2. 数据数值与表格列优先使用等宽或类等宽字体
3. 一般说明文字使用清晰的无衬线字体

排版原则：

1. 数值优先对齐
2. 行高不要太松
3. 表格密度要高，但不能挤爆
4. 图表标题与卡片标题保持统一高度

---

## 6. 页面信息架构

第二阶段 UI 建议固定为 4 个一级页面：

1. `Source`
2. `Overview`
3. `Inspect`
4. `Settings`

说明：

1. `Source`
   - 新增页面
   - 承担多 device preview 与 source selection
2. `Overview`
   - 主监控台
3. `Inspect`
   - 明细与过滤工作台
4. `Settings`
   - 最小运行参数与连接状态

`Notifications` 不作为 phase2 一级必做页面，只在顶部状态区或后续阶段扩展。

### 6.1 基于 Stitch 的新页面分层

参考 `docs/ui-images/stitch_avalonia_ui_refinement/screen.png` 和对应 HTML 结构后，Flowarden 的新 UI 设计不再沿用 Sniffnet 原图那种“顶部 tab + 底部带”的页面壳，而是切换为更适合 Avalonia 的双层 shell：

1. 左侧纵向导航 rail
2. 顶部工作区 app bar
3. 中央单页工作台

这意味着：

1. `Source / Overview / Inspect / Settings` 会成为左侧主导航项
2. `Overview` 页内部再承载趋势图、状态卡、地图、榜单
3. `Inspect` 页内部再承载过滤条、结果表格和局部统计

这个分层更符合你指定的 `Cosmos Network System` layout，也更容易在 Avalonia 里形成稳定的容器结构。

---

## 7. 整体布局

## 7.1 主窗口布局

基于 Stitch 样版，主窗口建议采用如下结构：

```text
+------------------+-----------------------------------+
| Left Rail        | Top App Bar                       |
| nav / session    +-----------------------------------+
| quick actions    | Main Workbench                    |
| start capture    |                                   |
| docs / exit      |                                   |
+------------------+-----------------------------------+
```

说明：

1. 左 rail 固定承载全局导航、主动作和低频入口
2. 顶部 app bar 承载页面标题、二级模式切换、通知与连接态
3. 主内容区是单页工作台，整体采用 glass panel 组合
4. phase2 不再强依赖独立底部状态带，运行状态可并入 top app bar 与页面 summary 区

### 7.1.1 Left Rail 组成

参考 Stitch 样版，左 rail 建议固定包含：

1. 产品标识
   - `FLOWARDEN`
   - phase / version
2. 主导航
   - `Source`
   - `Overview`
   - `Inspect`
   - `Settings`
3. 主动作按钮
   - `Start Capture`
4. 低频入口
   - `Docs`
   - `Quit`

说明：

1. 这里不照搬 Stitch 中的 `Dashboard / Traffic / Packets / Devices / Settings`
2. Flowarden 要严格按已经冻结的 phase2 信息架构落导航
3. `Source` 页面承担原来 `Devices` 的职责，但语义更准确

### 7.1.2 Top App Bar 组成

顶部 app bar 建议包含：

1. 当前工作区标题
2. 二级模式切换
   - live
   - offline
3. core 状态
4. capture 状态
5. 通知入口
6. 全局工具入口

说明：

1. `Cosmos Network System` 的顶部 bar 是深色、轻玻璃感，不再做高亮色整条横幅
2. 高亮色更多留给 active tab、状态点和关键 CTA
3. 这样可以避免 shell 抢走监控内容的视觉重心

## 7.2 导航建议

基于新 shell，导航建议改成：

1. `Source`
2. `Overview`
3. `Inspect`
4. `Settings`

全局状态与动作建议放在：

1. left rail 的主 CTA
2. top app bar 的状态点与小动作

这样页面导航和运行控制是分层的，不会混在同一排里互相抢焦点。

---

## 8. Source 页面设计

## 8.1 目标

把 phase1 已经实现的“多 device preview + 单 source 正式 capture”清楚地呈现出来。

## 8.2 页面结构

```text
+------------------------------------------------------+
| Header: source selection / refresh / import offline  |
+--------------------------+---------------------------+
| Device list              | Preview workbench         |
| - device name            | - packets_seen            |
| - addresses              | - bytes_seen              |
| - status                 | - unsupported / error     |
| - select action          | - capability hints        |
+--------------------------+---------------------------+
| Footer actions: confirm source / offline file import |
+------------------------------------------------------+
```

按 Stitch/Cosmos 风格收敛后，Source 页建议做成“左列表 + 右详情”的运维工作台，而不是卡片拼贴页。

## 8.3 关键交互

1. 进入应用先加载 device list
2. 自动触发短时 preview
3. 用户选择一个 device 作为正式 source
4. 用户也可以切到 offline file import
5. 只有确认 source 后才能进入正式 capture

## 8.4 验收重点

1. 不能误导成“正式同时抓所有 device”
2. preview 与 formal capture 的文案必须严格区分
3. 权限错误与 unsupported 要看得见

---

## 9. Overview 页面设计

## 9.1 目标

让 Overview 成为主监控台，而不是数据列表页。

## 9.2 页面结构

基于 Stitch 样版，Overview 需要从 Sniffnet 的“三列榜单工作台”演进成“hero chart + status cards + map + destination list”的 bento 工作台，同时保留 phase1 的实体语义。

```text
+------------------------------------------------------+
| Hero Chart Panel                                     |
| title / mode switch / inbound-outbound legend        |
+------------------------------------------------------+
| Status Cards Row                                     |
| packets/s | dropped | active connections | source    |
+----------------------------------+-------------------+
| Destination Map                  | Top Destinations  |
| reserved or future map panel     | ranked list       |
+----------------------------------+-------------------+
| Lower Detail Row                                     |
| Top hosts | Top services | Top connections           |
+------------------------------------------------------+
```

这里按评审意见新增一条固定约束：

1. `Destination Map` 是 Overview 的保留区域
2. phase2 MVP 可先只放 placeholder / reserved panel
3. 但整体 layout 必须提前为它留位

同时按 Stitch 样版再加一条：

1. `Destination Map + Top Destinations` 应成为 Overview 的下半区主结构
2. 这不是附属小组件，而是 phase2 新版 Overview 的核心视觉板块

## 9.3 必备组件

1. `HeroTrafficChartPanel`
   - 以 `tick_snapshots` 为基础
   - 置顶，作为第一视觉焦点
2. `StatusCardsRow`
   - packets per second
   - dropped packets
   - active connections
   - current source or local address
3. `DestinationMapPanel`
   - MVP 中允许为 reserved panel
   - 作用是为后续 destination 分布视图保留稳定区域
4. `TopDestinationsPanel`
   - 展示目的地国家、区域或组织的排名列表
5. `TopHostsPanel`
6. `TopServicesPanel`
7. `TopConnectionsPanel`

## 9.4 图表策略

hero chart 只展示聚合后的入站/出站趋势，不展示逐包点。

必须做到：

1. 入站色和出站色稳定
2. Y 轴单位随模式切换
3. 无数据时有空态
4. offline 回放时按 `pcap` 时间轴推进

## 9.5 视觉重点

1. 趋势图是页面视觉中心
2. `Destination Map` 是第二层级视觉锚点，即使先不实现内容也要占位
3. `Top Destinations` 和地图区域要形成成对结构
4. 下层榜单保持紧凑，避免空白感
5. 所有状态卡都要可一眼读数

### 9.6 Flowarden Overview 与 Stitch 样版的映射关系

Stitch 样版中已有这些布局锚点可以直接借用：

1. 顶部大图表 panel
2. 中间四张状态卡
3. 左下大地图 panel
4. 右下 destinations 排名 panel

Flowarden 在此基础上的替换规则是：

1. `Real-time Traffic` 保留为 `Overview` 的 hero chart 区
2. `Packets / Dropped / Active Connections / Local IP` 保留为状态卡排
3. `Global Traffic Distribution` 改名为 `Destination Distribution`
4. `Top Destinations` 保留，但统计口径需以后续 destination 模型为准
5. Sniffnet 原图里的 `Top hosts / Top services / Top connections` 不能丢，放入下层 detail row

也就是说，Flowarden 的新 Overview 不再照抄 Sniffnet 原 overview，而是：

> 用 Stitch/Cosmos 的大盘布局，承载 Sniffnet/Flowarden 已冻结的流量监控实体。

---

## 10. Inspect 页面设计

## 10.1 目标

让用户可以在不进入 phase3 的前提下，对流量连接做明细过滤与检查。

## 10.2 页面结构

参考图的“过滤条 + 大表格”语义仍成立，但 layout 需要靠近 Stitch 的大工作台写法：

```text
+------------------------------------------------------+
| Inspect header                                       |
| title / active filters / quick clear                 |
+------------------------------------------------------+
| Filter bar                                           |
| source/destination/protocol/service/bpf/local flags  |
+------------------------------------------------------+
| Results table                                        |
| src addr | src port | dst addr | dst port | ...      |
+------------------------------------------------------+
| Footer summary                                       |
| result count | current sort | bytes total            |
+------------------------------------------------------+
```

Inspect 页的视觉重点不再是“像 Sniffnet 那样一整块表”，而是：

1. header 明确当前筛选上下文
2. filter bar 保持高可见性
3. 表格本体尽量占大部分垂直空间
4. footer 持续反馈当前结果集规模

## 10.3 过滤策略

第二阶段只做 phase1 已有数据可支撑的过滤：

1. source address
2. destination address
3. service
4. protocol
5. direction

不做：

1. payload 内容过滤
2. session id 过滤
3. 应用进程过滤

## 10.4 表格策略

表格列建议固定为：

1. source address
2. source port
3. destination address
4. destination port
5. protocol
6. service
7. direction
8. bytes
9. packets

说明：

这是 phase2 的 inspect MVP，重点是让 phase1 聚合结果被稳定查看，而不是模拟 Wireshark。

---

## 11. Settings 页面设计

## 11.1 目标

承载最小运行配置与连接信息。

## 11.2 页面内容

1. current source
2. BPF
3. tick interval
4. top N
5. core endpoint / process status
6. core version / UI version
7. 错误日志入口

Settings 必须收敛，不要做成“管理后台式配置中心”。

同时要保持 Cosmos 风格：

1. settings 也走 glass panel 容器
2. 不回退成纯表单页面
3. 配置项按“运行态相关优先”排序

---

## 12. 状态与反馈设计

## 12.1 状态层级

UI 必须清楚表达 4 类状态：

1. core 未连接
2. source 已选但未开始
3. capture 运行中
4. capture 暂停

## 12.2 错误反馈

错误展示必须区分：

1. 权限错误
2. source 不可用
3. core 连接失败
4. 运行中断
5. filter 下发失败

反馈形式建议：

1. 顶部短提示
2. 页面内状态面板
3. Settings 中的错误日志列表

---

## 13. 动效建议

phase2 只保留轻量动效：

1. 页面切换淡入
2. 图表刷新平滑过渡
3. 状态点颜色切换
4. 榜单刷新时轻微高亮

不要做：

1. 复杂粒子背景
2. 大量动画装饰
3. 为了“高级感”牺牲监控可读性

---

## 14. MVP 与后续边界

## 14.1 phase2 MVP 必须有

1. Source
2. Overview
3. Inspect
4. Settings
5. core 状态与错误反馈
6. `Destination Map` reserved panel

说明：

1. 这里要求的是“布局中必须有该区域”
2. 不是要求 phase2 MVP 立刻做真实地图引擎

## 14.2 phase2 可以不做

1. Notifications 独立页面
2. Thumbnail mode
3. 多主题完整系统
4. 程序图标展示
5. 国家旗帜与组织名增强
6. `Destination Map` 的真实地图绘制与交互

这些都可留给 phase2.1 或 phase3。

---

## 15. 可验收 UI 清单

评审阶段建议按下面清单看图和实现方向：

1. 是否仍然保持“监控台”气质，而不是默认企业后台风格
2. 是否清楚区分 preview 与 formal capture
3. Overview 是否以趋势图和榜单为中心
4. Inspect 是否以过滤条和明细表格为中心
5. 是否把 core 状态、错误状态显式化
6. 是否严格依赖 phase1 已有数据，而不是偷渡 phase3 需求
7. 是否真正把 Stitch/Cosmos 的 shell 和版式吸收进来了，而不是只换了配色
8. 是否把 `Destination Map + Top Destinations` 做成 Overview 的稳定结构
9. 是否仍然保留了 `Top hosts / Top services / Top connections` 这些 phase1 关键输出的落点

---

## 16. 页面级线框

下面的线框不是最终像素稿，而是为了把后续 Avalonia 组件树和页面职责定死。

## 16.1 App Shell 线框

```text
+----------------------+--------------------------------------------------+
| Brand / Version      | Workbench Header                                 |
|----------------------| title | mode switch | state dots | tools        |
| Source               +--------------------------------------------------+
| Overview             | active page content                              |
| Inspect              |                                                  |
| Settings             |                                                  |
|                      |                                                  |
| [Start Capture]      |                                                  |
|                      |                                                  |
| Docs                 |                                                  |
| Quit                 |                                                  |
+----------------------+--------------------------------------------------+
```

关键约束：

1. 左 rail 宽度固定，不随页面大幅变化
2. header 高度固定，保证不同页面切换时视觉稳定
3. 主内容区只切工作台，不切 shell

## 16.2 Source 页面线框

```text
+--------------------------------------------------------------------------------+
| Source Header                                                                  |
| source mode | refresh preview | import offline | last preview time             |
+--------------------------------------+-----------------------------------------+
| Device List                           | Preview Workbench                       |
| - device item                         | selected device summary                 |
| - device item                         | packets_seen / bytes_seen               |
| - device item                         | unsupported / permission / error        |
| - device item                         | addresses / capability hints            |
| - device item                         | preview sparkline or compact metrics    |
+--------------------------------------+-----------------------------------------+
| Footer: confirm selected source | start in live | load offline pcap            |
+--------------------------------------------------------------------------------+
```

## 16.3 Overview 页面线框

```text
+--------------------------------------------------------------------------------+
| Hero Traffic Chart                                                             |
| title | metric mode | live/offline badge | legend | current filter summary     |
+--------------------------------------------------------------------------------+
| Stat Card | Stat Card | Stat Card | Stat Card                                  |
| pps       | dropped   | active    | current source / local addr                |
+----------------------------------------------+---------------------------------+
| Destination Map (reserved or future map)     | Top Destinations                |
| large visual panel                            | ranked list                     |
| hotspot placeholders                          | country/region/org entries      |
+----------------------------------------------+---------------------------------+
| Top Hosts                     | Top Services                    | Top Connections|
| ranked rows                   | ranked rows                     | compact table   |
+--------------------------------------------------------------------------------+
```

## 16.4 Inspect 页面线框

```text
+--------------------------------------------------------------------------------+
| Inspect Header                                                                 |
| title | active filter chips | clear all | current result count                 |
+--------------------------------------------------------------------------------+
| Filter Bar                                                                     |
| source | destination | protocol | service | direction | bpf | time mode        |
+--------------------------------------------------------------------------------+
| Results Table                                                                  |
| src addr | src port | dst addr | dst port | protocol | service | bytes | ...   |
| ...                                                                            |
| ...                                                                            |
+--------------------------------------------------------------------------------+
| Footer Summary                                                                 |
| results | bytes total | packets total | current sort | stream freshness        |
+--------------------------------------------------------------------------------+
```

## 16.5 Settings 页面线框

```text
+--------------------------------------------------------------------------------+
| Settings Header                                                                |
| runtime settings | connection | diagnostics                                   |
+--------------------------------------+-----------------------------------------+
| Runtime Panel                         | Core Panel                              |
| current source                        | endpoint / process state                |
| bpf                                   | core version                            |
| tick interval                         | ui version                              |
| top N                                 | reconnect / ping                        |
+--------------------------------------+-----------------------------------------+
| Error / Diagnostic Panel                                                        |
| recent errors | filter apply failures | permission hints | logs entry          |
+--------------------------------------------------------------------------------+
```

---

## 17. 区块职责表

为了避免后面把页面做成“看起来差不多，但职责混乱”，每个主要区块都需要先定职责。

| 区块 | 页面 | 主要职责 | 数据来源 | MVP 状态 |
| --- | --- | --- | --- | --- |
| `AppRail` | 全局 | 主导航、主 CTA、低频入口 | UI state | 必做 |
| `AppHeader` | 全局 | 页面标题、模式切换、core/capture 状态 | UI state + health | 必做 |
| `SourceDeviceList` | Source | 展示全部设备并选择单 source | discovery | 必做 |
| `SourcePreviewWorkbench` | Source | 展示当前选中 device 的 preview 统计和错误状态 | preview stream | 必做 |
| `HeroTrafficChartPanel` | Overview | 展示 live/offline 聚合趋势 | overview snapshot | 必做 |
| `StatusCardsRow` | Overview | 展示关键摘要指标 | overview snapshot | 必做 |
| `DestinationMapPanel` | Overview | 预留 destination 分布可视区域 | reserved | 必做但可 placeholder |
| `TopDestinationsPanel` | Overview | 展示 destination 维度排行 | future destination projection | phase2 预留，MVP 可 placeholder |
| `TopHostsPanel` | Overview | 展示 host 排行 | overview snapshot | 必做 |
| `TopServicesPanel` | Overview | 展示 service 排行 | overview snapshot | 必做 |
| `TopConnectionsPanel` | Overview | 展示 connection 排行 | overview snapshot | 必做 |
| `InspectFilterBar` | Inspect | 下发过滤条件并反馈激活条件 | inspect query state | 必做 |
| `InspectResultsTable` | Inspect | 展示连接明细结果 | inspect projection | 必做 |
| `InspectFooterSummary` | Inspect | 展示结果集摘要和排序状态 | inspect projection | 必做 |
| `SettingsRuntimePanel` | Settings | 展示最小运行配置 | settings state | 必做 |
| `SettingsCorePanel` | Settings | 展示 core 连接与版本信息 | health/version | 必做 |
| `SettingsDiagnosticsPanel` | Settings | 展示近期错误与诊断入口 | error state | 必做 |

说明：

1. `TopDestinationsPanel` 在 phase2 中需要先有版位和占位数据协议方案
2. 如果 destination 统计模型在 phase2 代码侧还没有产出，UI 可先用 placeholder 文案占位
3. `DestinationMapPanel` 与 `TopDestinationsPanel` 是成对规划，不建议只留地图不留排行

---

## 18. Avalonia 视图树建议

下面给的是建议视图树，不是必须逐文件照抄，但分层要保持。

## 18.1 Shell 层

```text
App
  MainWindow
    AppShellView
      AppRailView
      AppHeaderView
      ContentHost
```

## 18.2 页面层

```text
ContentHost
  SourcePageView
  OverviewPageView
  InspectPageView
  SettingsPageView
```

说明：

1. 四个页面是同级工作区
2. 切换的是 page view，不是整个 window

## 18.3 Overview 视图树建议

```text
OverviewPageView
  HeroTrafficChartView
  StatusCardsRowView
  DestinationWorkbenchView
    DestinationMapView
    TopDestinationsView
  LowerDetailRowView
    TopHostsView
    TopServicesView
    TopConnectionsView
```

## 18.4 Inspect 视图树建议

```text
InspectPageView
  InspectHeaderView
  InspectFilterBarView
  InspectResultsTableView
  InspectFooterSummaryView
```

## 18.5 Source 视图树建议

```text
SourcePageView
  SourceHeaderView
  SourceDeviceListView
  SourcePreviewWorkbenchView
  SourceFooterActionsView
```

## 18.6 Settings 视图树建议

```text
SettingsPageView
  SettingsHeaderView
  SettingsRuntimePanelView
  SettingsCorePanelView
  SettingsDiagnosticsPanelView
```

---

## 19. ViewModel 划分建议

建议维持“页面级 + 区块级”两层，不要一开始就过度细碎。

```text
AppShellViewModel
  SourcePageViewModel
  OverviewPageViewModel
  InspectPageViewModel
  SettingsPageViewModel
```

区块级 ViewModel 建议只在下列场景再拆：

1. 图表区需要独立刷新节流
2. 过滤条需要独立输入状态
3. 结果表格需要独立排序/分页状态
4. destination workbench 后续要独立演进

---

## 20. 实现顺序建议

如果按 UI 设计落地顺序推进，建议按下面顺序做，而不是页面并行乱起：

1. `AppShellView`
2. `SourcePageView`
3. `OverviewPageView`
4. `InspectPageView`
5. `SettingsPageView`

原因：

1. `Source` 决定 session 入口
2. `Overview` 是主价值页
3. `Inspect` 依赖稳定过滤和表格骨架
4. `Settings` 最后补不会阻塞核心交互

---

## 21. 建议结论

第二阶段 UI 不应做成“Rust CLI 外面套一层普通表格壳”，而应明确做成：

> 一个基于 `Cosmos Network System` shell、强状态反馈、以 `Overview hero chart + destination workbench + inspect grid` 为核心工作面的 Avalonia 桌面前端。

这个方向既承接 Sniffnet 的优点，也更适合后续第三阶段继续接 payload 与 session 详情。
