# Flowarden 第一阶段实施细化方案

## 1. 文档目的

本文是 `flowarden_phased_development_plan.md` 的第一阶段实施细化版，面向评审和执行，回答四个问题：

1. 第一阶段到底交付什么
2. 第一阶段的模块边界怎么划
3. 第一阶段按什么顺序开发最稳
4. 第一阶段如何验收，才能保证质量高于 Sniffnet

本文只讨论第一阶段：

> 交付一个无 UI、可后台执行、可通过 CLI 控制、具备 Sniffnet 风格基础流量获取与分析能力的 Rust 核心。

对应的可执行开发任务拆解见：

- `flowarden_phase1_backlog.md`

---

## 2. 第一阶段收敛原则

第一阶段必须同时满足以下四条约束：

### 2.1 借鉴 Sniffnet，但不复制 Sniffnet

借鉴的部分：

- `pcap` 抓包路径
- 轻量头部解析
- 方向判断
- 服务识别思路
- 按秒聚合
- live/offline 共用分析链路

不复制的部分：

- `iced` UI 绑定式架构
- GUI 驱动的状态树
- 为了桌面展示而扩大的状态容器

### 2.2 严格执行 YAGNI

第一阶段不提前实现以下内容：

- gRPC 服务端
- Avalonia UI 适配层
- payload 深度解析
- TCP 会话重建
- 多线程多阶段复杂流水线
- 为未来插件化准备的大量抽象接口

只预留必要边界，不预实现未来复杂度。

### 2.3 代码质量目标高于 Sniffnet

这里的“高于”必须可操作，至少包含：

1. 核心职责边界更清楚
2. CLI 与核心分析逻辑解耦
3. 错误处理统一，不到处散落 `unwrap`
4. 有固定 `pcap` 样本和回归测试
5. 输出模型稳定，不让展示层反向污染核心
6. Rust 所有权、借用和分配路径必须写得直接且可辩护，不接受“先过编译再收口”的中间态实现；如果存在直接类型转换路径，就不应引入无意义的临时分配或容器搬运。

### 2.4 错误处理统一使用 `flowarden-error`

第一阶段所有可预期错误都必须进入 `flowarden-error`，不允许在 `flowarden-core` 或 `flowarden` 中再新造一套错误体系。

---

## 3. 第一阶段完成定义

第一阶段完成，不等于“命令行看到了几个包”，而是同时满足以下条件：

1. `flowarden devices` 能列出设备
2. `flowarden capture --device ...` 能稳定抓实时流量
3. `flowarden capture --read ...` 能读取离线 `pcap`
4. 能完成 L2/L3/L4 解析、方向判断、服务识别、按秒聚合
5. 能输出稳定的 `json` 快照和可读的 `table` 结果
6. 有一组固定样本 `pcap` 和 golden outputs
7. 关键路径测试通过
8. 关键错误都映射到 `flowarden-error`

## 3.1 与 Sniffnet 的核心能力对照

为了避免第一阶段在“去 UI、做 CLI”过程中把 Sniffnet 的关键能力一起削掉，建议明确分成三类：

更完整的对照矩阵见：

- `sniffnet_phase1_feature_matrix.md`

### 阶段一必须保留的核心能力

1. live capture 与 offline `pcap` 共用同一条分析链路。
2. BPF 过滤能力保留。
3. 当前已识别的重要链路类型能力保留：
   - `Ethernet`
   - `RawIp`
   - `IPv4`
   - `IPv6`
   - `LinuxSll`
   - `LinuxSll2`
   - `Loop`
   - `Null`
4. 按秒聚合保留，不能退化成“逐包直接打印”。
5. offline 回放必须按 `pcap` 包时间戳推进时间轴，而不是按文件读取速度推进；存在跨秒空洞时要能表达 gap。
6. 方向判定必须保留 Sniffnet 的关键判定思想，至少覆盖：
   - 本机地址判断
   - loopback 特判
   - `0.0.0.0` / `::` 等未分配地址场景
   - offline 模式缺少本机语义时的保守回退策略
