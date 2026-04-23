# SplitBrain.AI — Developer Suggestions & Gap Report

> Reviewed against `MasterPlan.md`, `DTOClasses.md`, `McpToolListing.md`, and `ProgressPlan.md`.
> Date: 2026-04-19

---

## Summary

The codebase is well-structured and aligned with the MasterPlan through Phase 1. However, there are several inconsistencies between the plans and the code, missing DTOs, a critical Windows/Unix tool mismatch, and Node B is architecturally wired but never actually reachable from the MCP server.

---

## Critical Issues

### 1. Node B is unreachable from `Orchestrator.Mcp`

`Program.cs` only registers `NodeAInferenceNode` and passes it as the sole node to `RoutingService`. All routing rules that resolve to "→ B" (Review, Refactor, TestGeneration, context >5k tokens) silently fall back to Node A.

**Fix:** Register a second `OllamaClient` with a config-driven Node B URL (e.g. `appsettings.NodeA.json` should include a `OllamaNodeB` section), register `NodeBInferenceNode`, and pass it to `RoutingService`.

---

### 2. `apply_patch` Tool Calls `patch --unified` (Unix CLI)

The deployment target is Windows (`setup-node-a.ps1`, `setup-node-b.ps1`). `patch.exe` is not a Windows built-in.

