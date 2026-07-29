# Filter UX + Visual Polish — 落地进度

| 字段 | 内容 |
| --- | --- |
| 日期 | 2026-07-29 |
| 设计 | `flowarden_filter_ux_and_visual_polish_design.md` r2.1 |
| 拍板 | Country experimental；Density defer；Overview 整行 pivot |

## 交付对照

| PR | 状态 | 备注 |
| --- | --- | --- |
| PR1a 状态模型 + chips + 统一路径 | completed | `InspectFilterMatcher`、`FilterChipViewModel`、可移除 chips、footer total/visible |
| PR1b Search debounce | completed | 300ms + generation；live 优先不打 GetInspectPage |
| PR2 proto process_name/sni | completed | `projection.proto` + `inspect_row_matches_filter` + UI client |
| PR3 Overview raw pivot | completed | raw keys + 整行点击 → Inspect |
| PR4 Capture BPF 编辑 | completed | Source 编辑区 + Start-only 文案 + Overview Capture Filter 卡 |
| PR5 Theme / chips / table hover | completed | mode-toggle、chip-remove、ranking-row；header 去 hex |
| PR6 EmptyStateView | completed | 组件已加；TCP 表内空态保留 |
| PR7 Chart fill/glow/stale | completed | Area path wire-up + glow + stale pill |
| PR8 Density | deferred | 按拍板跳过 |

## 质量

- `cargo test -p flowarden-core` / `cargo test --bin flowarden`：通过
- `dotnet build Flowarden.Ui.sln`：0 warning / 0 error

## 手测建议

1. Inspect 输入 Search → 300ms 后过滤；chip × 可删
2. More → Process/SNI → Apply structured
3. Overview Top Hosts/Services/Connections 整行点击 → Inspect pivot
4. Source 编辑 BPF → 运行中显示 pending Start
5. Overview 图有入出站 fill；Capture Filter 卡可见

## 后续跟进（2026-07-29 续）

| 项 | 状态 |
| --- | --- |
| Source 设备卡色 → TFC token 常量 | completed |
| Inspect Flows 空态 EmptyStateView | completed |
| Destination Map 叙事摘要 + 空态 | completed（地理 P2 轻量） |