7. 服务识别不能退化成“只看目标端口”，至少要综合：
   - 传输层协议
   - 源端口
   - 目标端口
   - 流量方向
8. 抓包质量指标保留，至少包括：
   - `dropped_packets`
   - `last_packet_timestamp`
9. pause/resume 能力不能在核心层丢失，即使 CLI 首版不一定暴露完整交互入口，也必须在 runtime 层保留软暂停/恢复能力。

### 阶段一建议保留的能力

1. 实时抓包同时落盘 `pcap`。

这项能力在 Sniffnet 的 capture abstraction 中是成立的，也与后续回放、复核、取证衔接自然。如果实现代价可控，建议纳入阶段一；如果排期紧张，可放到阶段一.1，但架构上不要阻断。

### 明确后置到后续阶段的能力

1. rDNS 解析
2. 国家/ASN 增强
3. 进程识别与程序图标
4. 通知系统
5. payload 深度解析
6. 会话级重建

---

## 4. 第一阶段建议架构

## 4.1 总体数据流

建议采用如下最小闭环：

```text
CLI args
  -> CaptureOptions
  -> CaptureRuntime
  -> PacketEnvelope
  -> DecodedPacket
  -> ClassifiedPacket
  -> FlowAggregator
  -> TickSnapshot / FinalSnapshot
  -> Formatter(table/json)
  -> stdout / file
```

其中：

- `PacketEnvelope` 表示抓到的原始包及元信息
- `DecodedPacket` 表示基础头部解析结果
- `ClassifiedPacket` 表示完成方向、服务识别后的包
- `FlowAggregator` 负责把逐包结果汇总为秒级统计
- `TickSnapshot` 是阶段一最重要的稳定输出模型

这里要额外强调两点：

1. live 模式的 tick 以 wall clock 为主。
2. offline 模式的 tick 必须以 `pcap` 包时间戳为主，必要时补出时间空洞。

## 4.2 并发模型建议

第一阶段建议不要直接做复杂多阶段并发流水线。

推荐实现：

- 一个主分析循环
- 一个可选的停止信号
- 每秒 flush 一次 snapshot

也就是：

```text
read packet -> decode -> classify -> aggregate -> maybe flush
```

这样做的原因：

1. CLI 阶段没有 GUI 压力，不需要像 Sniffnet 那样先为 UI 解耦线程
2. 单循环更容易保证顺序一致性和调试可读性
3. 更符合 `YAGNI`
4. 阶段二如果需要 service mode，再在外围加通信层即可

结论：

> 第一阶段优先做“单 worker 的稳定闭环”，而不是“看起来更先进的多级异步架构”。

## 4.3 workspace 内职责划分

基于当前目录结构，建议这样收敛：

### `./flowarden/flowarden-core`

负责：

- capture source
- capture runtime
- packet decode
- direction classify
- service classify
- flow aggregation
- snapshot projection
- 核心配置模型
- 核心错误映射

### `./flowarden/flowarden`

负责：

- CLI 参数解析
- 调用 core
- stdout/stderr 输出
- 文件输出
- exit code
- CLI 上下文错误包装

### `./flowarden/flowarden-error`

负责：

- 统一错误类型
- 错误上下文链
- source/type 语义
- `or_err` / `err_context` 等工具

第一阶段不建议新增更多 crate。

---

## 5. 第一阶段模块设计

## 5.1 `flowarden-core` 建议目录

建议在不大拆 workspace 的前提下，把 `flowarden-core/src` 收敛成：

```text
src/
  lib.rs
  config/
  device/
  capture/
    mod.rs
    source.rs
    context.rs
    runtime.rs
  analysis/
    mod.rs
    packet.rs
    decoder.rs
    direction.rs
    service.rs
    classify.rs
  flow/
    mod.rs
    key.rs
    counters.rs
    aggregator.rs
  projection/
    mod.rs
    snapshot.rs
    summary.rs
```

这已经足够支撑第一阶段，不需要再拆成 `core-domain` / `core-analysis` / `core-projection` 多 crate。

## 5.2 模块职责

### `capture/`

负责：

