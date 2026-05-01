# Flowarden 第二阶段开发计划

## 1. 文档目的

本文用于冻结第二阶段的开发边界、实施顺序和验收口径。

第二阶段只做两件事：

1. 建立 `Rust core service + Avalonia UI` 的稳定运行闭环
2. 将第一阶段已经完成的投影结果图形化

本文不进入第三阶段的 payload 深度解析和会话级重建。

审计说明：

1. 本文定义的是第二阶段目标状态，不是当前已完成状态。
2. 当前实现状态必须以：
   - `flowarden_phase2_progress.md`
   - `flowarden_phase2_audit_against_plan.md`
   为准。
3. 若当前代码仅达到样本驱动 MVP，而未形成真实运行闭环，不得把本文目标条目表述成“已完成”。

配套的界面设计说明见：

- `flowarden_phase2_ui_design.md`
- `flowarden_phase2_backlog.md`
- `flowarden_phase2_implementation_sequence.md`

上游参考文档见：

- `../flowarden_phased_development_plan.md`
- `../sniffnet_refactor_avalonia_rust_grpc.md`
- `../sniffnet_reverse_analysis.md`
- `../ui-images/image-url.md`

---

## 2. 第二阶段完成定义

第二阶段完成，不等于“能打开一个窗口”，而是同时满足以下条件：

1. `flowarden-ui` 能在本机 `dotnet` SDK `8.0.125` 下稳定构建与运行
2. UI 能拉起或连接本地 `flowarden-core service`
3. UI 能列出设备、显示多设备 preview，并选择单一 source 正式抓包
4. UI 能完成 `Start / Stop / Pause / Resume`
5. UI 能实时展示第一阶段的 `tick_snapshots` 和 `final_snapshot`
6. UI 的 Overview / Inspect 页面数据与第一阶段 CLI 输出一致
7. UI 与 core 通过稳定契约通信，UI 不直接依赖 Rust 内部结构体
8. 第二阶段新增错误仍统一落到 `flowarden-error` 语义上

---

## 3. 约束与前提

### 3.1 平台与运行时基线

固定约束如下：

1. 使用本机 `dotnet --version` 当前结果：`8.0.125`
2. `flowarden-ui` 目标框架固定为 `net8.0`
3. 第二阶段先以当前主开发平台完成闭环，不提前做跨平台打包广覆盖
4. 继续保持第一阶段的 `YAGNI` 原则，不提前做第三阶段 UI

### 3.2 继承第一阶段的边界

第二阶段直接复用第一阶段已经成立的领域边界：

1. 秒级聚合是主输出，不推逐包原始流给 UI
2. live/offline 继续走同一条核心分析链路
3. 正式 capture 仍然只选一个 source
4. 多 device 行为只用于启动前 preview
5. payload 和 session 详情继续后置

### 3.3 工程原则

1. UI 只消费契约模型，不共享 Rust 内部结构
2. 不把 Avalonia ViewModel 反向渗透进 Rust core
3. 不为了 phase3 预埋过度抽象
4. 所有跨进程错误都要映射回稳定错误语义
5. UI 设计借鉴 Sniffnet 的信息密度和视觉重心，但不复制其 `iced` 组件结构
6. phase2 默认视觉基线固定为 `Cosmos Network System`
7. `Destination Map` 必须在 Overview 布局中预留区域，MVP 可先不实现内容

---

## 4. 阶段目标架构

第二阶段建议冻结为双进程模型：

```text
flowarden-ui (Avalonia, net8.0)
  -> app shell
  -> views / viewmodels
  -> grpc client or equivalent IPC client

flowarden-core service (Rust)
  -> capture runtime
  -> analysis
  -> aggregation
  -> projection stream
```

### 4.1 为什么仍然坚持双进程

原因不变：

1. UI 崩溃不直接拖死抓包核心
2. UI 与 core 可以各自调试、各自测试
3. 契约一旦稳定，第三阶段只是在 core 与 projection 上扩展
4. 不把复杂抓包线程模型塞进 Avalonia 进程

### 4.2 第二阶段新增的最小边界

第二阶段只新增以下稳定边界：

1. `Control API`
2. `Discovery API`
3. `Projection Stream API`
4. `Health / Version API`

