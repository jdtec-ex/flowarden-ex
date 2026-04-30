# Flowarden 分阶段开发方案

## 1. 方案定位

当前 `docs` 的组织方式调整为：

- `phase1/`：第一阶段实施、backlog、进度、运行与样本文档
- `phase2/`：第二阶段开发计划与 UI 设计文档
- 根目录：跨阶段总方案、Sniffnet 参考分析、UI 参考图片索引

本方案基于当前 `docs` 中两份已有文档形成：

- `sniffnet_reverse_analysis.md`
- `sniffnet_refactor_avalonia_rust_grpc.md`

结合当前仓库现状，我建议将 Flowarden 明确定位为：

> 以 Sniffnet 的抓包、轻量解析、按秒聚合思路为参考，先做一个可长期演进的流量监控核心，再逐步补齐桌面 UI 与深度分析能力。

这意味着：

- 参考 Sniffnet 的能力边界和运行机制
- 不复制 Sniffnet 的 `iced` GUI 结构
- 第一阶段先交付无 UI 的可运行核心
- 第二阶段再补 `Avalonia` UI
- 第三阶段才进入 payload 深度解析与会话级重建

---

## 2. 当前基线判断

从现有仓库看：

- `flowarden-core` 已有 `capture`、`device`、`config`、`filters` 骨架
- `flowarden` 可执行程序仍是空入口
- `flowarden-ui` 目录尚未形成可运行的 Avalonia 工程
- `docs` 已经明确后续总体方向是 `Rust Core + Avalonia + gRPC`

因此当前最关键的问题不是“先做 UI”，而是：

> 先把一个无界面、可持续运行、可验证结果的 headless core 做出来。

---

## 3. 总体原则

### 3.1 产品原则

1. 第一阶段先做“流量监控器”，不是“Wireshark 替代品”。
2. 先保证抓得到、判得准、聚得稳，再做展示层。
3. UI 只能消费投影结果，不能反向绑死核心内部模型。
4. payload 与会话重建属于第三阶段，不提前透支复杂度。

### 3.2 工程原则

1. 第一阶段不引入 `iced`。
2. 第一阶段和第二阶段都不做多语言。
3. 第一阶段可以不正式引入 gRPC，但必须先抽出稳定的命令模型和投影模型，为第二阶段接 gRPC 预留边界。
4. 第一阶段不追求 crate 过度拆分，优先在当前 workspace 内完成最小闭环。
5. 优先借鉴 Sniffnet 已被验证的运行机制、分层思路和工程取舍，不重复发明已经被证明有效的核心路径。
6. 严格遵循 `YAGNI` 原则，不为第三阶段或假设需求提前引入复杂抽象、过度扩展点或暂时用不上的基础设施。
7. 代码质量目标必须高于 Sniffnet 参考实现，至少体现在模块边界更清楚、测试覆盖更完整、错误处理更一致、可维护性更强。
8. 所有可预期错误统一接入 `flowarden-error`，禁止各模块各自散落定义错误风格，CLI、core service、后续 UI 适配层都要复用同一套错误语义。
9. 每一阶段结束时都必须有可执行产物、可重复测试数据、明确验收口径。
10. Rust 实现必须优先采用语义直接、无多余中间步骤的所有权与分配写法；若目标类型可由源类型直接转换，就不允许先构造不必要的临时容器，例如 `&[u8] -> Box<[u8]>` 应直接使用 `data.into()`，而不是 `data.to_vec().into()`。

### 3.3 “后台执行”的定义

第一阶段这里的“后台执行”建议定义为：

- 无图形界面
- 可通过 CLI 启动
- 可长时间运行
- 可输出结构化结果
- 可被脚本或后续 UI 宿主调用

首期不把“守护进程安装”“系统服务注册”“托盘化”作为必选验收项，否则会过早扩大范围。

---

## 4. 三阶段总览

### 阶段一

目标：交付 `CLI + headless core`，实现类似 Sniffnet 当前的流量获取与基础分析闭环。

核心产物：

- `flowarden-core` 的抓包、解析、聚合、投影能力
- `flowarden` CLI 可执行程序
- 可用于验收的离线 `pcap` 样本、自动化测试、基准输出

### 阶段二

