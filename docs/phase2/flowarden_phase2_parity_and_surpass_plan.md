# Flowarden 相对 Sniffnet：补齐与反超方案

## 1. 文档目的

本文在 `docs/phase2` 下冻结一版**产品能力对照后的演进方案**，回答三个问题：

1. 当前 Flowarden 相对 Sniffnet **还差什么**（补齐）。
2. 如何在不推倒 phase1/phase2 架构的前提下 **追上可用产品体验**。
3. 如何在架构与分析深度上 **形成可验证的反超**，而不是简单复刻 Sniffnet。

本文是 phase2 主线封板后的**能力演进总纲**，不改写 `M2-001`～`M2-009` / `M2-101` 已完成结论。

配套文档：

| 文档 | 关系 |
| --- | --- |
| `flowarden_phase2_progress.md` | 当前实现事实 |
| `flowarden_phase2_audit_against_plan.md` | phase2 目标兑现审计 |
| `flowarden_phase2_followup_enhancements.md` | 已识别的最小后续增强 |
| `flowarden_phase2_parity_and_surpass_backlog.md` | 本方案可执行 backlog |
| `flowarden_phase2_inspect_live_refresh_proposal.md` | 统一 live 运行态方向 |
| `flowarden_phase2_live_timeline_window_proposal.md` | live 时间窗（已 Accepted） |
| `../flowarden_behavior_signals_implementation.md` | 行为信号 / 通知反超模型 |
| `../phase3/flowarden_phase3_development_plan.md` | Light DPI / 进程 / ARP 深度波次 |
| `../sniffnet_reverse_analysis.md` | Sniffnet 能力边界参考 |

---

## 2. 结论摘要

### 2.1 一句话定位

> Sniffnet 是成熟的**单体 GUI 流量监控产品**；Flowarden 是**可独立演进的分析引擎 + Avalonia 工作台**。  
> 补齐目标是追上 Sniffnet 的“实时可监控闭环与关键增强能力”；反超目标是在**架构、可验证性、分析线索与离线复盘**上拉开差距。  
> **交付标准**：每一项能力都必须以**高质量代码**与**风格一致、完成度高的 UI** 落地，禁止“先凑功能再抛光”。

### 2.2 三层策略

| 层级 | 代号 | 目标 | 成功标准 |
| --- | --- | --- | --- |
| **L0 闭环补齐** | Catch-up Core | 做出 Sniffnet 级“能开始、能暂停、能实时看” | UI 真控制 capture + 统一 live 运行态 + core 可恢复 + §4.3 质量门禁 |
| **L1 体验对齐** | Parity | 关键增强能力达到 Sniffnet 可用对等 | 进程/rDNS/通知/名单/地理/地图对等，且新表面与现有 Cosmos UI **同一产品语言** |
| **L2 能力反超** | Surpass | 形成 Sniffnet 当前模型难以自然长出的优势 | 行为信号、CLI/UI 同源、forensics/诊断等可验证反超，代码与 UI 质量不低于 L0/L1 |

### 2.3 明确不做的事

1. **不复制** Sniffnet 的 `iced` 页面结构、多语言全量、程序图标细节堆叠。
2. **不把** 完整会话重建 / 全量 payload 审计塞进本方案主线（归属 phase3+，本方案只做可衔接的 Light DPI 切口）。
3. **不推翻** phase1 聚合优先模型与 phase2 gRPC 投影契约。
4. **不把** 本方案任务混写回“phase2 backlog 未完成”。

推荐统一表述：

> phase2 backlog 已完成；本文件定义的是 **phase2.x 补齐波次 + 反超波次**，独立排期、独立验收。

---

## 3. 当前基线（相对 Sniffnet）

### 3.1 已对齐或接近对齐

| 能力 | Sniffnet | Flowarden | 判断 |
| --- | --- | --- | --- |
| live / offline 共用分析链路 | 有 | 有 | 对齐 |
| BPF | 有 | 有 | 对齐 |
| 轻量 L2/L3/L4 | 有 | 有 | 对齐 |
| 方向启发式 | 有 | 有 | 对齐 |
| 服务启发式 | 有 | 有 | 对齐 |
| 秒级聚合 | 有 | 有 | 对齐 |
| offline 按 pcap 时间推进 | 有 | 有 | 对齐 |
| dropped / last packet 质量指标 | 有 | 有 | 对齐 |
| live timeline 有界窗口（约 30 tick） | 有 | 已 Accepted / Implemented | 对齐 |
| Overview stream 第一步 | tick 推送 | `StreamOverview` + `LiveProjectionState` 已部分落地 | 部分对齐 |
| Country MMDB | 有 | CLI 输出已增强 | 部分对齐 |
| 桌面 UI 四页骨架 | Overview / Inspect / Notifications / Settings | Source / Overview / Inspect / Settings | 结构不同但壳已成立 |

