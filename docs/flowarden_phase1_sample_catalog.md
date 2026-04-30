# Flowarden 第一阶段样本清单建议

## 1. 目的

本文记录第一阶段已经落地的固定样本，以及后续建议补充的回归样本类型。

目标是让第一阶段的核心路径长期可重复复核，而不是只依赖临时手工验证。

---

## 2. 当前已落地样本

### `offline_mixed_ethernet.pcap`

位置：

- `flowarden/flowarden-core/tests/fixtures/offline_mixed_ethernet.pcap`

覆盖内容：

1. `Ethernet` link type
2. valid outbound `IPv4/TCP/https`
3. malformed packet
4. valid inbound `IPv4/UDP/dns`
5. offline second gap（时间从 `1s` 跳到 `3s`）

配套资产：

1. fixture 说明：
   - `flowarden/flowarden-core/tests/fixtures/README.md`
2. golden JSON：
   - `flowarden/flowarden-core/tests/golden/offline_mixed_ethernet.json`
3. 集成测试：
   - `flowarden/flowarden-core/tests/offline_capture_golden.rs`

---

## 3. 后续建议补充样本

第一阶段封板前，如果时间允许，建议继续补这些固定样本：

1. `offline_null_loopback_ipv4.pcap`
   - 用于稳定覆盖 `Null` / `Loop` link type
2. `offline_linux_sll_dns.pcap`
   - 用于稳定覆盖 `LinuxSll`
3. `offline_linux_sll2_dns.pcap`
   - 用于稳定覆盖 `LinuxSll2`
4. `offline_raw_ipv6_udp.pcap`
   - 用于稳定覆盖 `RawIp` + `IPv6`
5. `offline_unsupported_link_type.pcap`
   - 用于复核 `UnsupportedLinkType` 行为
6. `offline_bpf_split_mix.pcap`
   - 同时包含 `tcp` / `udp` / malformed
   - 用于复核 BPF 过滤后统计值是否稳定

---

## 4. 选样原则

第一阶段样本应遵循以下约束：

1. 样本尽量小，便于审阅和长期维护。
2. 单个样本尽量覆盖多条核心路径，但不要为了“全覆盖”把语义揉得过于复杂。
3. 优先覆盖 Sniffnet 已验证的重要运行语义：
   - offline 时间推进
   - 方向判定
   - 服务识别
   - malformed 容错
   - snapshot 稳定输出
4. 如果存在直接、稳定的构造方法，优先保留固定 fixture，不依赖运行时动态生成。
