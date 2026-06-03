# Solution Layout

**Solution file:** `Orchestrator.slnx` (VS 2026 slnx format, at repo root)

> **Naming note:** The plan documents use `SplitBrain.*` names conceptually; actual projects use `Orchestrator.*` and `NodeClient.*` prefixes (with `SplitBrain.Dashboard` and `SplitBrain.Meta` as exceptions).

## Project Map

| Plan Name | Actual Project | Description |
|-----------|---------------|-------------|
| SplitBrain.Core | `Orchestrator.Core` | Models, interfaces, configs (FluentValidation) |
| SplitBrain.Networking | `Orchestrator.Infrastructure` | Registry, routing, health, metrics, queue, storage |
| SplitBrain.Agents | `Orchestrator.Agents` | Bounded deterministic agent engine |
| SplitBrain.MCP | `Orchestrator.Mcp` | MCP server (HTTP + stdio), all tools |
| SplitBrain.Orchestrator | `Orchestrator.NodeWorker` | HTTP inference worker (Machine B) |
| SplitBrain.Dashboard | `SplitBrain.Dashboard` | Blazor Server dark-theme UI |
| (new) | `Orchestrator.Hosting` | Host bootstrap / shared startup |
| SplitBrain.Worker | `NodeClient.Worker` | HTTP relay to remote NodeWorker |
| (new) | `NodeClient.Ollama` | Ollama transport |
| (new) | `NodeClient.Copilot` | GitHub Copilot SDK transport |
| (new) | `NodeClient.LlamaCpp` | llama.cpp OAI-compatible transport |
| Tests | `Orchestrator.Tests` | NUnit test suite (221+ tests) |
| (meta) | `SplitBrain.Meta` | Doc-only, net8.0, no code |

## Key Interfaces (Orchestrator.Core)

```csharp
// The universal inference abstraction
IInferenceNode
  NodeId       // string
  Provider     // NodeProviderType (Ollama | CopilotSdk | Worker | LlamaCpp)
  Health       // NodeHealthStatus (Healthy | Degraded | Unavailable)
  Capabilities // NodeCapabilities (VramMb, SupportsStreaming)
  ExecuteAsync(InferenceRequest) → InferenceResult
  StreamAsync(InferenceRequest) → IAsyncEnumerable<InferenceChunk>
  GetHealthAsync() → NodeHealthStatus
  ListModelsAsync() → IReadOnlyList<ModelInfo>

// Routing
IRoutingService.RouteAsync(TaskType, InferenceRequest) → InferenceResult
INodeRegistry  — hot-reloadable from nodes.json (IOptionsMonitor)
IInferenceQueue — per-node queuing

// Storage
IAgentEventLog — event store (LiteDB → SQLite planned)
IPromptHistory — prompt history
```

## Storage

| Data | Format | Location |
|------|--------|----------|
| Node topology | JSON | `nodes.json` (IOptionsMonitor hot-reload) |
| Fallback chains | JSON | `routing.json` |
| Agent event log | LiteDB (→ SQLite) | `agent-events.db` |
| Security/audit | SQLite (planned) | `audit.db` |
| Code search index | SQLite (runtime, gitignored) | `data/Graph/code-search/code-search.db` |

## NuGet Package Notes

- `Orchestrator.Mcp`: uses `ModelContextProtocol` NuGet package (decision D1 — do not replace with hand-rolled controller)
- `NodeClient.Copilot`: pulls `Azure.Identity` + `Azure.Security.KeyVault.Secrets` (reason D2 keeps NodeClients separate)
- `NodeClient.LlamaCpp`: only `Microsoft.Extensions.Logging.Abstractions` + `Microsoft.Extensions.Options`
- `Orchestrator.Infrastructure`: currently uses `LiteDB` (D3: migrate to SQLite)
- Tests: `Blazor-ApexCharts 6.1.0` (dashboard charting)