### 3.2 明确落后（补齐对象）

| 能力 | Sniffnet | Flowarden | 补齐层级 |
| --- | --- | --- | --- |
| Start / Stop / Pause / Resume 真闭环 | 成熟 | control 骨架 / 未完成闭环 | L0 |
| 统一 live 运行态驱动多页 | 成熟 | Overview stream 有；Inspect TCP 等仍分裂 | L0 |
| stream 断线重连 / backpressure | 隐式于单体内核 | 仍后置 | L0 |
| core 运行中崩溃恢复 | 单体进程内 | 启动期探活为主 | L0 |
| 进程名 / PID 归属 | 有 | phase3 规划 | L1 |
| rDNS | 有 | 后置 | L1 |
| 通知（阈值/收藏/黑名单） | 有 | 有 signals 设计，未产品化 | L1 |
| 收藏 / 黑名单实体管理 | 有 | 无 | L1 |
| Destination Map 真内容 | 有 | reserved shell | L1 |
| ASN | 有 | 弱 / 未产品化 | L1 |
| 缩略模式 thumbnail | 有 | 无 | L1 可选 |
| 主题 / i18n / 程序图标 | 有 | 不做或后置 | 非主线 |
| 配置持久化 confy | 有 | 最小 / 不完整 | L1 |

### 3.3 已具备的结构性优势（反超底座，尚未完全兑现）

| 优势底座 | 说明 |
| --- | --- |
| Core / UI 解耦 | gRPC 契约 + projection，UI 不绑内部模型 |
| Headless CLI 一等公民 | JSON / golden / 脚本化验收 |
| 统一错误语义 | `flowarden-error` |
| 分阶段可验证工程 | runbook / acceptance / golden |
| 行为信号设计 | 比 notification 更强的证据链与 pivot 模型 |
| Avalonia 工作台形态 | filter-first Inspect、bento Overview，利于分析深化 |

---

## 4. 设计原则

### 4.1 补齐原则

1. **先闭环，后增强**：没有真控制面和统一 live 态，再堆进程/通知也只是“会显示静态数据的壳”。
2. **对齐语义，不复制皮肤**：目标是 Sniffnet 的运行语义与信息密度，不是 1:1 抄页面。
3. **增强异步化**：进程 lookup、rDNS、地理补全不得阻塞抓包主循环（对齐 Sniffnet worker 思路，但落在 core 侧异步补全）。
4. **投影优先**：所有新增字段先进 core projection / gRPC DTO，再进 UI；禁止 UI 直连 OS API 做“旁路分析”。

### 4.2 反超原则

1. **证据优先于提醒**：通知只是 signal kind 的一种呈现，不是核心模型。
2. **CLI 与 UI 同契约**：任何 Sniffnet 没有的 headless 能力，都算可证明反超。
3. **离线复盘与实时监测对称**：同一 detector，输出 Offline Finding / Active Signal 两套语义。
4. **为 phase3 留切口，不提前做全量 Wireshark**：Light DPI（如 SNI）可进反超波次的衔接项；会话重建不进本方案必做。

### 4.3 硬性质量门禁（不可降级）

本方案所有 `M2E-*` / `M2X-*` 任务默认接受以下补充要求。**功能完成但未达质量门禁，一律不得标 `completed`。**

> 目标不是“先凑功能再抛光”，而是 **代码高质量 + UI 风格一致且高质量** 与功能交付同步验收。

#### 4.3.1 代码高质量（Rust core / CLI / gRPC）

| 维度 | 硬要求 |
| --- | --- |
| 工程门禁 | `cargo fmt --all -- --check`；`cargo clippy --all-targets --all-features -- -D warnings`；相关 crate `cargo test` 通过 |
| 错误处理 | 可预期错误统一进 `flowarden-error`；禁止热路径 `unwrap`/`expect`（不变量除外须注释说明） |
| 模块边界 | capture / analysis / enrichment / signals / projection / service 职责清晰；禁止 UI 语义渗入 core 领域模型 |
| 契约稳定 | gRPC/DTO 变更 backward-tolerant；新增字段有默认值或可选语义；禁止 UI 依赖 tonic/Rust 内部类型 |
| 并发与性能 | enrichment/lookup 异步化；stream/timeline 有界；禁止无界缓存、无界重连、主循环阻塞 IO |
| 所有权与分配 | 优先直接转换，避免多余中间容器；关键路径避免不必要 clone |
| 可测性 | 状态机、聚合、signal 去重、control 非法转移等必须有单测或 golden；bugfix 先复现再修 |
| 可观测性 | 关键失败可诊断（错误链/日志语义稳定），但不把 transport 细节泄漏给用户态文案 |

