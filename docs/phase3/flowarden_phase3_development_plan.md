# Flowarden 第三阶段开发计划

## 1. 文档目的

本文用于冻结第三阶段的开发边界、实施顺序和验收口径。

第三阶段只做两类能力：

1. payload 深度解析
2. 会话级重建

本文不再扩展第二阶段 UI 壳层本身，也不把第三阶段泛化成“完整取证平台”。

上游参考文档：

- `../flowarden_phased_development_plan.md`
- `../sniffnet_refactor_avalonia_rust_grpc.md`
- `../sniffnet_reverse_analysis.md`
- `../phase1/flowarden_phase1_detailed_plan.md`
- `../phase2/flowarden_phase2_development_plan.md`

---

## 2. 第三阶段完成定义

第三阶段完成，不等于“增加几个协议解析器”，而是同时满足以下条件：

1. core 能基于已建立的 flow/session 边界做稳定的 TCP 会话级重组
2. core 能对明确纳入范围的高价值协议做 payload 级解析
3. phase2 UI 与 CLI 都可以查看会话级详情，而不需要推倒现有壳层
4. 深度解析失败只影响当前会话或当前 payload，不拖垮采集主循环
5. payload 保留、截断、脱敏、导出边界明确
6. 第二阶段保留的 `Destination Map`、Inspect、Settings 壳层可继续承接新详情，而不是返工重做

---

## 3. 约束与前提

### 3.1 继承前两阶段的边界

第三阶段必须继承以下已成立原则：

1. Rust 服务端仍保持“内核同步，async shell”
2. UI 仍只通过 `gRPC` 或既定契约访问 core
3. 第二阶段 UI 壳层和页面结构不推倒重来
4. 错误语义继续统一落到 `flowarden-error`
5. 继续遵循 `YAGNI`，只做当前已确定的 payload / session 能力

### 3.2 第三阶段新增复杂度的来源

第三阶段的复杂度主要来自：

1. TCP 重组状态机
2. 双向字节流缓冲
3. payload 截断与内存占用
4. 协议识别与局部失败恢复
5. 会话详情查询与导出

因此第三阶段不能以“顺手再加一点解析”方式零散推进，必须先冻结边界。

### 3.3 明确非目标

第三阶段明确不做：

1. 通用 Wireshark 级协议全集
2. 完整 IDS/IPS
3. 全量文件还原
4. 跨主机分布式会话合并
5. 大规模远程 agent 架构
6. 深度机器学习检测

---

## 4. 第三阶段目标架构

第三阶段建议冻结为以下分层：

```text
flowarden-ui / CLI
  <-> local gRPC

flowarden-core service
  -> capture
  -> packet decode
  -> flow tracking
  -> session tracking
  -> stream reassembly
  -> payload analyzers
  -> projection / export
```

### 4.1 核心原则

1. 包级聚合与会话级解析分层
2. 会话跟踪先于 payload 解析
3. payload 解析器不直接感知 UI
4. 解析器只消费统一的 session / stream 输入

### 4.2 分层说明

#### `flow tracking`

负责：

1. 延续 phase1 的连接/主机/服务聚合
2. 作为会话跟踪的上游入口

#### `session tracking`

负责：

1. 建立五元组或等价方向上下文
2. 管理 session 生命周期
3. 维护双向流状态

#### `stream reassembly`

负责：

1. TCP 分段重组
2. 顺序控制
3. 重传与缺口状态记录
4. 截断与超时策略

#### `payload analyzers`

负责：

1. 对明确纳入范围的协议做 payload 级解析
2. 输出结构化 session details
3. 在局部失败时只报告当前 analyzer 错误，不破坏上游会话状态

---

## 5. 功能范围

## 5.1 必做范围

1. 会话对象模型
2. TCP 会话跟踪
3. 双向字节流重组
4. payload 缓冲与截断策略
5. 会话详情投影
6. CLI 侧会话详情查询
7. UI 侧 inspect / session detail 承接
8. 导出边界与脱敏边界

## 5.2 建议首批协议范围

第三阶段首批协议建议只做：

1. HTTP/1.x
2. DNS
3. TLS ClientHello / ServerHello 元信息级抽取

原因：

1. 都是流量监控里高价值协议
2. 能较快形成“session details”用户价值
3. 不会像完整 HTTP2/TLS 解密那样过早失控

## 5.3 明确后置

以下能力建议留到第三阶段之后：

1. HTTP/2 深入解析
2. QUIC / HTTP3 完整支持
3. 文件级对象重建
4. TLS 解密
5. WebSocket 深层消息级分析
6. SMTP/IMAP/FTP 等协议全集

---

## 6. 数据与模型设计建议

## 6.1 新增核心对象

建议新增以下模型层：

1. `SessionKey`
2. `SessionState`
3. `StreamDirection`
4. `ReassemblyBuffer`
5. `SessionDetail`
6. `PayloadSlice`
7. `AnalyzerOutput`

### `SessionKey`

建议最小包含：

1. source address
2. source port
3. destination address
4. destination port
5. transport

### `SessionState`

建议最小包含：

1. created_at
2. last_seen_at
3. direction state
4. bytes buffered
5. packets buffered
6. reassembly completeness
7. parse status

### `SessionDetail`

建议最小包含：

