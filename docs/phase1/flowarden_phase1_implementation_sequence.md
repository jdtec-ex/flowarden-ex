# Flowarden 第一阶段编码顺序与文件落位

## 1. 文档目的

本文在 `flowarden_phase1_backlog.md` 基础上进一步下钻，回答三个直接面向实现的问题：

1. 第一阶段应该按什么编码顺序推进
2. 每个 backlog 项会落到哪些文件
3. 哪些任务适合合并成一次提交，哪些应严格分开

本文只针对当前实际仓库结构：

- `./flowarden/flowarden-core`
- `./flowarden/flowarden`
- `./flowarden/flowarden-error`

不假设第一阶段会做 workspace 大拆分。

---

## 2. 当前文件基线

当前第一阶段相关文件很少，这意味着实施顺序必须尽量稳：

### `flowarden-core`

现有：

- `src/lib.rs`
- `src/config/mod.rs`
- `src/capture/context.rs`
- `src/capture/mod.rs`
- `src/capture/source.rs`
- `src/device/mod.rs`
- `src/device/link_type.rs`
- `src/filters.rs`

### `flowarden`

现有：

- `src/main.rs`

### `flowarden-error`

现有：

- `src/lib.rs`
- `src/thin_str.rs`

结论：

> 第一阶段前半程主要是“补结构”，后半程才是“补功能”。

---

## 3. 第一阶段建议编码顺序

建议按 6 个实现波次推进，而不是严格按 12 个 backlog 单点逐个提交。

### Wave 1：错误模型与 CLI 骨架

对应 backlog：

- `P1-001`
- `P1-002`

### Wave 2：设备与输入源

对应 backlog：

- `P1-003`
- `P1-004`

### Wave 3：抓包运行时

对应 backlog：

- `P1-005`
- `P1-006`

### Wave 4：解析与分类

对应 backlog：

- `P1-007`
- `P1-008`

### Wave 5：聚合与输出

对应 backlog：

- `P1-009`
- `P1-010`

### Wave 6：测试与封板

对应 backlog：

- `P1-011`
- `P1-012`

### 可插入 Wave：建议保留项

对应 backlog：

- `P1-101`

如果主线顺利，建议插在 Wave 3 结束后。

---

## 4. 文件落位总览

## 4.1 `flowarden-core` 预计新增文件

建议新增：

```text
flowarden/flowarden-core/src/capture/runtime.rs
flowarden/flowarden-core/src/analysis/mod.rs
flowarden/flowarden-core/src/analysis/packet.rs
flowarden/flowarden-core/src/analysis/decoder.rs
flowarden/flowarden-core/src/analysis/direction.rs
flowarden/flowarden-core/src/analysis/service.rs
flowarden/flowarden-core/src/analysis/classify.rs
flowarden/flowarden-core/src/flow/mod.rs
flowarden/flowarden-core/src/flow/key.rs
flowarden/flowarden-core/src/flow/counters.rs
flowarden/flowarden-core/src/flow/aggregator.rs
flowarden/flowarden-core/src/projection/mod.rs
flowarden/flowarden-core/src/projection/snapshot.rs
flowarden/flowarden-core/src/projection/summary.rs
```

## 4.2 `flowarden-core` 预计修改文件

建议修改：

```text
flowarden/flowarden-core/src/lib.rs
flowarden/flowarden-core/src/capture/mod.rs
flowarden/flowarden-core/src/capture/context.rs
flowarden/flowarden-core/src/capture/source.rs
flowarden/flowarden-core/src/device/mod.rs
flowarden/flowarden-core/src/device/link_type.rs
flowarden/flowarden-core/src/config/mod.rs
flowarden/flowarden-core/src/filters.rs
```

## 4.3 `flowarden` 预计修改文件

建议修改：

```text
flowarden/flowarden/Cargo.toml
flowarden/flowarden/src/main.rs
```

第一阶段为了控制复杂度，CLI 先不建议拆很多文件；如果 `main.rs` 膨胀，再拆：

