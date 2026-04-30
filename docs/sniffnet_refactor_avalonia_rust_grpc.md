# Sniffnet 重构方案

## 1. 目标

将现有 Sniffnet 从“Rust 单体桌面应用”重构为：

- UI：`Avalonia` 桌面前端
- Core：`Rust` 网络抓包与分析引擎
- 通信：`gRPC`

目标不是只替换一个 UI 框架，而是完成一次真正的架构重构，使系统具备：

- 前后端解耦
- 核心分析引擎可复用
- UI 可独立演进
- 后续支持 payload 深度解析与会话级分析
- 更好的可测试性、可维护性和可扩展性

---

## 2. 为什么要这样重构

现有 Sniffnet 的主要问题不是功能少，而是职责耦合过重：

- `Sniffer` 同时承担 UI 状态、抓包生命周期、线程编排、数据容器、通知入口
- `parse_packets` 同时承担收包、解析、聚合、调度
- GUI 和分析模型绑定过深
- 当前 `iced` UI 与 Rust 内部状态机耦合，难以独立替换展示层
- 后续如果增加 payload 解析、session 重建，会进一步放大复杂度

因此这次重构的核心思想是：

> 让 Rust Core 成为独立分析引擎，让 Avalonia 只承担展示和交互，让 gRPC 成为两者之间稳定的契约层。

---

## 3. 重构原则

### 3.1 保留 Rust 的部分

Rust 继续负责：

- 抓包
- 解析
- 流和会话分析
- 主机/服务/程序识别
- 投影计算
- 告警与导出

原因很直接：

- 现有抓包逻辑和领域模型都在 Rust
- 性能敏感路径更适合 Rust
- 后续 payload/session 能力也应在 Rust 内核层实现

### 3.2 替换的部分

替换 `iced` GUI，改用 `Avalonia`。

Avalonia 负责：

- 窗口与页面
- MVVM 交互
- 数据绑定
- 图表和表格展示
- 用户命令发起

### 3.3 新增的关键边界

新增一层稳定通信边界：

- Rust Core 对外暴露 `gRPC API`
- Avalonia UI 通过 `gRPC Client` 访问核心能力

这样 UI 和 Core 不再共享内存，不再共用内部类型，只通过协议通信。

---

## 4. 目标架构总览

建议的目标架构如下：

```text
+-----------------------------+
| Avalonia Desktop UI         |
| - Views / ViewModels        |
| - Local state store         |
| - gRPC clients              |
+-------------+---------------+
              |
              | gRPC
              v
+-----------------------------+
| Rust Core Service           |
| - Capture Plane             |
| - Analysis Plane            |
| - Projection Plane          |
| - Notification / Export     |
+-------------+---------------+
              |
              v
+-----------------------------+
| libpcap / mmdb / listeners  |
| local files / pcap / config |
+-----------------------------+
```

如果从运行形态看，建议采用：

- `Avalonia UI` 作为主桌面进程
- `Rust Core` 作为独立子进程或后台服务进程

而不是把 Rust 编译成动态库直接嵌进 .NET 进程。

---

## 5. 为什么选择独立进程而不是 FFI

这一步很关键。

### 不建议的方案

- C ABI / PInvoke 直接调 Rust 动态库
- 把抓包引擎直接嵌入 Avalonia 进程

原因：

- FFI 边界在复杂异步和高频流式场景下很脆弱
- 内存和线程模型更难管理
- 出错时 UI 与核心可能一起崩溃
- 后续协议扩展、日志观测、跨平台打包都更麻烦

### 建议方案

使用独立进程 + gRPC：

- Rust Core 独立崩溃不直接拖死 UI
- 契约清晰，语言边界明确
- 更容易做测试和录制回放
- Core 后续可以复用为 CLI、远程 Agent、服务端组件

结论：

> 这次重构应是“进程级解耦”，而不是“界面库替换”。

---

## 6. 进程模型

建议采用两进程模式。

### 6.1 进程划分

#### 1. `sniffnet-desktop`

Avalonia 桌面程序，负责：

- 启动 UI
- 管理用户交互
- 启动或连接 Rust Core
- 订阅流式投影
- 渲染 Overview / Inspect / Notifications / Session Details

#### 2. `sniffnet-core`