#### 4.3.2 代码高质量（Avalonia UI / C#）

| 维度 | 硬要求 |
| --- | --- |
| 工程门禁 | `dotnet build` 通过；保持既有 format/分析约定；新增代码无无意义 warning 堆积 |
| MVVM 边界 | View 不写业务编排；ViewModel 不直接吃 gRPC 生成类型（经 Client/DTO）；状态集中在 shell / live state |
| 状态机 | loading/ready/running/paused/stopping/failed/offline/stale 等用户态统一；禁止页面私自发明冲突状态名 |
| 错误呈现 | 用户文案 + 可折叠诊断；禁止主路径直出 `Status(StatusCode=...)` |
| 资源与生命周期 | stream 订阅、timer、watcher 必须在页面/应用生命周期内释放；禁止泄漏订阅 |
| 可维护性 | 命名、分层、复用现有 `Services/` / `State/` / `Styles/`；避免复制粘贴出第二套 client 或状态源 |

#### 4.3.3 UI 风格一致（Cosmos / Technical Forensic Console）

视觉与交互基线固定为现有 phase2 体系，**新增页面与控件必须像同一产品**，不允许“功能岛”式另起风格。

| 维度 | 硬要求 |
| --- | --- |
| 设计系统 | 继续使用 `Cosmos Network System` / `Technical Forensic Console` token；颜色、字号、间距、圆角、描边从 `Styles/Theme.axaml`、`Styles/Controls.axaml` 与既有组件扩展，禁止页面内联一套新 palette |
| 参考对齐 | 结构与密度对齐 `flowarden_phase2_ui_design.md`、`stitch_flowarden_network_monitoring_*`、`tfc_runtime_screenshots/*_after*.png` |
| Shell 一致 | Left Rail + Top App Bar + Main Workbench；新页（如 Signals）必须接入同一 shell，不得独立窗口壳或另类导航 |
| 组件复用 | 状态卡、榜单、表格、filter bar、空态/错误态优先复用 `Views/Components/`；确需新组件时抽到共享层并吃同一 token |
| 信息架构 | 监控工作台密度：关键指标优先，避免标题重复、大块空洞、控件散落 |
| 状态表达 | Core/Capture 状态点、busy/empty/error/stale 视觉语言全页统一 |
| 图表与数据可视 | timeline、榜单、地图沿用现有 glow / rank / workbench 语言，不引入突兀第三套 chart 风格 |
| 文案语气 | 克制、诊断向；signal 用 candidate/signal/finding，不用“confirmed attack”等过度措辞 |

**明确禁止：**

1. 为赶功能临时使用默认 Fluent/系统原生杂糅外观覆盖主路径。  
2. 新页使用与 Overview/Inspect 明显不同的卡片阴影、字号阶梯或间距体系。  
3. 用 MessageBox/裸字符串堆叠替代既有状态区与诊断区模式。  
4. 把 Sniffnet 截图皮肤硬贴进 Cosmos 体系造成混搭。  

#### 4.3.4 UI 高质量（完成度，不只是“能点”）

| 维度 | 硬要求 |
| --- | --- |
| 主路径打磨 | Start/Stop/Pause、筛选、刷新、pivot 等主路径交互反馈明确（进行中/成功/失败） |
| 空态 / 加载 / 错误 / stale | 四态齐全且样式统一；不得空白闪烁或布局塌陷 |
| 布局稳定 | live 刷新时榜单/表格避免剧烈跳动；列宽、数字对齐、截断策略专业 |
| 可读性 | 对比度、层级、主次信息分明；长 IP/域名可截断+tooltip |
| 性能体感 | UI 线程不因高频 tick 卡顿；绑定更新有节流/批量策略（若需要） |
| 无障碍下限 | 关键按钮可键盘到达；状态不仅靠颜色表达（配文字/图标） |
| 回归对照 | 有 UI 改动的任务，对照最近 `tfc_runtime_screenshots` 做前后一致性检查；明显回退不得收口 |

#### 4.3.5 任务级质量验收公式

每个任务完成时必须同时满足：

```text
功能验收（本任务条款）
  AND 代码质量门禁（4.3.1 / 4.3.2）
  AND（凡触及 UI）风格一致 + UI 高质量（4.3.3 / 4.3.4）
  AND runbook / 测试证据可重复
```

