# Sniffnet 逆向分析

## 1. 分析目标

本文针对 `/Users/wangli/workspace/practise/rs-workship/sniffnet` 做一次面向实现的逆向阅读分析，重点回答三个问题：

1. Sniffnet 的核心是什么
2. 它抓包、解析、聚合、展示的原理是什么
3. 它的代码架构如何分层、模块如何协作

这不是功能说明书，而是从源码实现角度总结其运行机制。

---

## 2. 项目定位与技术栈

Sniffnet 是一个 Rust 编写的跨平台桌面网络流量监控器，目标不是做深度协议分析器，而是做“可视化、可交互、可持续运行”的网络流量观察工具。

从 `Cargo.toml` 可以看出它的关键技术栈：

- GUI: `iced`
- 抓包: `pcap`
- 包头解析: `etherparse`
- IP 地理/ASN: `maxminddb`
- 反向 DNS: `dns-lookup`
- 进程识别: `listeners`
- 程序图标: `picon`
- 图表: `plotters` + `plotters-iced2`
- 异步/任务: `tokio`、`async-channel`
- 配置持久化: `confy`

这说明它的总体设计不是命令行抓包器，而是一个“GUI 驱动的事件系统 + 后台抓包流水线”。

---

## 3. 核心结论

### 3.1 核心对象

Sniffnet 的真正核心不是某个单独模块，而是以下 4 个部分共同组成的运行时闭环：

1. `Sniffer`
2. `CaptureContext` / `CaptureSource`
3. `parse_packets`
4. `InfoTraffic`

可以把它理解成：

- `Sniffer` 是总控状态机
- `CaptureContext` 是抓包输入抽象
- `parse_packets` 是后台分析引擎
- `InfoTraffic` 是流量聚合结果模型

### 3.2 核心思想

Sniffnet 的核心思想不是“逐包直接驱动 UI”，而是：

- 后台线程持续抓包
- 解析后先汇总到时间片
- 按秒把增量结果推送给 UI
- UI 再把增量合并到全局状态

这是一种典型的“采集线程与展示线程解耦”的设计，优点是：

- UI 不会被高频包事件直接压垮
- 图表天然按时间片更新
- 通知、统计、搜索都能复用同一份聚合数据

### 3.3 项目本质

从实现上看，Sniffnet 本质上是一个：

`实时抓包器 + 周期聚合器 + 富状态 GUI 外壳`

而不是 Wireshark 那种“逐包深度解码器”。

---

## 4. 启动链路

入口在 `src/main.rs`。

主流程如下：

1. 加载持久化配置 `CONF`
2. 解析 CLI 参数 `cli::Args::handle()`
3. 构建 `iced::application`
4. 以 `Sniffer::new(conf)` 初始化应用状态
5. 通过 `args.get_boot_task_chain()` 注入启动任务链

关键点：

- GUI 运行时完全由 `iced` 驱动
- `Sniffer::update` 是统一消息分发入口
- `Sniffer::view` 负责根据状态渲染页面
- `Sniffer::subscription` 注册键盘、窗口、定时器等事件源

如果命令行带 `--adapter`，启动后会自动：

1. 切换捕获源到设备
2. 选择对应网卡
3. 自动发送 `Start`

这说明 Sniffnet 的 GUI 启动和自动化启动共享同一套消息机制，没有单独的“无界面抓包模式”。

---

## 5. 运行总流程

可以把核心运行链路概括为：

```text
main
  -> Sniffer::new
  -> StartApp
  -> 启动网卡流量预览
  -> 用户点击 Start
  -> Sniffer::start
  -> CaptureContext::new
  -> 后台线程 parse_packets
  -> parse_packets 按秒发送 BackendTrafficMessage
  -> Task::run 转成 GUI Message
  -> Sniffer::tick_run / refresh_data
  -> Overview / Inspect / Notifications 页面消费结果
```

对应的源码职责：

- 启动入口: `src/main.rs`
- CLI 链路: `src/cli/mod.rs`
- GUI 状态机: `src/gui/sniffer.rs`
- 抓包上下文: `src/networking/types/capture_context.rs`
- 后台解析: `src/networking/parse_packets.rs`
- 包处理辅助: `src/networking/manage_packets.rs`
- 聚合模型: `src/networking/types/info_traffic.rs`

---

## 6. 核心原理

## 6.1 抓包输入抽象

Sniffnet 用 `CaptureSource` 把输入统一成两类：

- `Device(MyDevice)`: 实时抓网卡
- `File(MyPcapImport)`: 读取离线 PCAP

