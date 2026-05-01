# Flowarden 第二阶段运行说明

## 1. 目标

本文用于第二阶段的重复运行与评审。

覆盖范围：

1. 如何构建 Rust core
2. 如何构建 Avalonia UI
3. 如何启动本地 gRPC core service
4. 如何启动 UI
5. 如何验证 `Source / Overview / Inspect / Settings`
6. 哪些能力已经实现，哪些仍是明确后置

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

## 4. 启动 core service

第二阶段本地通信基线是 `gRPC`。

启动命令：

```bash
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo run -p flowarden -- service --bind 127.0.0.1:39091
```

当前最小 RPC：

1. `GetHealth`
2. `GetVersion`
3. `ListDevices`
4. `ListDevicePreviews`

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

- 当前页面由稳定种子数据驱动
- `ListDevicePreviews` gRPC 通道已存在，但还未在 UI 中接成实时拉取

### 7.2 Overview

检查项：

1. 存在 `Hero Traffic Chart`
2. 存在四张状态卡
3. 存在 `Destination Map` 预留区
4. 存在 `Top Destinations`
5. 存在 `Top Hosts / Top Services / Top Connections`

说明：

- 当前页面由稳定样本 `OverviewSnapshotDto` 驱动
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

- 当前过滤作用于本地稳定样本结果集
- 尚未接入后端 inspect query/projection 通道
- 未引入 payload / session 字段

### 7.4 Settings

检查项：

1. runtime 面板可见
2. core 面板可见
3. diagnostics 面板可见
4. 当前 source / BPF / tick interval / top N 可见
5. endpoint / process state / version 可见
6. 错误提示入口可见

---

## 8. 已实现与后置能力

### 8.1 第二阶段已实现

1. Avalonia shell
2. Source 页面 MVP
3. Overview 页面 MVP
4. Inspect 页面 MVP
5. Settings 页面 MVP
6. Rust core 本地 gRPC service skeleton
7. `Destination Map` 稳定预留区

### 8.2 明确后置

1. Source 页真实 gRPC preview/device 拉取接线
2. Overview 实时 projection stream
3. Inspect 后端 query/projection 接线
4. Settings 的真实 runtime write-back
5. `Destination Map` 真实地图能力
6. phase3 的 payload 深度解析
7. phase3 的会话级重建

---

## 9. 第二阶段质量门禁

已验证命令：

```bash
dotnet format /Users/wangli/workspace/coding/flowarden/flowarden-ui/Flowarden.Ui.sln --verify-no-changes
dotnet build /Users/wangli/workspace/coding/flowarden/flowarden-ui/Flowarden.Ui.sln
cd /Users/wangli/workspace/coding/flowarden/flowarden
cargo test -q -p flowarden
```