- 设备或文件输入统一抽象
- `pcap` 句柄打开
- link type 获取
- BPF 应用
- next packet 读取
- soft pause/resume
- 可选的 savefile 输出

建议新增：

- `runtime.rs`

建议不要让 `capture/context.rs` 直接承担后续聚合和业务判断。

### `analysis/decoder.rs`

负责：

- 根据链路类型解析头部
- 输出统一的 `DecodedPacket`

建议借鉴 Sniffnet 的思路：

- 优先采用轻量头部解析
- 重点支持 `Ethernet`、`RawIp`、`IPv4`、`IPv6`、`LinuxSll`、`LinuxSll2`、`Loop`、`Null`

### `analysis/direction.rs`

负责：

- live capture 基于本机地址判断方向
- offline capture 在缺少“本机”语义时采用保守回退规则
- loopback 特判
- `0.0.0.0` / `::` 等未分配地址特判
- 必要时保留 bogon/私网语义回退

### `analysis/service.rs`

负责：

- 根据协议、端口、方向输出服务名或服务类型

阶段一建议收敛为：

- 先支持常见 well-known services
- 优先覆盖验收 `pcap` 涉及协议
- 不在第一阶段实现完整 IANA 级全端口库

这是一个明确的 `YAGNI` 取舍。

但要注意：

- 服务识别不能简化成“优先看目标端口”。
- 必须保留源端口、目标端口和方向联合判断的启发式逻辑。

### `flow/aggregator.rs`

负责：

- 维护全局统计
- 维护秒级增量统计
- 对连接、主机、服务做聚合
- 生成 `TickSnapshot`
- 维护 `dropped_packets`
- 维护 `last_packet_timestamp`
- offline 模式下根据 `pcap` 时间戳处理跨秒和 gap

这里是第一阶段的核心。

### `projection/`

负责：

- 从聚合状态生成稳定输出模型
- 保证 CLI 与后续 UI 可复用同一套投影对象

第一阶段不要在 CLI 层直接拼接太多业务逻辑字符串。

---

## 6. 第一阶段建议数据模型

## 6.1 运行参数模型

建议核心先收敛成稳定的命令模型，而不是让 CLI 参数直接渗透整个 core。

建议至少定义：

```text
CaptureOptions
  - source: device | file
  - bpf: Option<String>
  - snaplen: u32
  - tick_interval: Duration
  - duration_limit: Option<Duration>
  - promiscuous: bool

OutputOptions
  - format: table | json
  - output_path: Option<PathBuf>
  - top_n: usize
  - include_final_summary: bool
```

## 6.2 抓包与解析模型

建议定义以下内部模型：

```text
PacketEnvelope
  - timestamp
  - captured_len
  - original_len
  - link_type
  - raw_bytes

DecodedPacket
  - timestamp
  - protocol
  - src_ip
  - dst_ip
  - src_port
  - dst_port
  - l4_protocol
  - tcp_flags
  - packet_len
```

说明：

- 第一阶段不需要把 payload 做成对外稳定模型
- `raw_bytes` 只在包解析阶段短生命周期使用

## 6.3 分类模型

```text
TrafficDirection
  - Inbound
  - Outbound
  - Local
  - Unknown

ServiceLabel
  - name
  - transport
  - confidence
```

建议把“方向”和“服务”从 `DecodedPacket` 后置分类出来，而不是让 decoder 一次性承担全部职责。

## 6.4 聚合模型

建议至少定义：

```text
FlowKey
  - src_ip
  - dst_ip
  - src_port
  - dst_port
  - protocol

FlowCounters
  - packets_in
  - packets_out
  - bytes_in
  - bytes_out
  - first_seen
  - last_seen

ServiceCounters
HostCounters
GlobalCounters
```

## 6.5 投影模型

第一阶段至少冻结以下输出模型：

```text
TickSnapshot
  - capture_id
  - sequence
  - timestamp
  - totals
  - dropped_packets
  - last_packet_timestamp
  - top_connections
  - top_hosts
  - top_services

FinalSnapshot
  - capture_id
  - started_at
  - ended_at
  - totals
  - dropped_packets
  - last_packet_timestamp
  - aggregate_summary
```