再通过 `CaptureContext` 把执行态统一成：

- `Live`
- `LiveWithSavefile`
- `Offline`
- `Error`

这层抽象很关键，因为后续的 `parse_packets` 并不关心来源是实时网卡还是文件，只需要消费 `CaptureType` 即可。

### 设计价值

- 统一实时和离线分析路径
- 可在实时抓包时同时导出 PCAP
- BPF 过滤在上下文创建阶段统一生效

### 实现细节

`CaptureType::from_source` 中的 live capture 配置：

- `promisc(false)`: 默认非混杂模式
- `buffer_size(2_000_000)`: 2MB 缓冲
- `snaplen(200)` 或 `u16::MAX`
- `timeout(150)`: 保证 UI 即使没包也能周期更新

这里有一个很明显的工程取舍：

- 不导出 PCAP 时只抓包前 200 字节
- 导出 PCAP 时抓完整包

这说明作者优先考虑实时监控的吞吐和缓冲利用率，而不是默认保留完整 payload。

---

## 6.2 数据链路层到传输层的解析原理

抓到原始包后，Sniffnet 并不做全协议树解析，而是使用 `etherparse::LaxPacketHeaders` 提取轻量包头信息。

入口函数是 `parse_packets::get_sniffable_headers`，根据链路类型选择不同解析方式：

- Ethernet
- Raw IP / IPv4 / IPv6
- Linux cooked capture `LinuxSll` / `LinuxSll2`
- `Null` / `Loop`

然后由 `manage_packets::analyze_headers` 分三层解析：

1. `analyze_link_header`
2. `analyze_network_header`
3. `analyze_transport_header`

最终得到一个 `AddressPortPair`，其包含：

- 源 IP
- 目标 IP
- 源端口
- 目标端口
- 协议类型

同时还得到：

- MAC 地址
- 字节数
- ICMP 类型
- ARP 类型

### 关键特征

Sniffnet 关注的是“流量观察所需的最小关键信息”，不是完整协议还原：

- 谁和谁通信
- 通信方向
- 用了什么协议/服务
- 传输了多少数据
- 目标主机是谁

这正好服务于 GUI 统计与监控目标。

---

## 6.3 流量方向判定原理

方向判定在 `manage_packets::get_traffic_direction`。

它的判定策略不是简单“源端为本机就是出站”，而是结合多种情况：

- loopback TCP/UDP 特判
- 依据当前网卡地址判断本地 IP
- 离线 PCAP 无本机地址时回退到 bogon 逻辑
- 处理 `0.0.0.0` / `::` 这类未分配源地址情况

因此它能在以下场景保持可用：

- 实时网卡抓包
- 离线 PCAP 导入
- 本地回环通信
- DHCP 等特殊地址阶段

### 为什么这很重要

Sniffnet 的很多后续逻辑都依赖“方向”：

- 入站/出站统计
- 图表正负轴展示
- 远端地址选择
- 服务识别倾向
- 本地端口到进程映射

方向一旦错，后面大量展示都会偏。

---

## 6.4 服务识别原理

服务识别在 `manage_packets::get_service`。

服务表不是运行时动态加载，而是构建期通过 `build.rs` 读取 `services.txt` 生成静态 `phf::Map<ServiceQuery, Service>`。

### 识别逻辑

对于 TCP/UDP：

- 同时检查源端口和目标端口
- 根据端口是否是 well-known port 加权
- 根据流量方向给远端端口额外加权
- 返回得分更高的那个服务

这不是“端口号直接映射服务”的死板逻辑，而是一个简单的启发式选择器。

### 设计收益

- 查询速度极快，适合高频抓包路径
- 静态生成避免运行时解析大表
- 方向感知能减少把本地临时端口误判为服务端口

---

## 6.5 按秒聚合原理

`parse_packets` 是整个后台分析引擎的核心。

它没有把每个包直接发给 UI，而是先在本地维护一个 `info_traffic_msg: InfoTraffic`，然后按时间片发送。

### 实时抓包

实时模式下通过 `maybe_send_tick_run_live`：

- 以 `Instant` 计算 1 秒节拍
- 每秒发送一次 `BackendTrafficMessage::TickRun`
- 发送后调用 `take_but_leave_something()`

`take_but_leave_something()` 会把本秒聚合结果取走，同时保留：

- `last_packet_timestamp`
- `dropped_packets`

因此下一秒聚合可以从“干净但不断裂”的状态继续。

### 离线 PCAP

离线模式下不是看真实 wall clock，而是看 PCAP 包时间戳：

