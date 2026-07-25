# Flowarden 三项体验增强开发方案

**程序图标 / ASN 地理运营商 / 缩略小窗（Thumbnail）**

| 字段 | 内容 |
| --- | --- |
| 状态 | **Accepted**（评审冻结 2026-07-25） |
| 日期 | 2026-07-25 |
| 质量基线 | 与 `flowarden_phase2_parity_and_surpass_plan.md` §4.3 同等硬门禁 |
| 参考 | Sniffnet `picon` / `mmdb/asn` / thumbnail chart & window config |

---

## 1. 目标与非目标

### 1.1 目标

在不破坏现有 **projection 契约优先、enrichment 异步、Resident 有界内存** 的前提下，交付三项可验收的产品能力：

| ID | 能力 | 用户可感知结果 |
| --- | --- | --- |
| **F-ICON** | 程序 / 进程图标 | Overview Top Connections、Inspect Flows 行上显示进程图标（有进程归属时） |
| **F-ASN** | ASN 地理 / 运营商 | Host / Destination /（可选）Connection 显示 `ASxxxx · Org`，CLI JSON 同源字段 |
| **F-THUMB** | 缩略小窗 | 一键进入置顶紧凑窗：吞吐 sparkline + KPI + 捕获状态 + 信号角标；位置可持久化 |

### 1.2 非目标（本方案明确不做）

1. 不在 `StreamOverview` 每帧嵌入图标位图或 base64（避免 stream 膨胀）。
2. 不做完整「应用商店级」图标主题系统 / 用户自定义图标包。
3. 不引入完整 GIS / 运营商画像 / ASN 历史轨迹。
4. 不做 Sniffnet iced 皮肤复刻；缩略窗是 Flowarden TFC 语言的紧凑壳。
5. 不把缩略窗做成第二个完整应用（无 Source/Settings 全功能）。

### 1.3 成功标准（总）

1. **功能**：三项均可在 macOS 主开发环境演示；Windows/Linux 有明确降级策略且不崩溃。  
2. **契约**：CLI JSON 与 UI DTO 字段同源；缺省字段向后兼容。  
3. **性能**：live stream 帧体不因图标增大；ASN 解析不进抓包热路径。  
4. **质量**：`cargo fmt/clippy/test` + `dotnet build`；新增单测覆盖解析/缓存/窗口状态机。  
5. **UI**：图标与 ASN 标签对齐现有 panel 密度（32px 行高、4–6px radius、等宽数据区）。

---

## 2. 设计原则（高质量硬约束）

### 2.1 分层

```text
Capture hot path     → 禁止 OS icon / MMDB ASN 同步阻塞
Enrichment workers   → process path、ASN resolve（可缓存）
Projection plane     → 只放文本元数据（name/pid/path/asn）
UI presentation      → 图标解码、缩略窗布局、偏好持久化
```

### 2.2 契约优先

- 新增字段一律：`proto` → core convert → DTO → ViewModel。  
- UI **不得**直接调 `listeners` / 读 MMDB 旁路 core（图标提取是展示层例外，见 §3）。  
- CLI `capture --format json` 与 Overview/Inspect 使用同一语义字段名。

### 2.3 有界与可失败

- 一切缓存有上限与淘汰。  
- 图标/ASN 失败时 **静默降级为文字**，不阻断 capture、不刷红错误风暴。  
- Offline 模式：进程图标多数不可得 → 明确 empty；ASN 仍可对远程 IP 解析。

### 2.4 平台现实

| 平台 | 图标 | ASN | 缩略窗 |
| --- | --- | --- | --- |
| macOS | 一等实现（开发主路径） | 一等 | 一等 |
| Windows | 一等或同期交付 | 一等 | 一等 |
| Linux | 尽力 + 占位 glyph | 一等 | 一等（无特殊 OS 依赖） |

---

## 3. F-ICON：程序 / 进程图标

### 3.1 问题拆解

已有能力：

- Core `ProcessLookup`：async 得到 `process_name` / `process_pid`。  
- UI `ConnectionRowDto.ProcessDisplayLabel`。

