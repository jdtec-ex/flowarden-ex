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
| `M2-003` | 已完成 | phase2 UI 侧最小 DTO/契约模型已冻结，足以承接 Source / Overview / Inspect / Settings 开发。 |
| `M2-004` | 已完成 | `Left Rail + Top App Bar + Main Workbench` 已落地，页面切换与全局状态点已稳定。 |
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
  - inner repo `7b1c4d8` `Rework M2-002 to gRPC service mode skeleton`
  - outer repo `83a8b89` `Rework M2-002 to gRPC ui client skeleton`

### M2-003 phase2 契约模型冻结

- 状态：已完成
- 完成内容：
  - 冻结 `DeviceSummaryDto`
  - 冻结 `DevicePreviewDto`
  - 冻结 `CaptureSessionStateDto`
  - 冻结 `OverviewSnapshotDto`
  - 冻结 `InspectFilterDto`
  - 冻结 `InspectResultDto`
  - 冻结 `CoreErrorDto`
- 结构约束：
  - DTO 命名明确区分 UI 传输模型与 Rust 内部领域模型
  - `DestinationMapPanel` 和 `TopDestinationsPanel` 已保留 placeholder 契约
  - `InspectResultDto` 只承接 phase2 的连接明细和结果摘要，不提前引入 phase3 的 payload / session 字段
  - 当前只冻结 UI 侧最小数据面，不新增 core 侧 proto 扩展
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - UI 项目未直接依赖 Rust 内部结构体
  - phase2 页面所需最小数据面已齐备
- 提交：
  - outer repo `a21b72a` `Complete M2-003 phase 2 dto contract freeze`

### M2-004 App Shell 与全局状态层

- 状态：已完成
- 完成内容：
  - `AppShellView`
  - `AppRailView`
  - `AppHeaderView`
  - `AppShellViewModel`
- 具体落地：
  - 固定 `Left Rail + Top App Bar + Main Workbench`
  - 左 rail 已承载主导航与 `Start Capture` 主 CTA
  - top app bar 已承载 mode 切换、core/capture 状态点、tools 入口
  - main workbench 已作为稳定页面宿主，支持 `Source / Overview / Inspect / Settings` 切换
  - `Overview` 仍保留 `Hero Chart / Status Cards / Top Destinations / Destination Map / Lower Detail Row` 的稳定版位
- 范围边界：
  - 当前任务只落 shell、切页、状态层与壳层样式
  - `Source / Overview / Inspect / Settings` 的业务内容仍分别留给 `M2-005` 到 `M2-008`
- 验证结果：
  - `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过
  - shell 可启动并稳定切页面
  - 全局状态点可显示 `core / capture` 状态
  - 风格已脱离默认 Avalonia 壳，收敛到 `Cosmos Network System` 的 shell 基线
- 提交：
  - outer repo `a457e25` `Complete M2-004 app shell and global state layer`