- 如果下一包时间戳跨秒，则发送一个 `TickRun`
- 如果中间有空洞，则再发 `OfflineGap`

这样图表可以近似复原离线抓包的时间轴，而不是以文件读取速度播放。

### 这是项目里最重要的设计之一

因为它统一了：

- 实时图表
- 汇总统计
- 通知判定
- 搜索/报表数据源

所有这些都建立在“按秒聚合后的增量结果”上。

---

## 6.6 主机识别原理

主机识别不是同步做的，而是异步拆成独立线程。

在 `parse_packets` 中：

1. 根据流量方向选出需要识别的远端 IP
2. 若此前未解析过，则加入等待表
3. 发送到 rDNS 线程
4. 后台线程执行：
   - `lookup_addr` 做反向 DNS
   - `get_country` 做国家查询
   - `get_asn` 做 ASN 查询
5. 结果以 `HostMessage` 回送

`AddressesResolutionState` 维护两张表：

- `addresses_waiting_resolution`
- `addresses_resolved`

这个设计避免了两个问题：

- 同一 IP 被重复发起 rDNS 查询
- 主线程被网络查询阻塞

### MMDB 原理

MMDB 由 `MmdbReader` 封装：

- 优先使用用户自定义 MMDB
- 失败则回退到内置数据库
- 仍失败则进入 `Empty`

这种设计保证了“功能可选增强，而不是功能硬依赖”。

---

## 6.7 程序识别原理

Sniffnet 支持把连接映射到本机程序，这部分实现比普通流量工具更接近操作系统层。

原理在 `program_lookup.rs`：

1. 从连接键推导“本地端口 + 协议”
2. 交给 `listeners::get_process_by_port`
3. 返回进程信息后映射为 `Program`
4. 再异步获取应用图标 `picon`

### 关键点

- 只在 live capture 模式启用
- 查不到会做有限重试
- 已识别结果有有效期缓存
- 识别成功后会回填近期未分配程序的连接

这说明程序识别是“最终一致”的，而不是“包到即得”：

- 先把连接记成 `Unknown`
- 查到进程后再回写

这种策略比同步查进程更适合实时 GUI。

---

## 6.8 暂停/恢复原理

Sniffnet 的暂停不是直接停止线程，而是通过广播信号控制两个消费环节：

- `parse_packets` 主解析循环
- `packet_stream` 抓包流循环

更有意思的是 live capture 的 `pause()` 实现：

- 暂停时将 BPF 改成 `"less 2"`

也就是通过一个几乎不可能命中的过滤条件，把抓包“软暂停”。

恢复时再重新应用用户原有 BPF。

这是一个很实用的工程技巧：

- 不必销毁抓包句柄
- 不必重建整个上下文
- 跨平台行为更容易统一

---

## 6.9 页面与通知原理

### 图表

`TrafficChart` 维护四条序列：

- 入站字节
- 出站字节
- 入站包数
- 出站包数

其中出站数据用负值表示，这样图表可以围绕 0 轴上下展开，视觉上天然区分收发方向。

### 搜索/报表

`report/get_report_entries.rs` 并没有单独维护搜索索引，而是：

- 直接从 `info_traffic.map` 过滤
- 结合 `addresses_resolved`
- 按排序规则排序
- 再分页

这说明 Inspect 页面是“视图层查询”，而不是额外的数据仓库。

### 通知

通知由 `notify_and_log` 基于每个时间片的 `InfoTraffic` 增量判断：

- 流量阈值通知
- 收藏项流量通知
- IP 黑名单通知
- 可选远程 webhook 推送

也就是说通知不是看全局累计，而是看“本秒是否发生符合条件的流量事件”。

---

## 7. 核心数据结构

## 7.1 `Sniffer`

`Sniffer` 是整个 GUI 应用状态根。

它同时持有：

- 配置 `Conf`
- 当前抓包通道 `current_capture_rx`
- 预览通道 `preview_captures_rx`
- 全局流量状态 `info_traffic`
- 地址解析缓存 `addresses_resolved`
- 通知日志 `logged_notifications`
- 页面状态 `running_page` / `settings_page` / `modal`
- 图表状态 `traffic_chart`
- 搜索、分页、缩略图、冻结等 UI 状态

这说明 Sniffnet 采用的是“单根状态树 + Message 驱动更新”的 GUI 架构。

## 7.2 `InfoTraffic`

`InfoTraffic` 是最核心的领域聚合对象，包含：

- `tot_data_info`: 总体数据量
- `dropped_packets`: 丢包数
- `last_packet_timestamp`
- `map`: 每条连接的统计
- `services`: 每类服务的统计
- `hosts`: 每个主机的统计