缺口：

- 无稳定 **进程可执行路径 / bundle id**，UI 难以可靠取图标。  
- 无图标缓存与行级展示组件。

### 3.2 架构决策（推荐）

| 决策 | 选择 | 理由 |
| --- | --- | --- |
| 图标字节是否进 gRPC stream | **否** | 每秒推送 top-N 连接时复制图像会炸内存与带宽 |
| 谁负责取图标 | **UI 展示层**（`IProcessIconService`） | 图标是 OS 资源与像素，属 presentation |
| Core 额外提供什么 | `process_path`（及可选 `process_bundle_id`） | 提高命中率；仍可无 path 时按 name 降级 |
| 取图时机 | 异步、按需、防抖 | 不阻塞 UI 线程；与 live 刷新解耦 |
| 失败表现 | 单色首字母 / 通用 process glyph | 与 TFC 视觉一致 |

**可选增强（二期，不阻塞一版）：**  
`GetProcessIcon(path|name|pid)` 按需 RPC —— 仅当未来出现「无 UI 的远程 headless 客户端」再考虑。

### 3.3 Core 变更

#### 3.3.1 ProcessInfo 扩展

```text
ProcessInfo {
  name: String,
  pid: u32,
  path: Option<String>,      // 绝对路径 / macOS .app 路径
  bundle_id: Option<String>, // macOS only, optional
}
```

#### 3.3.2 查找实现

- 在现有 `listeners` 结果上扩展：若 API 提供 path 则填充。  
- 不足时平台补充（**仅在 lookup worker 内**，禁止 capture 线程）：  
  - macOS：`libproc` / `proc_pidpath` 或等价安全封装  
  - Windows：`QueryFullProcessImageName`  
  - Linux：`/proc/<pid>/exe` readlink  
- 权限失败 → `path=None`，保留 name/pid。

#### 3.3.3 Proto（向后兼容）

`ConnectionRow` 新增：

```protobuf
string process_path = 14;       // empty if unknown
string process_bundle_id = 15;  // empty if unknown
```

Inspect 行与 Overview top_connections 共用。

#### 3.3.4 缓存

沿用 process lookup TTL：

- 命中：name/pid/path 一并缓存  
- path 解析失败：negative cache 短 TTL（与现有 3s 一致）  
- 队列仍 `REQUEST_QUEUE_CAP`，满则丢弃新 key（不阻塞）

### 3.4 UI 变更

#### 3.4.1 服务

```text
IProcessIconService
  Task<IImage?> GetIconAsync(ProcessIconKey key, CancellationToken ct)
  void Invalidate(ProcessIconKey key) // 可选
```

`ProcessIconKey`：优先 `path` → `bundle_id` → `(name, pid)`。

实现：

| 平台 | 策略 |
| --- | --- |
| macOS | `NSWorkspace` / AppKit 图标（经 Avalonia 平台互操作或小 P/Invoke 助手） |
| Windows | 从 exe `ExtractAssociatedIcon` / Shell |
| Linux | FreeDesktop 图标主题按 `StartupWMClass`/name 猜测；失败则 glyph |

#### 3.4.2 缓存策略（UI）

| 参数 | 建议 |
| --- | --- |
| 最大条目 | 256 |
| 淘汰 | LRU |
| 解码尺寸 | 16×16 与 32×32 各一档（或单档 32 缩放） |
| 并发 | 最大 2 路解码，避免启动风暴 |
| 线程 | 后台线程池 → UI 线程赋值 |

#### 3.4.3 展示位置（一版）

1. **Inspect Flows** 表：进程列左侧 16px 图标。  
2. **Overview Top Connections**：进程/远程摘要行可选 16px（空间紧时仅 Inspect）。  

**一版必做 Inspect；Overview 为同 PR 或紧随 PR。**

#### 3.4.4 组件

- `ProcessIconView`（UserControl）：`Icon` + 占位 glyph。  
- ViewModel 只持有 `ProcessIconKey` 与 `IImage?`，不直接调 OS API。

