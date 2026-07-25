# Flowarden 第二阶段 Resident Snapshot Retention

**状态：Accepted / Implemented**

- 决策日期：2026-07-25
- 实现提交：
  - 子仓 `flowarden`：`6de4daf` Add resident bounded aggregation mode for long-lived captures
  - 父仓：`a97117f` Point flowarden submodule at resident bounded aggregation
- 关联文档：`flowarden_phase2_live_timeline_window_proposal.md`（live timeline rolling window）

---

## 1. 文档目的

明确 resident core 在运行中和停止后的数据保留策略，避免：

1. `Overview` / `Inspect` 运行越久，resident core 内存持续线性增长；
2. UI 常驻运行态承担了原本只适合 CLI / 导出路径承担的完整历史数据。

本文从提案收敛为 **已接受实现说明**。

---

## 2. 已接受原则

### 2.1 双模式分离

| 模式 | 入口 | 职责 |
| --- | --- | --- |
| **Forensic** | `flowarden capture`（CLI） | 完整聚合与完整 tick 历史，服务导出 / golden / 取证 |
| **Resident** | `flowarden core`（UI 宿主） | 有界运行态投影，服务长时间 live 监控 |

### 2.2 数据类型分离

1. **累计标量（精确）**：`totals.packets` / `totals.bytes` / dropped 等 —— 全程精确累计，O(1)
2. **排名结构（有界、近似 top-K）**：flows / hosts / services / tcp —— Resident soft-cap
3. **时间序列（有界）**：live timeline —— 最近 30 tick；offline report 仍保留全量 ticks 供压缩时间线

### 2.3 Inspect 语义（已拍板）

Resident 路径 **接受有界 Inspect 视图**：

1. Inspect / TCP Connections 基于有界全局表 + summary 截断后的投影
2. 不是完整取证明细；完整明细走 CLI / 导出路径
3. 这是刻意的产品边界，不是临时缺陷

---

## 3. 实现映射

### 3.1 代码入口

| 概念 | 位置 |
| --- | --- |
| `AggregatorMode::{Forensic, Resident}` | `flowarden-core/src/flow/aggregator.rs` |
| `ResidentBounds` 默认 CAP | 同上 |
| soft-cap + replace-min | `upsert_*` / `insert_or_replace_min_by_bytes` / `update_tcp_tracker(..., cap)` |
| `TickHistoryMode::{Full, Windowed(n)}` | `flowarden-core/src/capture/runtime.rs` |
| CLI = Forensic + Full ticks | `flowarden/src/main.rs` |
| Core = Resident + Windowed(30) live ticks | `flowarden/src/service.rs` |
| Geo cache 20_000 半淘汰 | `flowarden/src/geo.rs` |

### 3.2 Resident 默认 CAP

| 资源 | 默认值 | 说明 |
| --- | --- | --- |
| `max_flows` | **30_000** | 全局五元组 soft-cap |
| `max_hosts` | **15_000** | 全局主机 soft-cap |
| `max_tcp_connections` | **30_000** | 全局 TCP 连接 soft-cap |
| `max_services` | **512** | 服务名基数通常很小 |
| `summary_limit` | **100** | finish / progress 摘要行数上限（对齐 `PROJECTION_MAX_TOP_N`） |
| live tick window | **30** | `PROJECTION_TICK_WINDOW`；约 30 秒 |
| offline UI timeline 点数 | **160** | `OFFLINE_TIMELINE_POINTS` 下采样展示 |
| geo cache | **20_000** | 满则丢弃约一半条目 |

### 3.3 soft-cap 策略（replace-min）

对新 key：

1. map 未满 → 直接插入
2. map 已满 → 找当前 **bytes 最小** 的条目
3. 仅当候选 entry 的 bytes **严格大于** 最小条目时替换
4. 否则丢弃候选（不再进入全局排名表）

对已存在 key：始终更新 counters（热路径不受 cap 阻挡）。

**精确性保证：**

- `global_totals` 对每个观测包仍精确累计
- 被淘汰 / 未入选的 flow **不影响** packets/bytes 总量

**近似性说明：**

- Overview top-N、Inspect 行集、TCP 页均为 cap 内近似排名
- 高流量桌面场景通常与真 top-N 一致；极端“海量低频唯一流”下为近似

### 3.4 Tick 历史

| 场景 | 行为 |
| --- | --- |
| CLI / Forensic | `TickHistoryMode::Full` |
| Resident **Live** | `TickHistoryMode::Windowed(30)`，运行中与报告均有界 |
| Resident **Offline** | 报告侧仍 Full（gap 压缩后的 tick），UI timeline 再压到 ≤160 点 |

### 3.5 投影层截断（既有）

gRPC / OverviewRuntimeSnapshot 侧仍保留：

1. overview lists `take(PROJECTION_MAX_TOP_N)`（100）
2. UI 默认 top-N 可配置，默认 10

底层 Resident maps 的 cap **远大于** 投影 top-N，避免“底层只留 10 条导致 Inspect 几乎不可用”。

---

## 4. 与早期提案差异

早期草案曾建议：

1. 仅 Overview 投影有界
2. Inspect / TCP 暂时基于完整全局聚合

**最终决策（1A）改为：**

1. Resident 全局聚合表本身 soft-cap
2. Inspect / TCP 接受有界视图
3. Forensic CLI 保持完整

原因：长时间 live 的主内存增长源是 `global_*` maps；只裁投影层无法止血。

---

## 5. 非目标（本轮不做）

1. Resident 磁盘 spill / 外部索引
2. 精确 heavy-hitters 结构（count-min 等）
3. Pause/Resume 会话状态持久化
4. 改变 gRPC 契约字段
5. 削弱 CLI golden / 导出完整性

---

## 6. 验收口径（已满足）

1. Resident live Overview 投影有界（timeline ≤ 30，lists ≤ top-N/100）
2. Resident `global_flows/hosts/tcp/services` 长度不超过默认 CAP
3. `totals.packets/bytes` 在有界模式下仍精确
4. `flowarden capture`（Forensic）仍保留完整最终结果；golden 通过
5. `cargo test` / `cargo clippy -D warnings` 通过
6. 单测覆盖：
   - `resident_mode_soft_caps_global_flows_and_keeps_heavier_entries`
   - `forensic_mode_keeps_all_flows`

---

## 7. 推荐结论（冻结）

1. Resident core 只服务 **有界 UI 投影 + 有界查询面**
2. Forensic CLI 保留完整分析资产
3. 默认 CAP 如上表；后续若需调参，优先通过 `ResidentBounds` / 配置扩展，不改模式语义
4. Inspect 完整性需求若升级为“全量取证”，应新开设计（外部存储 / 分页），不回退 Resident 无界 maps