这里的 `map` 键是 `AddressPortPair`，值是 `InfoAddressPortPair`。

也就是说 Sniffnet 的最细粒度统计单位不是“单包”，而是“连接五元组近似体”。

## 7.3 `AddressPortPair`

它描述一条连接的基本键：

- source IP
- dest IP
- source port
- dest port
- protocol

Sniffnet 的大多数连接级统计、搜索、详情弹窗、程序映射都围绕这个键展开。

## 7.4 `Host`

`Host` 并不是单纯 IP，而是高层主机语义：

- domain
- asn
- country

IP 到 `Host` 的映射保存在 `addresses_resolved` 中。

这使得 Overview 页面能按“主机实体”而不是原始 IP 展示。

---

## 8. 架构分层

## 8.1 模块分层

从 `src` 目录看，项目大致可分为 8 层：

### 1. 启动与外壳层

- `main.rs`
- `cli/`

职责：

- 程序入口
- CLI 参数
- 应用初始化

### 2. GUI 状态与页面层

- `gui/sniffer.rs`
- `gui/pages/`
- `gui/components/`
- `gui/styles/`
- `gui/types/`

职责：

- 状态机
- 事件处理
- 页面渲染
- 交互组件

### 3. 抓包与解析层

- `networking/parse_packets.rs`
- `networking/manage_packets.rs`
- `networking/traffic_preview.rs`
- `networking/types/`

职责：

- 实时/离线抓包
- 解析链路/网络/传输层
- 连接聚合
- 网卡预览

### 4. 数据增强层

- `mmdb/`
- `countries/`
- `networking/types/program_lookup.rs`

职责：

- 国家/ASN 查询
- 主机可视化信息
- 程序识别与图标

### 5. 可视化层

- `chart/`

职责：

- 实时流量曲线
- 预览图
- 图表样式与数据点维护

### 6. 告警层

- `notifications/`

职责：

- 告警规则
- 声音播放
- 日志记录
- 远程通知

### 7. 查询与报表层

- `report/`

职责：

- 搜索过滤
- 排序分页
- 检视页面结果生成

### 8. 横切支持层

- `translations/`
- `utils/`

职责：

- 国际化
- 字符串格式化
- 错误日志
- 更新检查

---

## 8.2 线程模型

Sniffnet 不是纯 async 模型，而是“GUI 主线程 + 多个后台工作线程”的混合架构。

主要线程如下：

1. GUI 主线程
2. `thread_parse_packets`
3. `thread_packet_stream`
4. `thread_reverse_dns_lookups`
5. `thread_lookup_program`
6. `thread_get_picon`
7. `thread_traffic_preview`
8. 若干 `thread_traffic_preview_<device>`

### 线程职责

- GUI 主线程只处理状态更新和渲染
- `packet_stream` 只从 pcap 读取数据
- `parse_packets` 负责解析和聚合
- rDNS 线程负责主机增强信息
- 程序/图标线程负责进程归属和视觉补充

### 设计优点

- 避免 UI 被阻塞
- 将高延迟操作拆离抓包热路径
- 抓包与增强解耦

---

## 8.3 消息流架构

Sniffnet 的整体通信模型可以概括为两层消息：

### 第一层：后台到 GUI

- `BackendTrafficMessage`
  - `TickRun`
  - `PendingHosts`
  - `OfflineGap`

### 第二层：GUI 内部消息

- `Message`

`Task::run(rx, ...)` 把后台通道消息桥接成 `iced` 的 UI 消息。

这使得后台线程不需要知道任何 GUI 实现，只需发领域消息。

---

## 9. 关键设计特点

## 9.1 统一实时与离线模式

Sniffnet 最值得注意的架构点之一，是尽量用一套通路同时支持：

- 实时抓网卡
- 导入 PCAP 文件

差异主要被限制在：

- `CaptureSource`
- Tick 产生方式
- 程序识别是否启用

其余统计、通知、搜索、图表基本复用。

## 9.2 聚合优先而不是逐包展示

它天然更适合：

- 长时间运行
- 低 UI 压力
- 统计/监控类可视化

但也带来一个限制：

- 它不是精确的逐包 inspection 工具
- payload 级分析能力有限

## 9.3 主机与程序信息是异步补全

这让数据展示具备“先有粗信息，再逐步补全”的特性：

- 先看到连接和流量
- 再看到域名、国家、ASN
- 再看到进程与图标

这是一种很适合桌面监控产品的用户体验模型。

## 9.4 预览模式是独立子系统

