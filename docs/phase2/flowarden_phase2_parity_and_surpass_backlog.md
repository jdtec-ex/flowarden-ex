# Flowarden 补齐与反超 Backlog（phase2.x）

## 1. 文档目的

将 `flowarden_phase2_parity_and_surpass_plan.md` 拆成可排期、可验收的 backlog。

编号约定：

| 前缀 | 含义 |
| --- | --- |
| `M2E-0xx` | L0 闭环补齐（Catch-up Core） |
| `M2E-1xx` | L1 体验对齐（Parity） |
| `M2X-0xx` | L2 能力反超（Surpass） |

状态约定：

- `not_started`
- `in_progress`
- `completed`
- `blocked`
- `deferred`

本 backlog **不**回写 `M2-001`～`M2-009` / `M2-101` 完成状态。

---

## 1.1 跨任务硬性质量门禁（适用全部 M2E / M2X）

补充要求（与总纲 §4.3 一致）：

1. **代码要高质量**  
2. **UI 要风格保持一致，且高质量**  

**功能做完但门禁未过 → 不得标记 `completed`。**

### 代码（每项必过）

| 检查 | 标准 |
| --- | --- |
| Rust 格式/静态检查 | `cargo fmt --all -- --check`；`cargo clippy --all-targets --all-features -- -D warnings` |
| Rust 测试 | 相关 crate `cargo test` 通过；新增逻辑有单测/golden 或明确不可测说明 |
| .NET | `dotnet build` 通过；无新增无意义 warning 堆积 |
| 错误 | 可预期错误进 `flowarden-error`；UI 不直出原始 gRPC Status 文案 |
| 边界 | core 不渗 UI；UI 不吃 Rust 内部类型；enrichment 不堵抓包热路径 |
| 生命周期 | 订阅/timer/watcher 可释放；无无界缓存/无界重连 |

### UI（凡改动界面的项必过）

| 检查 | 标准 |
| --- | --- |
| 设计体系 | 沿用 Cosmos / Technical Forensic Console token 与 `Styles/*`；禁止页面私有 palette |
| 一致性 | 与 `flowarden_phase2_ui_design.md`、`stitch_flowarden_network_monitoring_*`、`tfc_runtime_screenshots` 同一产品语言 |
| 组件 | 优先复用 `Views/Components/`；新组件进共享层 |
| 壳层 | 新页必须进既有 App Shell（Rail + Top Bar + Workbench） |
| 完成度 | 空态/加载/错误/stale 四态齐全；主路径有明确反馈；live 刷新布局稳定 |
| 对照 | 完成记录中写清 UI 对照结论（一致 / 有意差异及原因） |

### 完成记录必填质量段

每个任务完成记录除功能证据外，追加：

```markdown
### 质量验收
- 代码门禁：fmt/clippy/test/build 结果
- UI 触及：是/否
- 若是：风格一致性说明 + 四态/主路径打磨说明
- 已知债务：无 / 已单列 follow-up（链接）
```

---

## 2. 依赖总览

```text
M2E-001 Control
  -> M2E-002 Unified LiveProjection
  -> M2E-003 Stream robustness
  -> M2E-004 Core recovery
  -> M2E-005 UX state machine
        |
        +-> M2E-101 Process
        +-> M2E-102 rDNS
        +-> M2E-105 Geo -> M2E-106 Map
        +-> M2E-103 Watchlist -> M2E-104 Signals UI
        +-> M2E-107 Settings persist
        +-> M2E-108 Thumbnail (optional)
        |
        v
M2X-001 Signal model
  -> M2X-002 Live/Offline detectors
  -> M2X-003 Analyst pivot
  -> M2X-004 CLI/UI contract parity
  -> M2X-005 Diagnostics console
  -> M2X-006 SNI light DPI (optional bridge)
  -> M2X-007 Replay forensics
```

---

## 3. L0 Catch-up Core

