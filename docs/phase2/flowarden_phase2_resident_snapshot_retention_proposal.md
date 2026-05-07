# Flowarden 第二阶段 Resident Snapshot Retention 提案

## 1. 文档目的

本文用于明确 resident core 在运行中和停止后的数据保留策略，避免：

1. `Overview` / `Inspect` 运行越久，resident core 内存持续线性增长；
2. UI 常驻运行态承担了原本只适合 CLI / 导出路径承担的完整历史数据。

---

## 2. 当前问题

当前 resident core 的运行态和停止后结果中，存在两类容易无限增长的数据：

1. live timeline 历史
2. 最终完整 snapshot / tick 历史

同时，最终 `FinalSnapshot.aggregate_summary` 中的：

1. `top_connections`
2. `top_hosts`
3. `top_services`
4. `tcp_connections`

也直接来自底层完整全局聚合，随着会话规模增长而增长。

---

## 3. 推荐原则

建议区分两类保留对象：

### 3.1 resident core 面向 UI 的投影

resident core 的职责应是：

1. 提供有界的运行态 projection
2. 提供当前查询所需的数据面

而不是长期保留完整历史分析资产。

### 3.2 CLI capture / 导出路径

完整最终结果：

1. `tick_snapshots`
2. `final_snapshot`
3. 完整整轮 capture 结果

更适合保留在：

1. `flowarden capture`
2. 文件导出
3. 后续 replay / forensics 路径

---

## 4. 建议策略

### 4.1 Overview 类投影按 top N 保留

resident core 面向 `Overview` 的数据应只保留有界投影：

1. `top_connections`
2. `top_hosts`
3. `top_services`
4. `top_destinations`

建议全部按 `top N` 输出与保留，而不是持有整轮完整结果。

`N` 的具体数值可以沿用当前 UI 展示需求，例如：

- `N = 20`

重点是：

1. resident core 的 Overview projection 是有界的；
2. 不需要为 UI 长期保存整轮所有连接/主机/服务历史。

### 4.2 live timeline 使用 rolling window

resident core 的 live timeline 只保留最近固定数量 tick：

1. 建议先与 Sniffnet 对齐：`30 tick`
2. UI 只消费这 30 个 timeline point

### 4.3 resident core 不长期保存完整 final snapshot

建议：

1. resident core 在 UI 常驻运行态中，不保留完整最终 `tick_snapshots`
2. resident core 停止后，UI 继续看到的是“最终有界投影”
3. 完整 `final_snapshot` / 完整 tick 历史仅保留在：
   - `flowarden capture`
   - 导出文件

---

## 5. Inspect / TCP Connections 的注意点

这里需要特别区分：

### 5.1 不建议直接把底层聚合也裁成 top N

如果把 resident core 底层：

1. `global_flows`
2. `global_hosts`
3. `global_services`
4. `global_tcp_connections`

也直接裁成 `top N`，那么：

1. `Inspect` 将只能看到 `top N flows`
2. `TCP Connections` 将只能看到 `top N tcp connections`

这会破坏它们作为查询/明细视图的语义。

### 5.2 推荐做法

先把 `top N` 限制作用于：

1. `Overview` 投影层

而不是立刻作用于：

1. `Inspect` 查询层
2. `TCP Connections` 查询层

也就是说：

1. resident core 的 **Overview projection** 有界；
2. resident core 的 **query surface** 暂时继续基于完整聚合；
3. 后续如果 query 面也需要有界化，再单独设计策略。

---

## 6. CLI 模式的保留策略

`flowarden capture` 应继续保留完整结果：

1. `RuntimeReport.tick_snapshots`
2. `RuntimeReport.final_snapshot`

因为 CLI 模式的职责就是：

1. 一次性 capture
2. 输出完整最终分析结果

这里不建议为了 resident core 的内存收敛，反向削弱 CLI 的结果完整性。

---

## 7. 建议实施顺序

### 第一步

resident core `Overview` live projection 改成：

1. rolling timeline window
2. `top N` overview lists

### 第二步

resident core stop 后不再保留完整 `final_snapshot` 给 UI；
改为保留最终有界 projection。

### 第三步

CLI `flowarden capture` 保持完整最终结果不变。

### 第四步

后续再单独评估：

1. `Inspect`
2. `TCP Connections`

是否也要进一步做查询面收敛。

---

## 8. 验收口径

完成后应满足：

1. resident core live `Overview` 投影是有界的；
2. `top_connections / top_hosts / top_services / top_destinations` 只保留 `top N`；
3. resident core live timeline 只保留最近固定数量 tick；
4. resident core 停止后，UI 不再依赖完整 final snapshot；
5. `flowarden capture` CLI 仍保留完整最终结果；
6. `Inspect` 和 `TCP Connections` 当前查询能力不被误伤。

---

## 9. 推荐结论

推荐按以下口径推进：

1. resident core 只保留 **有界 UI 投影**
2. `top N` 先作用于 `Overview` 投影层
3. 完整 final snapshot 只保留在 `CLI capture` 模式
4. resident core 查询面是否也需要收敛，后续再单独设计
