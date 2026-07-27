# CLI / UI 字段契约对照（M2X-004）

目标：进入 UI 的增强字段在 CLI JSON 中可对齐验证；UI-only 字段显式标注。

## 1. 连接（Connection）

| 语义 | CLI JSON 路径 | UI / gRPC | 备注 |
| --- | --- | --- | --- |
| 源地址 | `top_connections_enriched[].source.address` | `ConnectionRow.source_address` | 同源；**本机视角**：有 local_ips（或 offline 私网启发）时 Src 为本机侧 |
| 源国家码 | `…source.country_code` | Overview destination / host country | CLI 有 endpoint 级；UI Host 有 `country_label` |
| 目的地址 | `…destination.address` | `ConnectionRow.destination_address` | 同源；对端侧（本机取向后） |
| 端口 | `source_port` / `destination_port` | `source_port` / `destination_port` | 同源；与取向后的 Src/Dst 一致 |
| 协议 | `protocol` | `protocol` | CLI Debug 风格字符串 |
| **SNI** | `top_connections_enriched[].sni` | `ConnectionRow.sni` / Inspect 列 | **契约字段**（M2X-006） |
| 字节/包 | `bytes` / `packets` | `bytes` / `packets` | 同源；反向路径合并进同一 flow |
| Process | — | `ConnectionRow.process_name` / `process_pid` | **UI-only 运行态**（live lookup）；CLI offline 默认无进程表 |
| Direction | — | `direction` | 本机视角：local_ips + in/out 字节；非纯线序 |

## 2. 主机（Host）

| 语义 | CLI JSON | UI / gRPC | 备注 |
| --- | --- | --- | --- |
| IP | `top_hosts_enriched[].host` | `HostRow.host` | 同源 |
| Country | `country_code` / `country_label` | `HostRow.country_label` | 同源语义 |
| **SNI** | `top_hosts_enriched[].sni` | `HostRow.sni` | **契约字段** |
| Hostname (PTR) | — | `HostRow.hostname` | **UI/core 机会性 rDNS**；CLI 未导出 |
| 流量 | `packets` / `bytes` / in/out | `packets` / `bytes` | 同源 |

## 3. 服务（Service）

| 语义 | CLI JSON | UI / gRPC | 备注 |
| --- | --- | --- | --- |
| 名称 | `final_snapshot.aggregate_summary.top_services[].service.name` | `ServiceRow.name` | 同源 |
| 传输 | `service.transport` | `ServiceRow.transport` | 同源 |
| 流量 | counters | `packets` / `bytes` | 同源 |

## 4. Behavior Signal

| 语义 | CLI JSON | UI / gRPC | 备注 |
| --- | --- | --- | --- |
| Finding 列表 | `findings[]` | `OverviewSnapshot.signals[]` / Signals 页 | **同源 detector**；CLI via `evaluate_cli_findings` |
| kind / mode / status | `findings[].kind/mode/status` | `BehaviorSignalRow` | mode=`live\|offline`；status=`active\|updated\|finding` |
| pivot | `pivot_kind` / `pivot_value` | 同名字段 | UI Inspect + offline Overview focus |
| CLI policy | `--data-threshold` / `--watch` / `--known-bad` | Settings → SetSignalPolicy | 模式对齐 |

## 5. Capture quality（诊断）

| 语义 | CLI / FinalSnapshot | UI | 备注 |
| --- | --- | --- | --- |
| dropped | `final_snapshot.dropped_packets` | Overview + Settings Diagnostics | 同源字段 |
| last packet | `last_packet_timestamp` | Settings last packet age | UI 展示相对年龄 |
| stream state | — | Settings `StreamStateLabel` | **UI-only**（shell 运行态） |
| core uptime | — | Settings from Health `started_at` | **UI/core health** |
| process lookup queue | — | `OverviewSnapshot.process_lookup_pending` | **Resident core** 诊断字段 |
| process lookup cache | — | `process_lookup_cache_size` | **Resident core** 诊断字段 |
| core restarts | — | Settings `CoreRestartCountLabel` | **UI-only**（Reconnect relaunch 计数） |
| diagnostics export | — | Settings Export JSON | **UI-only** 快照文件 |

## 6. Light DPI / snaplen（Phase3 P3-012）

| CLI | 默认 | 说明 |
| --- | --- | --- |
| `--snaplen N` | live 512 | 监控 snaplen；有 `--pcap-out` 时仍 65535 |
| `--no-sni` | false | 关闭 ClientHello SNI 解析 |
| `--sni-max-payload N` | 512 | TCP payload 扫描上限 |

## 7. 验证方式

1. CLI：`flowarden capture … -o json` 检查 `top_*_enriched[].sni` / `country_*`。  
2. UI：Inspect 列 SNI / Process；Overview Top Hosts 显示 SNI 优先名。  
3. 单测：`output::tests::json_output_is_stable_and_parseable` 断言 SNI 字段。  
4. 信号：`service::signals::tests::offline_finding_is_stable_and_deduped`。  
5. Light DPI：`tls_sni` / `light_dpi` 单测；压测见 phase3 load runbook。

## 7. 明确的 UI-only 列表

- `process_name` / `process_pid` / `process_inferred`（live OS lookup）
- `HostRow.hostname`（opportunistic rDNS PTR）
- `UserRunState` / stream reconnect 状态文案
- Signal unread / toast / sound 偏好
- Destination map region markers 展示态