**Options:**
- Bundle `patch.exe` via `setup-node-a.ps1` (e.g. via `winget` or Chocolatey).
- Replace with a managed .NET diff library such as [DiffPlex](https://github.com/mmanela/diffplex).
- At minimum, document the dependency explicitly and fail fast with a clear error if the binary is absent.

---

### 3. No Remote Node B URL Configured in `Orchestrator.Mcp`

`appsettings.NodeA.json` (in `Orchestrator.Mcp`) only configures Node A's local Ollama endpoint. There is no configuration entry for reaching Node B's Ollama remotely.

**Fix:** Add an `OllamaNodeB` config section in `appsettings.NodeA.json` (within `Orchestrator.Mcp`) with Node B's LAN IP.

---

## Plan Inconsistencies

### 4. Contradictory Routing Status in `ProgressPlan.md`

- Phase 1.4 marks "Hard routing rules §6.3 ✅"
- Phase 2 marks "Hard routing rules §6.3 ⬜"

The routing logic *is* implemented in `RoutingService.cs`. The Phase 2 row is stale.

**Fix:** Remove or mark the Phase 2 "Hard routing rules" row as ✅ in `ProgressPlan.md`.

---

### 5. CI Workflow and Publish Profiles Marked ✅ But Not Found

`ProgressPlan.md` marks `.github/workflows/ci.yml`, `NodeA.pubxml`, and `NodeB.pubxml` as complete, but none of these paths are visible in the workspace.

**Fix:** Verify these files exist and are committed. If not, create them or update `ProgressPlan.md` to reflect the true status.

---

### 6. Structured JSON Output Column — "Other Tools TBD"

`ProgressPlan.md` §1.5 notes structured JSON output as 🔄 for `review_code` and TBD for remaining tools. Currently, `RefactorCodeTool`, `GenerateTestsTool`, `SearchCodebaseTool`, `ApplyPatchTool`, and `RunTestsTool` all return raw `InferenceResult.Text` strings, not typed `IMcpResponse` objects.

---

## Missing DTOs and Validators

DTOClasses.md §16.3–16.6 defines formal C# request/response classes and validators for four tools. None of these exist in `Orchestrator.Core`.

### Missing Types to Add in `Orchestrator.Core/Models/`

| File | Types |
|------|-------|
| `RefactorCode.cs` | `RefactorCodeRequest`, `CodeFile`, `RefactorConstraints`, `RefactorCodeResponse` |
| `ApplyPatch.cs` | `ApplyPatchRequest`, `ApplyPatchResponse` |
| `RunTests.cs` | `RunTestsRequest`, `RunTestsResponse`, `TestFailure`, `TestSummary` |
| `SearchCodebase.cs` | `SearchCodebaseRequest`, `SearchFilters`, `SearchCodebaseResponse`, `SearchResult` |
| `GenerateTests.cs` | `GenerateTestsRequest`, `GenerateTestsResponse`, `GeneratedTestFile` *(omitted from DTOClasses.md — add for consistency)* |

### Missing Validators to Add in `Orchestrator.Core/Validation/`

| File | Validator |
|------|-----------|
| `RefactorCodeRequestValidator.cs` | Goal NotEmpty; Codebase NotEmpty, count ≤ 20; MaxFiles 1–50 |
| `ApplyPatchRequestValidator.cs` | Diff.Files NotEmpty; ChangeType ∈ {modify, create, delete}; Patch NotEmpty when not delete |
| `RunTestsRequestValidator.cs` | ProjectPath NotEmpty; TimeoutSeconds 1–120 |
| `SearchCodebaseRequestValidator.cs` | Query NotEmpty; TopK 1–20 |

---

## Architecture Gaps

### 7. Node A Health Check Always Returns `Healthy`

`NodeAInferenceNode.GetHealthAsync()` returns `Healthy` unconditionally. Health-based routing (Phase 2) cannot function correctly without a real probe.

**Fix:** Make an HTTP call to Ollama's `/api/tags` or `/api/ps` endpoint and return `Degraded`/`Unavailable` on failure or timeout.

---

### 8. Node B Inference Queue Has No Consumer

`IInferenceQueue` for Node B exists in `Orchestrator.Infrastructure` and `RoutingService` enqueues to it, but nothing dequeues. `NodeWorkerService` only runs a health heartbeat — it never processes inference requests.

**Design decision needed:** Choose a transport layer between the MCP server (Node A) and the Node Worker (Node B):
- Lightweight HTTP endpoint exposed by `Orchestrator.NodeWorker`
- Named pipe / gRPC
- Shared queue via a broker (Redis, etc.)

This is the most significant unresolved architectural question before Phase 2 can be called complete.

---

### 9. Semantic Search Not Implemented

`SearchCodebaseTool` reads raw file lines and forwards them to the model as context. MasterPlan §3.3 specifies `nomic-embed-text` embeddings on Node A for semantic vector search. This subsystem (embedding generation, vector store, similarity search) does not exist and needs design before Phase 3 agents depend on it.

---

### 10. `NodeQueue` Uses `DropWrite` on Full — Silent Data Loss

`Channel.CreateBounded` with `BoundedChannelFullMode.DropWrite` silently discards requests when the queue is full. MasterPlan §7.2 says overloaded Node B should trigger rerouting to Node A, not silent loss.

**Fix:** Use `Wait` mode and let the enqueue path time out, or check queue depth before enqueuing and return a structured error/reroute signal to the caller.

---

### 11. `NodeWorkerService` Swallows All Exceptions Indefinitely

The heartbeat loop catches all non-cancellation exceptions and continues. A misconfigured Node B URL will loop forever logging errors with no state transition.

**Fix:** Add a consecutive-failure counter. After N failures (e.g. 5), transition node status to `Unavailable` and reduce heartbeat frequency.

---

## Minor Issues

| # | Location | Issue |
|---|----------|-------|
| 12 | `ReviewCodeRequest` | Defined as `record` in code, `sealed class` in DTOClasses.md. `record` is fine but note value-equality semantics may affect caching. |
| 13 | `OllamaClientOptions` | Config section `"OllamaNode"` is a single instance. If both node clients are ever registered in one DI container, use named `IOptionsSnapshot<T>` to differentiate. |
| 14 | `ProgressPlan.md` | "Last updated: 2025-01-20" is outdated. |

---

## Recommended Implementation Order

1. **Register Node B in `Orchestrator.Mcp/Program.cs`** — prerequisite for all routing to work as designed.
2. **Design and implement Node B transport layer** — HTTP endpoint on `NodeWorkerService` is the simplest path.
3. **Add missing DTOs + validators** (5 tool types) — unblocks structured response work.
4. **Fix `apply_patch` Windows compatibility** — blocks deployment on any clean Windows machine.
5. **Implement real Node A health probe** — prerequisite for Phase 2 health-aware routing.
6. **Fix `NodeQueue` backpressure behaviour** (`DropWrite` → reroute signal).
7. **Add failure state to `NodeWorkerService`** (consecutive failure counter → `Unavailable`).
8. **Verify/commit CI workflow and publish profiles** — or update `ProgressPlan.md` status.
9. **Design semantic search subsystem** — required before Phase 3 agents.
10. **Wire structured `IMcpResponse` types into MCP tool responses** — completes the §1.5 🔄 item.

---

## Files to Modify

| File | Change |
|------|--------|
| [src/Orchestrator.Mcp/Program.cs](../src/Orchestrator.Mcp/Program.cs) | Register Node B client + node |
| [src/Orchestrator.Mcp/appsettings.NodeA.json](../src/Orchestrator.Mcp/appsettings.NodeA.json) | Add Node B remote URL config |
| [src/Orchestrator.Mcp/Tools/ApplyPatchTool.cs](../src/Orchestrator.Mcp/Tools/ApplyPatchTool.cs) | Replace `patch` CLI with managed library |
| [src/Orchestrator.Core/Models/](../src/Orchestrator.Core/Models/) | Add 5 missing DTO files |
| [src/Orchestrator.Core/Validation/](../src/Orchestrator.Core/Validation/) | Add 4 missing validator files |
| [src/NodeClient.Ollama/NodeAInferenceNode.cs](../src/NodeClient.Ollama/NodeAInferenceNode.cs) | Real Ollama health probe |
| [src/Orchestrator.Infrastructure/Queue/NodeQueue.cs](../src/Orchestrator.Infrastructure/Queue/NodeQueue.cs) | Fix `DropWrite` backpressure |
| [src/Orchestrator.NodeWorker/NodeWorkerService.cs](../src/Orchestrator.NodeWorker/NodeWorkerService.cs) | Add failure counter + `Unavailable` state |
| [Plans/ProgressPlan.md](ProgressPlan.md) | Fix contradictory routing status + update date |
