# SplitBrain.AI — Implementation Progress

> Tracks actual build status against `MasterPlan.md`.
> Original spec is never modified — all status lives here.
> Last updated: 2025-07-15

---

## Legend

| Symbol | Meaning                     |
| ------ | --------------------------- |
| ✅      | Complete                    |
| 🔄      | In Progress                 |
| ⬜      | Not Started                 |
| ❌      | Blocked / Needs Decision    |

---

## Phase 0 — Deployment & Automation

> Goal: Both nodes provisionable from scratch, CI produces deployable artifacts, no manual .NET/Ollama setup required.

### 0.1 Node Provisioning Scripts

| Task | Status | Notes |
| ---- | ------ | ----- |
| `deploy/setup-node-a.ps1` | ✅ | Idempotent; installs .NET SDK, Ollama, env vars, pulls models, registers Windows service |
| `deploy/setup-node-b.ps1` | ✅ | Idempotent; Runtime only, flash attention off, Pascal-safe env, fallback model pull |

### 0.2 Publish Profiles

| Task | Status | Notes |
| ---- | ------ | ----- |
| `NodeA.pubxml` → `Orchestrator.Mcp` | ✅ | win-x64, self-contained, single-file, ReadyToRun → `publish/node-a/` |
| `NodeB.pubxml` → `Orchestrator.NodeWorker` | ✅ | win-x64, self-contained, single-file, ReadyToRun → `publish/node-b/` |

### 0.3 Per-Node Configuration

| Task | Status | Notes |
| ---- | ------ | ----- |
| `OllamaClientOptions` extracted | ✅ | `BaseUrl` + `TimeoutSeconds`; replaces hardcoded `localhost:11434` |
| `appsettings.json` — `Orchestrator.Mcp` | ✅ | Shared defaults |
| `appsettings.NodeA.json` — `Orchestrator.Mcp` | ✅ | Node A endpoint, 30s timeout |
| `appsettings.json` — `Orchestrator.NodeWorker` | ✅ | Shared defaults |
| `appsettings.NodeB.json` — `Orchestrator.NodeWorker` | ✅ | Node B endpoint, 120s timeout |

### 0.4 Node Worker Project (Node B host)

| Task | Status | Notes |
| ---- | ------ | ----- |
| `Orchestrator.NodeWorker` project created | ✅ | Worker SDK, added to solution |
| `NodeWorkerService` background service | ✅ | 2s heartbeat loop, logs health to structured sink |
| `NodeBInferenceNode` | ✅ | `q5_K_M` primary, `deepseek-coder q4_K_M` fallback, flash attention off |

### 0.5 CI Workflow

| Task | Status | Notes |
| ---- | ------ | ----- |
| `.github/workflows/ci.yml` | ✅ | 3 jobs: `build-test` → `publish-node-a` + `publish-node-b` |
| Test results uploaded as artifact | ✅ | TRX, 14-day retention |
| Node A artifact uploaded | ✅ | `node-a-mcp-server`, 30-day retention |
| Node B artifact uploaded | ✅ | `node-b-worker`, 30-day retention |
| Automatic deployment | ⬜ | Intentionally deferred — manual deploy via setup scripts |

### 0.6 Documentation

| Task | Status | Notes |
| ---- | ------ | ----- |
| Section 15 added to `MasterPlan.md` | ✅ | §15.1–15.5; original §1–14 untouched |
| `ProgressPlan.md` updated with Phase 0 | ✅ | This file |

---

## Phase 1 — Foundation

> Goal: MCP server online, both node clients wired, routing live, logging + queues in place.

### 1.1 Solution & Project Structure

| Task                                      | Status | Notes                                        |
| ----------------------------------------- | ------ | -------------------------------------------- |
| Create solution `SplitBrain.AI.sln`       | ✅      |                                              |
| `Orchestrator.Core` project               | ✅      | Interfaces, models, enums, validation        |
| `Orchestrator.Infrastructure` project     | ✅      | `RoutingService` (Node A only, Phase 1 stub) |
| `NodeClient.Ollama` project               | ✅      | `OllamaClient`, `IOllamaClient`, `NodeAInferenceNode` |
| `Orchestrator.Mcp` project                | ✅      | MCP server host, `ReviewCodeTool`            |
| `Orchestrator.Tests` project              | ✅      | xunit + NSubstitute + FluentAssertions; 21 tests passing |
| All projects added to solution            | ✅      |                                              |
| Project references wired                  | ✅      |                                              |