## M2E-001 Control plane 真闭环

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L0 |
| 目标 | UI 与 resident core 完成真实 Start/Stop/Pause/Resume |
| 依赖 | phase2 resident core / control skeleton 已存在 |
| 输入 | `control.proto`、capture runtime、Source/Shell UI |
| 输出 | 可工作的 ControlService + UI 控制入口 + session state 联动 |

### 实现要点

1. Rust 侧把 Start/Stop/Pause/Resume/SetSource/ApplyFilter 接到真实 runtime。  
2. 非法状态转移返回稳定 `flowarden-error` 语义。  
3. UI `Start Capture` 成为控制动作，而不仅是导航。  
4. Capture 状态投影到 shell / Source / Overview。  
5. offline 与 live 的 pause 语义在 runbook 中分别验收。  

### 验收条件

1. live：Start 后有 tick；Pause 后聚合冻结；Resume 恢复；Stop 产出 final。  
2. 未选 source 时 Start 失败且 UI 可读。  
3. 非法状态转移有单测；控制按钮 loading/disabled 态与 shell 状态点一致。  
4. **§1.1 质量门禁**通过；控制区视觉与现有 Top Bar / Source 工作台一致，无临时控件堆砌。  

---

## M2E-002 统一 LiveProjection 运行态

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L0 |
| 目标 | Overview / Inspect Flows（及后续 Signals）共享单一 live 源 |
| 依赖 | M2E-001（强依赖控制生命周期）；现有 StreamOverview / LiveProjectionState |
| 输入 | `flowarden_phase2_inspect_live_refresh_proposal.md` |
| 输出 | 冻结的 live 数据策略 + 无分叉实现 |

### 实现要点

1. 文档与代码同时冻结：唯一 `LiveProjectionState`。  
2. 页面切换不重复订阅、不产生状态分叉。  
3. 明确 TCP Connections 模式：纳入统一 live 切片 **或** 文档化独立 query 策略。  
4. Stop 后统一 final snapshot。  

### 验收条件

1. 同一 tick 下 Overview 与 Inspect Flows 关键计数一致。  
2. 快速切换页面无泄漏订阅。  
3. runbook 有“同源性”手测步骤。  
4. live 刷新时榜单/图表无严重布局抖动；**§1.1** 通过。  

---

## M2E-003 Stream 健壮性

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L0 |
| 目标 | stream 中断可检测、可降级、可有限重连 |
| 依赖 | M2E-002 |

### 实现要点

1. 中断检测 + 退避重连。  
2. 失败降级为 latest snapshot 或 stale。  
3. subscriber 与 capture 生命周期绑定。  
4. 防止无界重连。  

### 验收条件

1. 中断后 UI 有明确降级态（统一 stale/offline 视觉语言）。  
2. 恢复后可回到 live。  
3. 重连策略可配置或有硬上限。  
4. **§1.1** 通过；降级文案不暴露 transport 细节。  

---

## M2E-004 Core failure recovery

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L0 |
| 目标 | 运行中 core 失联可感知、可 relaunch |
| 依赖 | M2E-001；现有 launcher/health |

### 实现要点

1. runtime health watcher。  
2. offline 降级 + stale projection。  
3. 手动 reconnect/relaunch。  
4. **默认不自动恢复 capture**。  

### 验收条件

1. kill core 后 shell 进入 offline。  
2. relaunch 成功后可重新 Start。  
3. 旧数据标记 stale。  
4. 恢复入口按钮/横幅样式与现有诊断区一致；**§1.1** 通过。  

---

## M2E-005 运行态 UI 语义收口

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L0 |
| 目标 | 用户态状态机完整，无 transport 细节泄漏 |
| 依赖 | M2E-001～004 |

### 实现要点

1. 统一状态：`loading/ready/running/paused/stopping/failed/offline/stale`。  
2. 错误映射为用户文案 + 可折叠诊断。  
3. Core/Capture 双状态点与后端一致。  