Rust 核心服务，负责：

- 抓包
- 解析与聚合
- 会话跟踪
- payload 解析
- 读模型投影
- 告警
- 导出

### 6.2 启动方式

默认建议：

- `sniffnet-desktop` 启动时检查 `sniffnet-core`
- 若未启动，则拉起子进程
- Core 启动后输出监听地址和一次性 session token
- UI 用 token 建立 gRPC 连接

这样可以兼顾：

- 本地桌面一体化体验
- 进程隔离
- 后续支持“独立运行 core”

---

## 7. gRPC 通信设计

## 7.1 通信原则

需要明确一点：

- 不要逐包通过 gRPC 往 UI 推

gRPC 适合：

- 命令调用
- 流式推送聚合结果
- 按需拉取详情

不适合：

- 直接把原始包洪泛到前端

因此通信策略应是：

- 控制命令：Unary RPC
- 状态变化：Server Streaming
- 大对象详情：按需查询
- 长时任务：状态流或 job 查询

## 7.2 服务拆分建议

建议把接口拆成 5 组服务。

### 1. `ControlService`

负责控制命令：

- `StartCapture`
- `StopCapture`
- `PauseCapture`
- `ResumeCapture`
- `ApplyFilter`
- `SetCaptureSource`
- `SetMode`
- `LoadConfig`
- `SaveConfig`

### 2. `DiscoveryService`

负责静态或低频信息：

- `ListDevices`
- `GetCapabilities`
- `GetSupportedProtocols`
- `GetThemes`
- `GetVersion`

### 3. `ProjectionService`

负责 UI 所需投影：

- `StreamOverview`
- `StreamNotifications`
- `StreamTimeline`
- `GetInspectPage`
- `GetSessionDetails`
- `SearchConnections`

### 4. `ExportService`

负责导出：

- `ExportPcap`
- `ExportSessions`
- `ExportReport`

### 5. `HealthService`

负责可用性：

- `Ping`
- `GetRuntimeStatus`
- `WatchCoreStatus`

这样拆分后，接口边界清楚，前端也更容易按 ViewModel 组织调用。

---

## 8. gRPC 模型建议

## 8.1 消息分层

gRPC 消息不应该复用 Rust 内部结构体，应定义独立的跨语言传输模型。

建议区分三层：

### 1. Command Model

前端发给 Core 的控制命令。

例如：

- `CaptureOptions`
- `FilterOptions`
- `UiPreferences`
- `SearchRequest`

### 2. Projection Model

Core 发给前端的读模型快照或增量。

例如：

- `OverviewSnapshot`
- `InspectPage`
- `NotificationItem`
- `TimelinePoint`
- `SessionSummary`

### 3. Detail Model

按需拉取的深度对象。

例如：

- `SessionDetails`
- `ProtocolMetadata`
- `PayloadSlice`
- `HostDetails`

## 8.2 建议公共字段

所有流式消息都建议带：

- `capture_id`
- `sequence`
- `timestamp`
- `kind`
- `is_snapshot`

这样前端才能：

- 去重
- 重排序
- 做断线重连恢复
- 区分全量和增量更新

## 8.3 建议不要直接暴露原始 payload

payload 很大、很敏感，不应默认进入投影流。

建议策略：

- Overview/Inspect 流只传摘要
- 用户点开详情时，再按需调用 `GetSessionPayload`
- payload 支持：
  - 截断
  - 分页
  - 脱敏
  - 十六进制/文本双视图

---

## 9. 核心服务端架构

建议 Rust Core 拆成 5 个主层。

## 9.1 Capture Plane

职责：

- 实时设备抓包
- 离线 PCAP 回放
- BPF 应用
- pause/resume
- 模式切换

建议模块：

- `capture-source`
- `capture-runtime`
- `capture-controller`

## 9.2 Analysis Plane

职责：

- L2/L3/L4 解码
- flow 跟踪
- session 跟踪
- 流重组
- payload 深度解析

建议模块：

- `decoder`
- `flow-tracker`
- `session-tracker`
- `reassembly`
- `protocol-analyzers`

## 9.3 Enrichment Plane

职责：

- rDNS
- ASN / Country
- 程序识别
- 图标识别

建议模块：

