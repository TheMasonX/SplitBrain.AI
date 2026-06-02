# Architecture — Current State

Last updated: June 2026 (session 11)

## Solution File
`Orchestrator.slnx` at repo root (`.slnx` format, VS 2026).

## Project Naming Convention
The plan uses `SplitBrain.*` names conceptually, but the actual solution uses `Orchestrator.*` / `NodeClient.*` prefixes.

| Plan Name | Actual Project | Status |
|---|---|---|
| SplitBrain.Core | `Orchestrator.Core` | ✅ Exists |
| SplitBrain.Networking | `Orchestrator.Infrastructure` | ✅ Exists |
| SplitBrain.Routing | Inside `Orchestrator.Infrastructure` | ✅ `RoutingService.cs` present |
| SplitBrain.Models | Inside `Orchestrator.Infrastructure` | ✅ `InMemoryModelRegistry.cs` present |
| SplitBrain.Agents | `Orchestrator.Agents` | ✅ Exists |
| SplitBrain.MCP | `Orchestrator.Mcp` | ✅ Exists |
| SplitBrain.Observability | Inside `Orchestrator.Infrastructure` | Partial |
| SplitBrain.Dashboard | `SplitBrain.Dashboard` | ✅ Exists (kept SplitBrain prefix) |
| SplitBrain.Orchestrator | `Orchestrator.NodeWorker` | ✅ Exists |
| SplitBrain.Worker | `NodeClient.Worker` | ✅ Exists |
| (new) NodeClient.Ollama | `NodeClient.Ollama` | ✅ Exists |
| (new) NodeClient.Copilot | `NodeClient.Copilot` | ✅ Exists |
| Tests | `Orchestrator.Tests` | ✅ Exists (NUnit) — **221 tests, all passing** |
| (meta) | `SplitBrain.Meta` | ✅ Doc-only, net8.0, no code |

## Dashboard — Current State
- **Pages (8):** Home (`/`), Nodes (`/nodes`), Models (`/models`), Tasks (`/tasks`), Logs (`/logs`), Metrics (`/metrics`), Settings (`/settings`), NotFound
- **CSS:** Live dark theme in `SplitBrain.Dashboard/wwwroot/app.css` using `sb-` CSS prefix and custom palette (bg `#0f1117`, accent `#6366f1`)
- **Charting:** `Blazor-ApexCharts 6.1.0` — ✅ **installed and wired**. Metrics page has latency-over-time (line) + tokens/sec (bar) charts. Home page has per-node **ApexCharts RadialBar VRAM gauges** (green ≤70%, yellow ≤90%, red >90%). `VramGaugePoint(decimal Pct)` model added to `MetricsChartModels.cs`.
- **Log viewer:** `<Virtualize>` applied — fixed-height rows (min-height: 24px), container height: 65vh.
- **SignalR hub:** `DashboardHub` with `IDashboardClient` strongly-typed interface — wired.

## Node Factory — Resolved ✅
- `InferenceNodeFactory.cs` is fully implemented — delegates to `Func<NodeConfiguration, IInferenceNode>` registered in DI.
- Both `SplitBrain.Dashboard/Program.cs` and `Orchestrator.Mcp/Program.cs` use a `config.Provider switch` factory lambda (Ollama → keyed `OllamaInferenceNode`, Worker → keyed `WorkerInferenceNode`, CopilotSdk → `NodeCInferenceNode` singleton).
- `OllamaInferenceNode` — config-driven node created from `NodeConfiguration`, one keyed singleton per topology entry. Replaces hardcoded NodeA/B for new topology entries.

## RoutingService — Refactored ✅
- **Primary constructor:** `RoutingService(INodeRegistry, Func<string,IInferenceQueue>, ILogger, INodeHealthCache?, IOptions<RoutingOptions>?)` — registry-driven, no hardcoded node IDs.
- **Compat constructor** wraps old (NodeA/NodeB/NodeC) signature; both `Program.cs` files updated to use new path.
- **Role-based scoring** (§6.2): `NodeRole.Fast/Deep/Hybrid/Standby` drives `roleFit` weight — replaces hardcoded NodeId strings.
- **Hard rules** (§6.3): Autocomplete always routes to Fast; overloaded queues (>2 pending) trigger fallback.
- **`RoutingServiceRegistryTests`** — 16 NUnit tests: null guards, empty registry, hard rules, scoring, health-cache skip, CopilotSdk VRAM, fallback chain, queue overload, large-context, cancellation.

## Settings Page — Topology Management ✅
- Full node CRUD: add/edit/remove nodes with provider-specific forms (Ollama URL+model, Worker URL, CopilotSdk).
- Fallback chain editor: ordered list per node.
- View-model `NodeForm` with `ToConfig()`/`From()` helpers; seeded from `IOptionsSnapshot<RoutingOptions>`.
- Save flow now persists **both** `nodes.json` (topology) and `routing.json` (fallback chains) with reload-on-change enabled.

