# Phase3 热路径压测 Runbook（P3-031）

目标：验证 **Light DPI（SNI）** 与 **进程 lookup** 不拖垮抓包主循环（丢包 / 解码失败率可接受）。

---

## 1. 环境

1. Release 构建：`cargo build -p flowarden --release`  
2. macOS 建议有线或稳定 Wi‑Fi；记录机型与 OS 版本  
3. 关闭无关重网络应用  

---

## 2. 基线：仅解码（关 SNI）

```bash
./target/release/flowarden capture \
  --device <iface> \
  --duration 30 \
  --no-sni \
  --format table \
  --top 10
```

记录 stderr 摘要：`packets_seen`、`bytes_seen`（若后续接入 stats 行）、主观 UI/CPU。

---

## 3. Light DPI 开 SNI + 默认 snaplen 512

```bash
./target/release/flowarden capture \
  --device <iface> \
  --duration 30 \
  --format json \
  --top 20 \
  --output /tmp/flowarden-dpi.json
```

检查：

1. `top_connections_enriched[].sni` / hosts SNI 是否有样本  
2. CPU 相对基线涨幅（`top -pid` 或 Activity Monitor）  
3. 与 `--no-sni` 对比：吞吐不应断崖下降  

---

## 4. Snaplen 敏感度

```bash
# 更紧
./target/release/flowarden capture --device <iface> --duration 20 --snaplen 128 --format table
# 更松
./target/release/flowarden capture --device <iface> --duration 20 --snaplen 2048 --format table
```

期望：

| snaplen | SNI 命中 | 相对 CPU |
| --- | --- | --- |
| 128 | 偏低（ClientHello 常被截断） | 较低 |
| 512（默认） | 常见 HTTPS 可用 | 基线 |
| 2048 | 略升 | 略升 |

---

## 5. Resident core + UI（进程 lookup）

1. 启动 UI → 自动/手动 Start live capture  
2. 打开多个浏览器标签（HTTPS）  
3. Settings Diagnostics：`PROCESS LOOKUP q=… cache=…`  
4. 观察 2–3 分钟：  
   - `q` 不应持续单调冲顶不降  
   - Inspect Process 列逐渐填充  
   - Capture 保持 Running，无长期 stale  

---

## 6. Offline 吞吐（解码+ARP+SNI）

```bash
./target/release/flowarden capture \
  --read path/to/large.pcap \
  --format json \
  --data-threshold 1 \
  --watch 'service:https' \
  --output /tmp/offline.json
```

记录墙钟时间与 findings 数量。可对比 `--no-sni`。

---

## 7. 通过标准（最小）

1. 默认 `--snaplen`/`SNI` 下 live 30s 不崩溃、可正常 Stop  
2. `--no-sni` 与默认模式均可完成 capture  
3. UI live 下 process queue 不无限堆积（经验：峰值后回落）  
4. 有至少一例 HTTPS 流量时，默认模式 JSON 中可能出现 SNI（视握手可见性）  

---

## 8. 失败处理

| 现象 | 动作 |
| --- | --- |
| SNI 全空 | 检查是否 ECH/会话复用；试 `--snaplen 1024` |
| 高丢包 | 降 snaplen；确认 buffer；关其它抓包工具 |
| process q 一直涨 | 降 TopN；确认 OS 权限；提 issue 记 P3-032 |

---

## 9. 签署

| 项 | 值 |
| --- | --- |
| 执行人 | |
| 日期 | |
| 接口 / 机型 | |
| 结果 | Pass / Fail |
| 备注 | |
