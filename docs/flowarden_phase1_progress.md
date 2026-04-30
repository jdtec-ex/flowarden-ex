# Flowarden 第一阶段执行进度记录

## 1. 文档目的

本文用于记录第一阶段 backlog 的实际执行状态、完成证据和未完成项。

状态约定：

- `not_started`
- `in_progress`
- `completed`
- `blocked`

本文只记录已经实际推进过的任务，不替代 `flowarden_phase1_backlog.md`。

---

## 2. 当前状态总览

截至当前，第一阶段状态如下：

| 任务 | 状态 | 说明 |
| --- | --- | --- |
| `P1-001` | `completed` | 错误处理基线已建立 |
| `P1-002` | `completed` | CLI 最小命令集已建立 |
| `P1-003` | `completed` | 设备枚举已可用 |
| `P1-004` | `completed` | capture source 校验已落地 |
| `P1-005` | `completed` | runtime 主循环与 graceful stop 已验收 |
| `P1-006` | `completed` | BPF、link type、unsupported 行为已验收 |
| `P1-007` | `completed` | 包解码器已按 backlog 验收完成 |
| `P1-008` | `completed` | 方向判定与服务识别已按 backlog 验收完成 |
| `P1-009` | `completed` | 聚合器与时间推进已按 backlog 验收完成 |
| `P1-010` | `completed` | 输出格式化与文件输出已按 backlog 验收完成 |
| `P1-011` | `not_started` | 未开始 |
| `P1-012` | `not_started` | 未开始 |
| `P1-101` | `not_started` | 未开始 |

---

## 3. 已完成任务记录

## P1-001 统一错误处理基线

### 状态

- `completed`

### 完成内容

1. 在 `flowarden-error` 中补齐第一阶段领域错误类型：
   - `InvalidInput`
   - `PermissionDenied`
   - `UnsupportedLinkType`
   - `PacketDecodeError`
   - `FilterApplyError`
   - `DeviceNotFound`
   - `DeviceListError`
   - `CaptureStartError`
   - `CaptureStopError`
2. `flowarden-core` 继续统一使用 `flowarden_error::Result<T>`。
3. CLI 和 core 的错误链已经能统一展示上下文。

### 主要代码位置

- `flowarden/flowarden-error/src/lib.rs`
- `flowarden/flowarden-core/src/lib.rs`

### 验收依据

1. 设备不存在、文件不存在、capture 打开失败等路径都已走统一错误体系。
2. live capture 权限不足时，错误链能明确显示：
   - `CaptureStartError`
   - network cause
   - libpcap 原始原因

---

## P1-002 CLI 骨架与命令模型

### 状态

- `completed`

### 完成内容

1. 引入 `clap`。
2. 建立两个主命令：
   - `devices`
   - `capture`
3. 建立最小命令模型：
   - `CaptureOptions`
   - `OutputOptions`
4. 保留 `--device` / `--read` 互斥约束。

### 主要代码位置

- `flowarden/flowarden/Cargo.toml`
- `flowarden/flowarden/src/cli.rs`
- `flowarden/flowarden/src/main.rs`

### 验收依据

1. `cargo run -p flowarden -- devices`
2. `cargo run -p flowarden -- capture --device ...`
3. CLI 参数错误时返回统一错误

---

## P1-003 设备模型与设备枚举

### 状态

- `completed`

### 完成内容

1. 增加 `DeviceSummary` 和 `DeviceAddressSummary`。
2. 增加 `list_devices()`。
3. 设备列表可输出 table/json 两种格式。

### 主要代码位置

- `flowarden/flowarden-core/src/device/mod.rs`
- `flowarden/flowarden/src/main.rs`

### 验收依据

