# Flowarden 第一阶段开发 Backlog

## 1. 文档目的

本文将第一阶段实施方案拆成可执行 backlog，目标是让后续开发可以直接进入排期和实现，而不是继续停留在原则层。

任务设计遵循以下约束：

1. 借鉴 Sniffnet 核心能力
2. 严格遵循 `YAGNI`
3. 代码质量目标高于 Sniffnet
4. 错误处理统一使用 `flowarden-error`

对应的编码顺序、文件落位和建议提交边界见：

- `flowarden_phase1_implementation_sequence.md`

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

- `P0`：阶段一不可缺少
- `P1`：阶段一建议完成
- `P1.1`：阶段一后续小版本可补

本文默认所有 `P1-xxx` 都是阶段一主线任务。

---

## 3. 总体依赖顺序

建议顺序如下：

```text
P1-001 -> P1-002 -> P1-003 -> P1-004
                       |
                       v
P1-005 -> P1-006 -> P1-007 -> P1-008 -> P1-009
                                   |
                                   v
                          P1-010 -> P1-011 -> P1-012

P1-101 可作为阶段一.1 插入在 P1-006 之后
```

说明：

- `P1-001` 到 `P1-004` 是基础设施和错误模型。
- `P1-005` 到 `P1-009` 是抓包、解析、聚合主线。
- `P1-010` 到 `P1-012` 是输出、测试、封板。
- `P1-101` 是建议保留但可后置的小版本能力。

---

## 4. Backlog 明细

## P1-001 统一错误处理基线

### 目标

让第一阶段所有模块从一开始就统一在 `flowarden-error` 之上工作，避免后期返工。

### 输入

- 现有 `flowarden-error`
- 现有 `flowarden-core` prelude

### 输出

- 第一阶段错误使用规范
- `flowarden-core` 统一 `Result<T>` 风格
- 必要的领域错误类型或稳定错误常量

### 依赖

- 无

### 实现要点

1. 确认 `flowarden-core` 对外统一返回 `flowarden_error::Result<T>`。
2. 评估并补齐第一阶段需要的错误类型：
   - `InvalidInput`
   - `PermissionDenied`
   - `UnsupportedLinkType`
   - `PacketDecodeError`
   - `FilterApplyError`
   - `DeviceNotFound`
   - `CaptureStartError`
   - `CaptureStopError`
3. 约定第三方错误进入边界立即 `or_err(...)`。
4. 约定 CLI 出口补 `.into_cli()`。

### 验收条件

1. `flowarden-core` 不再暴露杂散错误类型。
2. 第一阶段主要错误都能映射到稳定错误语义。
3. 代码规范中明确禁止核心路径新增 `unwrap` / `expect`。

---

## P1-002 CLI 骨架与命令模型

### 目标

先把 CLI 命令面固定住，但保持最小集合，不提前做复杂命令树。

### 输入

- 第一阶段细化方案中的 CLI 约束

### 输出

- `flowarden devices`
- `flowarden capture`
- 内部 `CaptureOptions` / `OutputOptions`

### 依赖

- `P1-001`

### 实现要点

1. 选择 `clap` 作为 CLI 参数库。
2. 建立最小命令集：
   - `devices`
   - `capture`
3. 约束：
   - `--device` 和 `--read` 互斥
   - 至少提供一个 source
   - 默认 `format = table`
   - 默认 `tick_interval = 1s`

### 验收条件

1. CLI 帮助信息可用。
2. 参数错误返回非零退出码。
3. `CaptureOptions` 与 CLI 解析解耦，不把命令行细节扩散到 core 全部模块。

---

## P1-003 设备模型与设备枚举

### 目标

完善 `DeviceEx` 和设备列表能力，为 live capture 做稳定入口。

### 输入

- 现有 `device/`
- `pcap::Device`

### 输出

- 列设备 API
- `devices` 命令输出
- 设备校验逻辑

### 依赖

- `P1-001`
- `P1-002`

### 实现要点

1. 补设备枚举函数。
2. 把 `pcap::Device` 映射到稳定的 `DeviceEx`。
3. 对设备不存在、权限不足等场景给出清晰错误。
4. `devices --format json` 也要可用。

### 验收条件

1. `flowarden devices` 可列设备。
2. 错误设备名时错误清晰。
3. 输出字段足够后续 capture 使用。

---

## P1-004 输入源校验与 capture source 收敛

### 目标

统一设备和文件输入源的合法性校验，形成 capture 前的干净边界。

### 输入

- `CaptureSource`
- CLI source 参数

### 输出

- source 解析逻辑
- file/device 前置校验

### 依赖