初始页面的网卡预览并不是复用主抓包线程，而是自己起一套轻量预览抓包线程。

这意味着：

- 未开始正式抓包前，用户仍可看到各设备流量热度
- 正式开始抓包时再切换到主分析链路

这是产品体验上的一个亮点。

---

## 10. 可能的局限与取舍

## 10.1 默认 `promisc(false)`

默认非混杂模式意味着它更偏“观察本机相关通信”，而不是充当完整网络嗅探器。

## 10.2 默认 `snaplen(200)`

实时模式下默认只抓包前 200 字节，说明它目标是统计与识别，不是完整 payload 分析。

## 10.3 连接键粒度有限

核心键是 IP/端口/协议组合，因此更像“连接视图”，不强调 TCP 会话阶段、重传、分片重组等深层协议状态。

## 10.4 程序识别依赖本地端口映射

这种方法对桌面场景足够实用，但本质上是启发式，不保证在所有平台和瞬时状态下 100% 精确。

## 10.5 缺少 payload 全量解析能力

这是 Sniffnet 当前最明显的能力边界之一。

现有实现里，实时抓包默认 `snaplen(200)`，而且解析重点集中在：

- 链路层头部
- IP 层头部
- TCP/UDP/ICMP/ARP 基本头部
- 端口到服务的轻量映射

因此它更擅长回答：

- 谁在通信
- 流量多大
- 协议和服务大致是什么

但不擅长回答：

- HTTP 请求方法、URL、Header、Body 是什么
- TLS ClientHello / SNI / ALPN 的完整细节是什么
- DNS Query / Response 的具体内容是什么
- 应用层自定义协议字段和业务负载是什么

换句话说，Sniffnet 当前主要停留在：

- L2/L3/L4 轻量解析
- 少量基于端口号的 L7 推断

而没有真正进入“应用层 payload 深度解析”。

### 这会带来的限制

- 无法做协议内容级审计
- 无法做应用层威胁检测
- 无法从真实 payload 中提取更准确的服务和业务语义
- 无法支持像 HTTP/DNS/TLS 详情面板那样的深度展示

如果要补齐这部分能力，至少需要：

- 提供完整抓包模式，而不是默认截断到 200 字节
- 在 TCP/UDP 之上增加应用层协议识别器
- 为 HTTP、TLS、DNS 等常见协议做 payload 解码
- 设计 payload 存储、裁剪、脱敏和展示策略

## 10.6 缺少通信会话级解析能力

Sniffnet 当前的数据核心是 `AddressPortPair -> InfoAddressPortPair`，本质上更接近“连接键聚合”，而不是“会话状态机”。

这意味着它能统计一条连接组合上传输了多少数据，但它并不真正建模以下内容：

- TCP 三次握手和四次挥手阶段
- 会话建立、活跃、半关闭、关闭等状态迁移
- 重传、乱序、分片、重组
- request/response 配对
- 单个会话中的多轮应用层交互

### 当前模型的结果

它更适合：

- 流量监控
- 连接概览
- 主机/服务/程序维度统计

但不适合：

- 还原一条完整 HTTP 会话
- 分析一次 TLS 握手是否成功
- 追踪 DNS 请求和响应是否匹配
- 判断连接异常中断、重传异常、时延异常

### 更准确地说

Sniffnet 目前做的是“连接聚合视图”，不是“会话分析视图”。

如果要补齐这部分能力，需要在现有 `InfoTraffic` 之下新增一层真正的会话模型，例如：

- `Flow`
- `Session`
- `Transaction`

可能的增强方向包括：

- 为 TCP 建立状态机
- 为 UDP 建立超时驱动的伪会话
- 引入流重组和双向关联
- 在会话上挂载应用层解析结果
- 将图表/搜索/详情从“连接键”升级为“连接键 + 会话实例”

## 10.7 当前更偏“监控视图”，不偏“取证视图”

把前两点合起来看，Sniffnet 当前更适合作为：

- 桌面流量监控器
- 网络活动可视化工具
- 主机/服务/程序观察面板

而不是：

- 深度协议分析器
- 完整应用层审计工具
- 取证级会话重建平台

这不是简单的“功能少”，而是架构目标不同。

它当前的核心模型是“秒级聚合 + GUI 展示”，如果要演进到更强的分析器，通常需要新增两层能力：

1. payload 级协议解析层
2. 会话级状态重建层

也就是说，Sniffnet 现在的强项在“宏观观察”，短板在“微观还原”。

---

## 11. 可增强方向

如果以“保留现有 GUI 监控优势”为前提继续增强，建议优先按下面顺序演进：