手工验证通过：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo run -p flowarden -- devices
cargo run -p flowarden -- devices --format json
```

---

## P1-004 输入源校验与 capture source 收敛

### 状态

- `completed`

### 完成内容

1. `CaptureSource::from_device_name(...)`
2. `CaptureSource::from_file_path(...)`
3. source 校验下沉到 core
4. CLI 不再直接承担 source 合法性判断

### 主要代码位置

- `flowarden/flowarden-core/src/capture/source.rs`
- `flowarden/flowarden-core/src/device/mod.rs`
- `flowarden/flowarden/src/cli.rs`

### 验收依据

手工验证通过：

```bash
cargo run -p flowarden -- capture --device does-not-exist
cargo run -p flowarden -- capture --read /tmp/does-not-exist.pcap
```

错误输出清晰，且走统一错误链。

---

## P1-005 CaptureRuntime 主循环

### 状态

- `completed`

### 完成内容

1. 新建 `capture/runtime.rs`
2. 建立 live/offline 共用同步主循环
3. 引入 stop flag
4. 接入 CLI 的 `Ctrl+C` graceful stop
5. offline 读到 EOF 正常退出
6. duration limit 到时正常退出
7. 新增 offline 自动化测试

### 主要代码位置

- `flowarden/flowarden-core/src/capture/runtime.rs`
- `flowarden/flowarden-core/src/capture/context.rs`
- `flowarden/flowarden/src/main.rs`

### 自动化验证

通过：

```bash
cargo test -q -p flowarden-core -p flowarden -p flowarden-error
```

其中包含：

1. `offline_runtime_reads_complete_file`
2. `stop_handle_requests_runtime_exit`

### 手工验收依据

用户已手工确认：

1. live capture 能真实运行
2. `Ctrl+C` graceful close 正常
3. 最终 report 可正常输出

### 结论

按 backlog 验收口径，`P1-005` 已完成。

---

## P1-006 BPF、链路类型与运行参数接入

### 状态

- `completed`

### 完成内容

1. BPF 接入 runtime，并统一在 capture 层应用
2. runtime report 增加 `link_type`
3. unsupported link type 明确报 `UnsupportedLinkType`
4. CLI 最终输出中显式显示 `bpf` 与 `link_type`
5. 新增 offline BPF 自动化测试

### 主要代码位置

- `flowarden/flowarden-core/src/capture/context.rs`
- `flowarden/flowarden-core/src/capture/runtime.rs`
- `flowarden/flowarden/src/main.rs`

### 自动化验证

通过：

```bash
cargo test -q -p flowarden-core -p flowarden -p flowarden-error
```

新增覆盖：

1. `offline_runtime_applies_bpf_filter`
2. offline runtime 的 `link_type` 断言

### 手工验收依据

用户已手工给出 live capture 输出：

```text
capture completed: mode=live, link_type="Link type: NULL (BSD loopback)", packets_seen=87, bytes_seen=8456, timed_out_ticks=10, stopped_by_request=false, bpf=Some("tcp"), format=Table, top_n=20, output_path=None
```

可据此确认：

1. live capture 已实际运行
2. `link_type` 已进入后续链路
3. `bpf=Some("tcp")` 已进入最终输出，可作为手工验证证据

### 结论

按 backlog 验收口径，`P1-006` 已完成。

---

## P1-007 包解码器

### 状态

- `completed`

### 完成内容

1. 新建 `analysis` 模块与 decoder 入口：
   - `analysis/mod.rs`
   - `analysis/packet.rs`
   - `analysis/decoder.rs`
2. 建立统一模型：
   - `PacketEnvelope`
   - `DecodedPacket`
   - `TransportProtocol`
3. `DecodedPacket` 已覆盖阶段一所需最小字段：
   - 时间戳
   - IP
   - 端口
   - 协议
   - `tcp_flags`
   - `packet_len`
4. decoder 已支持 backlog 要求的 link types：
   - `Ethernet`
   - `RawIp`
   - `IPv4`
   - `IPv6`
   - `LinuxSll`
   - `LinuxSll2`
   - `Loop`
   - `Null`
5. `capture/runtime.rs` 已接入 decoder，并统计：
   - `packets_decoded`
   - `packets_decode_failed`
6. malformed packet 只影响当前包，不会打崩 runtime 主循环。
7. 解码错误统一走 `flowarden-error` 的 `PacketDecodeError` / `UnsupportedLinkType`。

### 主要代码位置

- `flowarden/flowarden-core/src/analysis/mod.rs`
- `flowarden/flowarden-core/src/analysis/packet.rs`
- `flowarden/flowarden-core/src/analysis/decoder.rs`
- `flowarden/flowarden-core/src/capture/runtime.rs`

### 自动化验证

通过：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo test -q -p flowarden-core
```

当前 `flowarden-core` 共 16 个测试全部通过，其中与 `P1-007` 直接相关的新增验证包括：

1. `decodes_ethernet_ipv4_tcp_packet`
2. `decodes_null_ipv4_udp_packet`
3. `decodes_loop_ipv4_udp_packet`
4. `decodes_raw_ipv4_udp_packet`
5. `decodes_raw_ipv6_udp_packet`
6. `decodes_ipv4_link_type_udp_packet`
7. `decodes_ipv6_link_type_udp_packet`
8. `decodes_linux_sll_udp_packet`
9. `decodes_linux_sll2_udp_packet`
10. `decodes_first_packet_from_sample_pcap`
11. `runtime_continues_after_malformed_packet`

### 验收依据

对应 backlog 的三条验收条件，当前结论如下：

