# Flowarden 第二阶段任务进度

## 1. 记录规则

1. 仅当任务同时满足 backlog 中的“输出”和“验收条件”时，才标记为“已完成”。
2. 若实现偏离既定架构约束，即使代码可运行，也不得标记为“已完成”。
3. 每个任务完成后先记录状态，再等待用户确认是否进入下一个任务。

---

## 2. 当前状态总览

| 任务 | 状态 | 说明 |
| --- | --- | --- |
| `M2-001` | 已完成 | Avalonia 工程骨架与 `net8.0` / `8.0.125` 基线已落地。 |
| `M2-002` | 已完成 | 已用 `proto + tonic + .NET gRPC client` 重做最小 service mode 与本地通信骨架。 |
| `M2-003` | 未开始 | 之前有未提交 DTO 草稿；在 `M2-002` 修正完成并经确认前，不进入该任务。 |
| `M2-004` | 未开始 | 等待 `M2-003` 完成并经确认。 |
| `M2-005` | 未开始 | 等待前置任务完成并经确认。 |
| `M2-006` | 未开始 | 等待前置任务完成并经确认。 |
| `M2-007` | 未开始 | 等待前置任务完成并经确认。 |
| `M2-008` | 未开始 | 等待前置任务完成并经确认。 |
| `M2-009` | 未开始 | 等待前置任务完成并经确认。 |
| `M2-101` | 未开始 | 作为增强预留项，等待 `M2-006` 后评估。 |

---

## 3. 任务记录

### M2-001 UI 工程骨架与 SDK 基线

- 状态：已完成
- 完成依据：
  - `flowarden-ui/`、`Flowarden.Ui.sln`、`src/Flowarden.Ui/` 已建立
  - `global.json` 固定 `8.0.125`
  - `TargetFramework = net8.0`
  - 窗口可启动，`dotnet build` 通过
- 提交：
  - outer repo `9315b9a` `Complete M2-001 Avalonia UI scaffold and sdk baseline`

### M2-002 core service mode 与本地 IPC 骨架

- 状态：已完成
- 原偏差说明：
  - 曾落成 loopback HTTP skeleton，不符合 [sniffnet_refactor_avalonia_rust_grpc.md](/Users/wangli/workspace/coding/flowarden/docs/sniffnet_refactor_avalonia_rust_grpc.md) 中已明确的 `Rust Core + Avalonia + gRPC` 架构要求
  - 该偏差已在本任务内修正，不再作为正式通信基线保留
- 修正后完成内容：
  - 建立共享 `proto/flowarden/health.proto`
  - 建立共享 `proto/flowarden/discovery.proto`
  - Rust 侧改为 `tonic` gRPC service mode
  - Avalonia 侧改为 `.NET gRPC client`
  - 最小 RPC 已落地：
    - `GetHealth`
    - `GetVersion`
    - `ListDevices`
- 验证结果：
  - `cargo build -p flowarden` 通过
  - `cargo test -q -p flowarden` 通过
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - 端到端探针验证通过：
    - `health.status=ok`
    - `version.service=flowarden-core-service`
    - `devices.count=22`
- 原提交：
  - inner repo `0daff75` `Complete M2-002 local service mode and health skeleton`
  - outer repo `0099fc7` `Complete M2-002 local service mode and health skeleton`
- 修正后提交：
  - 待本轮提交后补充
