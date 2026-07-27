# Flowarden 行为信号监测实现方案

## 目标

Flowarden 的行为信号监测用于从网络流量中提炼安全分析线索。它需要同时兼顾两种场景：

- 离线复盘：对已经解析完成的数据集进行完整回看，输出稳定的 `Offline Finding`。
- 实时监测：对持续到来的流量进行滑动窗口检测，输出可更新、可过期的 `Active Signal`。

行为信号不是最终攻击定性，而是可解释、可追溯、可筛选、可跳转的研判线索。

核心问题：

- 谁在什么时候参与了操作
- 对谁进行了连接或数据传输
- 使用了什么协议和服务
- 造成了多大流量、会话或地域影响
- 支撑判断的证据是什么
- 当前信号是离线 finding，还是实时 active signal
- 分析人员下一步应该 pivot 到哪里

## 非目标

第一阶段不做：

- 自动攻击链推理
- AI 判定攻击结论
- 黑盒风险评分
- 强依赖历史基线的长期画像
- 深度 DNS / HTTP / TLS payload 解析规则
- 默认远程 webhook 投递

实时模式可以保留 alert 能力，但 UI 文案仍应克制，使用 `candidate`、`signal`、`finding`，不要表达为 confirmed attack。

## 场景划分

| 维度 | 离线复盘 | 实时监测 |
| --- | --- | --- |
| 输入 | 完整离线数据集 | packet / flow / session stream |
| 时间视角 | 完整时间范围 | rolling window / sliding window |
| 输出 | stable finding | active signal |
| 状态 | 一次性生成后稳定 | New / Active / Updated / Resolved / Expired |
| 去重 | 批处理归并 | 持续归并 + cooldown |
| 证据 | 可引用完整 flow/session | 增量积累 current / peak evidence |
| UI | Replay Timeline / Evidence | Live Signal Feed / Active Signals |
| 误报处理 | 用户复盘确认 | 抑制、冷却、升级、过期 |

统一原则：

- detector 规则尽量复用。
- 输出统一 `BehaviorSignal`。
- 离线模式生成 `OfflineFinding` 语义。
- 实时模式生成 `ActiveSignal` 语义。
- Sniffnet 的三类 notification 作为 Flowarden 的 signal kind 纳入统一模型。

## Sniffnet Notification 纳入方式

参考代码：

- `/Users/wangli/workspace/practise/rs-workship/sniffnet/src/notifications/types/logged_notification.rs`
- `/Users/wangli/workspace/practise/rs-workship/sniffnet/src/notifications/types/notifications.rs`
- `/Users/wangli/workspace/practise/rs-workship/sniffnet/src/notifications/notify_and_log.rs`

Sniffnet 包含三类 `LoggedNotification`：

| Sniffnet 类型 | 含义 | 记录内容 | Flowarden 统一 signal 类型 |
| --- | --- | --- | --- |
| `DataThresholdExceeded` | 数据阈值超过 | timestamp、threshold、actual data、top hosts、top services | `DataThresholdExceeded` |
| `FavoriteTransmitted` | 收藏对象出现新流量 | timestamp、favorite metadata、data amount | `WatchedEntityTransmitted` |
| `BlacklistedTransmitted` | 黑名单 IP 出现流量 | timestamp、ip、country、domain、ASN、data amount | `KnownBadHostTransmitted` |

Sniffnet 还包含：

- volume
- sound
- unread notification count
- notification log，最多保留最近 30 条
- clear all notifications
- expandable data threshold notification
- remote notifications webhook
- webhook JSON payload

Flowarden 不直接复制 Sniffnet 的通知页面，但吸收三类通知语义：

- `DataThresholdExceeded`：作为阈值类行为信号，适用于离线和实时。
- `WatchedEntityTransmitted`：由用户标记的 host / service / role / watchlist 触发。
- `KnownBadHostTransmitted`：由黑名单、威胁情报或用户标记风险对象触发。

对照结论：

- Sniffnet notification 是 `event alert`。
- Flowarden signal 是 `offline finding` 或 `active signal`。
- Sniffnet 强调提醒。
- Flowarden 强调证据链、pivot、状态和复盘。
- Flowarden 需要比 Sniffnet 多出时间窗口、主体对象、关联 flows、关联 sessions、严重度、置信度、状态、去重键和 analyst note。

## 统一数据模型

建议在 core projection 层增加统一模型。