这一层非常关键。

阶段二 UI 如果要少返工，就必须从第一阶段开始，把输出对象当成稳定契约，而不是把 CLI 打印结果当契约。

---

## 7. `flowarden-error` 接入方案

## 7.1 接入原则

第一阶段建议把错误处理当成基础设施优先项，而不是编码结束后再补。

所有核心模块统一返回：

```rust
flowarden_error::Result<T>
```

推荐模式：

- 第三方库错误进入本项目边界时立即 `or_err(...)`
- 跨层返回时追加 `err_context(...)`
- CLI 出口再补 `.into_cli()`

## 7.2 当前 `flowarden-error` 的适用方式

现有 `flowarden-error` 已具备：

- `ErrorType`
- `ErrorSource`
- `or_err`
- `or_err_with`
- `err_context`
- `or_fail`

第一阶段应直接复用这些能力，不要在 core 再做包装层。

## 7.3 建议补齐的错误类型

为了让第一阶段错误语义更清楚，建议优先在 `flowarden-error` 中补齐一批领域错误类型。

建议新增方向：

1. `InvalidInput`
2. `PermissionDenied`
3. `UnsupportedLinkType`
4. `PacketDecodeError`
5. `FilterApplyError`
6. `DeviceNotFound`
7. `CaptureStartError`
8. `CaptureStopError`

如果你希望保持 `ErrorType` 更克制，也至少应统一用少量稳定 `Custom(...)` 常量，而不是在各处随意拼不同错误字符串。

## 7.4 错误来源约定

建议这样使用 `ErrorSource`：

- 抓包、链路、包读取错误：`Network`
- CLI 参数、输出路径、终端格式错误：`CliTerminal`
- 状态机、聚合、排序、内部断言错误：`Internal`

## 7.5 代码约束

第一阶段建议作为强制规范写入：

1. `flowarden-core` 关键路径禁止直接 `unwrap` / `expect`
2. 任何对外暴露函数都必须返回统一错误类型
3. 错误上下文里应带关键现场：
   - device 名
   - file path
   - bpf
   - link type
   - capture mode
4. CLI 只负责把错误展示清楚，不吞错误上下文

---

## 8. CLI 契约建议

## 8.1 最小命令集

建议第一阶段只保留两个主命令：

```bash
flowarden devices
flowarden capture
```

这是一个有意收敛的 `YAGNI` 取舍。

先不拆太多子命令，避免阶段一 CLI 表面很完整，核心却没收实。

## 8.2 `devices`

目标：

- 列出当前可用抓包设备

建议参数：

```bash
flowarden devices
flowarden devices --format json
```

输出字段建议包含：

- name
- description
- addresses
- supported_link_type 或当前可识别 link type

## 8.3 `capture`

建议参数：

```bash
flowarden capture --device en0
flowarden capture --read ./fixtures/http.pcap
flowarden capture --device en0 --bpf "tcp or udp"
flowarden capture --device en0 --duration 30
flowarden capture --device en0 --format json
flowarden capture --device en0 --output ./out/report.json
flowarden capture --device en0 --top 20
```

建议强约束：

1. `--device` 与 `--read` 互斥
2. 至少提供一个 source
3. 默认 `tick_interval = 1s`
4. 默认输出 `table`
5. `--duration` 缺省时持续运行，直到 `Ctrl+C`

## 8.4 首版不建议暴露的参数

以下参数首版不建议暴露：

1. 自定义线程数
2. 多档运行模式
3. payload 开关
4. 会话缓存大小
5. 多级输出格式插件

这些都不是第一阶段闭环所必需。

---

## 9. 详细开发任务拆解

## 9.1 任务 0：补齐基础设施

目标：

- 把后续开发最容易反复返工的基础问题先压住

任务内容：

1. 明确 `flowarden-core` 公共返回类型和 prelude
2. 补齐 `flowarden-error` 的领域错误类型或稳定常量
3. 确定 CLI 参数库
4. 确定日志方案

