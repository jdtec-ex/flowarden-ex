# Flowarden 第一阶段验收记录模板

## 1. 基本信息

- 评审日期：
- 评审人：
- 代码仓库提交：
- 文档仓库提交：

---

## 2. 质量门禁

### `cargo fmt --all -- --check`

- [ ] 通过
- 备注：

### `cargo clippy -q --all-targets --all-features -- -D warnings`

- [ ] 通过
- 备注：

### `cargo test -q`

- [ ] 通过
- 备注：

---

## 3. CLI 基本能力

### 设备枚举

命令：

```bash
cargo run -p flowarden -- devices
cargo run -p flowarden -- devices --format json
```

- [ ] 通过
- 备注：

### offline 回放

命令：

```bash
cargo run -p flowarden -- capture --read ./sample.pcap
cargo run -p flowarden -- capture --read ./sample.pcap --format json
```

- [ ] 通过
- 备注：

### live capture

命令：

```bash
cargo run -p flowarden -- capture --device <device> --duration 5
```

- [ ] 通过
- 备注：

### BPF

命令：

```bash
cargo run -p flowarden -- capture --device <device> --duration 5 --bpf "tcp"
```

- [ ] 通过
- 备注：

---

## 4. 输出契约

### JSON

检查项：

1. `tick_snapshots`
2. `final_snapshot`
3. `totals`
4. `top_connections`
5. `top_hosts`
6. `top_services`

- [ ] 通过
- 备注：

### 文件输出

命令：

```bash
cargo run -p flowarden -- capture --read ./sample.pcap --format json --output ./out.json
```

- [ ] 通过
- 备注：

---

## 5. 固定回归资产

检查项：

1. `flowarden-core/tests/fixtures/offline_mixed_ethernet.pcap`
2. `flowarden-core/tests/golden/offline_mixed_ethernet.json`
3. `flowarden-core/tests/offline_capture_golden.rs`

- [ ] 通过
- 备注：

---

## 6. 范围确认

### 第一阶段已交付

- [ ] CLI + headless capture/analyze pipeline
- [ ] live/offline 共用分析链路
- [ ] decoder + direction + service + aggregation
- [ ] table/json 输出

### 第一阶段明确后置

- [ ] Avalonia UI
- [ ] payload 深度解析
- [ ] 会话级重建
- [ ] channel/gRPC 通信
- [ ] live capture 同时落盘 `pcap`

---

## 7. 结论

- [ ] 第一阶段通过验收
- [ ] 第一阶段需整改

备注：