### 3.5 质量门禁 F-ICON

| 项 | 要求 |
| --- | --- |
| 单测 | process path 解析单元测（mock）；key 规范化测 |
| 集成 | 有 path / 无 path / 权限拒绝 三种 UI 状态截图或自动化 |
| 性能 | 100 行 Inspect 刷新：图标异步补齐，滚动不卡死主线程 |
| Clippy / build | 全绿；禁止热路径 unwrap |
| 隐私 | 不把 path 写进日志默认级别；诊断页可折叠展示 |

### 3.6 实施波次

| 波次 | 内容 | 预估 |
| --- | --- | --- |
| I1 | Proto + ProcessInfo path/bundle + worker 填充 | 1–2d |
| I2 | UI `IProcessIconService` + 缓存 + Inspect 列 | 2–3d |
| I3 | macOS 达标；Windows 达标；Linux 降级 | 2–3d |
| I4 | Overview 接入 + 文档/截图验收 | 1d |

---

## 4. F-ASN：ASN 地理 / 运营商

### 4.1 问题拆解

已有：

- 嵌入 `GeoLite2-Country.mmdb`  
- `HostRow.country_label` / Destination `country_code`  
- UI 地图 markers 用 country code  

缺口：

- 无 ASN 号与组织名（运营商/云厂商）  
- CLI 与 UI 无法按 ASN 归类流量  

### 4.2 架构决策

| 决策 | 选择 | 理由 |
| --- | --- | --- |
| 数据源 | **GeoLite2-ASN.mmdb**（与 Sniffnet 同思路） | 成熟、离线、与现有 maxminddb 栈一致 |
| 加载方式 | `include_bytes!` 默认库 + 可选外部路径覆盖 | 开箱可用；企业可换自有库 |
| 解析位置 | Core geo 模块（与 country 并列） | UI 不读 MMDB |
| 热路径 | 否；仅在 projection convert / CLI enrich 时查缓存 | 与 country 相同模型 |
| 缓存 | 与 country 共用 soft-cap 或独立 20k cap | Resident 有界 |

### 4.3 Core 变更

#### 4.3.1 模型

```rust
pub struct AsnInfo {
    pub number: u32,          // 0 = unknown
    pub organization: String, // empty if unknown
}

impl AsnInfo {
    pub fn display_label(&self) -> String {
        // "AS15169 · Google LLC" or "Unknown"
    }
}
```

`GeoCountryResolver` 升级为 **`GeoIpResolver`**（或并列 `GeoAsnResolver`，再由 facade 组合）：

- `resolve_country(ip) -> CountryInfo`  
- `resolve_asn(ip) -> AsnInfo`  
- 本地/loopback/private → ASN empty（与 country Local 规则一致）

#### 4.3.2 资源与许可

1. 仓库增加 `resources/DB/GeoLite2-ASN.mmdb`（或构建脚本下载）。  
2. 文档声明 MaxMind GeoLite2 许可与署名要求（Settings → About / docs）。  
3. CI：库文件存在性检查；单测用 8.8.8.8 → AS15169 等稳定样例（与 Sniffnet 类似）。

#### 4.3.3 Proto

`HostRow`：

```protobuf
uint32 asn_number = 7;
string asn_organization = 8;
string asn_label = 9; // 预格式化 "AS15169 · Google LLC"，减少 UI 分叉
```

`DestinationSummary`：

```protobuf
uint32 asn_number = 6;
string asn_organization = 7;
string asn_label = 8;
```

`ConnectionRow`（可选但推荐，便于 Inspect 远程端）：

```protobuf
string remote_asn_label = 16;
```

规则：**远程端** ASN；本机地址不解析。

#### 4.3.4 CLI

`EnrichedHostSummary` / connection enrich 增加 `asn_number` / `asn_organization` / `asn_label`。  
Golden：若依赖 ASN 库，更新 golden 或对 ASN 字段做稳定样例夹具。

#### 4.3.5 Destination 聚合增强