- `host-enrichment`
- `program-enrichment`

## 9.4 Projection Plane

职责：

- 生成 UI 所需读模型
- 维护分页、排序、聚合、时间线

建议模块：

- `overview-projection`
- `inspect-projection`
- `notification-projection`
- `session-projection`

## 9.5 Transport Plane

职责：

- gRPC 服务实现
- 请求鉴权
- 流式推送
- 生命周期管理

建议模块：

- `grpc-api`
- `grpc-server`

---

## 10. Rust Core 内部推荐分层

推荐不要继续按“GUI / networking / chart / notifications”来组织新项目，而要改成按职责分层。

建议的 Rust workspace：

```text
sniffnet-v2/
  proto/
  rust/
    Cargo.toml
    crates/
      core-domain/
      core-capture/
      core-analysis/
      core-enrichment/
      core-projection/
      core-notification/
      core-export/
      core-grpc/
      core-app/
```

### 各 crate 作用

#### `core-domain`

放稳定领域模型：

- `PacketRecord`
- `FlowKey`
- `FlowState`
- `Session`
- `Transaction`
- `ProtocolMetadata`

#### `core-capture`

放设备和文件抓包能力。

#### `core-analysis`

放解码、流分类、session、重组、payload 分析。

#### `core-enrichment`

放 rDNS、MMDB、程序识别。

#### `core-projection`

放面向 UI 的只读模型与投影逻辑。

#### `core-notification`

放告警规则、阈值、黑名单、远程通知。

#### `core-export`

放 PCAP 导出、报表导出、会话导出。

#### `core-grpc`

放 tonic 生成代码、服务实现、协议适配层。

#### `core-app`

把上述模块组装成可执行服务。

---

## 11. Avalonia UI 架构建议

Avalonia 前端建议采用标准 MVVM。

## 11.1 前端分层

建议结构：

```text
dotnet/
  Sniffnet.Desktop/
    App.axaml
    Views/
    ViewModels/
    Services/
    Models/
    Stores/
    Converters/
```

### 分层职责

#### `Views`

只负责 XAML 页面与交互绑定。

#### `ViewModels`

负责：

- 页面状态
- 命令绑定
- 调用 gRPC client
- 接收投影更新

#### `Services`

负责：

- `CoreProcessService`
- `GrpcChannelFactory`
- `ProjectionStreamService`
- `SessionQueryService`

#### `Stores`

负责：

- 全局 UI 状态
- 当前连接状态
- 当前 capture 状态
- 页面间共享选择项

### 11.2 页面映射建议

现有 Sniffnet 页面可映射为：

- `HomeView`
- `OverviewView`
- `InspectView`
- `NotificationsView`
- `SessionDetailsView`
- `SettingsView`

缩略图模式如果保留，可单独设计为：

- `MiniModeWindow`

### 11.3 UI 只消费投影，不直接理解底层包

Avalonia 前端不应直接处理：

- 原始包
- flow 内部状态
- session 重组细节

它只理解：

- 设备列表
- 统计卡片
- 图表点
- 表格行
- 详情对象

这是新架构成功与否的关键约束。

---

## 12. proto 目录与代码生成建议

建议单独维护 `proto/` 目录：

```text
proto/
  sniffnet/common.proto
  sniffnet/control.proto
  sniffnet/discovery.proto
  sniffnet/projection.proto
  sniffnet/export.proto
  sniffnet/health.proto
```

### 12.1 代码生成策略

Rust：

- 用 `tonic-build`

C#：

- 用 `Grpc.Tools`

### 12.2 协议治理建议

必须建立 proto 版本治理规则：

- 字段只增不删
- 废弃字段先 `deprecated`
- 保持向后兼容
- 所有 breaking changes 走版本升级

这样 UI 和 Core 才不会频繁因为契约漂移而失配。

---

## 13. 推荐的 gRPC 契约形态

下面不是最终 proto，只是建议的接口形态。

## 13.1 控制类 RPC

```text
rpc StartCapture(StartCaptureRequest) returns (StartCaptureReply)
rpc StopCapture(StopCaptureRequest) returns (StopCaptureReply)
rpc PauseCapture(PauseCaptureRequest) returns (PauseCaptureReply)
rpc ResumeCapture(ResumeCaptureRequest) returns (ResumeCaptureReply)
rpc ApplyFilter(ApplyFilterRequest) returns (ApplyFilterReply)
rpc SetMode(SetModeRequest) returns (SetModeReply)
```

