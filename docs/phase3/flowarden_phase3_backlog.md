# Flowarden Phase 3 Backlog

依据 `flowarden_phase3_development_plan.md`。Phase2 已交付的 SNI / Process 记 **baseline**，不重复排期。

## 状态约定

`not_started` | `in_progress` | `completed` | `baseline` | `deferred`

---

## 波次 1 — ARP / L3

| ID | 项 | 状态 |
| --- | --- | --- |
| P3-001 | ARP 解码（etherparse NetHeaders::Arp） | `completed` |
| P3-002 | 服务标签 arp-request / arp-reply | `completed` |
| P3-003 | 进入秒级聚合与 Top Services | `completed` |
| P3-004 | CLI/UI protocol 标签 `arp` | `completed` |
| P3-005 | 非 IPv4 ARP / 畸形鲁棒 | `deferred` |

## 波次 2 — Light DPI

| ID | 项 | 状态 |
| --- | --- | --- |
| P3-010 | TLS SNI 提取与展示 | `baseline`（M2X-006） |
| P3-011 | SNI 过滤（Inspect） | `baseline` |
| P3-012 | 采样策略可配置 / snaplen 文档化 | `completed` |

## 波次 3 — Process

| ID | 项 | 状态 |
| --- | --- | --- |
| P3-020 | async process lookup + UI | `baseline`（M2E-101） |
| P3-021 | Linux/Windows 矩阵验证 | `not_started` |
| P3-022 | 推断置信度 UI 文案 | `baseline`（inferred 字段已有） |

## 波次 4 — UI / 性能

| ID | 项 | 状态 |
| --- | --- | --- |
| P3-030 | ARP 在 Overview 可见 | `completed`（via top services） |
| P3-031 | 大流量压测 runbook | `completed`（文档） |
| P3-032 | 进程/lookup 队列告警阈值 | `not_started` |

---

## 出口（Phase3 最小）

1. ARP 可解码并出现在 projection 服务榜 ✅  
2. SNI + Process 在 Inspect 可见（baseline）✅  
3. 压测证明热路径不阻塞 ⏳  