- `P1-001`
- `P1-002`
- `P1-003`

### 实现要点

1. `--read` 路径存在性校验。
2. 设备名存在性校验。
3. source 解析结果统一变成 `CaptureSource`。
4. 错误上下文保留 file path 或 device name。

### 验收条件

1. 不存在的 `pcap` 路径报错清楚。
2. 无效设备名报错清楚。
3. 进入 runtime 前，source 一定是已校验状态。

---

## P1-005 CaptureRuntime 主循环

### 目标

打通 live/offline 共用的包读取主循环。

### 输入

- `CaptureSource`
- `CaptureContext`

### 输出

- `capture/runtime.rs`
- 统一 `next_packet` 调度
- stop 控制

### 依赖

- `P1-001`
- `P1-004`

### 实现要点

1. 实现单 worker 主循环。
2. 封装 live/offline 打开、读包、关闭逻辑。
3. 提供优雅停止能力。
4. 核心层保留 pause/resume 能力，不要求 CLI 首版完整暴露。

### 验收条件

1. live 模式可持续读包。
2. offline 模式可完整读完 `pcap`。
3. `Ctrl+C` 退出时不出现资源泄露或异常崩溃。

---

## P1-006 BPF、链路类型与运行参数接入

### 目标

把 Sniffnet 基础抓包能力中的关键运行参数接入到 runtime。

### 输入

- `CaptureOptions`
- `LinkTypeEx`

### 输出

- BPF 生效
- link type 获取
- snaplen / timeout 等基础运行参数

### 依赖

- `P1-005`

### 实现要点

1. BPF 在 live/offline 两侧统一应用。
2. 获取并保存当前 capture 的 link type。
3. 第一阶段继续采用 Sniffnet 风格的轻量抓包取舍。
4. 对不支持 link type 的行为给出明确策略：
   - 明确报错
   - 或明确降级

### 验收条件

1. BPF 可见且可验证。
2. link type 可进入后续 decoder。
3. 不支持 link type 时行为清楚。

---

## P1-007 包解码器

### 目标

把原始包转换成统一 `DecodedPacket`。

### 输入

- `PacketEnvelope`
- `LinkTypeEx`

### 输出

- `analysis/decoder.rs`
- `DecodedPacket`

### 依赖

- `P1-006`

### 实现要点

1. 支持以下 link types：
   - `Ethernet`
   - `RawIp`
   - `IPv4`
   - `IPv6`
   - `LinuxSll`
   - `LinuxSll2`
   - `Loop`
   - `Null`
2. 提取阶段一所需最小字段：
   - IP
   - 端口
   - 协议
   - 长度
   - TCP flags
3. malformed packet 不得直接打崩主循环。
4. 解析错误统一走 `flowarden-error`。

### 验收条件

1. 样本 `pcap` 的协议类型识别正确。
2. malformed packet 仅影响当前包，不拖垮整个捕获流程。
3. decoder 输出字段足够支撑方向、服务、聚合。

---

## P1-008 方向判定与服务识别

### 目标

补齐 Sniffnet 风格的关键分类逻辑。

### 输入

- `DecodedPacket`
- live 本机地址信息
- offline 回退策略

### 输出

- `analysis/direction.rs`
- `analysis/service.rs`
- `ClassifiedPacket`

### 依赖

- `P1-003`
- `P1-007`

### 实现要点

1. 方向判定至少覆盖：
   - 本机地址判断
   - loopback 特判
   - `0.0.0.0` / `::`
   - offline 回退策略
2. 服务识别至少综合：
   - 协议
   - 源端口
   - 目标端口
   - 方向
3. 明确 `Unknown` 或低置信度输出路径。

### 验收条件

1. 方向判定在样本 `pcap` 上结果可解释。
2. 服务识别不退化成简单目标端口映射。
3. live/offline 统计语义保持一致。

---

## P1-009 聚合器与时间推进

### 目标

实现第一阶段最关键的秒级聚合逻辑。

### 输入

- `ClassifiedPacket`
- live/offline 时间推进语义

### 输出

- `flow/aggregator.rs`
- `TickSnapshot`
- `FinalSnapshot`

### 依赖

- `P1-005`
- `P1-008`

### 实现要点

1. 保留 Sniffnet 的聚合优先思路，不做逐包直接输出。
2. live 模式按 wall clock flush。
3. offline 模式按 `pcap` 时间戳 flush。
4. 跨秒空洞时支持 gap 表达。
5. 聚合指标至少包括：
   - totals
   - top connections
   - top hosts
   - top services
   - `dropped_packets`
   - `last_packet_timestamp`

### 验收条件