## 13.2 流式投影 RPC

```text
rpc StreamOverview(StreamOverviewRequest) returns (stream OverviewEnvelope)
rpc StreamNotifications(StreamNotificationsRequest) returns (stream NotificationEnvelope)
rpc StreamTimeline(StreamTimelineRequest) returns (stream TimelineEnvelope)
```

## 13.3 查询类 RPC

```text
rpc SearchSessions(SearchSessionsRequest) returns (SearchSessionsReply)
rpc GetSessionDetails(GetSessionDetailsRequest) returns (SessionDetails)
rpc GetSessionPayload(GetSessionPayloadRequest) returns (GetSessionPayloadReply)
rpc ListDevices(ListDevicesRequest) returns (ListDevicesReply)
```

## 13.4 为什么这样分

这样分的目的很明确：

- 高频数据用流
- 配置控制用 unary
- 重对象详情按需拉

这会显著降低 UI 压力和协议复杂度。

---

## 14. 通信安全与本地 IPC 建议

因为这是桌面应用，本地通信默认即可，不需要一开始就引入完整远程安全体系。

### 14.1 MVP 建议

建议第一版先使用：

- `127.0.0.1` 上的随机端口
- 一次性启动 token
- Core 仅监听本地回环地址

启动流程：

1. Desktop 拉起 Core
2. Core 生成随机 token
3. Core 输出地址和 token
4. UI 用 token 建立连接

### 14.2 后续增强

后续如果要更严格，可再评估：

- Unix Domain Socket
- Windows Named Pipe 包装
- mTLS

但第一阶段不建议为此增加过高复杂度。

---

## 15. 性能与流控策略

引入 gRPC 之后，最大的风险不是“RPC 不通”，而是“数据过多导致 UI 和 transport 被淹没”。

因此必须明确以下策略。

## 15.1 不传逐包事件给 UI

UI 只接收：

- 秒级或更细粒度但仍是聚合后的投影
- 增量详情变更
- 用户点开时才拉 payload

## 15.2 有界通道

Core 内部每个阶段都使用有界队列，避免高流量下无限堆积。

## 15.3 分模式运行

建议保留三档：

- `Lite`
- `Deep`
- `Forensic`

不同模式决定：

- snaplen
- 是否保留 payload
- 是否启用重组
- 是否启用深度协议解析
- 投影刷新频率

## 15.4 前端节流

Avalonia 前端应对流式更新做：

- 批量刷新
- 节流合并
- 虚拟化列表
- 图表采样

否则换了 UI 框架仍然会卡。

---

## 16. 打包与部署建议

## 16.1 打包结构

桌面发布包中建议同时包含：

- `sniffnet-desktop`
- `sniffnet-core`
- `resources/`
- `proto version manifest`

## 16.2 版本匹配

UI 和 Core 必须做版本握手。

例如：

- UI 启动后调用 `GetVersion`
- 检查 `core_version`
- 检查 `api_version`
- 若不匹配则提示升级

这可以避免桌面端升级后协议不兼容。

## 16.3 日志与诊断

建议将日志分开：

- `desktop.log`
- `core.log`

并让 UI 提供一键打开日志入口。

---

## 17. 测试策略

这次重构后，测试会比当前单体模型更容易做。

## 17.1 Rust Core 测试

- 包解析单元测试
- flow/session 状态机测试
- 协议解析器测试
- 投影一致性测试
- gRPC 服务测试

## 17.2 Avalonia UI 测试

- ViewModel 单元测试
- gRPC mock 集成测试
- 页面绑定测试

## 17.3 端到端测试

- 启动 desktop + core
- 导入测试 pcap
- 断言 overview/inspect/notification 的结果

这是独立进程架构的一大收益。

---

## 18. 分阶段迁移计划

建议分五阶段推进。

## 18.1 第 0 阶段：冻结现有领域边界

输出：

- 当前抓包、统计、通知、搜索的领域模型整理
- 划清哪些留在 Core，哪些属于 UI

目标：