```text
flowarden/flowarden/src/cli.rs
flowarden/flowarden/src/output.rs
```

## 4.4 `flowarden-error` 预计修改文件

建议修改：

```text
flowarden/flowarden-error/src/lib.rs
```

---

## 5. 各 backlog 项的文件级落位

## P1-001 统一错误处理基线

### 主要修改文件

- `flowarden/flowarden-error/src/lib.rs`
- `flowarden/flowarden-core/src/lib.rs`

### 可能修改文件

- `flowarden/flowarden-core/src/config/mod.rs`
- `flowarden/flowarden-core/src/capture/context.rs`

### 目标落点

1. 在 `flowarden-error` 中补第一阶段所需领域错误类型，或至少定义稳定常量/固定命名约定。
2. 在 `flowarden-core/src/lib.rs` 中确保 prelude 和统一 `Result<T>` 暴露清晰。
3. 先把现有模块里的错误使用方式统一起来。

### 建议提交边界

建议单独一个提交完成。

原因：

- 这是后续所有任务的基础约束。

---

## P1-002 CLI 骨架与命令模型

### 主要修改文件

- `flowarden/flowarden/Cargo.toml`
- `flowarden/flowarden/src/main.rs`

### 可能新增文件

- `flowarden/flowarden/src/cli.rs`

### 目标落点

1. 引入 `clap`。
2. 建立 `devices` 和 `capture` 两个命令。
3. 在 CLI 层构造 `CaptureOptions` / `OutputOptions`。

### 建议提交边界

建议与 `P1-001` 分开提交。

原因：

- 错误基线和 CLI 骨架是两类变化，拆开更清晰。

---

## P1-003 设备模型与设备枚举

### 主要修改文件

- `flowarden/flowarden-core/src/device/mod.rs`
- `flowarden/flowarden/Cargo.toml`
- `flowarden/flowarden/src/main.rs`

### 可能修改文件

- `flowarden/flowarden-core/src/lib.rs`

### 目标落点

1. 在 `device/mod.rs` 增加设备枚举函数。
2. 让 CLI `devices` 命令实际调用 core。
3. 输出设备模型给 CLI formatter。

### 建议提交边界

可与 `P1-004` 合并为一次提交。

---

## P1-004 输入源校验与 capture source 收敛

### 主要修改文件

- `flowarden/flowarden-core/src/capture/source.rs`
- `flowarden/flowarden/src/main.rs`

### 可能新增文件

- `flowarden/flowarden/src/cli.rs`

### 目标落点

1. 把 device/file 统一映射为 `CaptureSource`。
2. 在进入 runtime 前完成 source 合法性校验。
3. 错误上下文带 file path 或 device name。

### 建议提交边界

建议与 `P1-003` 同一次提交。

原因：

- 设备枚举和 source 校验天然属于 capture 入口层。

---

## P1-005 CaptureRuntime 主循环

### 主要新增文件

- `flowarden/flowarden-core/src/capture/runtime.rs`

### 主要修改文件

- `flowarden/flowarden-core/src/capture/mod.rs`
- `flowarden/flowarden-core/src/capture/context.rs`
- `flowarden/flowarden-core/src/lib.rs`

### 目标落点

1. 新建 runtime 层，避免把读包循环塞回 `context.rs`。
2. 封装 live/offline 共用主循环。
3. 提供 stop 与 core 级 pause/resume 接口。

### 建议提交边界

建议单独一个提交完成。

原因：

- runtime 是第一阶段主骨架，值得单独审查。

---

## P1-006 BPF、链路类型与运行参数接入

### 主要修改文件

- `flowarden/flowarden-core/src/capture/context.rs`
- `flowarden/flowarden-core/src/device/link_type.rs`
- `flowarden/flowarden-core/src/config/mod.rs`
- `flowarden/flowarden-core/src/filters.rs`

### 可能修改文件

- `flowarden/flowarden/src/main.rs`

### 目标落点

1. 统一 BPF 应用入口。
2. 保证 link type 可进入后续 decoder。
3. 明确不支持 link type 的错误或降级行为。