这里不做：

1. 导出服务大扩展
2. 远程 core
3. 多用户或多会话服务化

---

## 5. 功能范围

## 5.1 必做范围

1. 建立 `flowarden-ui` Avalonia 工程
2. 固定本机 `dotnet` SDK 与构建脚本
3. 建立本地 core service mode
4. 冻结 phase2 的本地 IPC / gRPC 契约
5. 设备列表与多 device preview
6. 设备选定后的正式 capture 启停控制
7. Overview 页面
8. Inspect 页面
9. 基础状态栏、错误提示、连接状态提示
10. 设置页中的最小运行参数项：
   - capture source
   - BPF
   - tick interval
   - top N
   - output mode 只读展示
11. Overview 中的 `Destination Map` reserved panel

## 5.2 明确不做

1. payload 深解析 UI
2. 会话详情 UI
3. 通知中心完整版
4. 图标识别、国家旗帜、rDNS、MMDB 全量增强
5. 主题系统泛化
6. 插件化
7. 远程节点
8. 多语言
9. `Destination Map` 的真实地图引擎、地理增强与交互探索

---

## 6. 契约设计建议

## 6.1 第二阶段 UI 需要的最小服务组

### `DiscoveryService`

用于低频静态信息：

1. `ListDevices`
2. `PreviewDevices`
3. `GetVersion`
4. `GetRuntimeStatus`

### `ControlService`

用于抓包控制：

1. `StartCapture`
2. `StopCapture`
3. `PauseCapture`
4. `ResumeCapture`
5. `ApplyFilter`
6. `SetSource`

### `ProjectionService`

用于 UI 实时消费：

1. `StreamOverview`
2. `GetLatestOverview`
3. `GetInspectPage`

### `HealthService`

用于 UI 启动、重连、错误恢复：

1. `Ping`
2. `GetCoreInfo`

## 6.2 不要把逐包数据送进 UI

这条必须继续定死：

1. UI 只看 snapshot / projection
2. 逐包数据仍留在 core 内部
3. 不允许为了“看起来实时”就把 raw packet stream 暴露给 Avalonia

## 6.3 phase2 建议冻结的传输模型

建议冻结以下跨语言模型：

1. `DeviceSummaryDto`
2. `DevicePreviewDto`
3. `CaptureSessionStateDto`
4. `OverviewSnapshotDto`
5. `InspectFilterDto`
6. `ConnectionRowDto`
7. `ServiceRowDto`
8. `HostRowDto`
9. `CoreErrorDto`

这里特意使用独立 `Dto` 命名，是为了明确：

- 它们不是 Rust 内部领域对象
- 也不是 Avalonia ViewModel
- 它们只是进程间契约

---

## 7. UI 工程建议

## 7.1 目录建议

第二阶段建议在仓库内新增：

```text
flowarden-ui/
  Flowarden.Ui.sln
  src/Flowarden.Ui/
    App.axaml
    Program.cs
    Views/
    ViewModels/
    Models/
    Services/
    State/
    Assets/
    Styles/
```

## 7.2 Avalonia 结构建议

建议采用标准 MVVM，但保持收敛：

1. `Views`
   - 只负责布局与绑定
2. `ViewModels`
   - 负责状态整形、命令、局部 UI 逻辑
3. `Services`
   - 封装 IPC / gRPC client
4. `State`
   - 持有当前 capture 会话状态与页面共享状态

不建议第二阶段就引入复杂事件总线或全局 store 框架。

## 7.3 运行时建议

1. UI 启动时先检查 core
2. 若未启动，则尝试拉起本地 core 子进程
3. 建立连接后进入 device preview / source selection
4. 进入正式 Overview / Inspect 页面后，只订阅聚合流
5. core 断开时，UI 进入可恢复状态，而不是直接退出

---

## 8. 里程碑拆解

## M2-001 UI 工程骨架与运行基线

### 目标

建立 `net8.0 + Avalonia` 最小工程，并固定本机 SDK 基线。

### 输出

1. `flowarden-ui` 工程
2. `global.json` 或等价约束
3. 基础构建与运行说明

### 验收条件

1. 使用本机 `dotnet 8.0.125` 可构建
2. 窗口可启动
3. 工程结构可承接后续页面