- 防止一边重构一边继续把逻辑写回 UI

## 18.2 第 1 阶段：抽出 Rust Core Service

工作：

- 从现有仓库抽离 GUI 依赖
- 提炼 `CaptureService`、`AnalysisService`、`ProjectionService`
- 用 CLI 或最简控制接口先跑通

里程碑：

- 没有 Avalonia，Core 也能独立运行

## 18.3 第 2 阶段：建立 proto 和 gRPC 骨架

工作：

- 定义 proto
- 用 `tonic` 和 `Grpc.Net.Client` 跑通服务
- 实现设备发现、启动抓包、overview 流

里程碑：

- 最小前后端可通信

## 18.4 第 3 阶段：实现 Avalonia MVP

工作：

- 用 Avalonia 实现：
  - 设备选择
  - Start/Stop
  - Overview 页面
  - Notifications 页面

里程碑：

- 完成第一版替换 `iced`

## 18.5 第 4 阶段：补 Inspect / Session Details / Export

工作：

- 搜索分页
- 连接详情
- payload 按需拉取
- 导出流程

里程碑：

- 功能接近现有版本

## 18.6 第 5 阶段：引入 Deep/Forensic 能力

工作：

- 会话重组
- payload 深度解析
- 协议插件
- 更强异常检测

里程碑：

- 新架构开始超过旧版本能力边界

---

## 19. 推荐的新仓库结构

建议采用 mono-repo：

```text
sniffnet-v2/
  README.md
  docs/
    architecture.md
    api.md
    migration-plan.md
  proto/
    sniffnet/
      common.proto
      control.proto
      discovery.proto
      projection.proto
      export.proto
      health.proto
  rust/
    Cargo.toml
    crates/
      core-domain/
      core-capture/
      core-analysis/
      core-enrichment/
      core-projection/
      core-notification/
      core-export/
      core-grpc/
      core-app/
  dotnet/
    Sniffnet.Desktop/
      App.axaml
      App.axaml.cs
      Views/
      ViewModels/
      Services/
      Stores/
      Assets/
  scripts/
    build-core.sh
    build-desktop.sh
    package.sh
```

这个结构的好处是：

- proto 单一来源
- Rust 与 .NET 各自独立
- 文档、脚本、打包清晰

---

## 20. 风险与对策

## 20.1 风险：gRPC 边界过细

如果把过多细碎状态暴露成 RPC，前后端会非常啰嗦。

对策：

- 以投影为中心设计 API
- 不把内部领域模型直接外露

## 20.2 风险：UI 仍然承担过多状态

如果 ViewModel 又开始自己拼装连接状态，重构会失败。

对策：

- 让 UI 只消费投影和详情对象

## 20.3 风险：Core 成为新的 God Service

虽然把 UI 拆掉了，但如果 Core 内部不分层，问题只是换个地方继续积累。

对策：

- Rust workspace 分 crate
- 领域层、投影层、transport 层明确拆开

## 20.4 风险：引入 gRPC 后性能下降

对策：

- 不传逐包消息
- 只传快照和增量
- 大对象详情按需取
- 内部使用 bounded queue 和批处理

## 20.5 风险：发布复杂度上升

对策：

- 统一打包脚本
- UI 负责拉起 Core
- 做版本握手和日志分离

---

## 21. 最终建议

如果只给一个最终结论，那就是：

> 这次重构应该把 Sniffnet 从“Rust GUI 应用”升级为“Rust 网络分析引擎 + Avalonia 桌面壳 + gRPC 契约层”。

真正应该保留的是：

- Rust 的抓包与分析能力
- 现有统计和投影思路

真正应该替换和重构的是：

- UI 技术栈
- 进程边界
- 内部模块职责
- 前后端通信方式

最重要的架构改善有三点：

1. 进程级解耦，替代当前单体 UI 架构
2. 以 gRPC 契约隔离 UI 和 Core
3. 以分层分析引擎替代当前 `Sniffer + parse_packets + InfoTraffic` 的紧耦合模型

如果按这个方向推进，新的 Sniffnet 会同时具备：

- 更现代的桌面 UI 能力
- 更稳定的核心分析边界
- 更强的后续演进空间
- 对 payload 和 session 分析更友好的架构基础
