# Design Decisions — Locked

Last updated: May 2026

These decisions are locked. Do not revisit without strong justification.

## Charting Library: Blazor-ApexCharts
- **Decision:** Use `Blazor-ApexCharts` (NuGet: `Blazor-ApexCharts`)
- **Rationale:** MIT license, native Blazor components, `UpdateSeriesAsync()` for SignalR live updates, built-in dark theme compatible with our palette, active .NET 10 maintenance
- **Rejected alternatives:** Vizor.ECharts (heavier), MudBlazor (full UI framework — too opinionated), Arcadia.Charts (less community)
- **Status:** Selected but not yet installed in `SplitBrain.Dashboard.csproj`

## Dark Theme Palette
- **Decision:** Custom `sb-` CSS variable system in `wwwroot/app.css`
- **Key tokens:** bg `#0f1117`, surface `#1a1d27`, surface2 `#232636`, border `#2e3148`, text `#e2e8f0`, muted `#6b7280`, accent `#6366f1`, green `#22c55e`, yellow `#eab308`, red `#ef4444`
- **Rationale:** VSCode-inspired dark aesthetic, avoids heavy UI framework, fully owned CSS
- **Light mode:** Planned via `data-theme="light"` attribute swap on `<body>`. Not yet implemented.
- **Status:** ✅ Implemented

## No Heavy UI Framework
- **Decision:** No MudBlazor, no Radzen, no Ant Design Blazor
- **Rationale:** Keeping dashboard lean, fully controlled styling, no framework update churn

## Testing Framework: NUnit
- **Decision:** NUnit for all test projects
- **Status:** `Orchestrator.Tests` uses NUnit

## Inference Abstraction: IInferenceNode
- **Decision:** Single interface (`IInferenceNode`) decouples all routing, agent, and MCP logic from providers
- **Providers:** Ollama, CopilotSdk — extensible via `NodeProviderType` enum + `InferenceNodeFactory`

## Node Configuration: nodes.json + IOptionsMonitor
- **Decision:** All node topology lives in `nodes.json`. Hot-reload via `IOptionsMonitor<NodeTopologyConfig>`. Registry rebuilds atomically via `Interlocked.Exchange`.
- **Never use:** `ConcurrentDictionary.Clear()` + re-add pattern (race window)

## Agent Bounds (Locked from Spec)
- Max iterations: 4
- Max tokens per loop: 12,000 (this is the budget ceiling; actual model context window is 8K — the agent must fit prompt + history + output within both limits)
- Default timeout: 300s

## MCP Transport
- Streamable HTTP on port 5100 (`/mcp`) — default
- stdio — for IDE integration (VS Code, VS 2026)