## M2-002 core service mode 与 IPC 骨架

### 目标

让 Rust core 可以以 service mode 运行，并有本地通信入口。

### 输出

1. core service 启动模式
2. 最小健康检查接口
3. 控制与投影接口骨架

### 验收条件

1. UI 可判断 core 是否在线
2. UI 可拉起 core 或连接已运行 core
3. 错误路径清楚

## M2-003 设备列表与 preview 页面

### 目标

把 phase1 的多 device preview 图形化。

### 输出

1. device list panel
2. preview cards / table
3. 单 source 选择入口

### 验收条件

1. 所有 device preview 可展示
2. 错误设备或无权限场景可提示
3. 只能从一个 source 进入正式 capture

## M2-004 Overview 页面 MVP

### 目标

把第一阶段的 `tick_snapshots` 和 `final_snapshot` 图形化为主工作台。

### 输出

1. 总量卡片
2. 趋势图
3. `Destination Map` reserved panel
4. top hosts
5. top services
6. top connections

### 验收条件

1. 页面数据与 CLI 同步口径一致
2. 页面更新不卡死
3. live/offline 两种模式都可显示
4. `Destination Map` 区域在布局中固定存在，即使当前只显示 placeholder

## M2-005 Inspect 页面 MVP

### 目标

让用户按服务、地址、协议查看连接明细。

### 输出

1. filter bar
2. 连接表格
3. 排序、筛选、刷新

### 验收条件

1. 过滤条件可下发
2. 表格结果与 phase1 聚合结果一致
3. 不依赖 phase3 会话级数据

## M2-006 封板与打包

### 目标

完成第二阶段的本地可交付封板。

### 输出

1. 运行说明
2. UI 验收清单
3. 打包脚本或构建脚本

### 验收条件

1. 从启动到抓包到关闭流程完整
2. UI 与 core 可重复运行
3. 无明显资源失控

---

## 9. backlog 建议顺序

建议顺序如下：

```text
M2-001 -> M2-002 -> M2-003 -> M2-004 -> M2-005 -> M2-006
```

说明：

1. 先冻结工程和通信边界
2. 再做 device preview 与 source selection
3. 再做 Overview
4. 最后做 Inspect 与封板

不要把界面细节打磨放在协议和运行边界之前。

---

## 10. 验收口径

第二阶段最终验收建议按以下清单执行：

1. `dotnet --version` 为 `8.0.125`
2. `flowarden-ui` 可构建与启动
3. UI 能显示多 device preview
4. UI 能选择单一 source 正式抓包
5. UI 能完成 `Start / Stop / Pause / Resume`
6. Overview 页面数据与 phase1 CLI 一致
7. Inspect 页面过滤与结果一致
8. UI 遇到 core 错误时提示明确
9. core 意外退出时 UI 不崩溃
10. 文档齐备，可复跑
11. Overview 中已为 `Destination Map` 保留稳定布局区域

---

## 11. 风险与提前约束

### 风险一：过早沉迷 UI 细节

约束：

先冻结契约与页面骨架，再微调视觉。

### 风险二：UI 反向挤压 core 模型

约束：

UI 想要的新字段，先经过 projection / contract 评审，不能直接穿透到 capture 内部。

### 风险三：把多 device preview 误做成多 device 正式 capture

约束：

preview 与 formal capture 必须分开；正式 capture 仍保持单 source。

### 风险四：Avalonia 与 gRPC 同时引入导致调试面过宽

约束：

先做最小 service health + discovery，再做实时投影流。

---

## 12. 建议结论

如果用于评审，我建议把第二阶段的核心目标明确写成：

> 基于第一阶段已稳定的投影模型，建立 `Rust core service + Avalonia UI` 的本地桌面闭环，并以 `net8.0 / dotnet 8.0.125` 为开发基线完成 Overview 与 Inspect 的 MVP。

这样第二阶段结束时，Flowarden 将从“可运行 CLI 核心”升级为“可交互桌面监控器”，并在 `Cosmos Network System` 风格下为后续 `Destination Map` 和 phase3 深度分析预留稳定版位，但仍不提前进入第三阶段的深度分析复杂度。