### 11.1 增加可选的完整抓包模式

- 保留默认轻量模式
- 增加“完整 payload 模式”
- 允许用户按设备、协议、场景切换抓包深度

这样不会破坏当前性能导向设计。

### 11.2 新增应用层协议解析插件层

可优先支持：

- DNS
- HTTP/HTTPS 元信息
- TLS ClientHello / ServerHello
- QUIC 初始握手元信息

这样可以先补齐最有价值的 payload 解析能力。

### 11.3 在连接聚合之下增加会话层

建议不要直接推翻 `InfoTraffic`，而是在它下方增加：

- 原始包
- 流
- 会话
- 会话上的事务

然后让现有 Overview 继续消费聚合结果，让 Inspect/Details 页面逐步转向消费会话结果。

### 11.4 加强异常检测能力

在有了 payload 和会话层后，才能自然支持：

- 握手失败检测
- 重传异常检测
- DNS 异常响应检测
- TLS 指纹/SNI 异常识别
- 简单应用层安全规则

---

## 12. 架构改造方案

上面的增强方向回答的是“要补什么能力”，这一节回答的是“架构上应该怎么改，才能承载这些能力”。

核心判断是：

- 当前 Sniffnet 架构适合秒级聚合监控
- 如果继续直接往 `Sniffer`、`parse_packets`、`InfoTraffic` 上堆功能，复杂度会快速失控
- 想支持 payload 深度解析和会话级重建，必须把“采集、解析、会话、投影、展示”拆层

### 12.1 目标架构

建议把 Sniffnet 从“GUI 驱动的抓包器”演进为“分析引擎 + 多个读模型 + GUI 外壳”。

目标结构可以抽象为：

```text
PacketSource
  -> PacketIngress
  -> L2/L3/L4 Decoder
  -> Flow Tracker
  -> Reassembly / Session Engine
  -> L7 Protocol Parsers
  -> Detection / Notification Engine
  -> Read Model Projections
  -> GUI / Export / Remote API
```

这里最关键的变化是：

- 原来直接从“抓包 -> 聚合 -> GUI”
- 变成“抓包 -> 领域分析 -> 多视图投影 -> GUI”

这样 GUI 不再承担分析主逻辑，只消费投影结果。

### 12.2 建议拆成 4 个主平面

建议把系统明确拆成四个平面。

#### 1. 采集平面 Capture Plane

职责：

- 统一实时网卡和离线 PCAP 输入
- 提供原始包元数据
- 管理抓包模式、BPF、snaplen、buffer、pause/resume

建议抽象：

- `PacketSource`
- `LiveCaptureSource`
- `OfflineCaptureSource`
- `CaptureController`

这层只负责“拿包”，不负责业务分析。

#### 2. 分析平面 Analysis Plane

职责：

- 包头解析
- 流归类
- TCP/UDP 会话维护
- 重组
- L7 协议识别和 payload 解析
- 安全/异常规则判断

建议抽象：

- `PacketDecoder`
- `FlowTracker`
- `SessionTracker`
- `ReassemblyEngine`
- `ProtocolAnalyzer`
- `DetectionEngine`

这层应成为未来的核心引擎。

#### 3. 投影平面 Projection Plane

职责：

- 把分析结果转成不同视图可直接消费的读模型
- 维护总览、检索、通知、图表等专用投影

建议抽象：

- `OverviewProjection`
- `InspectProjection`
- `NotificationProjection`
- `TimelineProjection`
- `ExportProjection`

这层的思想类似 CQRS 里的 read model。

#### 4. 展示平面 Presentation Plane

职责：

- GUI 状态
- 页面路由
- 用户交互
- 配置编辑
- 读模型渲染

建议保留：

- `Sniffer` 作为 UI 根状态

但把它收缩成：

- 页面状态容器
- 后端控制命令发起者
- 投影订阅消费者

而不是继续承担分析聚合职责。

### 12.3 对当前 `Sniffer` 的架构改善

`Sniffer` 目前同时承担了太多角色：

- UI 状态根
- 抓包生命周期管理
- 后台线程编排
- 全局流量数据容器
- 通知触发入口
- 程序识别协调者

这在当前规模还能工作，但一旦引入 payload 和 session，`Sniffer` 会变成过大的 God Object。

建议拆分为：

- `SnifferUiState`
- `CaptureService`
- `AnalysisService`
- `ProjectionFacade`
- `NotificationService`

其中：

