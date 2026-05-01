# Flowarden 第二阶段验收记录模板

## 1. 基本信息

- 验收日期：
- 验收人：
- 代码提交范围：
- UI 提交：
- Rust core 提交：

---

## 2. 环境确认

- `dotnet --version = 8.0.125`
- Rust 环境可用
- `cargo build -p flowarden` 通过
- `dotnet build flowarden-ui/Flowarden.Ui.sln` 通过

---

## 3. 通信验收

### 3.1 gRPC core service

- `cargo run -p flowarden -- service --bind 127.0.0.1:39091`
- 结果：

### 3.2 gRPC probe

- `dotnet run --project flowarden-ui/tests/Flowarden.Ui.GrpcProbe/Flowarden.Ui.GrpcProbe.csproj -- http://127.0.0.1:39091`
- 结果：

检查项：

- `health.status=ok`
- `version.service=flowarden-core-service`
- `devices.count > 0`

---

## 4. 页面验收

### 4.1 Shell

检查项：

- 左 rail、顶部 header、主 workbench 存在
- 页面切换稳定
- `Start Capture` 不伪造运行状态
- 没有多余 `Docs / Quit` 入口

结果：

### 4.2 Source

检查项：

- 页面是“左列表 + 右详情”
- preview 与 formal capture 文案清楚区分
- 正式 capture 仍然是单 source
- offline import 入口存在

结果：

### 4.3 Overview

检查项：

- hero chart 存在
- status cards 存在
- destination map 预留区存在
- top destinations 存在
- top hosts / top services / top connections 存在

结果：

### 4.4 Inspect

检查项：

- filter bar 存在
- results table 存在
- footer summary 存在
- 过滤条件可影响当前结果集

结果：

### 4.5 Settings

检查项：

- runtime 面板存在
- core 面板存在
- diagnostics 面板存在
- 当前 source / BPF / tick interval / top N 可见
- endpoint / process state / version 可见

结果：

---

## 5. 范围边界确认

明确当前仍为后置能力：

- Source 实时 gRPC preview/device 拉取
- Overview 实时 projection stream
- Inspect 后端 query/projection 接线
- Settings 写回能力
- `Destination Map` 真实地图
- phase3 payload / session 能力

确认结果：

---

## 6. 结论

- 是否通过：
- 遗留问题：
- 进入 phase3 前是否需要补充修正：