```rust
pub struct BehaviorSignal {
    pub id: String,
    pub signal_type: BehaviorSignalType,
    pub mode: SignalMode,
    pub status: SignalStatus,
    pub severity: SignalSeverity,
    pub confidence: f32,
    pub subject: SignalSubject,
    pub window: TimeWindow,
    pub first_seen: DateTime<Utc>,
    pub last_seen: DateTime<Utc>,
    pub update_count: u32,
    pub cooldown_until: Option<DateTime<Utc>>,
    pub summary: String,
    pub evidence: SignalEvidence,
    pub peak_evidence: Option<SignalEvidence>,
    pub related_hosts: Vec<HostRef>,
    pub related_flows: Vec<FlowRef>,
    pub related_sessions: Vec<SessionRef>,
    pub related_services: Vec<ServiceRef>,
    pub related_regions: Vec<RegionRef>,
    pub recommended_pivot: PivotTarget,
    pub analyst_note: Option<String>,
}

pub enum SignalMode {
    Offline,
    Realtime,
}

pub enum SignalStatus {
    Candidate,
    New,
    Active,
    Updated,
    Resolved,
    Expired,
}

pub enum BehaviorSignalType {
    // Sniffnet-compatible notification semantics
    DataThresholdExceeded,
    WatchedEntityTransmitted,
    KnownBadHostTransmitted,

    // Security replay / monitoring signals
    ScanLikeFanOut,
    HighOutboundTransfer,
    ResetHeavySessions,
    LongLivedConnection,
    NewExternalEndpoint,
    RareCountry,
    UnusualService,
    RepeatedShortConnections,
    DnsAnomalyCandidate,
    InternalLateralMovementCandidate,
}

pub enum SignalSeverity {
    Low,
    Medium,
    High,
}

pub enum SubjectKind {
    Host,
    Flow,
    Session,
    Service,
    Country,
    EndpointPair,
    WatchlistItem,
    Threshold,
}

pub struct SignalSubject {
    pub kind: SubjectKind,
    pub value: String,
}

pub struct TimeWindow {
    pub start: DateTime<Utc>,
    pub end: DateTime<Utc>,
}

pub struct SignalEvidence {
    pub metrics: Vec<EvidenceMetric>,
    pub description: String,
}
```

最小字段要求：

- `signal_type`
- `mode`
- `status`
- `severity`
- `confidence`
- `time_window`
- `first_seen`
- `last_seen`
- `subject_kind`
- `subject_value`
- `summary`
- `evidence_metrics`
- `related_hosts`
- `related_flows`
- `related_sessions`
- `recommended_pivot`

实时模式额外要求：

- `update_count`
- `cooldown_until`
- `peak_evidence`
- `status`

离线模式中 `status` 可固定为 `Resolved` 或 `Candidate`，取决于 UI 文案。建议用 `Candidate` 表达“待分析人员确认”。

## 输入数据要求

第一阶段应只依赖当前 Flowarden 已能稳定得到的数据：

- flows / top connections
- hosts
- services
- regions / country enrichment
- TCP sessions
- inbound / outbound bytes
- packets
- first seen / last seen
- TCP state
- SYN / FIN / RST count
- protocol / transport
- endpoint address / port

可选增强数据：

- internal / external classifier
- known-bad IP list
- user marked host roles
- watchlist hosts / services
- threshold settings
- DNS payload metadata
- ASN / organization
- historical baseline

如果某类数据缺失，detector 应降级输出弱信号或不输出，不应伪造证据。

## 处理链路

### 离线链路

```text
raw packets / parsed sessions
  -> flow aggregation
  -> host / service / region aggregation
  -> full-dataset time-window aggregation
  -> detector execution
  -> signal normalization
  -> deduplication and ranking
  -> evidence projection
  -> UI / CLI / JSON output
```

关键要求：

- 一次性计算，不做 UI tick 驱动检测。
- detector 在聚合视图上运行，避免重复扫描原始 packet。
- 输出稳定排序，便于测试和 JSON 消费。
- 每个 signal 都必须能 pivot 到 Flow、Host、Session 或 Evidence。

### 实时链路

```text
packet / flow stream
  -> rolling flow/session aggregation
  -> sliding window aggregation
  -> detector execution per window
  -> signal state machine
  -> deduplication / cooldown / escalation
  -> active signal store
  -> UI signal feed / optional alert output
```

关键要求：