### 验收条件

1. 主路径无原始 `StatusCode=` 文案直出。  
2. Source / Settings / Shell 状态不互相矛盾。  
3. 四态（空/载/错/stale）在 Source/Overview/Inspect/Settings 视觉一致。  
4. **§1.1 代码 + UI 质量门禁**完整通过；L0 不以“能跑”代替“做好”。  

### L0 里程碑出口

M2E-001～005 全部 `completed`（含 §1.1）后，更新计划文档宣布 **Milestone A / L0 Closed Loop**。

---

## 4. L1 Parity

## M2E-101 进程归属

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | 连接行展示 process_name / pid |
| 依赖 | L0；与 phase3 进程方案协调 |
| 平台 | 先 macOS，再 Linux/Windows |

### 实现要点

1. async lookup worker + TTL 缓存。  
2. projection / gRPC 字段扩展。  
3. UI 标注推断属性。  

### 验收条件

1. 本机常见连接可见进程名。  
2. lookup 失败不阻断列表。  
3. 有基线压测记录。  
4. 列展示对齐 Inspect 既有表格排版；推断标记样式统一；**§1.1** 通过。  

---

## M2E-102 rDNS 异步补全

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | 公网 IP **机会性** reverse name（PTR）；非业务域名主解 |
| 依赖 | L0 |
| 定性 | **非最佳主解**。主解为 SNI（M2X-006/phase3）；底为 Country+IP |

### 实现要点

1. 限并发、正/负缓存。  
2. offline 默认策略明确（关或懒加载）。  

### 验收条件

1. 有 PTR 的公网地址可显示 reverse name；**无 PTR 回退 IP（属正常，不判失败）**。  
2. 不影响抓包热路径（超时 + 异步）。  
3. 长域名截断 + tooltip；**§1.1** 通过。  
4. **不**以「Top Hosts 大多有业务主机名」为验收标准（那是 SNI 的目标）。  

---

## M2E-103 观察列表 / 黑名单

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | 用户可标记 watched / known-bad 实体并持久化 |
| 依赖 | L0 |

### 实现要点

1. 实体 store（host/service 最小集）。  
2. control/config API 下发到 core。  
3. 为 signals 提供触发源。  

### 验收条件

1. 标记后重启仍在。  
2. 可在 UI 增删。  
3. 标记控件与列表行样式融入现有 Inspect/Overview 组件语言；**§1.1** 通过。  

---

## M2E-104 通知对等与 Signals 入口

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | 三类 Sniffnet 通知语义可触发可查看 |
| 依赖 | M2E-103；阈值配置 |

### 实现要点

1. `DataThresholdExceeded`  
2. `WatchedEntityTransmitted`  
3. `KnownBadHostTransmitted`  
4. 未读计数 + 列表页/面板  
5. 字段预留 severity/confidence/status（可先填默认）  

### 验收条件

1. 三类均可在受控场景触发。  
2. 列表可清空/标记已读（最小集）。  
3. 点击至少能导航到相关 host 或 Inspect。  
4. Signals 页/面板接入 App Shell；token/组件与四页一致，禁止另起通知皮肤；**§1.1** 通过。  

---

## M2E-105 地理增强进入 projection/UI

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | Country 信息进入 overview/inspect，不仅 CLI |
| 依赖 | L0；现有 GeoLite country 资源 |

### 实现要点

1. core projection 输出 country code/name。  
2. UI 列表展示。  
3. ASN 标为可选子项，不阻塞。  

### 验收条件

1. 公网 IP 连接可见 country。  
2. 内网/未知有稳定占位。  
3. 国家展示与现有列表密度/徽章风格一致；**§1.1** 通过。  

---

## M2E-106 Destination Map 真内容

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | 地图区展示 country 级流量分布 |
| 依赖 | M2E-105；现有 map asset |

### 实现要点

1. country 聚合数据。  
2. equal-earth 路径着色/高亮。  
3. 与 Top Destinations 对齐说明。  