当前 top destinations 按 **country** 聚合。本方案：

- **保持** country 聚合为默认（地图仍按国家）。  
- 新增可选 `metric_mode` / Settings：`group_destinations_by = country | asn`（二期）。  
- **一版**：Host 行与 Destination 副标签展示 ASN，不改地图投影主键。

### 4.4 UI 变更

1. **Top Hosts**：`IP(国家)` 旁或第二行 `AS… · Org`（muted 12px）。  
2. **Top Regions / Destinations**：主标签仍国家/区域；副标签 ASN 占比最大 org（若该国多 ASN，显示 top ASN）。  
3. **Inspect**：远程主机列 tooltip 含 country + ASN。  
4. **Settings → Diagnostics**：显示 ASN DB 版本/加载状态（loaded / missing / error）。

### 4.5 质量门禁 F-ASN

| 项 | 要求 |
| --- | --- |
| 单测 | known public IP → 稳定 ASN；private/loopback → empty |
| 兼容 | 旧 UI 忽略新字段仍可运行 |
| 性能 | 连续 resolve 1e5 次有缓存命中；convert 路径无文件 IO |
| Clippy | 全绿 |
| 法务 | README/Settings 署名 MaxMind |

### 4.6 实施波次

| 波次 | 内容 | 预估 |
| --- | --- | --- |
| A1 | ASN mmdb + `GeoAsnResolver` + 单测 | 1–2d |
| A2 | Proto/DTO/CLI enrich | 1d |
| A3 | Overview/Inspect 展示 | 1–2d |
| A4 | 诊断与文档署名 | 0.5d |

---

## 5. F-THUMB：缩略模式 / 小窗

### 5.1 问题拆解

Sniffnet：独立 thumbnail 页面 + 简化 chart + 窗口位置 confy 持久化。  

Flowarden：

- 已有 `MainWindow` 全壳 + live projection。  
- `UserPreferences` 已可扩展。  
- 无 always-on-top 紧凑窗状态机。

### 5.2 产品定义（一版）

**Thumbnail 模式** = 同一 `MainWindow` 的 **Chrome 模式切换**（推荐），不是第二进程。

| 元素 | 全屏工作台 | 缩略窗 |
| --- | --- | --- |
| 目标尺寸 | 1440×900 等 | 默认 **360×220**（可配置 320–480 宽） |
| 置顶 | 否 | **是**（`Topmost=true`） |
| 导航 rail / 多页 | 有 | 无 |
| 内容 | 全功能 | KPI（packets/bytes/in/out）+ 迷你吞吐 sparkline + capture 状态 + signals 未读角标 |
| 操作 | 全套 | **Pause/Resume/Stop**（可选）+ **Expand** 回全屏 |
| 数据源 | `LiveProjectionState` | **同一** `LiveProjectionState`（零第二套数据） |

### 5.3 状态机

```text
Normal ──(EnterThumbnail)──► Thumbnail
   ▲                              │
   └────────(ExitThumbnail)───────┘

规则：
- Enter：保存 Normal 的 Bounds → 应用 Thumbnail Bounds/Topmost → 切换 Content
- Exit：恢复 Normal Bounds → Topmost=false → 恢复 AppShellView
- 关闭窗口：若 Thumbnail，先按用户偏好决定是否停 core（沿用现逻辑）
- Capture 生命周期：模式切换不得 Stop capture
```

### 5.4 UI 结构

```text
Views/
  Thumbnail/
    ThumbnailView.axaml          # 紧凑布局
    ThumbnailView.axaml.cs
ViewModels/
  ThumbnailViewModel.cs          # 从 LiveProjectionState 投影只读属性
```

`AppShellViewModel`：

- `bool IsThumbnailMode`  
- `IRelayCommand EnterThumbnailCommand / ExitThumbnailCommand`  
- Header 增加 **Thumbnail** 图标按钮（全模式可见，Source 忙碌时仍可用）

`MainWindow`：