### 建议提交边界

建议与 `P1-005` 分开提交。

原因：

- runtime 主循环和运行参数/BPF 接入虽然相关，但拆开审查更清楚。

---

## P1-007 包解码器

### 主要新增文件

- `flowarden/flowarden-core/src/analysis/mod.rs`
- `flowarden/flowarden-core/src/analysis/packet.rs`
- `flowarden/flowarden-core/src/analysis/decoder.rs`

### 主要修改文件

- `flowarden/flowarden-core/src/lib.rs`

### 目标落点

1. 新建 `analysis` 模块。
2. 先把 `DecodedPacket` 与 decoder 主入口建起来。
3. 只解析阶段一需要的字段。

### 建议提交边界

建议单独一个提交完成。

原因：

- decoder 是后续 direction/service/aggregator 的共同上游。

---

## P1-008 方向判定与服务识别

### 主要新增文件

- `flowarden/flowarden-core/src/analysis/direction.rs`
- `flowarden/flowarden-core/src/analysis/service.rs`
- `flowarden/flowarden-core/src/analysis/classify.rs`

### 主要修改文件

- `flowarden/flowarden-core/src/analysis/mod.rs`
- `flowarden/flowarden-core/src/device/mod.rs`

### 目标落点

1. `direction.rs` 负责方向规则。
2. `service.rs` 负责服务识别启发式。
3. `classify.rs` 把 `DecodedPacket` 变成 `ClassifiedPacket`。

### 建议提交边界

建议作为单独提交。

原因：

- 这是第一阶段最容易做偏的逻辑，应该可单独 review。

---

## P1-009 聚合器与时间推进

### 主要新增文件

- `flowarden/flowarden-core/src/flow/mod.rs`
- `flowarden/flowarden-core/src/flow/key.rs`
- `flowarden/flowarden-core/src/flow/counters.rs`
- `flowarden/flowarden-core/src/flow/aggregator.rs`
- `flowarden/flowarden-core/src/projection/mod.rs`
- `flowarden/flowarden-core/src/projection/snapshot.rs`
- `flowarden/flowarden-core/src/projection/summary.rs`

### 主要修改文件

- `flowarden/flowarden-core/src/lib.rs`
- `flowarden/flowarden-core/src/capture/runtime.rs`

### 目标落点

1. 新建 `flow` 和 `projection` 模块。
2. 将 live/offline 时间推进差异内聚到 aggregator 逻辑中。
3. 生成稳定的 `TickSnapshot` / `FinalSnapshot`。

### 建议提交边界

建议拆成两个连续提交：

1. `flow` 数据结构和聚合器
2. `projection` 输出模型与 runtime 接线

原因：

- 这样能把“内部聚合逻辑”和“对外输出契约”分开评审。

---

## P1-010 输出格式化与文件输出

### 主要修改文件

- `flowarden/flowarden/src/main.rs`

### 可能新增文件

- `flowarden/flowarden/src/output.rs`

### 目标落点

1. CLI formatter 放在可执行 crate，不污染 core。
2. `json` 和 `table` 在 CLI 层格式化。
3. 文件输出也放在 CLI 层处理。

### 建议提交边界

建议单独一个提交完成。

原因：

- 这一步主要是适配层，不要和 core 聚合逻辑搅在一起。

---

## P1-011 测试资产与回归样本

### 主要新增文件

建议至少新增：

```text
flowarden/flowarden-core/tests/*.rs
flowarden/flowarden-core/tests/fixtures/*.pcap
flowarden/flowarden-core/tests/golden/*.json
```

### 可能修改文件

- `flowarden/flowarden-core/Cargo.toml`
- `flowarden/flowarden/Cargo.toml`

### 目标落点

1. 测试尽量放在 `flowarden-core`，因为核心逻辑在那里。
2. CLI 只做少量烟雾测试即可。
3. golden output 主要校验 snapshot 契约。

### 建议提交边界

建议至少拆成两个提交：

1. 单元测试 + fixtures
2. golden output + 集成测试

---