1. 同一输入多次运行结果稳定。
2. offline 回放按 `pcap` 时间戳推进，而不是按读取速度推进。
3. 聚合结果可直接供 CLI 和后续 UI 复用。

---

## P1-010 输出格式化与文件输出

### 目标

把聚合结果变成可消费的 CLI 输出。

### 输入

- `TickSnapshot`
- `FinalSnapshot`
- `OutputOptions`

### 输出

- `table` formatter
- `json` formatter
- stdout/file output

### 依赖

- `P1-002`
- `P1-009`

### 实现要点

1. `json` 优先稳定。
2. `table` 只做清晰展示，不做复杂终端 UI。
3. 写文件失败时错误要带输出路径上下文。

### 验收条件

1. `json` 可被脚本稳定解析。
2. `table` 适合人工查看。
3. stdout 与 file output 行为一致、可预期。

---

## P1-011 测试资产与回归样本

### 目标

建立第一阶段质量高于 Sniffnet 所必需的可重复验证资产。

### 输入

- 样本 `pcap`
- 输出模型

### 输出

- 单元测试
- 集成测试
- golden JSON
- 建议样本清单

### 依赖

- `P1-007`
- `P1-008`
- `P1-009`
- `P1-010`

### 实现要点

1. 单元测试覆盖：
   - link type
   - 方向判定
   - 服务识别
   - snapshot 序列化
2. 集成测试覆盖：
   - 固定 `pcap`
   - malformed packet
   - offline gap
3. 维护 golden outputs。

### 验收条件

1. `cargo test` 可覆盖第一阶段核心路径。
2. golden output 稳定。
3. 关键统计值可复核。

---

## P1-012 封板与质量门禁

### 目标

把第一阶段从“开发完成”提升到“可评审、可验收、可继续承接 UI”的状态。

### 输入

- 全部前置任务产物

### 输出

- 第一阶段封板版本
- 运行文档
- 验收记录模板

### 依赖

- `P1-010`
- `P1-011`

### 实现要点

1. 固定质量门禁：
   - `cargo fmt`
   - `cargo clippy`
   - `cargo test`
2. 对外文档至少说明：
   - 如何列设备
   - 如何 live capture
   - 如何 offline 回放
   - 如何输出 JSON
3. 明确已保留和明确后置的 Sniffnet 能力。

### 验收条件

1. 第一阶段可被独立评审和重复验收。
2. 文档、测试、产物齐备。
3. 输出模型可以直接作为第二阶段契约起点。

---

## P1-101 实时抓包同时落盘 `pcap`

### 目标

保留 Sniffnet capture abstraction 中很有价值的一项能力，但作为建议保留项，不阻塞第一阶段主线封板。

### 输入

- live capture runtime
- output file path

### 输出

- savefile 支持

### 依赖

- `P1-005`
- `P1-006`

### 实现要点

1. live capture 时可选输出原始 `pcap`。
2. 不影响主分析循环。
3. 写文件错误要明确映射。

### 验收条件

1. 开启时能生成有效 `pcap`。
2. 不开启时不影响主流程。
3. 错误行为清楚、可定位。

---

## 5. 建议排期分组

如果按最小风险排期，建议分 4 组推进。

### 组 A：基础设施

- `P1-001`
- `P1-002`
- `P1-003`
- `P1-004`

### 组 B：主链路

- `P1-005`
- `P1-006`
- `P1-007`
- `P1-008`

### 组 C：结果模型

- `P1-009`
- `P1-010`

### 组 D：封板

- `P1-011`
- `P1-012`

### 组 E：阶段一.1

- `P1-101`

---

## 6. 我建议优先盯紧的任务

从风险角度，我建议你评审和执行时优先盯住以下 4 项：

1. `P1-001`
   - 如果错误模型不先统一，后面一定返工。
2. `P1-005`
   - 如果 runtime 边界不清晰，后续所有模块都会耦合进去。
3. `P1-008`
   - 方向和服务识别是最容易“做了但做偏”的地方。
4. `P1-009`
   - 聚合和 offline 时间推进是 Sniffnet 核心价值的真正承接点。

---

## 7. 最终建议

如果你认可这个 backlog，我建议后续开发执行时按下面方式管理：

1. 每完成一个 `P1-xxx` 就有对应测试或验证记录。
2. 不跨编号乱跳实现，避免先写输出再回头补模型。
3. `P1-001`、`P1-005`、`P1-008`、`P1-009` 应作为阶段一里程碑检查点。

这样推进，第一阶段更容易做成一个质量稳定、边界清楚、后续能直接承接 Avalonia UI 的 headless core。