- 订阅 shell 模式变化，改 `Width/Height/Topmost/CanResize`  
- `Position` 变化时 debounce 写入 preferences  

### 5.5 迷你图表

复用 Overview 的 path 构建思想，**独立简化实现** `ThumbnailSparkline`：

- 仅 outbound 或 in+out 双线（线宽 1px）  
- 无 hover tooltip（避免小窗抖动）  
- 无数据时 muted 空态「waiting」  
- 数据来自同一 `TimelinePoints`（已 30 点有界）

### 5.6 偏好持久化

扩展 `UserPreferences`：

```csharp
public bool StartInThumbnail { get; set; } = false;
public double ThumbnailX { get; set; } = double.NaN; // NaN = 默认右下/右上
public double ThumbnailY { get; set; } = double.NaN;
public double ThumbnailWidth { get; set; } = 360;
public double ThumbnailHeight { get; set; } = 220;
public double NormalWidth { get; set; } = 1440;
public double NormalHeight { get; set; } = 900;
// 可选：NormalX/Y
```

校验：sanitize 到虚拟屏幕工作区内（防显示器拔掉后坐标飞走）—— 对标 Sniffnet `thumbnail_position.sanitize()`。

### 5.7 交互与无障碍

1. **快捷键**：`Cmd/Ctrl+Shift+T` 切换（可配置二期）。  
2. **双击缩略窗空白** → Expand。  
3. **Signals 角标** 点击 → Expand 并导航到 Signals。  
4. 缩略窗必须键盘可 Expand（按钮 focus）。

### 5.8 质量门禁 F-THUMB

| 项 | 要求 |
| --- | --- |
| 状态机单测 | Enter/Exit 边界、重复 Enter、捕获中切换 |
| 手动验收 | 捕获运行中切换 20 次无泄漏、stream 不断 |
| 布局 | 360×220 / 320×200 / 480×260 三档无裁切溢出 |
| 视觉 | 与 TFC token 一致（背景/边框/等宽数字） |
| 性能 | 缩略模式 CPU ≤ 正常模式（更少控件） |

### 5.9 实施波次

| 波次 | 内容 | 预估 |
| --- | --- | --- |
| T1 | 窗口模式状态机 + preferences 几何 | 1–2d |
| T2 | ThumbnailView + sparkline + KPI | 2d |
| T3 | 控制按钮 Pause/Expand + 快捷键 | 1d |
| T4 | 三档尺寸截图验收 + 文档 | 0.5–1d |

---

## 6. 跨特性依赖与推荐总顺序

```text
并行轨 A（数据面）          并行轨 B（窗口面）
─────────────────          ─────────────────
A1 ASN resolver            T1 窗口状态机
A2 Proto/DTO/CLI           T2 ThumbnailView
I1 Process path            T3 控制与快捷键
I2 Icon service + Inspect  T4 验收
A3 ASN UI
I3 平台图标
I4 Overview 图标
A4 署名/诊断
```

**推荐合并迭代（约 2 周高强度）：**

| Sprint | 交付 |
| --- | --- |
| S1 | ASN 数据面 + Thumbnail 状态机/壳 |
| S2 | ASN UI + Thumbnail 内容与控制 |
| S3 | Process path + Icon Inspect + 平台 |
| S4 | Overview 图标、联调、文档、质量门禁收口 |

依赖关系：

- 图标 **依赖** process path 增强，不依赖 ASN。  
- 缩略窗 **不依赖** 图标/ASN，但应预留 KPI 副行显示 ASN top（可选）。  
- 三者共享：`UserPreferences`、TFC 视觉 token、LiveProjectionState。

---

## 7. 质量门禁总表（不可降级）

### 7.1 Rust

1. `cargo fmt --all -- --check`  
2. `cargo clippy --all-targets --all-features -- -D warnings`  
3. `cargo test`（含 geo ASN 样例、process path 规范化）  
4. 禁止 capture 热路径同步 icon/ASN 文件 IO  
5. Resident 缓存有 cap；stream 不传图标字节  

### 7.2 UI

