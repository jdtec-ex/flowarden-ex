# Flowarden Phase 3 — 落地进度

## 1. 范围对照（与 phase2 已交付）

| 波次 | 目标 | Phase2 已具备 | Phase3 本轮 |
| --- | --- | --- | --- |
| 1 | ARP L3 | 拒绝 ARP | **completed** 解析+聚合 `arp-request/reply` |
| 2 | Light DPI / SNI | M2X-006 已交付 | 记入 baseline，不重做 |
| 3 | 进程溯源 | M2E-101 async lookup | 记入 baseline，平台矩阵后续 |
| 4 | UI 联动 / 压测 | Process/SNI/Inspect | ARP 可见于 Top Services |

总纲：`flowarden_phase3_development_plan.md`

---

## 2. 总览

| 任务 | 状态 | 备注 |
| --- | --- | --- |
| P3-001 ARP 解码 | `completed` | `TransportProtocol::Arp` + SPA/TPA IPv4 |
| P3-002 ARP 服务标签 | `completed` | `arp-request` / `arp-reply` |
| P3-003 ARP 进 tick 聚合 | `completed` | 同 IP flow/host/service 路径 |
| P3-004 CLI/UI 协议标签 | `completed` | `transport_protocol_label` → `arp` |
| P3-010 SNI baseline | `baseline` | phase2 M2X-006 |
| P3-011 Process baseline | `baseline` | phase2 M2E-101 |
| P3-012 采样策略可配置 | `completed` | snaplen / --no-sni / sni-max-payload |
| P3-031 压测 runbook | `completed` | `flowarden_phase3_load_test_runbook.md` |

---

## 3. 执行日志

### 2026-07-25 — Phase3 启动 / 波次 1 ARP

#### 实现要点

1. `DecodedPacket.arp_operation`；`TransportProtocol::Arp`  
2. `decode_packet`：`NetHeaders::Arp` → sender/target IPv4  
3. `classify_service`：operation 1/2 → `arp-request` / `arp-reply`  
4. 单测：单元解码 + runtime ARP+TCP 双包均 decoded  
5. 旧测试「拒绝 ARP」改为「解码 ARP」  

#### 质量

- `cargo test -p flowarden-core`  
- `cargo test --bin flowarden`  
- `cargo clippy -p flowarden-core --bin flowarden -- -D warnings`  

#### 手测

1. 本地 LAN 抓包或含 ARP 的 pcap  
2. Overview Top Services 出现 `ARP-REQUEST` / `ARP-REPLY`  
3. Inspect protocol 过滤 `arp`  

### 2026-07-25 — P3-012 Light DPI 可配置 + P3-031 压测 runbook

#### 实现

1. `LightDpiOptions`：`sni_enabled` / `sni_max_payload`（默认 512）  
2. `RuntimeConfig.snaplen` + `light_dpi`；live 默认 snaplen 512，pcap-out 仍 65535  
3. CLI：`--snaplen` / `--no-sni` / `--sni-max-payload`  
4. `decode_packet_with_options` 热路径尊重策略  
5. 压测 runbook：`docs/phase3/flowarden_phase3_load_test_runbook.md`  

#### 质量

- light_dpi / tls_sni 单测  
- `cargo test -p flowarden-core` / `cargo test --bin flowarden`  
- clippy `-D warnings`  

#### 下一步

1. Resident core 暴露 snaplen/SNI 策略到 Settings（可选）  
2. Process 跨平台矩阵 P3-021  
3. 非 IPv4 ARP deferred  
