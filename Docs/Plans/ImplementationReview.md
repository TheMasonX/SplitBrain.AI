# SplitBrain.AI — Implementation Review

> Reviewed against `Plans/MasterPlan.md` (Final Technical Specification)
> Date: 2025-07-13

---

## 1. Phase Completion Status

| Phase | Description | Status |
|-------|-------------|--------|
| **Phase 1** | Foundation (MCP server, node clients, routing, queues, logging) | ✅ Mostly complete |
| **Phase 2** | Stability (model tuning, fallback handling, dashboard) | ⚠️ Partial — fallback logic exists, dashboard absent |
| **Phase 3** | Agents (state machine, controlled loop, code patching) | ✅ Implemented |
| **Phase 4** | Validation (Playwright, FlaUI, UI serializer) | ❌ Not started |
| **§15 Deployment** | PS1 scripts, CI workflow, publish profiles | ✅ Complete |

---

## 2. What Is Implemented — Positive Findings

- **All core interfaces and models** are faithfully translated from the spec (`IInferenceNode`, `IRoutingService`, `IInferenceQueue`, `INodeHealthCache`, `IMetricsCollector`, `IPromptHistory`).
- **Hard routing rules §6.3** are all implemented in `RoutingService.SelectNode`: Autocomplete → Node A, context threshold, Node B queue depth threshold, Node B unavailability fallback.
- **Scoring function §6.2** coefficients (`0.35`, `0.25`, `0.20`, `0.10`, `0.10`) match the spec exactly in `ScoringFunctionTests` and the routing service.
- **Agent state machine §9.2** (`INIT → PLAN → IMPLEMENT → REVIEW → TEST → DONE | FAIL`) is fully implemented in `AgentOrchestrator`.
- **Agent abort conditions §9.3** are all checked: max iterations (4), token budget (12k), consecutive failures (2), no diff produced, identical repeated diff.
- **Agent role→node mapping §9.4** is respected: Architect/Coder → `TaskType.Chat`/`Refactor` (Node A preferred), Reviewer/Tester → `TaskType.Review`/`TestGeneration` (Node B preferred).
- **ProcessCodeSandbox §13** enforces: isolated child process, restricted working directory, 30-second hard kill timeout, no shell injection (`UseShellExecute = false`).
- **NodeB fallback model** (`deepseek-coder:6.7b-instruct-q4_K_M`) is wired in `NodeBInferenceNode` and toggled via `InferenceRequest.UseFallback`.
- **Heartbeat interval** (2 seconds) matches §5.3 exactly in `NodeWorkerService`.
- **MCP tool surface §8** covers 6 of 7 tools: `review_code`, `refactor_code`, `generate_tests`, `search_codebase`, `apply_patch`, `run_tests`, plus `agent_task`.
- **Publish profiles** (`NodeA.pubxml`, `NodeB.pubxml`) exist for both nodes.
- **Per-node appsettings** (`appsettings.NodeA.json`) and the base `appsettings.json` are present.

---

## 3. Bugs and Defects

### 3.1 NodeQueue Priority Is Not Enforced

**Severity: Medium**

`NodeQueue` is backed by a single `System.Threading.Channels.Channel<InferenceQueueItem>` which is FIFO. The `QueuePriority` constants (`High = 10`, `Normal = 50`, `Low = 90`) and the `Priority` field on `InferenceRequest` exist but are **never used for ordering** — a low-priority item enqueued first will always be dequeued before a high-priority item enqueued second.

**Fix:** Replace the single channel with a `PriorityQueue<InferenceQueueItem, int>` protected by a `SemaphoreSlim`, or use two channels (high/normal) and drain high-priority first.

---

### 3.2 Node B Health Is Invisible to Node A's RoutingService

**Severity: High**

`NodeWorkerService` runs on **Node B** and writes to a local `INodeHealthCache`. Node A's `RoutingService` reads from **its own** `INodeHealthCache` instance in-process — which is **never populated with Node B health data**.

