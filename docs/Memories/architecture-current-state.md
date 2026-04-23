# Architecture — Current State

Last updated: May 2026

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
| Tests | `Orchestrator.Tests` | ✅ Exists (NUnit) |
| (meta) | `SplitBrain.Meta` | ✅ Doc-only, net8.0, no code |

## Dashboard — Current State
- **Pages (8):** Home (`/`), Nodes (`/nodes`), Models (`/models`), Tasks (`/tasks`), Logs (`/logs`), Metrics (`/metrics`), Settings (`/settings`), NotFound
- **CSS:** Live dark theme in `SplitBrain.Dashboard/wwwroot/app.css` using `sb-` CSS prefix and custom palette (bg `#0f1117`, accent `#6366f1`)
- **Charting:** `Blazor-ApexCharts` selected (MIT) — **not yet installed**. `SplitBrain.Dashboard.csproj` has no ApexCharts reference yet.
- **Log viewer:** Functional `@foreach` loop. Virtualization not yet applied.
- **SignalR hub:** `DashboardHub` with `IDashboardClient` strongly-typed interface — wired.

## Critical Gap
- **`Orchestrator.Infrastructure/InferenceNodeFactory.cs` is an empty file.** This is the single most important unimplemented piece. Without it, node creation from `nodes.json` is not dynamic — nodes must be hardcoded. Implementing this is the first priority for Phase 3.

## Infrastructure Files Present
`NodeRegistry.cs`, `NodeHealthCheckService.cs`, `RoutingService.cs`, `FallbackChainResolver.cs`, `NodeQueue.cs`, `LiteDbAgentEventLog.cs`, `InMemoryMetricsCollector.cs`, `InMemoryModelRegistry.cs`, `InMemoryNodeHealthCache.cs`, `FileLoggingService.cs`, `PromptHistoryService.cs`

## Recent Commits (as of May 2026)
- `e72858c` — SK planner, metrics, alerts, dashboard upgrades
- `0563208` — Remote worker node support via HTTP relay
- `27ef391` — Initial Blazor dashboard and telemetry
