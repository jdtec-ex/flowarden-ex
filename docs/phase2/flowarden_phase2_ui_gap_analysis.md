# Flowarden Phase2 UI 差距分析与修改方案

## 1. 文档目的

本文用于基于运行结果图，对照 phase2 最初 UI 设计要求，明确当前差距与后续修改方案。

本次检查的输入来源：

1. `../ui-images/result_image.md`
2. `flowarden_phase2_ui_design.md`
3. `../ui-images/stitch_avalonia_ui_refinement/DESIGN.md`

本文只做差距分析和修改方案，不代表本轮已经完成 UI 修正。

## 2. 对照基线

本次差距判断以两类基线为准：

1. 监控信息与页面结构基线
   - `flowarden_phase2_ui_design.md`
2. 视觉风格与层次基线
   - `../ui-images/stitch_avalonia_ui_refinement/DESIGN.md`

重点检查以下要求是否真正落地：

1. 保留 Sniffnet 参考图中的关键监控实体与页面要素
2. 采用 `Cosmos Network System` 风格
3. `Overview` 必须是趋势图 + destination workbench + ranked detail row
4. `Inspect` 必须是 filter-first workbench，而不是普通表格页
5. `Destination Map` 在 MVP 中可不实现，但必须有稳定预留区域
6. UI 不应暴露原始 transport 或 gRPC 错误细节

## 3. 总体结论

当前运行结果已经具备：

1. `Cosmos shell` 基础骨架
2. `Source / Overview / Inspect / Settings` 四页结构
3. `Core / Capture` 顶部状态区
4. `Overview` 曲线图替代直方图

但与最初要求相比，仍存在四类核心差距：

1. 运行态状态表达不正确
2. 信息架构偏松，监控密度不足
3. `Overview / Inspect` 工作台表达不足
4. Cosmos 风格只落到配色，没有完整落到层次、质感和数据可视化语言

因此，当前 UI 不能视为“只差局部润色”，而应视为“骨架已成立，但仍需要按原要求收口”。

## 4. 总体差距

### 4.1 运行态表达差距

当前 UI 中最明显的问题，不是控件数量，而是运行态语义暴露方式不对。

具体表现：

1. `Source` 页直接显示原始错误文本
   - 例如 `Status(StatusCode="Unimplemented", Detail=...)`
2. UI 还没有形成统一的用户态状态模型
   - `loading`
   - `ready`
   - `failed`
   - `unavailable`
3. 错误信息仍然过于接近 transport 层

这与 phase2 设计目标不一致。UI 应表达“监控状态”，而不是泄露底层 gRPC 返回细节。

### 4.2 信息架构差距

当前各页普遍存在“页面标题重复、内容承载不足”的问题。

具体表现：

1. 外层页头和内层内容区重复显示同名标题
2. 页面有效工作区被重复标题与留白占用
3. 核心监控信息没有被放到最显眼的位置

这导致页面看起来“有设计”，但监控工作台的密度不够。

### 4.3 工作台表达差距

最初 phase2 的核心要求，不是做四个页面，而是做出“可工作的流量监控台”。

当前主要差距：

1. `Source` 更像设备详情页，不像 source selection workbench
2. `Overview` 更像大图卡片 + 统计卡，不像趋势图 + 榜单工作台
3. `Inspect` 更像普通结果表，不像 filter-first workbench
4. `Settings` 更像静态信息页，不像运行诊断页

### 4.4 视觉层次差距

当前视觉基线已接近 `Cosmos Network System` 的颜色方向，但仍未形成足够的质感与层次。

主要问题：

1. 卡面偏平
2. 缺少更明确的边缘高光和玻璃层次
3. 数据图表没有完整使用 glow-path 语言
4. 榜单、表格、状态块之间层级差异不够明显

## 5. 分页差距

### 5.1 Source 页

当前状态：

1. 已有双栏骨架
2. 已有 device list
3. 已有 preview workbench
4. 已有 formal capture boundary

主要差距：

1. 原始错误文本直接进入 UI
2. `Preview` 状态语义不完整
3. 左侧设备卡片没有形成足够明确的状态层次
   - selected
   - ready
   - preview failed
   - unavailable
4. 右侧 preview workbench 缺少：
   - preview 更新时间
   - preview 状态摘要
   - formal capture readiness 的稳定表达
5. 页面整体更像“列表 + 表单”，不像“source selection workbench”

### 5.2 Overview 页

当前状态：