- `SnifferUiState` 只保留页面、窗口、交互、筛选、当前选择项
- `CaptureService` 管理抓包源与生命周期
- `AnalysisService` 管理分析流水线
- `ProjectionFacade` 给 UI 提供只读查询接口
- `NotificationService` 从投影或领域事件中产生告警

这样可以把“状态展示”和“领域分析”真正剥离。

### 12.4 对当前 `InfoTraffic` 的架构改善

`InfoTraffic` 当前是一个非常成功的“聚合结果对象”，但它不应该继续膨胀为所有分析能力的承载者。

建议把它定位成：

- 读模型
- 不是底层事实模型

也就是说，未来应该新增更底层的数据层次：

```text
PacketRecord
  -> Flow
  -> Session
  -> Transaction
  -> Projection(InfoTraffic / HostStats / ServiceStats / ...)
```

建议新增的数据模型：

- `PacketRecord`
  - 原始时间戳
  - 链路层/网络层/传输层元数据
  - payload 切片或引用
- `FlowKey`
  - 5 元组标准键
- `FlowState`
  - 双向流量、最近活跃时间、重组缓存
- `Session`
  - 会话状态、起止时间、关闭原因、异常标记
- `Transaction`
  - 例如 DNS query/response、HTTP request/response、TLS handshake

这样 `InfoTraffic` 继续服务现有 Overview 逻辑，但不再是底层唯一真相。

### 12.5 对当前 `parse_packets` 的架构改善

`parse_packets` 现在既负责：

- 收包
- 解析头部
- 聚合统计
- 主机识别调度
- Tick 发送

这对轻量模式是高效的，但对深度分析不够可扩展。

建议拆成分段流水线：

1. `packet_ingress`
2. `packet_decode`
3. `flow_classify`
4. `session_update`
5. `protocol_parse`
6. `event_emit`
7. `projection_update`

每一段输出明确的领域事件，例如：

- `PacketDecoded`
- `FlowUpdated`
- `SessionOpened`
- `SessionClosed`
- `TransactionObserved`
- `ProtocolMetadataExtracted`
- `AnomalyDetected`

然后再由投影层把这些事件折叠成当前 UI 需要的结果。

这种方式的好处是：

- 更容易加协议解析器
- 更容易做离线回放
- 更容易做测试
- 更容易给后续 CLI / API / 导出功能复用

### 12.6 建议引入统一事件模型

当前后台到前台主要传的是聚合对象 `BackendTrafficMessage`。

未来建议区分两类消息：

#### 领域事件 Domain Events

面向分析引擎内部：

- `PacketDecoded`
- `FlowCreated`
- `FlowUpdated`
- `SessionStateChanged`
- `TransactionCompleted`
- `PayloadDecoded`

#### UI 投影消息 Projection Messages

面向 GUI：

- `OverviewSnapshot`
- `InspectDelta`
- `NotificationDelta`
- `TimelineDelta`

这样做的意义是：

- GUI 不直接理解底层包细节
- 分析引擎不直接理解 GUI 页面结构
- 中间由投影层完成适配

### 12.7 建议引入协议解析插件层

payload 解析不应该继续写死在 `manage_packets.rs` 里。

建议定义统一协议解析接口，例如：

```text
trait ProtocolAnalyzer {
    fn can_parse(&self, session: &Session) -> bool;
    fn on_data(&mut self, session: &mut Session, chunk: &[u8]) -> Vec<DomainEvent>;
}
```

优先做的解析器：

- `DnsAnalyzer`
- `TlsAnalyzer`
- `HttpAnalyzer`
- `QuicAnalyzer`

这样有几个好处：

- 可以分协议逐步落地
- 不会污染核心抓包路径
- 每个协议都可以单独测试

### 12.8 建议引入分层模式配置

不是所有用户都需要全量 payload 和会话重建，因此建议增加三档运行模式：

#### 1. Lite

- 近似当前模式
- 只做头部解析和秒级聚合
- 默认 `snaplen` 小
- 资源消耗最低

#### 2. Deep

- 完整抓包
- 支持常见协议 payload 元信息解析
- 支持会话状态跟踪

#### 3. Forensic

- 完整包保留
- 会话重组
- 更长时间窗口缓存
- 更适合离线分析和问题复盘

这种模式化设计很重要，因为它能避免“为了深度分析牺牲掉轻量体验”。

### 12.9 建议重构线程与通道模型

当前大量逻辑通过线程 + channel 已经能工作，但未来建议让线程模型更清晰。

建议的后台并发结构：

```text
Capture Thread
  -> bounded channel
Decode/Flow Worker
  -> bounded channel
Session/Reassembly Worker
  -> bounded channel
Protocol Analyzer Workers
  -> bounded channel
Projection Worker
  -> GUI
```