### 验收条件

1. 有跨境流量时地图非空态。  
2. reserved/future 文案退出主路径。  
3. 地图区与 Destination workbench 视觉一体，不出现第三套图表皮肤；**§1.1** 通过。  

---

## M2E-107 配置持久化

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L1 |
| 目标 | 关键偏好与名单可持久化 |
| 依赖 | M2E-001；M2E-103 可并行后半程合并 |

### 最小字段

1. source 偏好  
2. BPF  
3. data threshold  
4. watchlist/blacklist  
5. UI 偏好（密度/window）  

### 验收条件

1. 重启后配置恢复。  
2. 损坏配置可回退默认且不崩溃。  
3. Settings 表单控件沿用现有样式体系；**§1.1** 通过。  

---

## M2E-108 Thumbnail / compact mode（可选）

| 项 | 内容 |
| --- | --- |
| 状态 | `deferred` |
| 层级 | L1 optional |
| 目标 | 迷你实时窗口 |
| 依赖 | L0 + L1 主项稳定 |

### 验收条件

1. 显示关键吞吐。  
2. 可回主窗。  
3. 不新开分析链路。  
4. 迷你窗仍使用 Cosmos token，不得回退系统默认皮；**§1.1** 通过。  

### L1 里程碑出口

必做项 `M2E-101`～`M2E-107` 完成（含 §1.1）后宣布 **Milestone B / L1 Parity**（`M2E-108` 不阻塞）。

---

## 5. L2 Surpass

## M2X-001 BehaviorSignal 统一模型

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L2 |
| 目标 | core projection 落地统一信号模型 |
| 依赖 | L0；建议 M2E-103/104 之后 |
| 输入 | `../flowarden_behavior_signals_implementation.md` |

### 验收条件

1. 信号结构含 kind、window、subject、evidence refs、status。  
2. Sniffnet 三类 kind 可映射。  
3. gRPC/DTO 可传输最小列表。  
4. 模型与 detector 有单测；**§1.1 代码门禁**通过。  

---

## M2X-002 Live / Offline 对称检测

| 项 | 内容 |
| --- | --- |
| 状态 | `not_started` |
| 层级 | L2 |
| 目标 | 同一 detector 输出 ActiveSignal / OfflineFinding |
| 依赖 | M2X-001 |

### 验收条件

1. live 产生可过期 active signal。  
2. offline replay 产生 stable finding。  
3. 去重键与 cooldown 行为可测。  
4. **§1.1** 通过。  

---

## M2X-003 Analyst pivot 工作流

| 项 | 内容 |
| --- | --- |
| 状态 | `not_started` |
| 层级 | L2 |
| 目标 | 从 signal 一键进入 Inspect 并预填上下文 |
| 依赖 | M2X-001；Inspect filter 能力 |

### 验收条件

1. pivot 后 filter/time window 正确。  
2. 相关 host/connection 可定位。  
3. 跳转过渡无整页闪白/布局塌陷；高亮样式统一；**§1.1** 通过。  

---

## M2X-004 CLI / UI 契约同源

| 项 | 内容 |
| --- | --- |
| 状态 | `not_started` |
| 层级 | L2 |
| 目标 | 增强字段在 CLI JSON 与 UI 可对齐验证 |
| 依赖 | L1 enrichment 字段存在 |

### 验收条件

1. 字段对照表文档化。  
2. 至少一组 golden/fixture 覆盖新增字段。  
3. UI-only 字段显式标注。  
4. **§1.1 代码门禁**通过（本项以契约质量为主）。  

---

## M2X-005 Capture / Core 诊断台

| 项 | 内容 |
| --- | --- |
| 状态 | `not_started` |
| 层级 | L2 |
| 目标 | Settings 升级为可运维诊断台 |
| 依赖 | L0 stream/health 指标 |

### 最小指标