任一项缺失 → 状态保持 `in_progress` 或退回，不得标 `completed`。

#### 4.3.6 波次封板最低清单

每一波次（L0/L1/L2）封板至少满足：

1. Rust：`fmt` + `clippy -D warnings` + `test`  
2. UI：`dotnet build`；主路径手测无崩溃、无状态分叉  
3. 错误语义统一，无 transport 泄漏  
4. 新增 UI 与现有四页 **同一视觉体系**（截图或手测记录）  
5. 有可重复 runbook 场景  
6. 无已知“先功能后抛光”债务写入完成说明（若有债务必须单列 follow-up，不得默默吞掉）  

---

## 5. 总体路线图

```text
[L0 Catch-up Core]     2~4 周量级（按 1 人全职粗估）
    M2E-001 Control plane 真闭环
    M2E-002 统一 LiveProjection 运行态
    M2E-003 Stream 健壮性（重连 / 降级）
    M2E-004 Core failure recovery
    M2E-005 运行态 UI 语义收口
         |
         v
[L1 Parity]            4~8 周量级
    M2E-101 进程归属（与 phase3 波次协调，可先 macOS）
    M2E-102 rDNS 异步补全
    M2E-103 收藏 / 观察列表 / 黑名单实体
    M2E-104 通知兼容三类 + Signals 最小页
    M2E-105 地理增强（Country 进 UI/projection；ASN 可选）
    M2E-106 Destination Map 真内容（Country 聚合即可）
    M2E-107 配置持久化（source 偏好、阈值、观察列表）
    M2E-108 （可选）Thumbnail / compact live mode
         |
         v
[L2 Surpass]           可与 L1 后半并行
    M2X-001 Behavior Signal 统一模型落地
    M2X-002 Live / Offline 对称检测与 pivot
    M2X-003 Analyst workbench（从 signal 跳到 flow / host / time window）
    M2X-004 CLI export 与 UI 投影字段同源
    M2X-005 Capture quality / health 诊断台反超
    M2X-006 Light DPI 切口：TLS SNI（与 phase3 衔接）
    M2X-007 Replay forensics 时间轴 + finding 列表
```

说明：

- 时间量为粗估，用于排序，不是承诺排期。
- `M2E-101` 进程归属与 phase3 文档重叠：以本方案为**产品排期入口**，实现细节仍遵循 phase3 异步 lookup 约束。
- `M2X-006` 是反超衔接项：有则形成“比 Sniffnet 更深一点”的可感知差异；无则 L2 仍可通过 signals + CLI + 复盘成立。

---

## 6. L0 闭环补齐方案（Catch-up Core）

### 6.1 目标状态

用户打开 Avalonia UI 后可以：

1. 选择 live device 或 offline pcap  
2. **真正** Start / Stop / Pause / Resume  
3. Overview 与 Inspect Flows 在 live 下共享同一份运行态并持续刷新  
4. core 崩溃或失联时进入可理解、可恢复状态  
5. shell 状态点表达的是真实 capture / core 状态，而不是占位  

这是相对 Sniffnet 的**最低可用对等线**。

### 6.2 M2E-001 Control plane 真闭环

**范围**

1. Rust `ControlService` 从 skeleton 升级为真实控制：
   - `SetSource`
   - `ApplyFilter`（BPF）
   - `StartCapture`
   - `StopCapture`
   - `PauseCapture`
   - `ResumeCapture`
2. 控制结果反映到 `CaptureSessionState` projection / health。
3. UI shell 与 Source 页成为控制入口；`Start Capture` 不再只是导航。

**约束**

1. 正式 capture 仍单 source。  
2. pause 语义对齐 phase1 runtime：暂停消费/聚合推进，而不是“假装停了”。  
3. offline replay 的 pause/resume 语义必须单独写清验收口径。  

**验收**

1. UI 完成 live Start → 有 tick 增长 → Pause 冻结增长 → Resume 恢复 → Stop 收尾 final snapshot。  
2. CLI 与 gRPC 控制同一 runtime 语义（不要求 CLI 暴露全部按钮，但 core 行为一致）。  
3. 非法状态转移返回稳定错误（如未 Start 就 Pause）。  

### 6.3 M2E-002 统一 LiveProjection 运行态

**范围**

对齐 Sniffnet 模型：

```text
capture tick
  -> core 统一运行态 / 有界 timeline
  -> StreamOverview（或等价统一 live stream）
  -> UI LiveProjectionState
  -> Overview / Inspect Flows /（后续）Signals 共享
```

**当前已有**

1. `StreamOverview`  
2. `LiveProjectionState` 驱动 Overview + Inspect Flows  

**本项要完成**