1. 已有曲线图
2. 已有尺度标记
3. 已有状态卡
4. 已有 `Top Destinations`
5. 已有 `Destination Map` 预留位

主要差距：

1. 缺少更明确的 `CaptureSource`
2. 缺少更明确的 `FilterState`
3. 缺少 `MetricMode` 切换
4. 曲线图目前更像单一展示图，而不是监控趋势图
   - 缺少入站/出站双序列表达
   - 缺少 legend
5. `Destination Map` 预留区域存在感过弱
6. 榜单区层级不足，没有形成强工作台结构
7. 整页仍偏“大图卡片”，没有完全达到最初定义的：
   - trend chart
   - destination workbench
   - ranked detail row

### 5.3 Inspect 页

当前状态：

1. 已有 filter bar
2. 已有 results table
3. 已有 footer summary

主要差距：

1. 缺少 active filter chips
2. `No active filters` 与右侧结果计数存在，但过滤状态可视链不完整
3. 表格层级偏弱
   - 表头不够突出
   - 排序状态不可见
   - 列宽分配需收敛
4. 页面更像“表格页”，而不是“filter-first workbench”
5. 过滤器、结果表、底部汇总之间还没有形成足够强的结构关系

### 5.4 Settings 页

当前状态：

1. 已有 runtime 区
2. 已有 core 区
3. 已有 diagnostics 区

主要差距：

1. `Core version` 仍显示 `unknown`
2. `Diagnostics` 表达过轻
3. 缺少按优先级分级的状态呈现
   - normal
   - warning
   - degraded
   - error
4. 页面仍更像静态说明，而不是真正的运行诊断面板

## 6. 修改原则

后续修改应严格遵守以下原则：

1. 先修正确性，再修视觉
2. 不扩展 phase3 数据
3. 不新增通知中心、程序榜单、真实地图等后置能力
4. 不把 transport 层错误直接渲染给用户
5. 所有页面都要优先体现“监控语义”，而不是“通用仪表盘语义”

## 7. 修改方案

### 7.1 第一层：先修运行态正确性

目标：先把“运行状态表达正确”做对。

修改点：

1. `Source` 页移除原始 `Unimplemented` / gRPC 状态文本直出
2. 增加统一状态组件：
   - `Preview loading`
   - `Preview ready`
   - `Preview unavailable`
   - `Preview failed`
3. 全局 header 增加最小运行摘要
   - current source
   - current filter state
4. `Settings` 页补真实 `Core version`
5. 错误信息统一收敛到用户态表达

### 7.2 第二层：重做信息架构

目标：把页面从“有骨架”收口成“可工作的监控台”。

修改点：

1. 去掉外层页头与内层内容区的重复标题
2. `Source` 页强化为：
   - 左侧 device cards
   - 右侧 preview summary + selected source details + formal capture boundary
3. `Overview` 页强化为：
   - 顶部 source/filter/mode 摘要
   - 中部双序列趋势图
   - destination workbench
   - 下层 ranked detail row
4. `Inspect` 页强化为：
   - filter bar
   - active filter chips
   - table
   - footer summary
5. `Settings` 页强化为：
   - runtime
   - core
   - diagnostics
   三块按优先级分层

### 7.3 第三层：补 Cosmos 质感

目标：让视觉结果真正接近 `Cosmos Network System`，而不是只有配色接近。

修改点：

1. 卡面补更细的边缘高光和玻璃层次
2. hero chart 使用更完整的 glow-path 风格
3. 状态点、badge、chips 的语义区分更明显
4. 榜单和表格的行层级更清晰
5. 控制留白，提升信息密度，但不牺牲可读性

## 8. 建议执行顺序

建议按以下顺序实施：

1. `Source`
   - 原因：当前存在最明显的错误直出问题，且最能先修正运行态语义
2. `Overview`
   - 原因：它最能体现 phase2 是否真正达到最初目标
3. `Inspect`
   - 原因：当前功能链已基本存在，主要差距在结构与表达
4. `Settings`
   - 原因：当前骨架存在，主要差距在真实性与诊断表达

## 9. 本轮评审结论

本轮不建议直接进入零散样式修补。

建议先按本文的差距分类和执行顺序进行收口：

1. 先修运行态正确性
2. 再收口页面信息架构
3. 最后补 Cosmos 视觉层次

这样可以保证后续修改仍然对齐最初 phase2 需求，而不是继续在局部视觉上来回调整。