目标：交付 `Avalonia UI`，将阶段一核心能力图形化，并建立稳定的本地进程通信边界。

核心产物：

- `flowarden-ui` Avalonia 工程
- `flowarden` 或独立 core 进程的 service mode
- `gRPC` 或等价本地 IPC 契约

### 阶段三

目标：从“流量监控器”升级到“具备深度协议分析和会话级重建能力的监控/取证工具”。

核心产物：

- payload 保留与脱敏策略
- TCP 会话重组能力
- 协议级解析器
- 会话详情查询与导出能力

---

## 5. 建议的目标架构演进

### 5.1 阶段一目标架构

```text
flowarden (CLI)
  -> flowarden-core
       -> capture runtime
       -> packet decoder
       -> flow aggregation
       -> projection builder
       -> text/json output
```

### 5.2 阶段二目标架构

```text
flowarden-ui (Avalonia)
  <-> local gRPC / IPC
flowarden-core service
  -> capture
  -> analysis
  -> projection
```

### 5.3 阶段三目标架构

```text
flowarden-ui / CLI
  <-> local gRPC / IPC
flowarden-core service
  -> capture
  -> flow tracking
  -> session tracking
  -> stream reassembly
  -> payload analyzers
  -> projection / export
```

---

## 6. 阶段一：CLI 后台采集与基础分析

## 6.1 阶段目标

阶段一的完成标准不是“命令行能跑一下”，而是：

- 能对实时网卡抓包
- 能读取离线 `pcap`
- 能完成 L2/L3/L4 轻量解析
- 能完成方向判断、服务识别、按秒聚合
- 能以 CLI 文本或 JSON 输出稳定结果
- 能为第二阶段 UI 复用同一套核心模型

换句话说，阶段一要交付的是：

> Sniffnet 的核心引擎能力，但不要 `iced` UI、不要多语言、不要 payload 深解析。

## 6.2 阶段范围

### 必做范围

1. 设备发现
2. 实时抓包
3. 离线 `pcap` 回放
4. BPF 过滤
5. 支持当前 `flowarden-core` 已覆盖或计划覆盖的链路类型
6. 轻量包头解析
7. 方向判定
8. 基于端口和方向的服务识别
9. 按秒聚合的流量统计
10. Top connections / hosts / services 投影输出
11. 结构化日志和错误输出
12. 自动化测试和基准样本

### 明确不做

1. `iced` UI
2. Avalonia UI
3. 多语言
4. payload 默认保留
5. TCP/UDP 会话重建
6. HTTP/TLS/DNS 深度协议解码
7. 复杂告警系统
8. 进程识别、程序图标、MMDB、rDNS 全量增强

说明：

- `rDNS`、`MMDB`、进程识别都可以预留接口，但不作为阶段一验收前提。
- 如果阶段一把增强项一起做，极容易拖慢主线交付。

## 6.3 建议的模块落位

建议在当前 workspace 基础上演进，而不是一开始大拆 crate。

### `flowarden-core` 建议新增模块

- `analysis/`
- `projection/`
- `runtime/`
- `model/` 或 `domain/`

建议职责如下：

- `capture/`
  - 设备抓包
  - 文件回放
  - BPF 应用
  - pause/resume
- `analysis/`
  - link/network/transport 解码
  - 方向判定
  - 服务识别
  - flow 聚合
- `projection/`
  - 秒级 snapshot
  - overview 投影
  - inspect 投影
- `runtime/`
  - 采集循环
  - 通道编排
  - 停止、暂停、恢复
- `config/`
  - 默认配置
  - CLI 运行参数持久化

### `flowarden` 可执行程序建议承担

- CLI 参数解析
- 启动 live/offline capture
- 输出模式控制
- 结果落盘
- 退出码和错误打印

## 6.4 CLI 形态建议

建议阶段一至少提供以下命令形态：

```bash
flowarden devices
flowarden capture --device en0
flowarden capture --device en0 --bpf "tcp or udp"
flowarden capture --read ./samples/http.pcap
flowarden capture --device en0 --format json
flowarden capture --device en0 --duration 30 --output ./report.json
```

建议支持两种输出模式：

1. `table`
2. `json`

建议 JSON 输出优先稳定，作为第二阶段 UI 契约设计前的过渡验证格式。

