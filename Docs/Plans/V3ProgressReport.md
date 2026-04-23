# SplitBrain.AI — Master Plan V3 Progress Report

> Based on: `MasterPlanV3.md` | Branch: `next-gen` | Last Updated: 2026-04-23
> **179 / 179 tests passing. Build: ✅ Green.**

---

## Structural Deviations from Plan

The master plan specified a `SplitBrain.*` namespace across 9 fine-grained projects. The actual solution uses `Orchestrator.*` as the primary namespace and consolidates several planned projects:

| Planned Project | Actual Location | Rationale |
|---|---|---|
| `SplitBrain.Core` | `Orchestrator.Core` | Renamed; same responsibilities |
| `SplitBrain.Networking` + `SplitBrain.Routing` + `SplitBrain.Models` + `SplitBrain.Validation` | `Orchestrator.Infrastructure` | Consolidated into single library — reduces project overhead for single-team project |
| `SplitBrain.Agents` | `Orchestrator.Agents` | Renamed |
| `SplitBrain.MCP` | `Orchestrator.Mcp` | Renamed |
| `SplitBrain.Observability` | `Orchestrator.Core\Observability\` | Absorbed into Core namespace |
| `SplitBrain.Worker` | `Orchestrator.NodeWorker` | Renamed; partial implementation (see Remaining) |
| `SplitBrain.Dashboard` | `SplitBrain.Dashboard` | Unchanged |

**Architectural correction vs. plan:** Plan Section 3.6 specified `NodeHealthCheckService` depending directly on `IHubContext<DashboardHub, IDashboardClient>`, which would have created a cross-layer dependency from the infrastructure layer into the dashboard. The implementation correctly inverts this: `NodeHealthCheckService` depends only on `INodeHealthPublisher`, with `SignalRNodeHealthPublisher` wiring the SignalR dependency in the dashboard host. This is a deliberate improvement, not merely a deviation.

---

## Phase 1 — Core Infrastructure ✅

| Item | Status | Notes |
|------|--------|-------|
| `IInferenceNode` abstraction | ✅ Done | NodeA/B/C concrete impls |
| `INodeRegistry` + `NodeRegistry` | ✅ Done | Hot-reload from nodes.json |
| `NodeHealthCheckService` | ✅ Done | Per-node semaphore; `INodeHealthPublisher` (plan prescribed `IHubContext<DashboardHub>` directly — corrected to avoid cross-layer dep) |
| `INodeHealthPublisher` abstraction | ✅ Done | Null in MCP host; SignalR in Dashboard |
| `NodeQueue` + `IInferenceQueue` | ✅ Done | Ring buffer; keyed DI |
| `RoutingService` | ✅ Done | Large-ctx routing; queue-depth fallback |
| Polly resilience pipelines | ✅ Done | Retry + CB + timeout per Ollama node |
| `Func<NodeConfiguration, IInferenceNode>` dispatch | ✅ Done | NodeId A/B/C → singleton |

## Phase 2 — MCP Tools ✅

| Item | Status | Notes |
|------|--------|-------|
| All 7 MCP tools | ✅ Done | ReviewCode, Refactor, GenerateTests, SearchCodebase, ApplyPatch, RunTests, AgentTask |
| `IIdempotencyCache` + `InMemoryIdempotencyCache` | ✅ Done | Processing→Completed/Failed; TTL |
| `IdempotencyHelper.ExecuteAsync` | ✅ Done | All inference tools wired |

## Phase 3 — Agent System ✅

| Item | Status | Notes |
|------|--------|-------|
| `AgentOrchestrator` | ✅ Done | INIT→PLAN→IMPLEMENT→REVIEW→TEST→DONE/FAIL; 4-iter / 12K |
| `IAgentEventLog` + `LiteDbAgentEventLog` | ✅ Done | Append-only LiteDB; ordered replay |
| `IAgentEventLog` wired in orchestrator | ✅ Done | AppendAsync at every state transition |
| `ProcessCodeSandbox` | ✅ Done | Bounded dotnet test execution |

## Phase 4 — Semantic Kernel Bridge ✅

| Item | Status |
|------|--------|
| `InferenceNodeChatCompletionService` | ✅ Done |
| `Microsoft.SemanticKernel.Abstractions` 1.54.0 | ✅ Added |
| `IKernelPlannerService` + `KernelPlannerService` | ✅ Done — sequential planner backed by `IRoutingService`; plan-then-execute loop; registered in Mcp + Dashboard |

## Phase 5 — Output Validation ✅

| Validator | Status |
|-----------|--------|
| `ValidationPipeline` | ✅ Done |
| `LengthBoundsValidator` | ✅ Done |
| `RefusalDetector` | ✅ Done |
| `StructuredOutputValidator` | ✅ Done |
| `CodeSyntaxValidator` | ✅ Done |

> **Note:** Plan specified a separate `SplitBrain.Validation` project. Validation was consolidated into `Orchestrator.Core\Validation\` — no separate project was created.

## Phase 6 — Model Registry & Fallback ✅

| Item | Status |
|------|--------|
| `IModelRegistry` + `InMemoryModelRegistry` | ✅ Done |
| DI seeding from config | ✅ Done |
| `FallbackChainResolver` | ✅ Done |

## Phase 7 — Observability ✅

| Item | Status |
|------|--------|
| `Telemetry.cs` (ActivitySource + Meter + 10 instruments) | ✅ Done |
| `ILogEntryPublisher` + `NullLogEntryPublisher` | ✅ Done |
| `SignalRLogEntryPublisher` | ✅ Done |
| `SignalRLogSink` (Serilog) | ✅ Done |
| OpenTelemetry OTLP exporter — `Orchestrator.Mcp` | ✅ Done — traces + metrics; endpoint via `OTEL_EXPORTER_OTLP_ENDPOINT` |
| OpenTelemetry OTLP exporter — `SplitBrain.Dashboard` | ✅ Done — same pattern |

## Phase 8 — Dashboard ✅

| Item | Status |
|------|--------|
| `DashboardHub` (SignalR strongly-typed) | ✅ Done |
| `IDashboardClient` + all DTOs | ✅ Done |
| `DashboardState` (singleton; OnChange + AgentTasks) | ✅ Done — extended with AgentSteps, Metrics, Alerts, TokenUsage |
| `Home.razor`, `Nodes.razor`, `Logs.razor` | ✅ Done |
| `Models.razor` | ✅ Done — model registry table with live VRAM/availability badges |
| `Tasks.razor` | ✅ Done — expandable rows with per-task agent step timeline |
| `Metrics.razor` | ✅ Done — summary cards, per-node breakdown, token usage table, recent requests with node/type filters |
| `Settings.razor` | ✅ Done — live node topology viewer + SaveTopologyAsync |
| `MainLayout.razor` nav | ✅ Done — Metrics link added |
| `SignalRNodeHealthPublisher` | ✅ Fixed — now updates DashboardState |
| `SignalRDashboardPublisher` | ✅ Done — forwards AgentStepEvent, TokenUsageRecord, MetricSnapshot, SystemAlert |
| `IModelRegistry` in Dashboard DI | ✅ Fixed — `InMemoryModelRegistry` was missing from `SplitBrain.Dashboard/Program.cs`; `/models` page threw `InvalidOperationException` on load |
| `app.css` design system | ✅ Done — full `sb-*` dark-theme token system; node cards, badges, tables, alerts, step timeline, metric cards |

## Phase 9 — Deploy Scripts ✅

| Script | Status | Notes |
|--------|--------|-------|
| `deploy\setup-orchestrator.ps1` | ✅ Done | |
| `deploy\setup-worker.ps1` | ✅ Done | |
| `deploy\setup-node-a.ps1` | ✅ Done | Node-A specific deployment config |
| `deploy\setup-node-b.ps1` | ✅ Done | Node-B specific deployment config |

---

## Test Coverage

| Suite | Tests |
|-------|-------|
| AgentOrchestratorTests | 12 |
| RoutingService (scoring + hard rules) | +8 |
| IdempotencyCacheTests | 6 |
| ValidationPipelineTests | 16 |
| ModelRegistryTests | 10 |
| LiteDbAgentEventLogTests | 8 |
| FallbackChainResolverTests | 6 |
| InferenceNodeChatCompletionServiceTests | 5 |
| All other existing tests | 108 |
| **Total** | **179 / 179 ✅** |

---

## Remaining / Nice-to-Have

| Item | Priority | Notes |
|------|----------|-------|
| `Orchestrator.NodeWorker` — gRPC / orchestrator push API | Medium | Current impl has HTTP `/health` endpoint + background health heartbeat only; plan described full gRPC proxy for remote inference hardware |
| Dashboard live-connection from MCP host (fan-out) | Low | MCP host has no SignalR publisher; agents running in Mcp won't push AgentStepEvents to Dashboard |
| GitHub Actions CI (dotnet test + publish on PR) | Low | |