1. 样本 `pcap` 的协议类型识别正确
   - 已满足
   - `decodes_first_packet_from_sample_pcap` 和 `decodes_ethernet_ipv4_tcp_packet` 已验证 TCP/IP/端口/flags 识别
2. malformed packet 仅影响当前包，不拖垮整个捕获流程
   - 已满足
   - `runtime_continues_after_malformed_packet` 已验证 runtime 会继续处理后续统计并正常退出
3. decoder 输出字段足够支撑方向、服务、聚合
   - 已满足
   - 当前 `DecodedPacket` 已提供 `src/dst ip`、`src/dst port`、`transport_protocol`、`tcp_flags`、`packet_len`

### 结论

按 backlog 验收口径，`P1-007` 已完成。

---

## P1-008 方向判定与服务识别

### 状态

- `completed`

### 完成内容

1. 新增分类模型：
   - `TrafficDirection`
   - `ServiceConfidence`
   - `ServiceLabel`
   - `ClassifiedPacket`
2. 新增 `analysis/direction.rs`
   - live 模式基于本机地址判断方向
   - loopback 特判
   - `0.0.0.0` / `::` 未分配地址特判
   - offline 模式私网/公网保守回退策略
3. 新增 `analysis/service.rs`
   - 服务识别综合协议、源端口、目标端口、方向
   - 不退化成简单“只看目标端口”
   - 支持常见阶段一服务：
     - `ssh`
     - `dns`
     - `http`
     - `https`
     - `ntp`
     - `mdns`
     - `quic`
     - `dhcp`
     - `icmp`
     - `icmpv6`
4. 新增 `classify_packet(...)`
   - 把 `DecodedPacket` 转成 `ClassifiedPacket`

### 主要代码位置

- `flowarden/flowarden-core/src/analysis/packet.rs`
- `flowarden/flowarden-core/src/analysis/direction.rs`
- `flowarden/flowarden-core/src/analysis/service.rs`
- `flowarden/flowarden-core/src/analysis/mod.rs`

### 自动化验证

通过：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo test -q -p flowarden-core
```

当前 `flowarden-core` 共 30 个测试全部通过，其中与 `P1-008` 直接相关的验证包括：

1. `marks_loopback_pair_as_local`
2. `marks_unspecified_as_unknown`
3. `marks_live_local_to_remote_as_outbound`
4. `marks_live_remote_to_local_as_inbound`
5. `uses_offline_private_to_public_fallback`
6. `uses_offline_public_to_private_fallback`
7. `offline_public_to_public_stays_unknown`
8. `outbound_uses_destination_port_for_service`
9. `inbound_uses_source_port_for_service`
10. `unknown_direction_does_not_degenerate_to_destination_only`
11. `local_direction_returns_low_confidence_for_well_known_service`
12. `icmp_is_classified_without_ports`
13. `classify_packet_combines_direction_and_service`
14. `sample_pcap_packet_classifies_to_outbound_https`

### 验收依据

对应 backlog 的三条验收条件，当前结论如下：

1. 方向判定在样本 `pcap` 上结果可解释
   - 已满足
   - `sample_pcap_packet_classifies_to_outbound_https` 验证了解码后的真实样本包可被解释为 `Outbound`
2. 服务识别不退化成简单目标端口映射
   - 已满足
   - `unknown_direction_does_not_degenerate_to_destination_only` 已验证在 `Unknown` 场景下会综合源/目标端口选择服务
3. live/offline 统计语义保持一致
   - 已满足当前任务口径
   - 当前方向/服务分类为纯函数模型，live 与 offline 共享同一套分类逻辑，未引入模式分叉统计语义

### 结论

按 backlog 验收口径，`P1-008` 已完成。

---

## P1-009 聚合器与时间推进

### 状态

- `completed`

### 完成内容

1. 新建 `flow` 模块：
   - `flow/mod.rs`
   - `flow/aggregator.rs`
2. 建立第一阶段稳定输出模型：
   - `FlowKey`
   - `AggregateTotals`
   - `FlowCounters`
   - `HostCounters`
   - `ServiceCounters`
   - `TickSnapshot`
   - `FinalSnapshot`
   - `AggregateSummary`
3. 实现 `FlowAggregator`
   - 全局聚合
   - 秒级 tick 聚合
   - `top_connections`
   - `top_hosts`
   - `top_services`
4. 实现时间推进语义：
   - offline 模式按 `pcap` 时间戳推进
   - live 模式按 wall clock tick 推进
   - 跨秒空洞可表达为零增量 tick
5. 将 `decode -> classify -> aggregate -> snapshot` 链路接入 `capture/runtime.rs`
6. `RuntimeReport` 现在已携带：
   - `tick_snapshots`
   - `final_snapshot`
7. `dropped_packets` 与 `last_packet_timestamp` 已进入 snapshot 模型

### 主要代码位置

- `flowarden/flowarden-core/src/flow/mod.rs`
- `flowarden/flowarden-core/src/flow/aggregator.rs`
- `flowarden/flowarden-core/src/capture/runtime.rs`
- `flowarden/flowarden-core/src/lib.rs`

### 自动化验证

通过：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo test -q -p flowarden-core
```