## 6.5 阶段一内部里程碑

### 里程碑 1：CLI 骨架与设备发现

交付内容：

- `flowarden devices`
- `flowarden capture --device ...`
- `flowarden capture --read ...`
- 基本错误码和帮助信息

验收标准：

- 能列出当前机器可抓取设备
- 能校验设备名和文件路径
- 参数错误时返回非零退出码

### 里程碑 2：抓包运行时闭环

交付内容：

- live capture 循环
- offline pcap 回放循环
- BPF 应用
- stop/pause/resume 生命周期

验收标准：

- 指定设备后能持续抓包
- 离线 `pcap` 能完整读完
- BPF 生效且结果可观察
- 中止时资源释放正常，不残留僵死进程

### 里程碑 3：基础解析与按秒聚合

交付内容：

- 链路层类型识别
- IP/TCP/UDP/ICMP 基础解析
- 方向判断
- 服务识别
- 秒级聚合模型

验收标准：

- 对参考样本包能正确识别协议和五元组近似键
- 秒级统计稳定输出
- 流量方向在 live 和 offline 两种模式下都可解释

### 里程碑 4：投影输出与测试封板

交付内容：

- Top connections/hosts/services 输出
- JSON 快照格式
- 自动化测试
- 样本 `pcap` 与 golden outputs

验收标准：

- 同一输入多次运行输出结果一致
- 对参考 `pcap` 的包数、字节数、方向统计可复核
- 关键解析函数和聚合逻辑具备单元测试与集成测试

## 6.6 阶段一验收口径

建议在阶段一冻结一套标准验收集。

### 验收数据建议

1. `tcp_http_basic.pcap`
2. `udp_dns_basic.pcap`
3. `tls_clienthello_basic.pcap`
4. `mixed_ipv4_ipv6.pcap`
5. `loopback_basic.pcap`

### 验收项建议

1. 能列设备并选择设备抓包
2. 能离线读取 `pcap`
3. 能应用 BPF
4. 能输出每秒流量统计
5. 能输出 top connections / services
6. live 与 offline 的统计模型一致
7. 对固定 `pcap`，总包数与总字节数与参考结果一致或偏差可解释
8. 运行 30 分钟以上不出现明显内存持续增长

### 阶段一交付物

1. `flowarden` CLI 可执行程序
2. `flowarden-core` 基础分析引擎
3. 样本 `pcap`
4. 自动化测试
5. 使用说明文档
6. 验收结果记录

## 6.7 阶段一关键设计约束

这里有三个约束必须提前定死。

### 约束一：阶段一做“轻量解析”，不做深度 payload

参考 Sniffnet，阶段一只解析到足够支撑流量监控的层级：

- 链路层
- 网络层
- 传输层
- 方向
- 服务
- 基础统计

不进入会话重组和 payload 语义解析。

### 约束二：阶段一先抽象投影，不直接为终端输出硬编码领域逻辑

CLI 打印只是展示形式，核心输出应该先形成统一投影，例如：

- `OverviewSnapshot`
- `ConnectionSummary`
- `ServiceSummary`

第二阶段 UI 才能直接复用。

### 约束三：阶段一优先把“正确性”和“稳定性”做实

相比花时间做更多增强项，阶段一更应该优先解决：

- 抓包权限问题
- 链路类型兼容问题
- offline/live 方向判定差异
- 聚合结果一致性
- 错误恢复和日志

阶段一的实施细化、模块设计、任务拆解与验收清单，见：

- `phase1/flowarden_phase1_detailed_plan.md`

---

## 7. 阶段二：Avalonia UI

第二阶段的详细计划与 UI 设计稿见：

- `phase2/flowarden_phase2_development_plan.md`
- `phase2/flowarden_phase2_ui_design.md`

## 7.1 阶段目标

阶段二的目标不是简单“给阶段一包个壳”，而是：

- 让 UI 与核心进程解耦
- 让核心既能被 CLI 调用，也能被桌面 UI 调用
- 建立稳定的本地通信边界
- 将阶段一投影图形化呈现

## 7.2 阶段范围

### 必做范围