建议取舍：

- CLI 参数库可采用 `clap`
- 日志先保持 `log` 体系，避免第一阶段引入更大 observability 栈

验收标准：

1. core 对外函数签名统一
2. `flowarden-error` 能表达第一阶段主要错误
3. CLI 基本骨架可编译

## 9.2 任务 1：设备发现与输入校验

目标：

- 打通 `devices` 和 capture source 校验

任务内容：

1. 完善 `DeviceEx`
2. 增加设备枚举 API
3. 统一 device/file source 校验
4. 为不可用设备、不存在文件输出明确错误

验收标准：

1. `flowarden devices` 可运行
2. 错误设备名返回清晰错误
3. 不存在的 `pcap` 路径返回清晰错误

## 9.3 任务 2：抓包运行时

目标：

- 实现 live/offline 共用输入循环

任务内容：

1. 实现 `CaptureRuntime`
2. 统一 live/offline `next_packet`
3. 支持 BPF
4. 支持时长限制
5. live/offline 两种 tick 推进语义
6. 支持优雅停止
7. 在 runtime 层保留 pause/resume 能力
8. 可选支持实时落盘 `pcap`

建议取舍：

- 首版只做 `Ctrl+C` 优雅退出
- pause/resume 先保留 core 内能力，不强制作为 CLI 首版用户入口
- 如果 `savefile` 实现代价低，优先并入阶段一；否则明确排入阶段一.1

验收标准：

1. live capture 能持续输出 tick
2. offline `pcap` 能完整回放
3. offline tick 依据 `pcap` 时间戳而不是文件读取速度
4. `Ctrl+C` 退出时能打印 final summary

## 9.4 任务 3：包解码与分类

目标：

- 打通从原始包到分类结果的完整路径

任务内容：

1. 按 link type 解码
2. 解析 IPv4/IPv6/TCP/UDP/ICMP 基础字段
3. 方向判定
4. 服务识别
5. 异常包和不支持包的统计策略

关键要求：

- 解析失败不应直接打崩整个捕获循环
- 不支持的链路类型要能报告并降级处理

验收标准：

1. 对样本 `pcap`，协议类型识别正确
2. 方向和服务对验收样本结果可解释，且不退化成简单目标端口映射
3. malformed packet 不导致整个流程退出

## 9.5 任务 4：聚合器与快照模型

目标：

- 把逐包数据变成稳定的秒级结果

任务内容：

1. 设计 `FlowKey`
2. 实现全局聚合和 tick 聚合
3. 生成 top connections / hosts / services
4. 纳入 `dropped_packets` 与 `last_packet_timestamp`
5. offline gap 的时间片表达
6. 生成 final summary

关键要求：

- live 和 offline 共用同一套聚合模型
- tick 输出顺序稳定
- 排序规则稳定、可测试

验收标准：

1. 同一输入多次运行输出一致
2. 总包数、总字节数、top 排名可复核

## 9.6 任务 5：CLI 格式化与文件输出

目标：

- 让阶段一结果真正可消费

任务内容：

1. `table` formatter
2. `json` formatter
3. 输出到 stdout
4. 输出到文件
5. 错误时 stderr 与退出码约定

建议取舍：

- `json` 结构先稳定
- `table` 只求清晰，不追求复杂终端 UI

验收标准：

1. `json` 可被脚本稳定解析
2. `table` 适合人工查看
3. 写文件失败时错误清楚

## 9.7 任务 6：测试、基准样本与封板

目标：

- 保证第一阶段不是一次性演示品

任务内容：

1. 单元测试
2. `pcap` 集成测试
3. golden output
4. 基础长稳测试
5. README/使用说明

建议样本集：

1. `tcp_http_basic.pcap`
2. `udp_dns_basic.pcap`
3. `tls_clienthello_basic.pcap`
4. `mixed_ipv4_ipv6.pcap`
5. `loopback_basic.pcap`
6. `malformed_packets.pcap`
7. `pcap_with_time_gaps.pcap`

验收标准：

1. `cargo test` 通过
2. 样本回归结果稳定
3. 文档足够支持重复验收

