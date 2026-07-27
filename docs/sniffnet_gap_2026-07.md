# Flowarden vs Sniffnet 差距对照（2026-07）

| 字段 | 内容 |
| --- | --- |
| 日期 | 2026-07-25 |
| 范围 | 产品能力与架构路径；非逐行代码 diff |
| 参考 | Sniffnet 公开特性 / 本仓 `docs/sniffnet_reverse_analysis.md` / Flowarden 当前实现 |
| 产品决策 | **UI 不展示 ASN**（Core/CLI 仍可保留解析能力） |

---

## 1. 一句话结论

- **桌面监控体验完成度：Sniffnet 仍领先**（打磨、地图叙事、过滤手感、ASN 展示）。
- **架构与工程演进：Flowarden 分叉并在多条线上领先**（双进程 projection、Resident 有界内存、CLI/UI 契约、Signals、本机视角会话键、Light DPI SNI）。
- **本轮已补齐：** 连接 Src/Dst 本机取向；Overview Top Connections **进程图标**（与 Inspect 对齐）。

---

## 2. 能力矩阵

图例：`✅` 可用 · `◐` 部分 · `❌` 无/关闭 · `→` 规划中

| 能力 | Sniffnet | Flowarden | 备注 |
| --- | --- | --- | --- |
| 选网卡 live 抓包 | ✅ | ✅ | |
| PCAP 导入 / 导出 | ✅ | ✅ | 导出体验 Sniffnet 更熟 |
| 实时吞吐图 | ✅ | ✅ | |
| Top hosts / services / connections | ✅ | ✅ | |
| 地理 Country | ✅ | ✅ | |
| 地理地图叙事 | ✅ 强 | ◐ markers | Sniffnet 更完整 |
| **ASN 展示** | ✅ | ❌ UI 关闭 | 产品决策；core 仍可有字段 |
| 缩略窗 | ✅ | ✅ | |
| 进程归属 | ✅ | ✅ | 双方均为启发式端口映射 |
| **进程图标** | ✅ | ✅ | Overview + Inspect |
| rDNS / 域名感 | ✅ | ◐ PTR + SNI 优先名 | |
| TLS SNI | ◐ 历史偏弱 | ✅ Light DPI | Flowarden 略强 |
| 过滤 UX | ✅ 成熟 | ◐ Inspect + BPF | Sniffnet 手感更好 |
| 行为信号 / 通知 | ◐ / webhook | ✅ Signals + policy | 路径不同 |
| CLI 同源输出 | ❌ 弱 | ✅ JSON + 契约 | Flowarden |
| 双进程 gRPC | ❌ 单体 | ✅ | Flowarden 架构优势 |
| 长跑有界内存 | ◐ | ✅ Resident B2 | Flowarden 更明确 |
| TCP 状态表 | ◐ | ✅ | |
| 本机会话 Src/Dst | ✅ 自然 | ✅ local-oriented FlowKey | 2026-07 对齐 |
| 深度 DPI / IDS | ❌ 非目标 | → Phase3/4 | 双方都未到 Suricata 级 |
| 主题 / i18n / polish | ✅ | ◐ | Sniffnet 领先 |

---

## 3. 架构对比

```text
Sniffnet                         Flowarden
────────                         ─────────
iced GUI 主线程                   Avalonia UI
  + 后台抓包/聚合线程               ↕ gRPC projection/control
单进程状态树                       Resident core (Rust)
连接键 → GUI 消息                  Capture → Aggregate → Projection DTO
```

| 维度 | Sniffnet | Flowarden | 谁更利于演进 |
| --- | --- | --- | --- |
| 职责边界 | GUI 与分析耦合 | Core / UI / CLI 分离 | **Flowarden** |
| 契约 | 内部结构 | proto + DTO | **Flowarden** |
| 长跑 | 实现内隐式 | Resident soft-cap + tick 窗 | **Flowarden** |
| 取证 CLI | 弱 | 一等 `capture` JSON | **Flowarden** |
| 安装与上手 | 更简单 | 双进程需 launcher | **Sniffnet** |

---

## 4. 差距分层（行动导向）

### 4.1 Sniffnet 仍明显领先（体验）

1. **视觉与交互 polish**（图表密度、空态、动画、默认好看）。  
2. **地理叙事 / 地图**（不只是 country code）。  
3. **过滤与搜索组合 UX**。  
4. **ASN 产品展示**（Flowarden 主动关闭 UI）。  
5. **跨平台开箱**（单包分发心智更简单）。

### 4.2 基本对齐（监控主路径）

1. Live 抓包、排行、吞吐图。  
2. 进程归属 + **进程图标**（Overview + Inspect）。  
3. 缩略窗。  
4. Country 级地理。  
5. 本机视角连接端点（local-oriented session key）。

### 4.3 Flowarden 领先或 intentional 分叉

1. **Resident 有界聚合**（长跑 UI core）。  
2. **Projection 契约 + CLI 同源**。  
3. **BehaviorSignal / Signals 页 / policy**。  
4. **Light DPI SNI** 进 host/connection。  
5. **TCP 连接状态投影**。  
6. 诊断导出、控制面 Pause/Resume 与 stream 恢复路径。

### 4.4 双方都弱（非当前监控竞品主战场）

1. 完整应用层 DPI（HTTP/DNS 详情面板）。  
2. 重传 / RTT / 会话取证级状态机。  
3. IDS / 规则引擎（Suricata 级）。

---

## 5. 建议优先级（相对 Sniffnet）

| 优先级 | 项 | 目的 |
| --- | --- | --- |
| P0 | Live 手测：新 core + 本机 Src + Overview 图标 | 验证本轮交付 |
| P1 | 过滤 / 搜索 UX 收口 | 日常对标 Sniffnet 手感 |
| P2 | 地理叙事增强（地图/区域讲故事，可不含 ASN UI） | 缩小“看起来弱” |
| P3 | Overview / Inspect 行密度与空态 polish | 体验 |
| 可选 | 恢复 ASN UI（组织名优先） | 仅当产品重新要求对齐 ASN |

**明确不做（除非改决策）：** 把 ASN 再作为 Overview 主展示字段。

---

## 6. 验收口径（本轮相关）

1. Overview **Top Connections** 在有 `process_path/name` 时显示 OS 图标，否则 monogram。  
2. Inspect 进程列保持图标行为不变。  
3. 本机 IP 参与的会话：Src 为本机侧（local_ips 已知时），回程不拆成“对端→本机”单独一行。  
4. UI **无** ASN chip/列。  
5. `cargo test`（core）+ `dotnet build`（UI）通过。

---

## 7. 修订记录

| 日期 | 变更 |
| --- | --- |
| 2026-07-25 | 初版：架构/体验/信号/连接模型；记录 ASN UI 关闭与 local-oriented FlowKey |
| 2026-07-25 | Overview Top Connections 图标落地后更新矩阵 |