1. 冻结“单一 live 源”：禁止页面各自再开互不兼容的 stream。  
2. Inspect 过滤优先在共享运行态上本地筛选；仅当结果集策略需要时才回源 query。  
3. TCP Connections 模式给出明确决策：
   - **推荐 A**：纳入统一 live snapshot 的可筛选切片（与 Sniffnet 更一致）  
   - **备选 B**：保持独立 query，但必须文档声明并限制刷新策略  

**验收**

1. live 下 Overview 与 Inspect Flows 数字同源（同一 tick）。  
2. 切换页面不造成双倍订阅或状态分叉。  
3. Stop 后统一落到 final snapshot，不残留假 live。  

### 6.4 M2E-003 Stream 健壮性

**范围**

1. stream 中断检测  
2. 有限次自动重连 / 退避  
3. 失败后降级为 `GetLatestOverview` 轮询或显式 stale  
4. subscriber 生命周期与 capture 生命周期绑定  

**验收**

1. 人为中断 stream 后 UI 进入可识别降级态，不静默空白。  
2. core 恢复后可重新进入 live。  
3. 不出现无界重连打爆 CPU。  

### 6.5 M2E-004 Core failure recovery

**范围**

1. runtime health watcher（周期 `GetHealth` / channel 存活）  
2. core 失联 → shell 降级  
3. 手动 Relaunch / Reconnect  
4. 页面 stale banner  
5. 恢复后是否自动恢复 capture：**默认不自动恢复抓包**（安全默认），仅恢复连接  

**验收**

1. kill core 进程后 UI 在合理时间内显示 core offline。  
2. 用户可 relaunch 并重新 Start。  
3. 旧 projection 标记 stale，避免被当成实时真相。  

### 6.6 M2E-005 运行态 UI 语义收口

**范围**

承接 `flowarden_phase2_ui_gap_analysis.md` 中仍有效的运行态要求：

1. 统一用户态：`loading / ready / running / paused / stopping / failed / offline / stale`  
2. 禁止把原始 gRPC `Status(StatusCode=...)` 直接甩给用户  
3. Core / Capture 双状态点与真实后端对齐  

**验收**

1. 主路径错误均可映射为用户可读文案 + 诊断详情折叠。  
2. shell 状态与 Source/Settings 诊断不矛盾。  

### 6.7 L0 完成定义

当且仅当：

1. 真控制闭环可用  
2. 统一 live 运行态成立  
3. stream 与 core 失败有降级/恢复  
4. 用户态状态机收口  
5. **§4.3 代码与 UI 质量门禁全部满足**（含 shell 控制区与 Source/Overview 主路径视觉一致性）  

则宣布：

> **Flowarden 达到 Sniffnet 级“实时监控闭环”最低对等线（L0 Parity），且交付质量不低于 phase2 已收口的 Cosmos 工作台标准。**

---

## 7. L1 体验对齐方案（Parity）

L1 的目标不是功能数量追平 Sniffnet 全部周边，而是补齐**用户会直接拿来比较**的关键增强。

### 7.1 能力映射

| Sniffnet 能力 | Flowarden 落地 | 任务 |
| --- | --- | --- |
| 连接 → 进程 | async port→pid/name lookup + projection 字段 | M2E-101 |
| rDNS | async reverse DNS cache | M2E-102 |
| Favorite / Blacklist | Watchlist / Known-bad 实体 store | M2E-103 |
| Notifications 三类 | Signals 最小 feed + 三类 kind | M2E-104 |
| MMDB country / ASN | Country 进 projection；ASN 可选 | M2E-105 |
| 地图 | Destination Map 按 country 聚合着色 | M2E-106 |
| confy 设置 | 本地 settings store | M2E-107 |
| Thumbnail | compact floating / mini overview（可选） | M2E-108 |

### 7.2 M2E-101 进程归属

**产品语义**

Inspect / Top connections 展示 `process_name`、`pid`（推断结果需可标记）。

**实现约束**

1. 主循环只提交 lookup 任务，不在热路径阻塞。  
2. `(protocol, local_port) → (pid, name)` 带 TTL 缓存。  
3. 先 macOS，再 Linux/Windows；文档标明平台矩阵。  
4. 与 `../phase3/flowarden_phase3_development_plan.md` 对齐，避免双实现。  

**验收**

1. live 下本机常见连接能显示进程名。  
2. lookup 失败不丢连接行。  
3. 压测下抓包丢包率不因 lookup 显著恶化（相对基线记录）。  

### 7.3 M2E-102 rDNS

**产品语义**

Host 列表在**有 PTR 时**可显示 reverse name；失败回退 **IP · 国家**。