- detector 输出不直接追加 UI 列表，必须先进入状态机。
- 同一信号在 cooldown 窗口内更新已有 active signal。
- 实时证据分为 current evidence 和 peak evidence。
- 过期信号进入 history，不再占据 active list。
- 长连接类信号需要跨窗口维护 session state。

## Time-window 聚合

行为信号需要时间窗口，不能只看全局总量。

离线窗口策略：

| 数据集跨度 | 默认窗口 |
| --- | --- |
| <= 10 min | 30s |
| <= 1 hour | 1 min |
| <= 6 hours | 5 min |
| <= 24 hours | 15 min |
| > 24 hours | 1 hour |

实时窗口策略：

| 目的 | 窗口 | 步长 |
| --- | --- | --- |
| 快速扫描发现 | 30s | 10s |
| 常规行为信号 | 1m | 15s |
| 中期趋势 | 5m | 1m |
| 长连接判断 | session lifetime | event-driven |

每个窗口至少聚合：

- bytes in / out
- packets in / out
- flow count
- session count
- unique hosts
- unique endpoint pairs
- unique services
- unique countries
- reset sessions
- incomplete sessions
- top talkers

对于跨窗口的长连接，保留原始 first seen / last seen。窗口只用于定位峰值和更新 active signal。

## 实时 Signal 状态机

实时模式需要状态机，避免每个 tick 都生成新 finding。

```text
Candidate -> New -> Active -> Updated -> Resolved
                         |
                         -> Expired
```

状态含义：

- `Candidate`：detector 命中内部阈值，但尚未达到 alert / display 阈值。
- `New`：首次进入 active signal list。
- `Active`：持续命中。
- `Updated`：证据增强、severity 升级或 peak evidence 更新。
- `Resolved`：条件明确结束，例如会话关闭或风险对象停止通信。
- `Expired`：超过 TTL 未再更新。

实时去重键建议：

```text
signal_type + subject_kind + subject_value + service + target_scope
```

cooldown 规则：

```text
same dedup_key within cooldown window
=> update existing signal
```

默认 TTL 建议：

- Scan-like fan-out：5 min
- High outbound transfer：10 min
- Reset-heavy sessions：5 min
- Long-lived connection：直到 session 结束，或 10 min 未更新
- Watchlist / known-bad hit：10 min
- Data threshold exceeded：按 threshold reset policy

## Detector 接口

建议 detector 支持离线和实时双入口。

```rust
pub trait BehaviorSignalDetector {
    fn detect_offline(&self, view: &OfflineSignalInput) -> Vec<BehaviorSignal>;
    fn detect_realtime(&self, view: &RealtimeSignalInput) -> Vec<SignalCandidate>;
}

pub struct OfflineSignalInput {
    pub dataset: DatasetContext,
    pub flows: Vec<FlowSummary>,
    pub hosts: Vec<HostSummary>,
    pub services: Vec<ServiceSummary>,
    pub regions: Vec<RegionSummary>,
    pub sessions: Vec<TcpSessionSummary>,
    pub windows: Vec<TimeWindowAggregate>,
    pub roles: HostRoleMap,
    pub watchlist: Watchlist,
    pub known_bad_hosts: KnownBadHostSet,
    pub thresholds: SignalThresholds,
}

pub struct RealtimeSignalInput {
    pub now: DateTime<Utc>,
    pub current_window: TimeWindowAggregate,
    pub rolling_windows: Vec<TimeWindowAggregate>,
    pub active_sessions: Vec<TcpSessionSummary>,
    pub recent_flows: Vec<FlowSummary>,
    pub roles: HostRoleMap,
    pub watchlist: Watchlist,
    pub known_bad_hosts: KnownBadHostSet,
    pub thresholds: SignalThresholds,
}
```

实时 detector 输出 `SignalCandidate`，再由 state manager 合并为 `BehaviorSignal`：

```rust
pub struct SignalCandidate {
    pub signal_type: BehaviorSignalType,
    pub severity: SignalSeverity,
    pub confidence: f32,
    pub subject: SignalSubject,
    pub window: TimeWindow,
    pub summary: String,
    pub evidence: SignalEvidence,
    pub related_refs: RelatedRefs,
    pub dedup_key: String,
}
```

统一 normalization：

- 补齐 severity
- 计算 confidence
- 生成 summary
- 绑定 related refs
- 生成 recommended pivot
- 去重
- 排序
- 实时模式更新状态机

## Sniffnet 三类通知的 Flowarden 实现

### DataThresholdExceeded

用途：