1. `dotnet build`  
2. View 无业务 OS 调用（经 Service）  
3. 异步图标不引发 `ObservableCollection` 跨线程异常  
4. Thumbnail 切换不 Dispose 全局 gRPC channel / 不 Stop capture  
5. 三档缩略尺寸 + Inspect 有/无图标截图入 `docs/phase2/tfc_runtime_screenshots/`  

### 7.3 契约与文档

1. proto 字段编号不复用、可缺省  
2. CLI/UI 字段对照表更新（`parity` 或本文件附录）  
3. MaxMind 署名  
4. runbook：如何更新 mmdb、如何在无图标平台验证降级  

---

## 8. 风险与缓解

| 风险 | 影响 | 缓解 |
| --- | --- | --- |
| 图标 API 平台差异大 | 工期 | 一版 macOS+Windows 硬达标；Linux glyph 降级写进验收 |
| GeoLite2 许可/分发 | 合规 | 署名 + 可选外部 mmdb 路径；CI 不依赖网络下载 |
| mmdb 体积增大 | 包体 | ASN 库单独文件；评估可选 feature `geo-asn`（默认 on） |
| 缩略窗多显示器坐标 | 可用性 | sanitize + 默认右下锚点 |
| process path 需更高权限 | 命中率 | 无 path 时 name 降级图标；不报错打断 |
| 图标缓存泄漏 | 长跑 RSS | LRU 256 + 弱引用可选 |

---

## 9. 验收清单（评审用）

### F-ICON

- [ ] Inspect 有进程时显示图标；无进程时 glyph  
- [ ] path 权限失败不崩溃  
- [ ] Stream 帧大小与改前同量级  
- [ ] macOS + Windows 演示通过  

### F-ASN

- [ ] 8.8.8.8 类公共 IP 显示合理 AS 与 org  
- [ ] 私网/loopback 无 ASN 噪音  
- [ ] CLI JSON 含 asn 字段  
- [ ] Settings/文档含 MaxMind 署名  

### F-THUMB

- [ ] 捕获中 Enter/Exit ≥20 次 stream 不断、状态正确  
- [ ] 置顶小窗 KPI/sparkline/信号角标可用  
- [ ] 位置重启后恢复（单显示器）  
- [ ] Expand 回到完整 shell 且页面状态合理  

---

## 10. 建议冻结的接口草案（摘要）

### Proto 增量

```protobuf
// ConnectionRow
string process_path = 14;
string process_bundle_id = 15;
string remote_asn_label = 16;

// HostRow
uint32 asn_number = 7;
string asn_organization = 8;
string asn_label = 9;

// DestinationSummary
uint32 asn_number = 6;
string asn_organization = 7;
string asn_label = 8;
```

### Preferences 增量

见 §5.6。

### 新 UI 类型

- `IProcessIconService` / `ProcessIconService`  
- `ThumbnailViewModel` / `ThumbnailView`  
- `AppShellViewModel.IsThumbnailMode`  

---

## 11. 结论

三项均应做成 **一等产品能力**，但必须遵守 Flowarden 已验证的边界：

1. **图标**：core 给身份元数据，UI 做像素与缓存。  
2. **ASN**：core geo 扩展 + 投影字段，与 country 同级工程标准。  
3. **缩略窗**：纯 UI 窗口状态机 + 共享 live projection，零第二套分析。  

按本方案实施，可在约 **2 个高强度工程周** 内达到可验收质量；若并行人力充足，S1–S2 可 ASN∥Thumbnail，S3–S4 收图标与联调。

---

## 12. 评审结论（已冻结）

| # | 议题 | 结论 |
| --- | --- | --- |
| 1 | 缩略窗 Pause/Resume | **必须包含** |
| 2 | 图标平台 | **macOS + Windows 一版硬达标**；Linux glyph 降级 |
| 3 | ASN mmdb | **默认嵌入**（与 Country 一致） |
| 4 | Destination 按 ASN 聚合 | **二期**；一版仅展示 ASN 标签 |

按 §6 Sprint 顺序实施。