## Remaining Gaps
- Legacy `NodeAInferenceNode`/`NodeBInferenceNode` singletons still present in both `Program.cs` files (compat ctor). Remove once topology is fully registry-driven.
- Settings persistence is file-based (`nodes.json` + `routing.json`) and currently does not include schema migration/versioning.

## Infrastructure Files Present
`NodeRegistry.cs`, `NodeHealthCheckService.cs`, `RoutingService.cs`, `FallbackChainResolver.cs`, `NodeQueue.cs`, `LiteDbAgentEventLog.cs`, `InMemoryMetricsCollector.cs`, `InMemoryModelRegistry.cs`, `InMemoryNodeHealthCache.cs`, `FileLoggingService.cs`, `PromptHistoryService.cs`
## Recent Changes (session 11 — June 2026)
- **Warning-free build:** Fixed CS8424, CA2024, NU1510, CS0105 across 7 files — `dotnet build` now produces 0 warnings.
- **MemorySmith wiki deployment:** Set up wiki service (port 6769, service name `SplitBrain.AI Wiki`) with deploy scripts in `scripts/` (`Deploy-WikiService.ps1`, `Publish-WikiEngine.ps1`, `Stop-WikiService.ps1`, `Get-WikiServiceStatus.ps1`, `Remove-WikiService.ps1`).
- **Code search integration:** Created `data/codesearch-config.json` indexing all `src/` projects + scripts. Warmup script at `scripts/Warm-CodeSearchIndex.ps1`.
- **Wiki content:** Created `data/Pages/index.md` as MCP server wiki hub; full `data/` directory layout (Events/, Keys/, Models/, Tasks/, Tags/, .history/).
- **NodeCInferenceNode.cs** — restored from base64 corruption, replaced collection expression initializers with `List<T>`.
- **IdempotencyHelper.cs** — adapted to current `IIdempotencyCache` API (GetAsync/SetAsync instead of TryReserve/UpdateAsync).
- **Disambiguated InferenceNodeFactory** — fully-qualified to `Orchestrator.Infrastructure.Registry.InferenceNodeFactory` in both Dashboard and MCP program files.
## Recent Changes (session 3 — June 2026)
- Added `RoutingOptionsPersistence` service in `Orchestrator.Infrastructure/Configuration` to persist fallback chains into `routing.json` under the `Routing` section.
- `SplitBrain.Dashboard/Program.cs` now loads `routing.json` (base directory + relative) with `reloadOnChange: true` and registers `RoutingOptionsPersistence` in DI.
- `Settings.razor` save flow now writes both topology and fallback chains: `NodeRegistry.SaveTopologyAsync()` + `RoutingPersistence.SaveFallbackChainsAsync(_fallbackChains)`.
- Added `RoutingOptionsPersistenceTests` (3 NUnit tests): null guard, routing JSON shape, sanitization (self-reference/whitespace/duplicates).
- **Total tests: 221, all passing.**

## Recent Changes (session 2 — June 2026)
- `Home.razor` — VRAM `<progress>` replaced with per-node `ApexChart<VramGaugePoint>` RadialBar gauges; `BuildVramGaugeOptions(color)` static factory; color thresholds green/yellow/red.
- `MetricsChartModels.cs` — `VramGaugePoint(decimal Pct)` record added.
- `app.css` — `.sb-vram-gauge-wrap`, `.sb-vram-label` added; `.sb-btn`, `.sb-form`, `.sb-panel`, `.sb-chain-*` topology UI styles added.
- `RoutingService.cs` — refactored to `INodeRegistry` primary ctor + role-based scoring; compat ctor retained.
- `Settings.razor` — rewritten (601 lines): full topology CRUD + fallback chain editor.
- `RoutingServiceRegistryTests.cs` — 16 NUnit tests (registry path).
- **Total tests: 218, all passing.**

## Previous Changes (session 1 — June 2026)
- `OllamaInferenceNode` — config-driven, constructor `(string nodeId, OllamaProviderConfig, IOllamaClient, ILogger<OllamaInferenceNode>)`
- `MetricsChartModels.cs` — `LatencyPoint`, `ThroughputPoint`, `NodeSeries<T>` for ApexCharts
- `Metrics.razor` — latency line chart + throughput bar chart using ApexCharts dark theme
- `Logs.razor` — `@foreach` replaced with `<Virtualize OverscanCount="20">`
- `OllamaInferenceNodeTests.cs` — 11 NUnit tests, all passing

## Recent Commits (as of May 2026)
- `e72858c` — SK planner, metrics, alerts, dashboard upgrades
- `0563208` — Remote worker node support via HTTP relay
- `27ef391` — Initial Blazor dashboard and telemetry