---

### 1.2 Core Domain (`Orchestrator.Core`)

| Task                                              | Status | Notes                                             |
| ------------------------------------------------- | ------ | ------------------------------------------------- |
| `IInferenceNode` interface                        | ✅      | `ExecuteAsync`, `GetHealthAsync`, `Capabilities`  |
| `IRoutingService` interface                       | ✅      |                                                   |
| `IMcpRequest` / `IMcpResponse` interfaces         | ✅      |                                                   |
| `InferenceRequest` / `InferenceResult` records    | ✅      | Converted to `record` for `with` expression support |
| `NodeCapabilities` / `NodeHealth` models          | ✅      |                                                   |
| `TaskType` / `NodeStatus` enums                   | ✅      | All 6 task types from spec                        |
| `ReviewCodeRequest` / `ReviewCodeResponse` models | ✅      | `ReviewCodeRequest` is a `record`                 |
| `ReviewCodeRequestValidator` (FluentValidation)   | ✅      | Validates code, language, focus enum values       |
| `ValidationExtensions.ValidateOrThrow`            | ✅      |                                                   |
| `JsonConfig` (AOT-safe serialization context)     | ✅      |                                                   |
| `Meta` / `McpError` / `Diff` models               | ✅      |                                                   |

---

### 1.3 Node Clients (`NodeClient.Ollama`)

| Task                                        | Status | Notes                                                   |
| ------------------------------------------- | ------ | ------------------------------------------------------- |
| `IOllamaClient` interface                   | ✅      | Enables mocking / future swap                           |
| `OllamaClient` (implements `IOllamaClient`) | ✅      | Streaming + non-streaming via `/api/generate`           |
| `NodeAInferenceNode` (Node A, RTX 5060)     | ✅      | Forces `q4_K_M` model, measures latency                 |
| `NodeBInferenceNode` (Node B, GTX 1080)     | ✅      | `q5_K_M` primary, `deepseek-coder` fallback, flash attention off |
| Node B configurable endpoint (not localhost) | ✅     | `OllamaClientOptions.BaseUrl` via appsettings / env var |
| `OllamaClientOptions` (base URL, timeout)   | ✅      | Bound via `IOptions<OllamaClientOptions>` |

---

### 1.4 Routing (`Orchestrator.Infrastructure`)

| Task                                         | Status | Notes                                                    |
| -------------------------------------------- | ------ | -------------------------------------------------------- |
| `RoutingService`                             | ✅      | §6.3 hard routing rules: Autocomplete→A, context>5k→B, deep tasks→B, queue>2→fallback A |
| Scoring function (§6.2)                      | ✅      | Phase 2 complete — 5-component weighted formula          |
| Hard routing rules (§6.3)                   | ✅      | Autocomplete→A; context>5k→B; queue>2→fallback A; deep tasks→B |
| Node B fallback logic                        | ✅      | `RoutingService` falls back to Node A on full queue or unavailable Node B |

---

### 1.5 MCP Server (`Orchestrator.Mcp`)

| Task                               | Status | Notes                                            |
| ---------------------------------- | ------ | ------------------------------------------------ |
| `Program.cs` host wiring           | ✅      | Node A + Node B clients, keyed queues, `RoutingService` wired |
| `review_code` MCP tool             | ✅      | Validates input, builds prompt, routes, returns `ReviewCodeResponse` |
| `refactor_code` MCP tool           | ✅      | Returns `RefactorCodeResponse` with Meta; FluentValidation added |
| `generate_tests` MCP tool          | ✅      | Returns `GenerateTestsResponse` with Files list and Meta |
| `run_tests` MCP tool               | ✅      | Returns `RunTestsResponse`; parses passed/failed/skipped counts + failures |
| `search_codebase` MCP tool         | ✅      | Returns `SearchCodebaseResponse` with Results list and Meta |
| `apply_patch` MCP tool             | ✅      | Managed `UnifiedDiffApplier` (no `patch` CLI); returns `ApplyPatchResponse` |
| `query_ui` MCP tool                | ⬜      | Phase 4                                          |
| Streaming response support         | ⬜      | `OllamaClient` streams internally; MCP layer needs SSE wiring |
| Cancellation propagation           | ✅      | `CancellationToken` threaded through all layers  |
| Structured JSON output             | ✅      | All 6 tools return typed `IMcpResponse` objects with `Meta` |