## P1-012 封板与质量门禁

### 主要修改文件

- `flowarden/README.md`
- 可能新增阶段一运行说明文档

### 目标落点

1. 更新根 README 或执行层 README。
2. 固定第一阶段运行方式、样本、验收命令。
3. 固定质量门禁命令。

### 建议提交边界

建议单独一个提交完成。

原因：

- 这是封板动作，不应该混在业务代码提交里。

---

## P1-101 实时抓包同时落盘 `pcap`

### 主要修改文件

- `flowarden/flowarden-core/src/capture/context.rs`
- `flowarden/flowarden-core/src/capture/runtime.rs`

### 可能修改文件

- `flowarden/flowarden/src/main.rs`

### 目标落点

1. 保持 capture 层可选 savefile 能力。
2. 不把导出逻辑扩散到 analysis/flow/projection。

### 建议提交边界

建议单独一个提交完成。

原因：

- 这是建议保留项，不应干扰主线封板节奏。

---

## 6. 建议的新模块暴露顺序

`flowarden-core/src/lib.rs` 不建议一次性暴露所有新增模块。

建议顺序：

1. 先暴露 `capture::runtime`
2. 再暴露 `analysis`
3. 再暴露 `flow`
4. 最后暴露 `projection`

原因：

- 可以确保上层只依赖已经稳定的模块。

---

## 7. 建议的提交边界

建议总提交边界如下：

1. `P1-001` 错误基线
2. `P1-002` CLI 骨架
3. `P1-003 + P1-004` 设备与 source
4. `P1-005` runtime 主循环
5. `P1-006` BPF 与 link type
6. `P1-007` decoder
7. `P1-008` direction + service classify
8. `P1-009a` flow 聚合器
9. `P1-009b` projection + runtime 接线
10. `P1-010` 输出格式化
11. `P1-011a` 单元测试与 fixtures
12. `P1-011b` 集成测试与 golden
13. `P1-012` README 与封板
14. `P1-101` savefile，可选

这条边界的好处是：

1. 每次提交都能解释清楚。
2. 每次 review 的职责边界明确。
3. 关键逻辑不会因为“大杂烩提交”而失焦。

---

## 8. 最先应该创建的文件

如果现在就开始编码，我建议先创建这些最关键的新文件：

1. `flowarden/flowarden-core/src/capture/runtime.rs`
2. `flowarden/flowarden-core/src/analysis/mod.rs`
3. `flowarden/flowarden-core/src/analysis/packet.rs`
4. `flowarden/flowarden-core/src/analysis/decoder.rs`
5. `flowarden/flowarden-core/src/flow/mod.rs`
6. `flowarden/flowarden-core/src/flow/aggregator.rs`
7. `flowarden/flowarden-core/src/projection/mod.rs`
8. `flowarden/flowarden-core/src/projection/snapshot.rs`

原因：

- 这些文件基本决定了第一阶段的主骨架。

---

## 9. 我建议你重点评审的实现决策

如果你要继续审这份实施文档，我建议重点拍板以下 5 个决定：

1. CLI 首版是否保持只有 `devices` 和 `capture` 两个主命令。
2. `flowarden` CLI 是否先不拆太多文件，避免前期结构噪音。
3. `runtime` 是否作为独立文件建立，而不是把循环继续堆进 `context.rs`。
4. `flow` 与 `projection` 是否在第一阶段就明确拆开。
5. `savefile` 是否作为 `P1-101` 放在阶段一.1，而不是主线硬门槛。

如果这 5 个点确认，后续实际编码会顺很多。

---

## 10. 最终建议

第一阶段编码时，最容易犯的错误不是“少写一个模块”，而是：

1. 把 CLI、runtime、analysis、projection 混写到一起。
2. 为未来第二、三阶段提前引入太多抽象。
3. 把 runtime 主循环做成一个过大的上帝函数。

按这份文档推进，可以比较稳地把第一阶段做成：

> 结构清楚、错误统一、可测试、可验收，并且后续能直接承接第二阶段 UI 的 headless core。