1. 会话标识
2. 协议识别结果
3. payload 元信息摘要
4. 关键字段抽取结果
5. 解析错误摘要
6. 截断或缺口标记

## 6.2 不要做的模型设计

第三阶段不要：

1. 直接把原始 payload 全量挂到 UI DTO
2. 把 analyzer 输出和重组缓冲耦死
3. 一开始就追求“所有协议一个统一巨型枚举”

---

## 7. payload 策略

## 7.1 为什么要先定策略

payload 不是“能拿到就全留”，必须先定：

1. 保留多长
2. 保存到哪
3. 是否脱敏
4. 是否可导出
5. 出错时如何回收

## 7.2 建议的首版策略

建议第三阶段首版采用：

1. 默认只保留有限长度 payload 片段
2. 默认只保留与已支持协议解析相关的窗口
3. 默认不做全量无限保留
4. 导出动作显式触发
5. 敏感字段优先做摘要化或脱敏展示

## 7.3 截断策略建议

每个方向建议独立设置：

1. 最大缓冲字节数
2. 最大会话保留时长
3. 最大解析窗口

出现超限时：

1. 标记 `truncated`
2. 保留已有结构化结果
3. 停止进一步增长缓冲

---

## 8. 会话重建策略

## 8.1 TCP 首批只做什么

第三阶段首批 TCP 重组建议只做：

1. 有序重组
2. 基本重传覆盖
3. 缺口标记
4. 超时关闭

不建议首批就做：

1. 极端乱序优化
2. 全部边界条件的完整取证级复原
3. 超大规模历史缓存

## 8.2 生命周期策略

每个会话至少有：

1. active
2. idle timeout
3. closed
4. evicted

这样 CLI 与 UI 都能清楚表达：

1. 会话是否完整
2. 会话是否仍在增长
3. 会话是否因限制被截断或淘汰

---

## 9. gRPC / UI 承接建议

## 9.1 建议新增服务面

建议在第二阶段已有边界上扩展：

1. `SessionQueryService`
2. `PayloadExportService`（可后置）

### `SessionQueryService`

建议最小包含：

1. `ListSessions`
2. `GetSessionDetail`
3. `GetSessionPayloadSummary`

## 9.2 UI 承接位置

建议第三阶段 UI 主要承接在：

1. `Inspect` 页扩展结果行
2. 新增 `Session Detail` 面板或抽屉
3. `Overview` 不承载 payload 细节

这样可以保持：

1. Overview 仍是总览
2. Inspect 仍是明细入口
3. 会话详情不污染 shell 结构

---

## 10. 模块落位建议

建议在 `flowarden-core` 内新增：

```text
session/
  key.rs
  state.rs
  tracker.rs

reassembly/
  buffer.rs
  tcp.rs

payload/
  mod.rs
  analyzer.rs
  http.rs
  dns.rs
  tls.rs

projection/
  session.rs
```

职责建议：

1. `session/`：会话生命周期
2. `reassembly/`：字节流重组
3. `payload/`：协议级 analyzer
4. `projection/`：对 UI / CLI 的会话级投影

---

## 11. 建议实施顺序

第三阶段建议按 5 个波次推进。

### 波次 1：会话模型与生命周期

目标：

1. 建立 `SessionKey / SessionState`
2. 建立 session tracker
3. 先不做 payload analyzer

### 波次 2：TCP 重组最小闭环

目标：

1. 建立双向 stream 状态
2. 支持基本有序重组
3. 标记 gap / truncation / timeout

### 波次 3：首批协议 analyzer

目标：

1. 接 HTTP/1.x
2. 接 DNS
3. 接 TLS hello 元信息

### 波次 4：CLI 与 UI 查询面

目标：

1. CLI 支持 session 查询
2. gRPC 暴露 session detail
3. UI 在 Inspect 中挂接详情

### 波次 5：封板与脱敏/导出边界

目标：

1. 固化 payload 保留策略
2. 固化导出边界
3. 补齐测试与验收记录

---

## 12. 风险与控制点

### 风险一：会话重组把 core 复杂度拉爆

控制策略：

1. 先最小闭环，不追求极致完整
2. 会话层和 analyzer 层严格分开

### 风险二：payload 保留导致内存不可控

控制策略：

1. 先做截断与上限
2. 所有会话缓冲必须可回收

### 风险三：UI 被会话细节反向绑架

控制策略：

1. UI 只消费 session projection / detail DTO
2. 不共享 Rust 内部对象

### 风险四：过早追求协议覆盖面

控制策略：

1. 先做少数高价值协议
2. 每个 analyzer 单独验收

---

## 13. 第三阶段验收建议

第三阶段建议至少验证：

1. 离线样本中 HTTP 会话可重组且关键字段可读
2. DNS 请求/响应可在会话或事务级被查看
3. TLS hello 元信息可被提取
4. 会话缺口、超时、截断状态可见
5. 深度解析失败不拖垮 live capture
6. UI 能从 Inspect 进入会话详情
7. 所有错误继续统一映射到 `flowarden-error`

---

## 14. 交付建议

第三阶段评审通过后，建议继续补以下文档：

1. `flowarden_phase3_backlog.md`
2. `flowarden_phase3_implementation_sequence.md`
3. `flowarden_phase3_acceptance_template.md`

当前这份文档只负责冻结：

1. 范围
2. 架构方向
3. 模型边界
4. 实施顺序
5. 验收口径