当前 `flowarden-core` 共 33 个测试全部通过，其中与 `P1-009` 直接相关的新增验证包括：

1. `offline_time_gap_emits_zero_tick`
2. `same_input_produces_stable_output`
3. `rankings_are_sorted_deterministically`
4. `offline_runtime_reads_complete_file`
5. `offline_runtime_applies_bpf_filter`

### 验收依据

对应 backlog 的三条验收条件，当前结论如下：

1. 同一输入多次运行结果稳定
   - 已满足
   - `same_input_produces_stable_output` 已验证相同输入会生成相同 snapshots
2. offline 回放按 `pcap` 时间戳推进，而不是按读取速度推进
   - 已满足
   - `offline_time_gap_emits_zero_tick` 已验证跨秒 gap 会生成零增量 tick
3. 聚合结果可直接供 CLI 和后续 UI 复用
   - 已满足
   - `RuntimeReport` 已携带稳定的 `tick_snapshots` 与 `final_snapshot`

### 结论

按 backlog 验收口径，`P1-009` 已完成。

---

## P1-010 输出格式化与文件输出

### 状态

- `completed`

### 完成内容

1. 新增 CLI 输出模块：
   - `flowarden/src/output.rs`
2. 建立稳定的 capture 输出封装：
   - `CaptureOutput`
3. 实现 `json` formatter
   - 直接序列化 `tick_snapshots + final_snapshot`
4. 实现 `table` formatter
   - 面向人工查看
   - 输出 capture 元信息、totals、top connections、top hosts、top services
5. 接入 stdout / file output
   - `--output` 现在会真正写文件
   - 写文件失败会带输出路径上下文
6. CLI `capture` 现在会：
   - 先渲染 snapshot
   - 再输出到 stdout 或文件
   - 最终 summary 走 `stderr`

### 主要代码位置

- `flowarden/flowarden/src/output.rs`
- `flowarden/flowarden/src/main.rs`
- `flowarden/flowarden/Cargo.toml`

### 自动化验证

通过：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo test -q -p flowarden
cargo test -q
```

其中与 `P1-010` 直接相关的验证包括：

1. `json_output_is_stable_and_parseable`
2. `table_output_is_human_readable`
3. `file_output_writes_expected_content`

### 运行验证

已完成一条实际 CLI 离线 JSON 文件输出验证：

```bash
cargo run -q -p flowarden -- capture --read <sample.pcap> --format json --output <out.json>
```

验证结果：

1. 进程退出码为 `0`
2. `out.json` 被真实创建
3. 输出内容包含稳定的：
   - `tick_snapshots`
   - `final_snapshot`
   - `totals`
   - `top_connections`
   - `top_hosts`
   - `top_services`

### 验收依据

对应 backlog 的三条验收条件，当前结论如下：

1. `json` 可被脚本稳定解析
   - 已满足
   - `json_output_is_stable_and_parseable` 与实际 CLI 文件输出均已验证
2. `table` 适合人工查看
   - 已满足
   - `table_output_is_human_readable` 已覆盖主要展示字段
3. stdout 与 file output 行为一致、可预期
   - 已满足当前任务口径
   - formatter 先统一产出字符串，再由 `emit_output(...)` 决定写 stdout 或 file

### 结论

按 backlog 验收口径，`P1-010` 已完成。

---

## 4. 当前未完成但需注意的事项

这些不是已完成任务的阻塞项，但需要明确记录：

1. `--output` 目前只完成参数解析，尚未真正写文件
   - 属于 `P1-010`
2. 当前 capture 主处理仍然是单 worker 同步 loop
   - 这是有意的第一阶段取舍
   - 不是遗漏
3. channel / gRPC / UI 通信尚未开始
   - 不属于当前已完成任务范围

---

## 5. 下一步

下一步进入：

- `P1-011` 测试资产与回归样本

计划内容：

1. 固定样本 `pcap`
2. 生成 golden JSON
3. 覆盖 CLI / core 的关键回归路径
4. 让第一阶段统计结果具备长期可复核资产