**定性（已确认）**

- rDNS **不是**主机语义最佳主解，仅为与 Sniffnet 同款的**机会性增强**。  
- 业务域名主解：TLS SNI（L2/Phase3）。  
- 稳定底：Country[+ASN] + IP；本机侧：进程归属。  
- **不**以「Top Hosts 大多有业务主机名」验收本项。  

**实现约束**

1. 异步、限并发/超时、带负缓存；不得阻塞抓包主路径。  
2. offline 可按策略开关（默认关或懒加载）。  

### 7.4 M2E-103 观察列表 / 黑名单

**产品语义**

用户可标记：

1. Watched host / service  
2. Known-bad host  

标记进入持久化 store，并成为 signal 触发源。

**注意**

不复制 Sniffnet 全部收藏交互细节；优先保证 **可标记 → 可触发 → 可跳转** 闭环。

### 7.5 M2E-104 通知对等 + Signals 入口

**产品语义**

兼容 Sniffnet 三类：

1. `DataThresholdExceeded`  
2. `WatchedEntityTransmitted`  
3. `KnownBadHostTransmitted`  

UI 最小形态：

1. shell 未读计数  
2. Signals / Notifications 列表（可并入 rail 新页或 Settings 旁独立页）  
3. 点击可 pivot 到相关 host / time window  

**反超预埋**

列表项字段预留 severity / confidence / status / evidence refs，即使 L1 只填最小集。

### 7.6 M2E-105 / M2E-106 地理与地图

**产品语义**

1. projection 中 host/connection 带 country code / name  
2. Destination Map 用现有 equal-earth 资产做 country 级聚合，不再是纯 placeholder  
3. ASN 作为可选增强，不阻塞地图 MVP  

**验收**

1. Overview 地图区显示真实 country 分布（有流量时）。  
2. 与 Top Destinations 数字可对上（允许 top-N 截断差异）。  

### 7.7 M2E-107 配置持久化

最小持久化集：

1. 上次 source 偏好  
2. BPF 文本  
3. 数据阈值  
4. watchlist / blacklist  
5. UI 密度 / timeline window 等非敏感偏好  

### 7.8 M2E-108 Thumbnail（可选）

仅当 L0 + L1 主项稳定后再做：

1. 迷你吞吐图  
2. 一键回主窗  
3. 不单独再开分析链路  

### 7.9 L1 完成定义

> **关键增强与 Sniffnet 达到“同场对比不明显短板”的产品对等（L1 Parity）。**  
> 仍允许：无 i18n、无程序图标精致度、无完整主题市场。  
> **不允许**：Signals/Map/名单等新表面与现有页风格割裂，或 enrichment 代码以牺牲边界/测试为代价换功能。

---

## 8. L2 反超方案（Surpass）

反超不靠“比 Sniffnet 多几个按钮”，而靠 Sniffnet 架构天然不擅长的方向。

### 8.1 反超主张（对外可讲述）

| 主张 | Sniffnet 现状 | Flowarden 反超落点 |
| --- | --- | --- |
| 分析引擎可独立运行 | GUI 驱动为主 | CLI + resident core + 同一 projection 契约 |
| 线索可复盘、可 pivot | 通知偏提醒 | BehaviorSignal 证据链 + 状态机 |
| 离线/实时对称 | 更偏 live 监控 | Offline Finding / Active Signal 统一 detector |
| 质量可证明 | 测试资产因项目而异 | golden / runbook / 契约测试常态化 |
| 工作台可深化 | 连接聚合视图 | Analyst pivot + Light DPI 切口 + phase3 会话路径 |
| 运行诊断 | 偏业务通知 | Capture quality / core health 诊断台 |

### 8.2 M2X-001 / M2X-002 Behavior Signal 体系

落地 `../flowarden_behavior_signals_implementation.md` 的最小可演示切片：

1. 统一 `BehaviorSignal` 模型进入 core projection  
2. 三类 Sniffnet 兼容 kind 作为子集  
3. 实时 ActiveSignal 状态：New / Active / Updated / Resolved / Expired  
4. 离线 OfflineFinding 批处理输出  
5. 去重键 + cooldown  

**相对 Sniffnet 的可感知差异**

1. 信号带时间窗、主体、关联 flows、严重度/置信度  
2. 不是只能看“响过一次”的日志  

### 8.3 M2X-003 Analyst workbench

**产品动作**

从一条 signal 一键：

1. 跳到 Inspect 并预填 filter  
2. 聚焦时间窗  
3. 高亮相关 host/connection  

这是监控器 → **研判台** 的关键跃迁。