- 数据量超过用户定义阈值。
- 兼容 Sniffnet 的 data threshold notification。
- 在 Flowarden 中同时支持离线和实时。

支持维度：

- bytes
- packets
- inbound bytes
- outbound bytes
- per host
- per service
- per country
- total dataset / total window

离线触发：

```text
aggregate_metric > threshold
```

实时触发：

```text
rolling_window_metric > threshold
AND same threshold signal not in cooldown
```

证据：

- threshold
- actual value
- time window
- top hosts
- top services
- top countries

推荐 pivot：

- Dashboard timeline marker
- Explore / Flows
- Evidence detail

### WatchedEntityTransmitted

用途：

- 用户关注对象出现流量。
- 兼容 Sniffnet 的 `FavoriteTransmitted`。
- Flowarden 中不叫 favorite，建议叫 watched entity 或 marked entity。

关注对象类型：

- host
- service
- country
- endpoint pair
- host role
- user marked target server
- user marked attacker

离线触发：

```text
watched_entity has traffic in dataset
```

实时触发：

```text
watched_entity has traffic in current rolling window
AND same watched entity not in cooldown
```

证据：

- watched entity
- bytes
- packets
- first seen
- last seen
- related flows
- related sessions

推荐 pivot：

- Hosts tab with watched entity filter
- Flows tab with endpoint / service filter

### KnownBadHostTransmitted

用途：

- 已知风险 IP / Host 出现通信。
- 兼容 Sniffnet 的 `BlacklistedTransmitted`。

风险来源：

- user blacklist
- threat intel list
- manually marked attacker
- imported known-bad list

离线触发：

```text
flow endpoint in known_bad_hosts
```

实时触发：

```text
current_window contains known_bad_host traffic
AND same host not in cooldown
```

证据：

- IP
- country
- ASN / organization if available
- bytes
- packets
- direction
- related internal host
- service
- first seen
- last seen

推荐 pivot：

- Host detail drawer
- Explore / Flows filtered by known-bad host
- Evidence detail

## Security Detector 规则

### Scan-like fan-out

特性：

- 一个 Host 在短时间内连接很多目标
- 或连接同一目标的很多端口
- 单连接短、bytes 小、reset / incomplete 比例高

聚合键：

- `time_window + source_host`

证据指标：

- unique target hosts
- unique target ports
- connection count
- avg bytes per connection
- reset ratio
- incomplete ratio

触发参考：

```text
(unique_target_ports >= 20 OR unique_target_hosts >= 30)
AND avg_bytes_per_connection < 20 KB
```

实时注意：

- 使用 30s / 1m sliding window。
- 同一 source host 持续扫描时更新 active signal。
- peak evidence 记录最大 fan-out 和最大 port count。

### High outbound transfer

特性：

- 内部 Host 对外发送大量数据
- outbound 明显高于 inbound
- 目标外部 endpoint、国家或服务集中

触发参考：

```text
outbound_bytes >= 1 GB
AND outbound_ratio >= 70%
```

更稳健规则：

```text
host_outbound_bytes > P95(host_outbound_bytes)
AND outbound_ratio >= 70%
```

实时注意：

- 使用 1m / 5m rolling window。
- 对持续出站传输更新同一 active signal。
- severity 可随累计 outbound bytes 升级。

### Reset-heavy sessions

特性：

- TCP RST 数量高
- Reset session 占比高
- 可能是扫描、拒绝连接、服务异常或阻断

触发参考：

```text
total_sessions >= 50
AND reset_ratio >= 40%
```

实时注意：

- 使用 session close / reset event 更新窗口。
- 避免每个 RST 都生成新 signal。

### Long-lived connection

特性：

- 会话持续时间远高于普通连接
- bytes 可能很低，也可能持续传输

触发参考：

```text
duration > P95(session_duration)
OR duration >= 30 minutes
```

实时注意：

- 以 active session 生命周期为准。
- 首次超过阈值生成 signal。
- 之后周期性更新 duration 和 bytes。
- session 结束后转为 Resolved。

### New external endpoint

特性：

- 外部 Host 在当前数据集或当前监测会话中首次出现。
- 与内部关键 Host 建立连接。

离线说明：

- 只有单个数据集时，new 只表示本数据集内首次出现。

实时说明：

- new 表示当前 capture session 内首次出现。
- 如果有历史库，可升级为 historical first seen。

### Rare country

特性：

- 某国家或地区占比低但有明显流量。
- 或 Host 数少但 bytes 高。

触发参考：