1. `flowarden-ui` Avalonia 工程初始化
2. 本地 core service mode
3. `gRPC` 或等价本地 IPC 契约
4. 设备选择页面
5. Start / Stop / Pause / Resume
6. Overview 页面
7. Inspect 页面
8. 基础设置和日志查看
9. 固定使用本机 `dotnet --version` 当前输出对应的 SDK `8.0.125`，目标框架为 `net8.0`

### 明确不做

1. payload 深度解析 UI
2. 会话级详情重建 UI
3. 多语言
4. 复杂插件系统
5. 高级告警中心

## 7.3 推荐实现策略

建议采用两进程模型：

```text
flowarden-ui (Avalonia)
  <-> local gRPC
flowarden-core service
```

原因：

- UI 崩溃不直接拖死核心抓包进程
- 第二阶段建立稳定契约，第三阶段更容易扩展 session/payload 接口
- 后续 CLI 和 UI 可共享同一套核心进程模型

## 7.4 阶段二内部里程碑

### 里程碑 1：core service mode 与契约冻结

交付内容：

- 本地监听模式
- proto 或等价契约文件
- control / projection / health 基础接口

验收标准：

- UI 可启动 core 或连接已启动 core
- 命令调用与结果推送稳定

### 里程碑 2：Avalonia 壳层与生命周期管理

交付内容：

- 主窗口
- MVVM 基础结构
- 进程启动与连接管理
- 错误提示和状态指示

验收标准：

- 用户可完成设备选择、启动、停止
- core 异常退出时 UI 可感知并提示

### 里程碑 3：Overview / Inspect 图形化

交付内容：

- 实时流量卡片
- 秒级趋势图
- top connections 表格
- 过滤和查询

验收标准：

- 页面数据显示与 CLI 输出一致
- 常规流量下 UI 更新不卡死、不明显掉帧

### 里程碑 4：桌面交付封板

交付内容：

- 打包脚本
- 配置文件落盘
- 基础运行文档

验收标准：

- 在主开发平台可完成安装、启动、抓包、关闭全流程

## 7.5 阶段二验收口径

1. UI 能列设备并选择设备
2. UI 能启动/停止抓包
3. UI 能实时展示秒级统计
4. UI 能展示 top connections / services
5. 过滤条件可下发并生效
6. UI 重启不要求重写核心逻辑
7. 长时间运行中 UI 与 core 都没有明显资源失控

---

## 8. 阶段三：payload 深度解析与会话级重建

## 8.1 阶段目标

阶段三开始，Flowarden 才从“流量监控器”进入“深度分析器”能力域。

目标包括：

- 保留必要 payload
- 对 TCP 会话做重组
- 对高价值协议做深度解析
- 暴露可查询、可脱敏、可导出的会话详情

## 8.2 阶段范围

### 必做范围

1. payload 保留策略
2. 会话模型
3. TCP stream reassembly
4. 协议级解析框架
5. 会话详情查询
6. payload 截断、脱敏、导出

### 推荐优先级

建议协议优先级如下：

1. DNS
2. HTTP/1.1
3. TLS ClientHello / ServerHello 摘要
4. 其他协议按实际需求扩展

说明：

- 阶段三不建议一开始就碰 QUIC 全量重组。
- 也不建议追求 Wireshark 式全协议覆盖。

## 8.3 建议的能力模式

建议在阶段三正式引入运行模式：

1. `Lite`
2. `Deep`
3. `Forensic`

模式差异建议体现在：

- `snaplen`
- 是否保留 payload
- 是否启用会话重组
- 详情保留时间
- 导出能力

## 8.4 阶段三内部里程碑

### 里程碑 1：会话模型与缓存策略

交付内容：

- `SessionKey`
- `SessionState`
- payload retain policy
- 有界缓存和淘汰策略

验收标准：

- 高流量下不会无限堆积内存
- 会话生命周期可追踪

### 里程碑 2：TCP 重组能力

交付内容：

- TCP segment 缓冲
- 重传处理
- 顺序重组
- 超时关闭

验收标准：

- 对参考 `pcap` 可还原完整 TCP 字节流
- 对乱序、重传样本有可解释结果

### 里程碑 3：协议解析器

交付内容：

- DNS 事务摘要
- HTTP 请求/响应摘要
- TLS 握手摘要

