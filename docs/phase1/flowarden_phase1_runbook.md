# Flowarden 第一阶段运行说明

## 1. 目的

本文用于支持第一阶段的独立评审、重复验收和后续 UI 阶段承接。

---

## 2. 环境

工作目录：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
```

---

## 3. 常用命令

### 列设备

```bash
cargo run -p flowarden -- devices
cargo run -p flowarden -- devices --format json
```

启动前做所有 device 的短时 preview：

```bash
cargo run -p flowarden -- devices --preview 2
```

说明：

1. `devices --preview <秒数>` 会对所有可用 device 做短时预览。
2. preview 只用于帮助选择 source。
3. 正式 `capture` 仍然只绑定一个 `--device` 或一个 `--read`。

### live capture

```bash
cargo run -p flowarden -- capture --device en0 --duration 5
```

带 BPF：

```bash
cargo run -p flowarden -- capture --device en0 --duration 5 --bpf "tcp"
```

同时落原始 `pcap`：

```bash
cargo run -p flowarden -- capture --device en0 --duration 5 --pcap-out ./capture.pcap
```

说明：

1. `--duration` 以秒为单位。
2. `Ctrl+C` 会触发 graceful stop。
3. macOS 上 live capture 可能需要 `/dev/bpf*` 权限。
4. `--output` 用于 table/json 结果输出。
5. `--pcap-out` 用于原始抓包落盘。

### offline 回放

```bash
cargo run -p flowarden -- capture --read ./sample.pcap
```

输出 JSON：

```bash
cargo run -p flowarden -- capture --read ./sample.pcap --format json
```

输出到文件：

```bash
cargo run -p flowarden -- capture --read ./sample.pcap --format json --output ./out.json
```

---

## 4. 第一阶段质量门禁

必须通过：

```bash
cargo fmt --all -- --check
cargo clippy -q --all-targets --all-features -- -D warnings
cargo test -q
```

---

## 5. 固定回归资产

### 固定样本

- `flowarden-core/tests/fixtures/offline_mixed_ethernet.pcap`

### golden

- `flowarden-core/tests/golden/offline_mixed_ethernet.json`

### 集成测试

- `flowarden-core/tests/offline_capture_golden.rs`

覆盖语义：

1. offline 固定回放
2. malformed packet 容错
3. offline second gap
4. BPF 过滤后的稳定统计值
5. final snapshot 稳定契约

---

## 6. 第一阶段保留与后置能力

### 已保留的 Sniffnet 核心语义

1. live 和 offline 共用分析链路
2. offline 按 `pcap` 时间戳推进
3. 方向判定保留 offline fallback
4. 服务识别不退化成只看目标端口
5. 关键统计值稳定输出
6. link type 和 BPF 行为明确可验

### 明确后置

1. Avalonia UI
2. payload 深度解析
3. 会话级重建
4. channel/gRPC 通信

---

## 7. Phase 2 契约起点

第一阶段可直接交给第二阶段的契约包括：

1. `tick_snapshots`
2. `final_snapshot`
3. `top_connections`
4. `top_hosts`
5. `top_services`
6. table/json 输出模型
