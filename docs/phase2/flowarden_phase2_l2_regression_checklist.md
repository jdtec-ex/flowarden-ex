# Phase2 L2 / Milestone C — 手测回归清单

用途：在宣布 L2 Surpass Slice 或发版前，按本清单做一轮人工回归。  
自动化证据：`cargo test --bin flowarden`、`cargo clippy --bin flowarden -- -D warnings`、`dotnet build`。

---

## 0. 启动前置

1. 完全退出 UI 与旧 core 进程  
2. `cargo build -p flowarden`（或 workspace 等价）  
3. 启动 UI（确保加载新 core 二进制）  
4. Settings → Core 显示 Connected  

---

## 1. Control plane（L0）

| # | 步骤 | 期望 |
| --- | --- | --- |
| 1.1 | Source 选 live 设备 → Start | Capture=Running，Overview 开始刷新 |
| 1.2 | Pause | Capture=Paused，数据冻结（不再增长） |
| 1.3 | Resume | 恢复增长 |
| 1.4 | Stop | Capture=Idle，可再 Start |

---

## 2. Live projection / Inspect

| # | 步骤 | 期望 |
| --- | --- | --- |
| 2.1 | Overview 有 timeline 与 Top Hosts/Services | 数据随流量变化 |
| 2.2 | Inspect Flows 有 Process / SNI 列 | 浏览 HTTPS 后 SNI 可出现 |
| 2.3 | Inspect TCP 与 Flows 同源 live | 同一 capture 下均有数据或合理空态 |

---

## 3. Settings / 偏好

| # | 步骤 | 期望 |
| --- | --- | --- |
| 3.1 | Top N 改 5 → Set | Overview 列表长度受影响 |
| 3.2 | Threshold=1000，Watched=`service:https, process:…` → **Apply** | 状态行含 `core: Signal policy applied` |
| 3.3 | 重启 UI | 偏好仍在；core 再次收到 policy |
| 3.4 | Desktop toast / sound 勾选 | 新信号时 toast（及可选声音） |

---

## 4. Signals（live）

| # | 步骤 | 期望 |
| --- | --- | --- |
| 4.1 | 低阈值 capture | Signals 出现条目，Rail 未读角标 |
| 4.2 | 点信号行 | 跳转 Inspect 并过滤 |
| 4.3 | Mark read | 角标与 ● 清除 |
| 4.4 | 模式文案 | `live · active/updated` |

---

## 5. Offline / Replay forensics

| # | 步骤 | 期望 |
| --- | --- | --- |
| 5.1 | Source 导入 pcap → Start | Offline 回放完成，Overview mode=offline |
| 5.2 | Timeline 标题 | `Timeline · full offline capture` |
| 5.3 | Signals offline finding | `offline · finding` |
| 5.4 | 点 finding | Overview 出现 FINDING 竖线；Top 列表重排 |
| 5.5 | **Open Inspect** | Inspect 已预填 pivot |

---

## 6. Diagnostics

| # | 步骤 | 期望 |
| --- | --- | --- |
| 6.1 | Settings Diagnostics 有 dropped / last pkt / stream / uptime | 随 capture 刷新 |
| 6.2 | PROCESS LOOKUP | `q=… · cache=…` |
| 6.3 | Reconnect（若 relaunch） | CORE RESTARTS +1 |
| 6.4 | **Export** | 得到 JSON；含 `core` / `capture` / `signals` / `preferences` |

---

## 7. CLI 契约冒烟

```bash
cd flowarden
cargo build --bin flowarden
./target/debug/flowarden capture \
  --read flowarden-core/tests/fixtures/offline_mixed_ethernet.pcap \
  --format json --top 5 \
  --data-threshold 1 \
  --watch 'service:https'
```

期望：

1. JSON 含 `top_hosts_enriched` / `top_connections_enriched`（含 `sni` 字段）  
2. JSON 含 `findings[]`，`mode=offline`，`status=finding`  
3. 退出码 0  

---

## 8. 失败速查

| 现象 | 处理 |
| --- | --- |
| Settings Apply 无效果 | 看状态行是否 Unimplemented → Reconnect 新 core |
| Process 列全空 | 强制新 core；仅 live OS lookup |
| SNI 全空 | 需看到 TLS ClientHello；snaplen 限制 |
| Signals 无 rail | 确认导航有 Signals 页 |
| Export 取消 | 状态 “Export cancelled”；可再试 |

---

## 9. 签署

| 项 | 结果 |
| --- | --- |
| 执行人 | |
| 日期 | |
| L0/L1/L2 主路径 | Pass / Fail |
| 备注 | |
