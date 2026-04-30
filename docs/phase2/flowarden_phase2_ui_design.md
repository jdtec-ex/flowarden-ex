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

## 4.1 默认主题建议

默认主题建议命名为 `Signal Amber`。

颜色角色：

1. `ShellBand`
   - 顶部/底部色带
   - 暖金黄到琥珀橙
2. `SurfaceBase`
   - 主背景
   - 深蓝黑
3. `SurfacePanel`
   - 卡片与列表容器
   - 稍亮的深蓝
4. `AccentPrimary`
   - 高亮边框、主按钮、选中项
   - 金黄
5. `TrafficInbound`
   - 入站流量
   - 琥珀黄
6. `TrafficOutbound`
   - 出站流量
   - 青蓝
7. `Danger`
   - 错误与异常
   - 红橙

建议的第一版变量：

```text
--shell-band-start: #f0b12a
--shell-band-end:   #f29a1f
--surface-base:     #16213b
--surface-panel:    #1e2d52
--surface-panel-2:  #24345d
--accent-primary:   #ffc43b
--traffic-in:       #ffc43b
--traffic-out:      #46b2ff
--text-primary:     #f4f7ff
--text-muted:       #aeb7cd
--danger:           #ff6c54
```

## 4.2 备用主题建议

参考 `deep_cosmos.png`，建议在 phase2 只预留一个备用主题：

- `Deep Cosmos`

但不要在 MVP 中做复杂主题系统，只要：

1. 默认主题可用
2. 备用主题变量可切换

即可。

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

---

## 7. 整体布局

## 7.1 主窗口布局

建议采用如下结构：

```text
+------------------------------------------------------+
| Top Shell Band                                       |
|  nav / session state / core state / quick actions    |
+------------------------------------------------------+
| Left rail (optional compact) | Main content          |
|                              |                       |
|                              |                       |
+------------------------------------------------------+
| Bottom status band                                   |
|  capture source / mode / errors / throughput hints   |
+------------------------------------------------------+
```

说明：

1. 顶部使用强色带，继承参考图的识别度
2. 主内容区保持深色面板系统
3. 底部状态带承载运行态，而不是装饰

## 7.2 导航建议

顶部导航建议包含：

1. `Source`
2. `Overview`
3. `Inspect`
4. core 状态点
5. capture 状态点
6. quick action：
   - start
   - stop
   - pause / resume

---

## 8. Source 页面设计

## 8.1 目标

把 phase1 已经实现的“多 device preview + 单 source 正式 capture”清楚地呈现出来。

## 8.2 页面结构

```text
+------------------------------------------------------+
| Header: source selection / refresh / preview window  |
+--------------------------+---------------------------+
| Device list              | Device preview details    |
| - device name            | - packets_seen            |
| - addresses              | - bytes_seen              |
| - status                 | - unsupported / error     |
| - select action          | - capability hints        |
+--------------------------+---------------------------+
| Footer actions: confirm source / offline file import |
+------------------------------------------------------+
```

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

建议沿用参考图的核心构成，但做更清楚的布局：

```text
+------------------------------------------------------+
| Summary strip                                        |
| source | mode | filter | dropped | last packet time  |
+-----------------------------+------------------------+
| Totals card                 | Traffic rate chart     |
| inbound/outbound/dropped    | inbound/outbound trend |
+-----------------------------+------------------------+
| Top hosts                   | Top services           |
+-----------------------------+------------------------+
| Top connections                                     |
+------------------------------------------------------+
```

## 9.3 必备组件

1. `TotalsCard`
   - incoming
   - outgoing
   - dropped
   - total packets
2. `TrafficRateChart`
   - 以 `tick_snapshots` 为基础
3. `TopHostsPanel`
4. `TopServicesPanel`
5. `TopConnectionsPanel`

## 9.4 图表策略

图表只展示聚合后的入站/出站趋势，不展示逐包点。

必须做到：

1. 入站色和出站色稳定
2. Y 轴单位随模式切换
3. 无数据时有空态
4. offline 回放时按 `pcap` 时间轴推进

## 9.5 视觉重点

1. 趋势图是页面视觉中心
2. 榜单保持紧凑，避免空白感
3. Summary strip 比 phase1 CLI 输出更易读，但口径不能变

---

## 10. Inspect 页面设计

## 10.1 目标

让用户可以在不进入 phase3 的前提下，对流量连接做明细过滤与检查。

## 10.2 页面结构

参考图的“过滤条 + 大表格”是对的，建议保留：

```text
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

## 14.2 phase2 可以不做

1. Notifications 独立页面
2. Thumbnail mode
3. 多主题完整系统
4. 程序图标展示
5. 国家旗帜与组织名增强

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

---

## 16. 建议结论

第二阶段 UI 不应做成“Rust CLI 外面套一层普通表格壳”，而应明确做成：

> 一个基于深色监控台视觉、强状态反馈、以 `Overview + Inspect` 为核心工作面的 Avalonia 桌面前端。

这个方向既承接 Sniffnet 的优点，也更适合后续第三阶段继续接 payload 与 session 详情。