The `NodeWorker.Program.cs` does expose a `/health` HTTP endpoint on `0.0.0.0:5100`, and `appsettings.NodeA.json` references `NodeWorkerB.BaseUrl = "http://192.168.1.101:5050"`, but nothing in the MCP process polls that endpoint or writes the result into its local health cache.

**Consequence:** `RoutingService.SelectNode` will always find `null` for Node B in the cache, meaning the Node B `Unavailable` check never triggers from real health data.

**Fix:** Add a background `HealthPollingService` to `Orchestrator.Mcp` that periodically calls `GET /health` on Node B and updates the local `INodeHealthCache`. The port in `appsettings.NodeA.json` should also be reconciled (5050 vs the NodeWorker's configured 5100).

---

### 3.3 NodeWorker Port Mismatch

**Severity: Low**

`Orchestrator.NodeWorker/appsettings.json` binds to port `5100` (`"Urls": "http://0.0.0.0:5100"`), but `Orchestrator.Mcp/appsettings.NodeA.json` references Node B at `http://192.168.1.101:5050`. These must match for the `/health` and `/inference` endpoints to be reachable.

---

### 3.4 ~~Node A Timeout Override Is Too Aggressive~~ ✅ Resolved

`appsettings.NodeA.json` `OllamaNodeB.TimeoutSeconds` corrected from 60 s to 120 s, matching the spec §15.3 and the base `appsettings.json` default.

---

## 4. Gaps Against the Specification

### 4.1 `query_ui` MCP Tool Missing

**§8** lists `query_ui` as part of the canonical MCP tool surface. It is not implemented. This is blocked by Phase 4 (Playwright/FlaUI), but there is no stub or `NotImplementedException` placeholder to signal the gap.

---

### 4.2 `nomic-embed-text` Not Integrated

**§3.3** marks embeddings as *Required* with `nomic-embed-text` on Node A. No embedding pipeline, vector index, or embedding client exists in the codebase. The `search_codebase` tool routes a file-content dump through the LLM instead, which will not scale beyond small codebases.

**Suggestion:** Add an `IEmbeddingClient` abstraction, implement an `OllamaEmbeddingClient` calling `/api/embeddings`, and replace the file-dump prompt in `SearchCodebaseTool` with an embed→cosine-similarity pipeline.

---

### 4.3 Streaming Not Exposed to MCP Callers

**§8.2** requires streaming responses. `OllamaClient.ExecuteAsync` does consume the Ollama streaming protocol and concatenates chunks into a single string, but all MCP tools return `Task<string>` — there is no chunked or SSE delivery to the MCP client. The `ModelContextProtocol` SDK supports streaming tool responses; this should be revisited once the MVP is stable.

---

### 4.4 ~~Deployment Scripts and CI Workflow Absent~~ ✅ Resolved

**§15** specifies:
- `deploy/setup-node-a.ps1` — ✅ exists, idempotent
- `deploy/setup-node-b.ps1` — ✅ exists, idempotent
- `.github/workflows/ci.yml` — ✅ created; builds, tests (TRX upload), publishes both nodes

> **Open note:** `setup-node-a.ps1` registers the MCP server as a Windows service, but the server uses `WithStdioServerTransport()`. Windows services have no interactive stdio, so MCP clients cannot connect to it via that service handle — they must launch the binary directly. The service registration is harmless but non-functional for MCP stdio. Consider switching to HTTP/SSE transport if background service operation is needed in future.

---

### 4.5 Metrics and Prompt History Have No Query Surface

`IMetricsCollector` and `IPromptHistory` are populated but there is no HTTP endpoint, MCP tool, or dashboard to read them. **§11.2** requires a node health dashboard and failure replay capability. `InMemoryMetricsCollector` also has no persistence — a process restart drops all history.

---

### 4.6 NodeBInferenceNode Used Directly in MCP Process

`Orchestrator.Mcp/Program.cs` instantiates `NodeBInferenceNode` and calls Node B's Ollama **directly** over HTTP from Node A. `Orchestrator.NodeWorker` also exposes a `/inference` endpoint. These are two separate execution paths for Node B — the routing service never calls the NodeWorker's `/inference` endpoint. This creates an architectural ambiguity:

- Which path is authoritative for Node B inference from Node A?
- The NodeWorker's `/inference` endpoint exists but is unreachable from the routing layer.

**Suggestion:** Decide whether Node A calls Ollama on Node B directly (current path), or routes through the NodeWorker HTTP API (the endpoint exists but is unused). Document and remove the dead path.

---

## 5. Open Questions

| # | Question | Relevant Section |
|---|----------|-----------------|
| Q1 | Should Node A call Node B's Ollama directly, or proxy through the NodeWorker `/inference` endpoint? The NodeWorker endpoint is unreachable from the routing layer today. | §5.1, §5.2 |
| Q2 | The `nomic-embed-text` embedding model is marked *Required* in §3.3. What is the target integration point — `SearchCodebaseTool`, a new `EmbedTool`, or both? | §3.3, §8 |
| Q3 | `InferenceRequest.UseFallback` is a caller-set boolean. Who decides to set it — the router on primary failure, or the caller explicitly? There is no automatic retry-with-fallback logic in `RoutingService`. | §3.2, §12 |
| Q4 | Agent tests are generated and run in the sandbox, but the generated test code is never written to disk before `dotnet test` is called. How is the sandbox test run expected to find the generated tests? | §9, §13 |
| Q5 | `ReviewApproves` checks for the literal string `"APPROVED"` in the model response. Is this prompt contract stable across Qwen model versions and temperatures? A structured JSON response (`{"approved": true}`) would be more robust. | §9.2 |
| Q6 | `InMemoryMetricsCollector` and `PromptHistoryService` are in-memory ring buffers. Is data loss on process restart acceptable, or should these be backed by SQLite / a local file? | §11 |
| Q7 | The NodeWorker exposes `/inference` on port 5100. Should this endpoint be authenticated (e.g. bearer token or mTLS) given it accepts arbitrary inference requests from the LAN? | §13 |
| Q8 | `MaxTokensPerLoop = 12_000` is enforced using a `Length / 4` estimate. Should actual token counts from the Ollama response be used instead once the API surface supports it? | §9.3 |

---

## 6. Minor Code Observations

- **`OllamaClient` sets `_http.Timeout` in the constructor** — this overrides the `HttpClientFactory`-managed client's timeout and may conflict when the same factory client is reused for different nodes with different timeout requirements.
- **`NodeAInferenceNode.GetHealthAsync`** does not populate `QueueDepth` or `AvailableVramMb` in the returned `NodeHealth` — both are left at `0`. The scoring function reads `AvailableVramMb` from health data, so Node A always scores as if it has zero available VRAM.
- **`InferenceQueueItem.TaskId`** is generated with `Guid.NewGuid()` but never correlated in structured log output. Adding it to log scopes would improve traceability.
- **`PromptHistoryService` capacity** is hardcoded at 50; `InMemoryMetricsCollector` at 1,000. Both should be configurable via `IOptions`.
- **`NodeBRetryTests.cs`** test file exists, suggesting retry logic was planned for `NodeBInferenceNode`, but the retry path in the production code is not visible — worth confirming test coverage is complete.

---

## 7. Suggested Next Steps (Priority Order)

1. **Fix Node B health propagation** (§3.2 above) — add `HealthPollingService` to MCP; reconcile port 5050/5100.
2. **Implement priority-ordered dequeue** in `NodeQueue` (§3.1).
3. **Add `nomic-embed-text` embedding pipeline** and wire it into `SearchCodebaseTool`.
4. **Expose metrics/history** via a `/metrics` endpoint in NodeWorker and a read-only MCP tool.
5. **Clarify the Node B call path** (direct Ollama vs. NodeWorker proxy) and remove the dead path.
6. **Add a `query_ui` stub** and plan Phase 4 (Playwright + FlaUI).
7. **Replace `ReviewApproves` heuristic** with a structured JSON approval response.