1. dropped packets  
2. last packet age  
3. stream state  
4. lookup queue depth（若有）  
5. core uptime / restart count  

### 验收条件

1. live 故障场景下诊断信息足够定位“丢包 / 失联 / 积压”。  
2. 诊断台版式与 Settings 既有 Cosmos 层次一致，指标卡可复用 status card 语言；**§1.1** 通过。  

---

## M2X-006 TLS SNI Light DPI 切口（可选衔接）

| 项 | 内容 |
| --- | --- |
| 状态 | `completed` |
| 层级 | L2 / phase3 bridge |
| 目标 | 有限 payload 提取 SNI 并展示 |
| 依赖 | enrichment 框架；对齐 phase3 |

### 验收条件

1. 典型 TLS 握手样本可见 SNI。  
2. 热路径有采样/长度上限。  
3. 可关闭。  
4. 有 fixture/golden；Inspect 展示样式统一；**§1.1** 通过。  

---

## M2X-007 Replay forensics 时间轴

| 项 | 内容 |
| --- | --- |
| 状态 | `not_started` |
| 层级 | L2 |
| 目标 | offline 提供 finding 列表 + 时间轴跳转 |
| 依赖 | M2X-002 |

### 验收条件

1. offline 跑完可浏览 finding。  
2. 跳转 tick/host 可用。  
3. 与 live 有界窗口策略区分清楚。  
4. forensics 时间轴与 Overview timeline 视觉家族一致；**§1.1** 通过。  

### L2 里程碑出口

`M2X-001`～`M2X-004` 完成（含 §1.1），且 `M2X-005` / `M2X-006` / `M2X-007` 至少完成其一，宣布 **Milestone C / L2 Surpass Slice**。

---

## 6. 与 follow-up 清单的映射

| `flowarden_phase2_followup_enhancements.md` | 本 backlog |
| --- | --- |
| Control plane | `M2E-001` |
| Real-time projection stream 后续 | `M2E-002` + `M2E-003` |
| Core failure recovery | `M2E-004` |
| （UI 运行态） | `M2E-005` |

后续 follow-up 文档只保留摘要，细节与验收以本 backlog 为准。

---

## 7. 建议迭代切片

### Iteration 1 — 能真的抓

- M2E-001  
- M2E-002  
- M2E-005（可先做最小状态集，随 001/002 完善）  

### Iteration 2 — 能扛得住

- M2E-003  
- M2E-004  
- M2E-005 收口  

### Iteration 3 — 能对等比较

- M2E-101  
- M2E-105  
- M2E-106  
- M2E-103  
- M2E-104  

### Iteration 4 — 能讲清反超

- M2E-102  
- M2E-107  
- M2X-001  
- M2X-002  
- M2X-003  
- M2X-004  
- 任选 M2X-005/006/007  

---

## 8. 进度记录模板

每完成一项，在本文件对应任务下追加：

```markdown
### 完成记录
- 日期：
- 证据：PR/commit/路径
- 测试：命令与结果
- 未纳入：
- 下一步：

### 质量验收
- 代码门禁：fmt/clippy/test/build 结果
- UI 触及：是/否
- 若是：风格一致性说明 + 四态/主路径打磨说明
- 对照基线：tfc_runtime_screenshots / 设计文档条款
- 已知债务：无 / 已单列 follow-up（链接）
```

并同步：

1. `flowarden_phase2_parity_and_surpass_plan.md` 里程碑勾选（若需要可另建 progress 文件）  
2. runbook 场景  
3. 若影响 phase3 边界，回链 phase3 计划  
4. 有 UI 变化时更新对照截图说明  

---

## 9. 文档状态

| 项 | 值 |
| --- | --- |
| 状态 | Draft for review |
| 对应总纲 | `flowarden_phase2_parity_and_surpass_plan.md` |
| 起始推荐任务 | `M2E-001` |
| 跨任务硬约束 | 代码高质量；UI 风格一致且高质量（§1.1） |