### 8.4 M2X-004 CLI / UI 同源

**规则**

任何进入 UI 的增强字段（process、sni、country、signal summary）必须能在 CLI JSON 契约中找到对应或明确标注 UI-only 展示态。

**反超点**

Sniffnet 难以在无 GUI 场景做同等自动化验收；Flowarden 可以。

### 8.5 M2X-005 诊断台反超

Settings / Diagnostics 升级为：

1. dropped packets 趋势  
2. last packet age  
3. stream 状态 / subscriber 数  
4. lookup queue 积压  
5. core uptime / restart count  

Sniffnet 强在业务可视化；Flowarden 应用**可运维性**反超。

### 8.6 M2X-006 Light DPI 切口（SNI）

**范围（克制）**

1. 仅对可疑/TLS 握手相关包做有限 payload preview  
2. 提取 SNI，写入 connection enrichment  
3. 可用于服务识别增强与 Inspect 过滤  

**不做**

1. 全量 HTTP 重组  
2. 全流量 payload 落盘  

这与 phase3 计划衔接，但作为反超的**第一可见差异化解析能力**。

### 8.7 M2X-007 Replay forensics

Offline 模式提供：

1. 全段 timeline（可与 live 有界窗口策略区分）  
2. finding 列表  
3. 跳转到对应 tick / host  

把 Sniffnet 偏 live 的优势，扩展为 **live 监控 + offline 复盘** 双模态。

### 8.8 L2 完成定义

至少同时满足以下 4 条中的 3 条，即可宣布阶段性反超成立：

1. BehaviorSignal 实时+离线可演示，且强于三类 notification 日志  
2. CLI JSON 与 UI 关键增强字段同源可测  
3. Analyst pivot 工作流可用  
4. SNI 或等价 Light DPI 增强在 Inspect 可见  

且必须同时满足：

5. signal/forensics/诊断相关 UI 完全融入 Cosmos shell，无“第二套产品皮肤”  
6. §4.3 质量门禁通过；反超能力有自动化或 golden 证据，不只是演示脚本  

---

## 9. 与 Phase3 的边界

| 主题 | 本方案（phase2.x） | Phase3 |
| --- | --- | --- |
| 进程归属 | L1 可先做最小可用 | 平台矩阵与性能打磨 |
| ARP 全量 | 不强制 | 波次 1 |
| TLS SNI | L2 切口可选 | Light DPI 正式波次 |
| 会话状态机 | 不做 | 后续更深阶段 |
| HTTP/DNS 全解析 | 不做 | 后续更深阶段 |
| 流重组 | 不做 | 后续更深阶段 |

原则：

> phase2.x 负责“产品闭环 + 对等 + 可感知反超”；phase3 负责“解析深度升级”。两者通过 projection 字段扩展衔接，不双线分叉模型。

---

## 10. 架构落位建议

```text
flowarden-core
  capture/runtime          # L0 控制语义
  analysis/*               # L1/L2 enrichment hooks
  flow/aggregator          # 累计聚合 + 有界 timeline
  enrichment/
    process_lookup         # async
    rdns                   # async
    geo                    # country/asn
    light_dpi              # sni optional
  signals/                 # detectors + state
  projection/              # overview/inspect/signals snapshots

flowarden (resident)
  grpc control/projection/health/discovery
  stream fan-out + backpressure

flowarden-ui
  LiveProjectionState      # 唯一 live 源
  ControlClient            # 真控制
  HealthWatcher            # 恢复
  Pages: Source/Overview/Inspect/Signals/Settings
```

契约扩展原则：

1. 新增字段必须 backward-tolerant（旧 UI 可忽略）。  
2. stream 消息保持有界（timeline window 已冻结）。  
3. UI-only 状态不得回写 core 领域真相，仅允许 watchlist 等用户实体经 control/config API 下发。  

---

## 11. 优先级与依赖

### 11.1 硬依赖

```text
M2E-001 Control
   -> M2E-002 统一 live 态验收才有意义
   -> M2E-003/004 健壮性与恢复
   -> M2E-005 用户态收口

M2E-103 Watchlist
   -> M2E-104 通知/信号触发

M2E-105 Geo
   -> M2E-106 Map

M2X-001 Signal 模型
   -> M2X-002/003
```

### 11.2 推荐实施顺序（摘要）

1. **先 L0 全完成**（不可并行砍掉 control）  
2. L1 按：进程 → 地理/地图 → watchlist → signals 页 → rDNS → 配置持久化  
3. L2 在 L1 的 watchlist/signals 之后启动，可与 rDNS/地图抛光并行  
4. SNI 切口在 enrichment 框架稳定后插入  