关键改进点：

- 全部通道改为有界队列，避免深度模式下无限堆积
- 每段明确批处理策略
- 区分实时优先队列和离线回放队列
- 为重度 payload 模式提供背压和降级策略

可选降级策略：

- 只保留元信息，不保留全部 payload
- 关闭低优先级协议解析器
- 降低 UI 推送频率

### 12.10 建议增加存储分层

要支持会话重建和 payload 详情，仅靠当前内存聚合对象不够。

建议至少引入三层存储：

#### 1. 热存储 Hot Memory

- 最近 N 秒或 N 分钟的包、流、会话
- 用于实时 UI 和近期详情展示

#### 2. 会话存储 Session Store

- 结构化会话对象
- 支持按时间、主机、协议、端口、程序检索

#### 3. 原始包存储 Packet Store

- 可选启用
- 可以是 PCAP 文件，也可以是块式文件索引
- 用于回放和取证

这样能避免所有功能都挤在 `InfoTraffic` 里。

### 12.11 建议改造页面数据来源

当前多个页面直接依赖 `Sniffer.info_traffic`。

未来建议页面只依赖各自的投影：

- Overview 页面 -> `OverviewProjection`
- Inspect 页面 -> `InspectProjection`
- Notifications 页面 -> `NotificationProjection`
- Connection Details -> `SessionDetailsProjection`

这会带来两个明显好处：

- 页面不会因底层模型变复杂而一起膨胀
- 可以在不改 GUI 页面结构的前提下替换底层分析引擎

### 12.12 建议分阶段迁移

这个改造不适合一次性推翻，建议分四期做。

#### 第一期：抽离分析接口

- 为 `CaptureSource`、`parse_packets`、`InfoTraffic` 外面加一层 service / trait
- 先把 `Sniffer` 从直接操纵底层细节，改成操纵 service

#### 第二期：引入 Flow / Session 模型

- 保留当前 `InfoTraffic`
- 在后台新增 flow/session 跟踪
- 先不改 Overview，只给 Details/Inspect 增强能力

#### 第三期：引入协议解析插件

- 先支持 DNS / TLS / HTTP 元信息
- 让 Inspect/Details 展示会话与应用层摘要

#### 第四期：引入可选深度模式

- 增加 Lite / Deep / Forensic 模式
- 加入存储分层、回放、异常检测

这种分期策略的价值在于：

- 不会一次性破坏现有体验
- 每一阶段都可交付
- 可以持续验证性能与复杂度

### 12.13 最终建议

如果只给一个架构方向建议，那就是：

不要把 payload 解析和会话重建直接塞进 `Sniffer` 和 `InfoTraffic`；
应该把 Sniffnet 演进成“分析引擎驱动、GUI 消费投影”的结构。

一句话概括这次改造的重点：

> 从“秒级统计工具”演进为“分层网络分析平台”，同时保留它现有的桌面监控体验。

---

## 13. 代码阅读建议

如果后续要继续深入，建议按这个顺序读源码：

1. `src/main.rs`
2. `src/gui/sniffer.rs`
3. `src/networking/types/capture_context.rs`
4. `src/networking/parse_packets.rs`
5. `src/networking/manage_packets.rs`
6. `src/networking/types/info_traffic.rs`
7. `src/networking/types/program_lookup.rs`
8. `src/chart/types/traffic_chart.rs`
9. `src/notifications/notify_and_log.rs`
10. `src/report/get_report_entries.rs`

这样能先建立主流程，再补细节，不容易迷失在 GUI 页面和类型定义中。

---

## 14. 总结

Sniffnet 的核心不是“抓到包”本身，而是把抓包结果组织成适合 GUI 监控的时间片数据流。

它的本质设计可以总结为：

- 输入侧：`pcap` 统一实时/离线抓包
- 解析侧：`etherparse` 做轻量协议头解析
- 聚合侧：`InfoTraffic` 做按秒统计
- 增强侧：rDNS、MMDB、进程识别补全上下文
- 展示侧：`Sniffer + iced` 统一驱动页面、图表、通知和搜索

如果只用一句话概括：

> Sniffnet 是一个以 GUI 状态机为壳、以后台按秒聚合抓包流水线为核的桌面流量监控系统。

它的优势在于轻量、直观、适合持续运行；它的不足则在于尚未深入到 payload 全量解析和通信会话级重建。

这也是它最清晰的架构边界：强在监控与可视化，弱在深度协议分析与取证级会话还原。