验收标准：

- 可提取域名、方法、路径、状态码、SNI 等关键字段
- 解析失败不影响整体抓包主循环

### 里程碑 4：详情查询与导出

交付内容：

- session details API
- UI 会话详情面板
- 十六进制/文本双视图
- 截断与脱敏

验收标准：

- 用户可查看单会话详情
- 敏感字段有明确脱敏策略
- 导出行为可控且有边界

## 8.5 阶段三验收口径

1. 对标准 `pcap` 可重建 TCP 会话
2. 能提取高价值协议摘要字段
3. payload 查看支持分页或截断
4. 内存占用有上限控制
5. UI 与 CLI 都可查询会话详情
6. 深度解析失败不会拖垮整体采集链路

---

## 9. 跨阶段公共要求

## 9.1 测试要求

每一阶段都建议同时建设三类测试：

1. 单元测试
2. 基于 `pcap` 的集成测试
3. golden output 回归测试

阶段三再增加：

4. 重组正确性测试
5. 压测与内存回归测试

## 9.1.1 质量基线

“质量高于 Sniffnet”在本项目里不应停留在口号，建议固化为以下要求：

1. 核心逻辑与展示/传输逻辑严格分离，避免形成类似单体状态根过度承载职责的问题。
2. 核心路径必须具备单元测试和基于 `pcap` 的集成测试，不接受仅靠手工运行验证。
3. 对外输出的数据模型、命令模型、错误模型都要稳定，不把内部临时结构直接暴露出去。
4. 阶段推进时优先重构掉含混职责，而不是为了赶进度堆叠分支判断和旁路逻辑。

## 9.2 性能要求

每一阶段都要保持以下原则：

1. 通道有界
2. 输出节流
3. 关键路径避免阻塞式增强查询
4. 统计结果以时间片聚合为主，不逐包推给 UI

## 9.3 安全与合规要求

阶段三之前，默认不长期保留 payload。

阶段三开始后，必须补齐：

1. payload 开关
2. 截断策略
3. 脱敏策略
4. 导出权限和边界
5. 日志中避免直接打印敏感内容

## 9.3.1 错误处理要求

错误处理统一以 `flowarden-error` 为基础，建议作为硬性约束执行：

1. `flowarden-core` 内部对 `pcap`、文件、配置、协议解析、运行时控制等错误统一映射到 `flowarden-error`。
2. CLI 层只负责补充命令上下文和退出码，不重新发明另一套错误体系。
3. 第二阶段开始，core service 和 UI 通信层继续沿用 `flowarden-error` 语义，再做协议级或展示级映射。
4. 禁止在关键路径大量直接 `unwrap`、`expect`，除非是明确不可恢复且属于程序员错误的断言场景。
5. 日志、终端输出、后续 RPC 错误返回都要保留足够上下文，便于定位抓包设备、过滤条件、输入文件、运行模式等现场信息。

## 9.4 平台策略

建议采用“先主平台、后扩平台”的验收策略：

1. 先在当前主开发平台完成闭环
2. 再补跨平台兼容验证

否则会在抓包权限、设备枚举、打包方式上过早分散精力。

---

## 10. 我建议的实施顺序

如果按风险最小、返工最少的顺序推进，我建议这样执行：

1. 先完成阶段一全部闭环，不碰 UI
2. 阶段一后半段冻结 command model 和 projection model
3. 阶段二引入本地进程通信和 Avalonia UI
4. 阶段二稳定后再进入 payload/session
5. 阶段三先做 TCP 重组，再做协议解析，再做详情 UI

这条顺序的核心目的是：

> 先把“采得到、算得准、跑得稳”做实，再做“看起来更完整”的能力。

---

## 11. 最终建议

如果这份方案用于立项和评审，我建议将阶段一的验收目标明确写成：

> 交付一个无 UI、可长期运行、支持 live/offline、具备 Sniffnet 风格基础流量分析能力的 Rust CLI 核心。

并把以下三项列为阶段一的硬门槛：

1. 实时抓包与离线回放都能跑通
2. 秒级聚合结果可复核
3. 输出模型可直接承接阶段二 UI

这样阶段一完成后，阶段二基本是“接展示层”，而不是“推倒重做核心”。