```text
country_ratio < 2%
AND bytes > 500 MB
```

实时注意：

- 短窗口内 ratio 波动大，建议使用 5m rolling window。
- 无 country enrichment 时不输出。

### Unusual service

特性：

- 出现少见服务
- 高风险服务
- 非标准端口承载常见协议

触发参考：

```text
service in [SSH, RDP, SMB, Telnet, FTP]
AND external_connection_count > threshold
```

非标准端口参考：

```text
service = HTTPS
AND port NOT IN [443, 8443]
```

### Repeated short connections

特性：

- 同一 Host 对同一目标重复建立短连接
- 每次 bytes 很小

触发参考：

```text
connection_count >= 100
AND avg_duration < 3s
AND avg_bytes < 10 KB
```

实时注意：

- 需要短期 rolling state，避免跨太长时间误判。

### DNS anomaly candidate

第一阶段弱信号：

- UDP/53 connection count high
- DNS bytes high
- one host dominates DNS traffic

增强阶段证据：

- unique domain count
- NXDOMAIN ratio
- avg domain length
- subdomain entropy

如果当前没有 DNS payload，只输出弱信号，或不启用该 detector。

### Internal lateral movement candidate

特性：

- 内部 Host 之间出现异常服务访问。
- 涉及 SMB、RDP、SSH、WinRM、数据库端口。

敏感服务：

- SMB 445
- RDP 3389
- SSH 22
- WinRM 5985 / 5986
- MSSQL 1433
- MySQL 3306
- PostgreSQL 5432

触发参考：

```text
internal_to_internal = true
AND service in sensitive_services
AND unique_target_hosts >= threshold
```

该 detector 建议第二阶段做，因为它依赖较可靠的内外网识别和业务基线。

## Severity 与 Confidence

`severity` 表达优先级，`confidence` 表达证据强度。

建议计算方式：

```text
severity = impact_score + rarity_score + risk_score
confidence = evidence_completeness + threshold_distance + corroboration
```

影响因子：

- bytes
- packets
- session count
- affected hosts
- sensitive service
- external country
- known-bad hit
- watched entity importance

证据完整度：

- 是否有明确 time window
- 是否有 related flows
- 是否有 related sessions
- 是否有 related hosts
- 是否有 country / service
- 是否有多个 detector 相互印证

实时模式额外因子：

- 持续时间
- update count
- peak value
- severity escalation
- cooldown 命中次数

UI 上：

- severity 用 `Low / Medium / High`
- confidence 可用百分比或三段式 `Low / Medium / High`
- 第一版不建议暴露复杂公式

## 去重、合并与排序

离线去重：

- 同一 `signal_type + subject + time_window` 合并。
- 同一 endpoint pair 上的 repeated short 和 reset-heavy 可互相关联，但不强行合并。
- 同一 Host 上的 high outbound 和 new external endpoint 保持独立，但在 evidence 中互相引用。

实时去重：

- 同一 dedup key 更新 active signal。
- cooldown 窗口内不新增同类 signal。
- severity 升级或 peak evidence 变化时更新 UI。

排序建议：

1. status，实时 Active 优先
2. severity
3. confidence
4. known-bad / watched entity 命中
5. bytes impact
6. session count
7. first seen

Dashboard / Live panel 只展示 Top N，完整列表进入 Evidence 或 Signal History。

## UI 集成

### 离线 UI

Dashboard：

- `Key Findings` 面板展示最高优先级 signals。
- `Replay Timeline` 展示 signal markers。
- Top Hosts / Top Connections / Top Services 显示 behavior badge。

Explore：

- Flows 表增加 `Signal` 列。
- Hosts 表增加 `Behavior` 列。
- Sessions 表支持 `Reset / Incomplete / Long-lived` 筛选。
- Evidence tab 展示完整 finding list、timeline、detail。

Host Detail Drawer：

- current role
- behavior signals
- related findings
- related flows
- related sessions
- analyst note

### 实时 UI

Realtime / Monitor 场景应使用：

- Active Signals list
- Signal Feed
- current window summary
- rolling throughput
- active signal detail drawer
- resolved / expired signal history

实时 UI 不应每个 tick 新增一条记录。应更新同一 signal：

- first seen
- last seen
- current evidence
- peak evidence
- update count
- status

如果当前产品仍以 offline-only 为主，实时 UI 可先不落地，但 core 模型应预留这些字段。

## CLI / JSON 集成

JSON 输出建议：

