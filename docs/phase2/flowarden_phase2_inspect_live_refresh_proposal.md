# Flowarden 第二阶段 Inspect 动态刷新方案提案

## 1. 文档目的

本文用于记录 `Inspect` 页面如何参考 Sniffnet 的实时刷新模型进行后续演进。

目标不是立即实现，而是先冻结一版架构方向，避免后续在：

1. `Overview` 单独 stream
2. `Inspect` 单独 query
3. `TCP Connections` 再单独一套刷新

之间继续分裂。

---

## 2. Sniffnet 的参考模型

Sniffnet 的运行方式并不是给 `Overview`、`Inspect` 分别设计两套刷新机制，而是：

1. 抓包线程按 tick 推送统一运行态增量；
2. GUI 维护统一共享状态；
3. `Overview` 和 `Inspect` 都直接从这份共享状态读取；
4. `Inspect` 页面上的搜索、排序、筛选主要在 UI 层完成。

简化为一句话：

> Sniffnet 是“统一 tick -> 统一运行态 -> 多页面共享”，而不是“每个页面各拉一份数据”。

---

## 3. 当前 Flowarden 与 Sniffnet 的差异

当前 Flowarden 已完成：

1. `Overview` 的 resident core 实时 stream
2. `Inspect` 的稳定后端查询

但这意味着当前数据链路已经分成两条：

1. `Overview`
   - `ProjectionService.StreamOverview`
   - UI 订阅并实时刷新

2. `Inspect`
   - `ProjectionService.GetInspectPage`
   - 当前仍以 query / stop 后刷新为主

这种结构能工作，但和 Sniffnet 的共享运行态模式不一致。

---

## 4. 推荐方向

### 4.1 总体建议

如果后续要让 `Inspect` 进入动态刷新，建议不要直接给它再加一条独立刷新链，而应逐步收敛到：

1. resident core 推送统一 live projection
2. UI 维护共享运行态
3. `Overview` 和 `Inspect` 共用这份状态
4. `Inspect` 在 UI 层本地筛选

这更接近 Sniffnet，也更容易避免后续继续产生：

1. `Overview` 一套
2. `Inspect` 一套
3. `TCP Connections` 再一套

的多路刷新机制。

---

## 5. 推荐架构

### 5.1 resident core

resident core 继续按 tick 产生统一运行态 projection。

这份统一 projection 至少应包含：

1. totals
2. timeline points
3. top connections
4. top hosts
5. top services
6. top destinations
7. capture state
8. last packet timestamp

当前 `OverviewSnapshotResponse` 已经非常接近这个角色。

### 5.2 UI 共享状态

UI 侧新增共享运行态对象，例如：

- `LiveProjectionState`

职责：

1. 持有 resident core 最新一帧 live projection
2. 负责 stream 生命周期
3. 供 `Overview` / `Inspect` / 后续 `TCP Connections` 共用

### 5.3 Overview

`OverviewPageViewModel` 不再直接持有 stream，而是从 `LiveProjectionState` 读共享快照。

### 5.4 Inspect

`InspectPageViewModel` 也从 `LiveProjectionState` 读取共享连接数据，然后在 UI 层做：

1. address filter
2. protocol filter
3. direction filter
4. service filter

这样做之后，`Inspect` 的动态刷新就不再依赖单独的后端 query / stream。

---

## 6. 为什么不建议直接给 Inspect 再加一条独立 stream

如果继续当前思路，给 `Inspect` 直接加：

- `ProjectionService.StreamInspectPage`

会有几个问题：

1. resident core 要维护另一套带 filter 语义的 stream
2. UI 会出现：
   - `Overview` 一个 stream
   - `Inspect` 一个 stream
   - `TCP Connections` 可能又一个 stream
3. 多页面数据时间点可能不一致
4. 后面会越来越不像 Sniffnet 的统一运行态模型

因此，这条路虽然短期能工作，但不是推荐的长期结构。

---

## 7. 建议分阶段推进

### 第一步

保留当前已完成的 `Overview` stream，不回退。

新增：

1. UI 共享 live projection 状态层
2. `Overview` 改为从共享状态读取，而不是自己直接消费 stream

### 第二步

`Inspect` 也改为从共享状态读取：

1. 当前 capture 运行中，实时刷新来自共享状态
2. 当前筛选在 UI 层执行
3. `GetInspectPage` 保留给：
   - stop 后稳定查询
   - fallback

### 第三步

再评估：

1. `TCP Connections` 是否也进入共享 live projection
2. `GetInspectPage` 是否需要继续长期保留

---

## 8. 与当前功能的关系

本提案不会推翻当前已完成的：

1. `Overview` 动态刷新
2. resident core `StreamOverview`
3. `Inspect` 当前 stop 后 / query 路径

它是未来 `Inspect` 动态刷新的推荐演进方向，而不是要求立即重做现有实现。

---

## 9. 当前不建议一起做的事

即使后续开始做 `Inspect` 动态刷新，也不建议同一轮一起做：

1. `TCP Connections` 动态刷新
2. payload / session 级实时视图
3. delta patch 协议
4. pause / resume 真控制面联动
5. 高复杂度分页与虚拟滚动

---

## 10. 推荐结论

推荐结论如下：

1. `Inspect` 动态刷新应当做；
2. 但长期正确方向应参考 Sniffnet，走“统一 live projection + UI 共享运行态 + 页面本地筛选”；
3. 不建议直接给 `Inspect` 单独再造一条独立实时数据链。
