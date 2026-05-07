# Flowarden 第二阶段后续增强清单

## 1. 文档目的

本文用于把第二阶段 backlog 主线之外、但仍与初始 phase2 目标相关的剩余增强项单独列出。

目标是明确：

1. `M2-001` 到 `M2-009`、`M2-101` 已完成；
2. 仍未完成的内容不是 backlog 任务本身，而是后续增强；
3. 这些增强项需要单独规划，不应再混写成 phase2 backlog 未完成。

---

## 2. 当前结论

截至当前代码与文档状态，第二阶段 backlog 已全部完成：

1. `M2-001` 到 `M2-009`
2. `M2-101`

但以下增强项仍未完成：

1. `ControlService` 真正控制面
2. `Overview` 实时 projection stream
3. core 异常退出后的 UI 可恢复状态

这些项不阻塞 phase2 backlog 封板，但若要进一步对齐初始总方案中的更强闭环要求，应作为后续增强独立推进。

---

## 3. 增强项清单

### 3.1 Control Plane

#### 目标

让 UI 不只具备 resident core 探活与页面数据读取能力，还具备真实 capture 生命周期控制能力。

#### 当前状态

当前 `ControlService` 仍是 skeleton / partial wiring：

1. UI 没有完整的 `Start / Stop / Pause / Resume` 运行闭环
2. shell 中的 `Start Capture` 仍不是正式 capture 控制入口
3. pause / resume 尚未形成真实 capture runtime 控制链

#### 后续项

1. `StartCapture`
2. `StopCapture`
3. `PauseCapture`
4. `ResumeCapture`
5. capture state 与 shell / Source / Overview 的联动状态更新

---

### 3.2 Real-time Projection Stream

#### 目标

让 `Overview` 不只依赖稳定 snapshot 拉取，而能持续消费 resident core 的实时 projection 更新。

#### 当前状态

当前该项已完成第一步：

1. `ProjectionService.GetLatestOverview` 已成立
2. `ProjectionService.StreamOverview` 已成立
3. UI `Overview` 已在 live capture 运行中订阅 resident core 的动态 tick 投影
4. `Stop` 后仍会回到最终 snapshot 收尾

#### 当前仍后置

以下内容仍未纳入本轮：

1. `TCP Connections` 实时 projection stream
2. stream 中断后的自动重连策略
3. 更细粒度的 backpressure / subscriber fan-out 策略

#### 补充状态

当前 `Inspect` 已完成第一步动态刷新收敛：

1. `Overview` 和 `Inspect Flows` 已共用 UI 侧 `LiveProjectionState`
2. resident core 运行中的 `StreamOverview` 会驱动 `Overview` 和 `Inspect Flows` 同步刷新
3. `Inspect` 的 TCP 连接模式仍保持独立查询路径，尚未进入动态刷新

#### 后续项

1. `TCP Connections` 是否也要进入实时流
2. stream 中断与重连语义
3. live / replay 模式下的刷新策略细化

补充提案见：

- `flowarden_phase2_inspect_live_refresh_proposal.md`

---

### 3.3 Core Failure Recovery

#### 目标

当 resident core 异常退出、崩溃或失联时，UI 能进入真实可恢复状态，而不是只停留在启动期探活逻辑。

#### 当前状态

当前 UI 已具备：

1. 启动期探活
2. 启动期拉起 core
3. 启动失败的最小错误展示

但仍缺：

1. 运行中 core 失联检测
2. 失联后的 UI 降级状态
3. 手动或自动恢复策略
4. 页面级 stale state 提示

#### 后续项

1. runtime health watcher
2. reconnect / relaunch policy
3. stale projection state handling
4. shell 级恢复提示与页面级恢复体验

---

## 4. 建议归类

为避免继续污染 phase2 backlog 状态，建议后续统一这样表述：

1. phase2 backlog：已完成
2. phase2 follow-up enhancements：待规划

不建议再把这些增强项写成：

1. `M2-005` 到 `M2-101` 未完成
2. phase2 未封板

因为这会和当前代码事实、审计文档以及验收口径冲突。

---

## 5. 推荐表述

推荐后续统一使用以下表述：

> 第二阶段 backlog 已完成；当前剩余项属于 phase2 后续增强，主要包括 control plane、实时 projection stream 和 core 异常恢复。