---

### 1.6 Logging & Queues

| Task                                   | Status | Notes                                            |
| -------------------------------------- | ------ | ------------------------------------------------ |
| Structured log model (§11.1)           | ✅      | `ILogger<T>` in `RoutingService`, `NodeAInferenceNode`, `NodeBInferenceNode` |
| `Microsoft.Extensions.Logging` wiring  | ✅      | Injected via DI in all routing and node classes  |
| Node A request queue                   | ✅      | `NodeQueue` (`Channel<T>`, bounded cap 64, high priority) |
| Node B request queue                   | ✅      | `NodeQueue` cap 32, backpressure falls back to Node A    |
| Prompt history (last N)                | ✅      | `IPromptHistory` + `PromptHistoryService` — ring buffer cap 50, O(1) Complete |
| Failure replay                         | ⬜      |                                                  |

---

### 1.7 Heartbeat / Health

| Task                                        | Status | Notes                                          |
| ------------------------------------------- | ------ | ---------------------------------------------- |
| `GetHealthAsync` on `IInferenceNode`        | ✅      | Interface defined                              |
| Node A health (real `/api/tags` probe)      | ✅      | Returns `Degraded`/`Unavailable` on failure    |
| Node B health check                         | ✅      | Real `/api/tags` probe via `IOllamaClient.IsHealthyAsync` |
| Heartbeat loop (every 2s per §5.3)          | ✅      | `NodeWorkerService` — consecutive failure counter → `Unavailable` after 5 |
| Node B HTTP transport (`/health`, `/inference`) | ✅  | `NodeWorkerService` exposes minimal API on port 5050 |
| Health-based routing influence              | ✅      | `INodeHealthCache` feeds `ComputeScore` in `RoutingService` (Phase 2) |

---

### 1.8 Tests (`Orchestrator.Tests`)

| Task                              | Status | Notes                                            |
| --------------------------------- | ------ | ------------------------------------------------ |
| `RoutingServiceTests` (3 tests)   | ✅      |                                                  |
| `NodeAInferenceNodeTests` (4 tests)| ✅     |                                                  |
| `ReviewCodeRequestValidatorTests` (14 tests) | ✅ |                                             |
| `NodeBInferenceNodeTests`         | ✅      | 5 tests — NodeId, capabilities, primary/fallback model, cancellation |
| `NodeWorkerServiceTests`          | ✅      | 3 tests — heartbeat, transient error resilience, clean stop |
| `ScoringFunctionTests`            | ✅      | 5 tests — hard rules §6.3, scoring §6.2, queue threshold, null Node B |
| `PromptHistoryTests`              | ✅      | 6 tests — add, complete, eviction, count limit, ordering |
| `MetricsCollectorTests`           | ✅      | 5 tests — record, aggregate summary, ring buffer cap |
| `NodeBRetryTests`                 | ✅      | 4 tests — fallback retry §12, cancellation passthrough, no double-retry |
| Integration test: MCP → Routing → OllamaClient | ⬜ |                                          |
| Queue behavior tests              | ⬜      |                                                  |

---

### 1.9 Deployment & CI

| Task                                  | Status | Notes                                                   |
| ------------------------------------- | ------ | ------------------------------------------------------- |
| `.github/workflows/ci.yml`            | ✅      | Build, test, publish Node A + B artifacts on `main`     |
| `NodeA.pubxml` publish profile        | ✅      | `src/Orchestrator.Mcp/Properties/PublishProfiles/`      |
| `NodeB.pubxml` publish profile        | ✅      | `src/Orchestrator.NodeWorker/Properties/PublishProfiles/` |
| `setup-node-a.ps1` deploy script      | ✅      | `deploy/setup-node-a.ps1`                               |
| `setup-node-b.ps1` deploy script      | ✅      | `deploy/setup-node-b.ps1`                               |

---

## Phase 2 — Stability ✅

> Goal: Smart routing, fallback handling, observability dashboard.
> **Completed 2025-07-15** — 54/54 tests passing.

### 2.1 Routing Intelligence

