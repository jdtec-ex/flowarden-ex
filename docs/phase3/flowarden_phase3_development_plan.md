# Flowarden 第三阶段开发计划 (解析增强与轻量 DPI 版)

## 1. 文档目的

本文用于重新定义并冻结第三阶段的开发边界、实施顺序 and 验收口径。

**核心目标**：在 Phase 1/2 的基础上，参考 Sniffnet 的高效实现思路，补齐 L3 网络层全面解析、L4 传输层轻量 DPI 以及本地进程溯源能力。

---

## 2. 第三阶段完成定义

第三阶段完成的标准是，Flowarden 的分析引擎能够满足以下深度监控需求：

1. **L3 网络层全面解析**：
   - 完整支持 IPv4 与 IPv6。
   - **新增 ARP 协议支持**：能够识别并统计本地网络中的 ARP 请求与响应。
2. **L4 传输层轻量 DPI (Light DPI)**：
   - **Payload 读取**：在不进行全量重组的前提下，支持对数据包首部 Payload 的按需读取（如前 64-128 字节）。
   - **TLS SNI 提取**：能够从 TLS ClientHello 包中提取 Server Name Indication (SNI) 域名，用于辅助服务识别。
3. **本地进程溯源 (Local Process Identification)**：
   - 参考 Sniffnet，建立“连接 -> 本地端口 -> 进程”的映射链路。
   - 在实时抓包中，能够准确识别发起或接收流量的本地进程名与 PID。
4. **Sniffnet 风格异步架构**：
   - 所有高延迟操作（如进程查找、rDNS、SNI 解析后的增强）均不阻塞主分析循环，采用异步 Worker 或延迟补全机制。

---

## 3. 核心功能设计

### 3.1 L3/L4 解析器升级 (Decoder Expansion)

- **ARP 支持**：
  - 更新 `DecodedPacket` 模型，支持表达非 IP 协议的地址（如 MAC 映射）。
  - 在 `decoder.rs` 中解除对 ARP 的屏蔽，实现 ARP 帧解析。
- **Light DPI 机制**：
  - **按需采样**：仅对特定协议（如 TCP 443 端口的首个数据包）触发 Payload 读取。
  - **SNI 解析器**：实现一个轻量级的 TLS 握手解析逻辑，直接从字节流中提取 SNI 字段。
  - **服务增强**：将提取到的 SNI 作为 `ServiceLabel` 的重要补充信息。

### 3.2 进程溯源实现 (Process Attribution)

- **实现机制**：
  - 引入 `listeners` 或类似 crate，通过查询操作系统的网络套接字表（Socket Table）建立映射。
  - **异步 Lookup**：仿照 Sniffnet 的 `thread_lookup_program`。当发现新连接时，将“本地端口 + 协议”提交给后台线程进行异步查找。
  - **解析结果缓存**：建立 `(Protocol, LocalPort) -> (PID, ProgramName)` 的带过期时间的缓存，避免对高频连接重复调用系统 API。

### 3.3 数据模型与契约变更

- **`DecodedPacket` 扩展**：
  ```rust
  pub struct DecodedPacket {
      // ...
      pub l3_info: L3Info, // 支持 IPv4, IPv6, ARP
      pub payload_preview: Option<Vec<u8>>,
      pub sni: Option<String>,
  }
  ```
- **gRPC 契约扩展**：
  - `ConnectionRow` 增加 `process_name`, `pid`, `sni` 字段。
  - UI 侧增加展示这些增强信息。

---

## 4. 实施波次 (Implementation Waves)

### 波次 1：L3 全解析与 ARP 落地
- 修改 `decoder.rs` 支持 ARP。
- 完善 `DecodedPacket` 对 ARP 数据的表达。
- 确保 ARP 统计进入秒级聚合 Tick。

### 波次 2：Light DPI (Payload & SNI)
- 在包解析路径中加入有限长度的 Payload 捕获。
- 实现 TLS ClientHello 的 SNI 提取逻辑。
- 将 SNI 数据挂载到连接摘要中，并支持通过 SNI 进行过滤。

### 波次 3：本地进程关联 (Process ID)
- 引入进程查找核心库。
- 实现异步进程查找 Worker 和结果缓存机制。
- 适配 macOS/Linux/Windows（按优先级排列）。

### 波次 4：UI 联动与性能优化
- 在 Avalonia UI 中展示 ARP 活动、SNI 域名和所属进程。
- 进行大流量压测，确保进程查找和 SNI 解析不会导致主循环丢包。

---

## 5. 风险与控制点

1. **性能风险**：在包解析循环中直接做 SNI 解析或进程查找会导致严重卡顿。
   - **对策**：必须严格执行**异步化**。主循环只负责标记“待解析”，由后台 Worker 消费并补全结果。
2. **准确性风险**：进程溯源是基于端口的启发式映射，存在偏差（如连接结束后端口被复用）。
   - **对策**：增加时间窗口校验，优先标记为“实时归属”，并在 UI 提示其为推断结果。
3. **Payload 内存占用**：保留 Payload 会增加内存开销。
   - **对策**：严格限制 `preview_len`（如 64 字节），仅对关键协议采样。