### 11.3 资源建议

| 角色 | 关注 |
| --- | --- |
| Rust core | control、stream、enrichment、signals |
| Avalonia UI | 状态机、共享 live 态、Signals/Map、pivot |
| 质量 | golden 扩展、live 手测 runbook、失败注入 |

---

## 12. 验收与里程碑

### Milestone A — L0 Closed Loop

- [ ] Start/Stop/Pause/Resume 真闭环  
- [ ] 统一 LiveProjection  
- [ ] stream 降级 + core 恢复  
- [ ] 用户态状态机无原始 gRPC 泄漏  
- [ ] 代码质量门禁（fmt/clippy/test/build）通过  
- [ ] 控制区与主路径 UI 与现有 Cosmos 截图基线一致、无风格回退  

### Milestone B — L1 Product Parity

- [ ] 进程归属（至少一平台）  
- [ ] 三类通知语义可触发可查看  
- [ ] watchlist/blacklist 可持久化  
- [ ] Country 进 UI + Map 非 placeholder  
- [ ] rDNS 或明确平台限制说明  
- [ ] 新增 Signals/Map 等表面与 Source/Overview/Inspect/Settings **同一设计体系**  
- [ ] enrichment 异步路径有测试与性能底线记录  

### Milestone C — L2 Surpass Slice

- [ ] BehaviorSignal 模型可演示  
- [ ] pivot 工作流  
- [ ] CLI/UI 同源字段  
- [ ] 诊断台或 SNI 至少一个可感知反超点  
- [ ] 反超相关 UI 高质量收口（空态/加载/错误/stale 齐全）  
- [ ] 无未单列的质量债务  

每个 Milestone 必须同步更新：

1. `flowarden_phase2_progress.md`（或新建 phase2.x progress）  
2. runbook 场景  
3. acceptance 勾选结果  
4. 若有 UI 变化：补充或更新对照截图说明（可放 `tfc_runtime_screenshots/` 或任务完成记录）  

---

## 13. 风险与对策

| 风险 | 影响 | 对策 |
| --- | --- | --- |
| 过早做 L1 导致 L0 长期假完成 | 产品仍不能真监控 | L0 未封板禁止宣称 parity |
| 进程/rDNS 阻塞热路径 | 丢包、卡顿 | 强制 async + 队列上限 + 可关闭 |
| Inspect 多数据源分裂 | 数字不一致 | 单一 LiveProjectionState 政策 |
| 信号系统做成第二套通知 | 模型分叉 | Sniffnet 三类只作 kind，不另起平行系统 |
| 与 phase3 重复造 enrichment | 浪费 | 共享 enrichment 模块与 DTO |
| 地图/ASN 耗时吞噬主线 | 延期 | Map 先 country 聚合，ASN 可关 |
| 自动恢复 capture 带来安全意外 | 非预期抓包 | 默认只恢复 core 连接 |
| 为赶功能降低代码质量 | 债务滚雪球、难维护 | §4.3 未过不得 `completed`；clippy/test 一票否决 |
| 新页/新控件风格漂移 | 产品像拼盘 | 强制 token/组件复用；里程碑做截图对照 |
| “能显示”但交互未打磨 | 观感劣于 Sniffnet | UI 四态与主路径反馈列入验收，禁止只交骨架 |

---

## 14. 成功叙事（对内）

补齐完成后，应能诚实地说：

> Flowarden 已经具备与 Sniffnet 同场比较的实时监控闭环，并补齐进程、通知、地理等关键增强。

反超切片完成后，应能进一步说：

> 在相同监控能力之上，Flowarden 提供可独立运行的分析引擎、可测试契约、以及面向研判的行为信号与离线复盘能力；后续深度解析沿 projection 持续增强，而不再受单体 GUI 绑定。

---

## 15. 下一步动作

1. 评审并冻结本文 L0/L1/L2 边界。  
2. 按 `flowarden_phase2_parity_and_surpass_backlog.md` 拆迭代。  
3. 立即启动 **M2E-001 Control plane**，未完成前不开启大范围 L1 UI 抛光。  
4. 将 `flowarden_phase2_followup_enhancements.md` 中的三项归入 L0 子集（已在 backlog 对齐）。  

---

## 16. 文档状态

| 项 | 值 |
| --- | --- |
| 状态 | Draft for review |
| 适用范围 | phase2 封板后的 phase2.x 演进 |
| 是否替代 phase3 计划 | 否，仅衔接 |
| 是否回写 M2 backlog 完成度 | 否 |
| 补充硬约束 | **代码高质量**；**UI 风格一致且高质量**（见 §4.3） |