| Task                              | Status | Notes                           |
| --------------------------------- | ------ | ------------------------------- |
| Scoring function implementation   | ✅      | §6.2: 5-component weighted formula (vramRatio, queueFactor, modelFit, latencyPenalty, contextFit) |
| Hard routing rules                | ✅      | §6.3: Autocomplete→A, Node B unavailable→A, queue>2→A |
| Node B client + registration      | ✅      | `NodeBInferenceNode` wired in both `Program.cs` files + keyed queue |
| Node B URL config (`OllamaNodeB`) | ✅      | `appsettings.NodeA.json` has `OllamaNodeB` section |
| Node B fallback on failure/slow   | ✅      | `RoutingService` falls back to Node A when Node B queue full or unavailable |
| `OllamaClientOptions` config      | ✅      | `IOptions<OllamaClientOptions>` bound per project |

### 2.2 Fault Tolerance (§12)

| Task                              | Status | Notes                           |
| --------------------------------- | ------ | ------------------------------- |
| Model crash → retry once with fallback | ✅ | `NodeBInferenceNode.ExecuteAsync` catches non-cancellation exceptions when `!UseFallback`, retries with `deepseek-coder:6.7b-instruct-q4_K_M` |
| `OperationCanceledException` not retried | ✅ | Explicit `when` guard on catch clause |
| `UseFallback=true` not double-retried | ✅ | Guard ensures single retry depth |

### 2.3 Observability (§11.2)

| Task                              | Status | Notes                           |
| --------------------------------- | ------ | ------------------------------- |
| `INodeHealthCache` / `InMemoryNodeHealthCache` | ✅ | `ConcurrentDictionary`-backed; NodeWorkerService pushes on every heartbeat |
| `IMetricsCollector` / `InMemoryMetricsCollector` | ✅ | Ring buffer cap 1000; per-request telemetry (tokens, latency, success); GET /metrics endpoints |
| `IPromptHistory` / `PromptHistoryService` | ✅ | Ring buffer cap 50 + O(1) `Complete(id)` index; wired into `RoutingService.RouteAsync` |
| Token + latency metrics           | ✅      | Recorded in `DrainAsync` on both success and failure paths |
| GET /metrics + GET /metrics/recent endpoints | ✅ | Exposed on NodeWorker WebApplication |
| Structured logging wired          | ✅      | `ILogger<T>` throughout; §11.1 JSON shape via host defaults |
| Failure replay                    | ⬜      | §11.2 stretch goal — deferred to Phase 3 |
| Node health dashboard             | ⬜      | §11.2 — TBD: Blazor / terminal  |

---

## Phase 3 — Agents

> Goal: Bounded autonomous agent loop via Semantic Kernel.

| Task                                     | Status | Notes                               |
| ---------------------------------------- | ------ | ----------------------------------- |
| `Orchestrator.Agents` project            | ⬜      |                                     |
| Semantic Kernel integration              | ⬜      | §9.1                                |
| Agent state machine (INIT→PLAN→…→DONE)   | ⬜      | §9.2                                |
| Max iteration / token guard (4 iters, 12k tokens) | ⬜ | §9.3                          |
| Abort conditions (no diff, repeated fail) | ⬜     | §9.3                                |
| Role → Node mapping (Architect/Coder=A, Reviewer/Tester=B) | ⬜ | §9.4          |
| `apply_patch` MCP tool                   | ✅      | Implemented in Phase 1 via `UnifiedDiffApplier` |
| Code execution sandbox (isolated process, 30s kill) | ⬜ | §13                      |

---

## Phase 4 — Validation / UI Automation

> Goal: Web + WPF UI automation via Playwright and FlaUI.

| Task                                      | Status | Notes              |
| ----------------------------------------- | ------ | ------------------ |
| `Orchestrator.Ui` project                 | ⬜      |                    |
| Playwright integration (web)              | ⬜      | §10.1              |
| FlaUI integration (WPF)                   | ⬜      | §10.1              |
| UI → semantic JSON serializer             | ⬜      | §10.2              |
| `query_ui` MCP tool                       | ⬜      | §8 tool list       |

---

## Current Status

**Phase 0 ✅ · Phase 1 ✅ · Phase 2 ✅ · Phase 3 ⬜ · Phase 4 ⬜**

- 54 / 54 tests passing (build clean)
- All 6 MCP tools live and returning typed `IMcpResponse` objects
- §6.2 scoring + §6.3 hard rules active in `RoutingService`
- §12 fault tolerance: Node B retries once with fallback model on crash
- §11.2 observability: prompt history, per-request metrics, health cache all wired end-to-end
- Next milestone: **Phase 3** — Semantic Kernel agent loop (`Orchestrator.Agents`)
