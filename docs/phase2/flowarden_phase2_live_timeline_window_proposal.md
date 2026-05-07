# Flowarden 第二阶段 Live Timeline Rolling Window 提案

## 1. 文档目的

本文用于记录 resident core 动态刷新过程中，`Overview` live timeline 的保留窗口如何收敛，以避免运行时间越长、内存和流式消息体越大的问题。

目标是参考 Sniffnet 的实时图表思路，在不破坏当前 phase2 已有：

1. resident core 动态 `Overview` stream
2. UI 共享 live projection
3. `Overview / Inspect Flows` 共享运行态

的前提下，把 live timeline 改成 rolling window。

---

## 2. 当前问题

当前 Flowarden 的 resident core live projection 里：

1. `latest_overview.tick_snapshots` 会随着 capture 持续累计整轮 tick 历史；
2. `StreamOverview` 每次推送都会带上整轮 timeline；
3. UI timeline 也基于这整轮 tick 历史重绘。

这会导致：

1. capture 跑得越久，resident core 内存中保留的 timeline 越长；
2. gRPC `StreamOverview` 每帧消息越来越大；
3. UI 重绘成本越来越高；
4. 该行为与 Sniffnet 的 live 图表窗口策略不一致。

---

## 3. Sniffnet 的参考策略

Sniffnet 并不是无限保留 live 图表历史。

它的图表序列在 live 模式下只保留最近固定数量的 tick：

1. `ChartSeries::update_series(...)`
2. 当 `spline.len() >= 30` 时，删除最旧点

也就是说：

1. 累计 traffic 聚合会持续增长；
2. 但 live 图表窗口只保留最近 30 个 tick。

这是一种“累计聚合 + 有界图表窗口”的结构。

---

## 4. 推荐方向

### 4.1 总体原则

按 Sniffnet 的思路，Flowarden 应区分两类数据：

1. **累计聚合**
   - totals
   - top connections
   - top hosts
   - top services
   - tcp connections
   - final snapshot

2. **live timeline**
   - 只保留最近固定数量的 tick

### 4.2 推荐窗口

建议第一版直接对齐 Sniffnet：

- `LIVE_TIMELINE_WINDOW = 30`

含义：

1. live capture 运行中，只保留最近 30 个 timeline point；
2. 若 tick 为 1 秒，则可视窗口约为最近 30 秒；
3. offline / stop 后最终结果不受该窗口限制。

---

## 5. 建议实现方式

### 5.1 resident core

resident core 内的 `OverviewRuntimeSnapshot` 不再保存“整轮所有 tick”用于 live projection，而是：

1. 维护一个最近 N 个点的 timeline window；
2. 每次新 tick 到来时追加新点；
3. 若超过 N，则移除最旧点。

建议把 live timeline 从“完整 `tick_snapshots` 语义”中分离出来，形成更清楚的运行态字段，例如：

1. `timeline_window`
2. `final_snapshot`

而不是继续让 `tick_snapshots` 同时承担：

1. live timeline 历史
2. 最终完整结果

### 5.2 `StreamOverview`

`StreamOverview` 每帧只发送：

1. 当前累计 totals
2. 当前 top lists
3. 最近 N 个 timeline points

不再发送整轮全部 tick 历史。

### 5.3 UI

UI 无需自己裁剪历史窗口。

UI 只消费 resident core 已裁剪过的最近 N 个点，保持：

1. `Overview`
2. `Inspect Flows`

共用同一份共享 live projection 状态。

---

## 6. CLI 与最终结果的关系

这次 rolling window 只针对 resident core 的 live projection。

不应影响：

1. `flowarden capture` CLI 最终输出
2. `RuntimeReport.tick_snapshots`
3. `FinalSnapshot`

也就是说：

1. CLI 仍可以保留完整 tick history 作为最终结果；
2. resident core live stream 只保留最近 N tick；
3. stop 后最终 `Overview / Inspect` 仍然从完整最终结果收尾。

---

## 7. 不建议的实现方式

### 7.1 UI 自己裁剪

不建议让 UI 自己把整轮 timeline 裁剪到最近 30 点。

原因：

1. 传输层消息仍然越来越大；
2. resident core 内存问题没有解决；
3. 只是把问题转移到 UI。

### 7.2 直接删除完整最终 tick 历史

不建议为了 live 窗口限制，顺便删掉 CLI / final snapshot 的完整 tick 结果。

原因：

1. 这会影响 phase1/CLI 语义；
2. 也会影响 stop 后最终结果的完整性。

---

## 8. 验收口径

完成后应满足：

1. resident core live `Overview` timeline 只保留最近固定数量 tick；
2. 默认窗口为最近 30 tick；
3. live capture 运行越久，stream 消息大小不会持续线性增长；
4. `Overview / Inspect Flows` 动态刷新语义不变；
5. `Stop` 后最终 snapshot/inspect 仍使用完整最终聚合结果；
6. `flowarden capture` CLI 不回归。

---

## 9. 推荐结论

推荐按以下口径推进：

1. resident core live projection 改为 rolling window；
2. 默认窗口与 Sniffnet 对齐：`30 tick`；
3. 累计聚合与图表窗口分离；
4. 不影响 CLI 最终结果与 stop 后最终快照。