```json
{
  "behavior_signals": [],
  "active_signals": [],
  "signal_history": []
}
```

离线 JSON：

- 输出 `behavior_signals`
- 默认按 severity / confidence 排序
- `--top N` 作用于默认展示数量
- 如需完整输出，可另设 `behavior_signals_all` 或 `--signals-all`

实时 JSON：

- 输出 `active_signals`
- 输出最近 resolved / expired `signal_history`
- 每个 signal 带 `status`、`first_seen`、`last_seen`、`update_count`

table 输出：

- 只展示摘要，不展开全部证据
- 详细证据放 JSON

## 配置

通用配置：

- enable / disable each detector
- Top N signals
- severity threshold
- bytes unit
- host role default
- show low severity findings
- group findings by host / time / signal

离线配置：

- timeline density
- replay time-window strategy
- include low severity findings

实时配置：

- sliding window size
- window step
- cooldown duration
- signal TTL
- active signal max count
- alert threshold

Sniffnet 兼容类配置：

- data threshold rules
- watched hosts / services / roles
- known-bad host list

不建议第一版做：

- sound
- notification volume
- remote webhook

这些可以作为后续 realtime alert channel。

## 实施阶段

### Phase 1: 统一模型与离线 MVP

交付：

- `BehaviorSignal` 数据模型
- `SignalMode` / `SignalStatus`
- detector trait
- offline signal projection
- DataThresholdExceeded
- WatchedEntityTransmitted
- KnownBadHostTransmitted
- Scan-like fan-out
- High outbound transfer
- Reset-heavy sessions
- Long-lived connection
- Rare country
- Unusual service
- New external endpoint
- JSON 输出
- UI Key Findings / Evidence 基础展示

### Phase 2: 实时状态机

交付：

- rolling window aggregation
- realtime detector input
- SignalCandidate
- active signal store
- dedup key
- cooldown
- TTL / expiration
- status transitions
- current / peak evidence
- active signal JSON output

### Phase 3: 证据增强

交付：

- related flow/session 精确引用
- timeline marker 绑定
- Host detail signals
- role 与 signal 联动
- repeated short connections
- weak DNS anomaly
- Signal History

### Phase 4: 基线与协议增强

交付：

- DNS payload metadata
- historical baseline
- known-bad IP list import
- ASN / organization enrichment
- internal lateral movement detector
- tunable detector thresholds
- optional realtime alert channels

## 测试策略

单元测试：

- 每个 detector 使用 synthetic flows / sessions 验证触发和不触发。
- Sniffnet 三类兼容信号的触发测试。
- 阈值边界测试。
- severity / confidence 测试。
- dedup 测试。
- realtime state transition 测试。
- cooldown / TTL 测试。

集成测试：

- offline fixture -> projection -> behavior signals。
- streaming fixture -> rolling windows -> active signals。
- JSON 输出稳定性。
- Top N 截断规则。
- country / role / service 关联证据。
- active signal update 不产生重复记录。

性能测试：

- 大 PCAP 离线处理不产生 UI tick 风暴。
- detector 在聚合视图上运行，避免重复扫描原始 packet。
- time-window 聚合可线性处理。
- realtime rolling window 内存有上限。
- active signal store 不无限增长。

回归测试：

- 无 TCP sessions 时，session-based detector 不应 panic。
- 无 country enrichment 时，RareCountry 不输出或降级。
- 无 DNS payload 时，DNS anomaly 只输出弱信号或禁用。
- direction 不明确时，HighOutbound 不应误报为高置信。
- 实时 cooldown 期间同一 signal 应更新而不是新增。
- long-lived session 结束后应转 Resolved。

## 风险与注意事项

- NAT 会导致多个真实主机合并为一个外部地址。
- 内外网判断不可靠会影响 High outbound 和 Lateral movement。
- 单数据集内的 `NewExternalEndpoint` 不是历史首次出现。
- 实时 session 内的 `NewExternalEndpoint` 也不等于历史首次出现。
- 高流量不等于恶意，必须展示证据而不是结论。
- Reset-heavy 可能来自正常服务拒绝或网络设备策略。
- Long-lived 可能是正常业务长连接。
- Rare country 依赖地理库质量。
- DNS anomaly 需要 payload 才能做强判断。
- 实时模式必须防止重复刷屏。
- webhook / sound 等 alert channel 不应影响 core signal 计算。

第一版产品文案要克制：显示 `candidate`、`signal`、`finding`，让用户看到证据并自行确认。
