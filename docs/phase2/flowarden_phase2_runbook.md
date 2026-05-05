# Flowarden 第二阶段运行说明

## 1. 目标

本文用于第二阶段当前实现的重复运行与评审。

覆盖范围：

1. 如何构建 Rust core
2. 如何构建 Avalonia UI
3. 如何启动本地 resident core
4. 如何启动 UI
5. 如何验证 `Source / Overview / Inspect / Settings`
6. 当前哪些能力已经实现，哪些仍未达到初始总方案要求

---

## 2. 环境基线

### 2.1 .NET

- 本机 SDK：`8.0.125`
- UI 目标框架：`net8.0`

验证命令：

```bash
dotnet --version
```

### 2.2 Rust

在仓库内层工程构建：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo --version
```

---

## 3. 构建

### 3.1 构建 Rust core

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo build -p flowarden
```

### 3.2 构建 Avalonia UI

```bash
dotnet build /Users/wangli/workspace/coding/flowarden/flowarden-ui/Flowarden.Ui.sln
```

---

## 4. 启动 resident core

第二阶段本地通信基线是 `gRPC`。

启动命令：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo run -p flowarden -- core --bind 127.0.0.1:39091
```

当前最小 RPC：

1. `GetHealth`
2. `GetVersion`
3. `ListDevices`
4. `ListDevicePreviews`
5. `GetLatestOverview`
6. `GetInspectPage`

---

## 5. 验证 gRPC 通道

使用本地 probe：

```bash
dotnet run --project /Users/wangli/workspace/coding/flowarden/flowarden-ui/tests/Flowarden.Ui.GrpcProbe/Flowarden.Ui.GrpcProbe.csproj -- http://127.0.0.1:39091
```

预期输出至少包含：

```text
health.status=ok
version.service=flowarden-core-service
version.version=0.1.0
devices.count=...
```

---

## 6. 启动 UI

```bash
dotnet run --project /Users/wangli/workspace/coding/flowarden/flowarden-ui/src/Flowarden.Ui/Flowarden.Ui.csproj
```

---

## 7. 页面级验证

### 7.1 Source

检查项：

1. 左侧是设备列表
2. 右侧是 preview workbench
3. 文案明确区分：
   - preview
   - formal capture
   - offline import
4. 正式 capture 语义仍是单 source

说明：

- 当前页面通过真实 `DiscoveryClient` 拉取设备与 preview
- 正式 capture 控制仍未接入 `ControlService`

### 7.2 Overview

检查项：

1. 存在 `Hero Traffic Chart`
2. 存在四张状态卡
3. 存在 `Destination Map` 预留区
4. 存在 `Top Destinations`
5. 存在 `Top Hosts / Top Services / Top Connections`

说明：

- 当前页面通过真实 `ProjectionService.GetLatestOverview` 拉取稳定 snapshot
- 口径受限于 phase1 聚合输出
- 尚未接入实时 projection stream

### 7.3 Inspect

检查项：

1. 存在 header
2. 存在高可见 filter bar
3. 存在结果表格
4. 存在 footer summary
5. 过滤条件下发后，结果数和表格内容会变化

说明：

- 当前页面通过真实 `ProjectionService.GetInspectPage` 拉取结果
- 过滤条件会下发到 core
- 未引入 payload / session 字段

### 7.4 Settings

检查项：

1. runtime 面板可见
2. core 面板可见
3. diagnostics 面板可见
4. 当前 source / BPF / tick interval / top N 可见
5. endpoint / process state / version 可见
6. 错误提示入口可见

说明：

- 当前页面通过 `CoreHealthService`、`DiscoveryClient` 和 shell 级错误状态组合最小运行态
- 仍未接入 runtime write-back 或 control plane

---

## 8. 当前已实现与未实现能力

### 8.1 当前已实现

1. Avalonia shell
2. Source 页面真实 discovery / preview 接线
3. Overview 页面真实 latest projection 接线
4. Inspect 页面真实 query / projection 接线
5. Settings 页面最小 runtime / health / diagnostics 接线
6. Rust core 本地 gRPC skeleton
7. `Destination Map` 稳定预留区

### 8.2 当前未实现但原方案要求第二阶段完成

1. `Start / Stop / Pause / Resume`
2. Overview 实时 projection stream
3. core 异常退出后的 UI 可恢复状态

### 8.3 明确后置

1. Settings 的真实 runtime write-back
2. `Destination Map` 真实地图能力
3. phase3 的 payload 深度解析
4. phase3 的会话级重建

---

## 9. 当前质量门禁

已验证命令：

```bash
dotnet format /Users/wangli/workspace/coding/flowarden/flowarden-ui/Flowarden.Ui.sln --verify-no-changes
dotnet build /Users/wangli/workspace/coding/flowarden/flowarden-ui/Flowarden.Ui.sln
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo test -q -p flowarden
```

说明：

- 这些命令只能证明当前代码可构建、可格式化、最小 Rust 测试通过
- 不能证明第二阶段已完成初始总方案要求的“真实运行闭环”