---

## 10. 建议的开发顺序

如果按降低返工的角度，我建议严格按以下顺序推进：

1. 先补 `flowarden-error`
2. 再做 `devices` 和 source 校验
3. 再做 capture runtime
4. 再做 decoder + direction + service
5. 再做 aggregator + snapshot
6. 最后做 CLI format 和测试封板

不建议的顺序：

- 先做复杂 CLI
- 先做 service mode
- 先做大量协议支持
- 先做多线程重构

因为这些都会在第一阶段制造不必要的分叉。

---

## 11. 第一阶段测试策略

## 11.1 单元测试

重点覆盖：

1. link type 映射
2. 方向判断
3. 服务识别
4. `FlowKey` 生成
5. 排序规则
6. snapshot 序列化
7. offline 时间推进规则

## 11.2 集成测试

重点覆盖：

1. 读取固定 `pcap`
2. 比较 final summary
3. 比较 top service / top connection
4. 校验 malformed packet 不崩
5. 校验 offline gap 与 tick 节奏

## 11.3 回归测试

建议对固定 `pcap` 生成 golden JSON。

每次改动后对比：

1. totals
2. top connections
3. top services
4. tick sequence

## 11.4 长稳测试

建议至少保留一个基础长稳场景：

- live capture 运行 30 分钟
- 观测内存是否异常持续增长
- 观测异常退出、死循环、日志泛滥

---

## 12. 第一阶段质量门禁

建议把以下项作为合并门禁，而不是“尽量做到”。

1. `cargo fmt`
2. `cargo clippy`
3. `cargo test`
4. 核心路径无新增 `unwrap` / `expect`
5. 新增 public API 有最基本文档或命名自解释
6. 对外输出模型保持稳定
7. 可预期错误必须使用 `flowarden-error`

如果这些门禁不立住，后续第二阶段 UI 接入时质量会快速下滑。

---

## 13. 第一阶段验收清单

评审和验收时，建议逐项检查：

### 功能

1. `flowarden devices` 可用
2. `flowarden capture --device ...` 可用
3. `flowarden capture --read ...` 可用
4. BPF 可用
5. `table` 输出可读
6. `json` 输出稳定

### 正确性

1. 样本 `pcap` 的 totals 可复核
2. top services 排名合理
3. top connections 排名合理
4. 方向判断在 live/offline 下可解释
5. offline 回放按 `pcap` 时间戳推进而不是按读取速度推进
6. `dropped_packets` 与 `last_packet_timestamp` 可见且可解释

### 健壮性

1. 不存在设备名时错误清楚
2. 不存在文件时错误清楚
3. 不支持 link type 时错误或降级行为清楚
4. malformed packet 不导致主循环崩溃
5. `Ctrl+C` 能优雅退出
6. pause/resume 在核心层未丢失

### 质量

1. 测试齐备
2. 错误统一走 `flowarden-error`
3. 没有因为“先兼容未来”而引入明显过度设计
4. 所有权与分配路径经过审视，不存在可直接转换却先构造临时容器的写法，例如 `&[u8] -> Box<[u8]>` 必须优先使用直接 `into()`

---

## 14. 我建议你重点评审的点

如果你准备评审第一阶段方案，我建议重点看以下几个决定是否认可：

1. 第一阶段坚持“单 worker 主循环”，不提前做复杂并发流水线。
2. 第一阶段只冻结 `snapshot` 契约，不提前实现 gRPC。
3. 第一阶段的服务识别先覆盖高价值常见协议，不追求大而全。
4. 第一阶段先把 `flowarden-error` 补齐，再写大量业务代码。
5. 第一阶段将 pause/resume 从“CLI 首版必做”收敛为“核心能力预留，用户入口可后置”。
6. 第一阶段把 offline 时间轴、抓包质量指标、方向/服务启发式判断视为不可丢失的 Sniffnet 核心能力。

如果这五点都认可，第一阶段的实施路径会明显更稳，也更符合“借鉴 Sniffnet + YAGNI + 质量高于 Sniffnet”的要求。
