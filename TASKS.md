# SplitBrain.AI — Task Tracker
<!-- Council-reviewed 2026-06-03. Cross-referenced: live tool evaluation, architecture audit, MCP expansion audit. -->
<!-- Status updated 2026-06-03 post Phase 0+1 implementation. -->

## Status Legend
`[x]` Done | `[ ]` Open | `[~]` In progress | `[!]` Blocking

---

## P0 — Foundation Stabilization · ✅ COMPLETE (Phase 0 PR #18 merged)

### P0.1 · Raw response logging ✅
**Area:** bug/tools
Added `ILoggingService` injection to all LLM-calling tools (RefactorCode, GenerateTests, SearchCodebase, AgentTask). Logs raw Ollama responses to `%TEMP%/splitbrain-mcp-*.log` before any parsing.

### P0.2 · Confirm response format via direct Ollama test ✅
Diagnosed via live MCP evaluation report (2026-06-02). `review_code` confirmed working. `refactor_code`, `generate_tests`, `agent_task` confirmed broken due to NodeB timeout cascade.

### P0.3 · Fix JSON/text response deserialization ✅
Root cause: not a deserialization bug. Root cause: `OllamaNodeB.TimeoutSeconds=300` with unreachable Node B = 600s HttpClient hang → Claude Desktop fires at ~240s. **Fix:** `OllamaNodeB.TimeoutSeconds=5` (fast-fail). Added `ResponseCleaner.ExtractCode` to strip markdown fences from codegen output.

### P0.4 · Per-tool timeout ✅
Added per-tool `CancellationTokenSource.CreateLinkedTokenSource` with timeouts:
- `refactor_code`: 45s
- `generate_tests`: 60s
- `search_codebase`: 60s
- `agent_task`: 300s (full loop)
- `splitbrain_explain_code`: 45s

### P0.5 · Write-access gate ✅ (Phase 1)
`WriteAccessMode` enum: `Disabled` (default) / `PerCallEnable` / `AlwaysEnabled`.
`WriteAccessGuard` singleton checks the gate for `apply_patch`, `splitbrain_write_file`, `agent_task`.
Configure via `Mcp:WriteAccess:Mode` in appsettings.json.

### P0.6 · Fix agent_task diff application ✅ (Phase 1)
`applyChanges=true` now: checks write gate → parses `+++ b/` paths → applies via `UnifiedDiffApplier` → returns `appliedFiles[]` + `writeGateError` in response.

---

## P1 — Read Surface + Discovery · ✅ COMPLETE (Phase 1 PR #19)

### P1.1 · `splitbrain_read_file` ✅
Reads file within `allowedRoot`. Temp-file envelope for files >8KB.

### P1.2 · `splitbrain_list_files` ✅
Real glob via `FileSystemGlobbing.Matcher`. Excludes bin/obj/.git/node_modules.

### P1.3 · `splitbrain_write_file` ✅
Atomic write (tmp+rename). Write-gated.

### P1.4 · `splitbrain_query_allowed_root` ✅
Returns current write mode + hint for the caller.

### P1.5 · `splitbrain_explain_code` ✅
"What does this do?" — routes to fast node, 45s timeout, ResponseCleaner.StripFences applied.

### P1.6 · Fix `search_codebase` glob ✅
Replaced extension-only stub with `FileSystemGlobbing.Matcher`. Pattern `src/**/*.cs` now correctly scopes to src/.

### P1.7 · `Meta.FromInferenceResult` factory ✅
Added to `SharedModels.cs`. All tools updated to use it.

---

## P2 — Operational Polish · [ ] OPEN

### P2.1 · Streaming tool responses
`review_code` architecture focus takes 12s+ and blocks. SSE streaming via MCP transport would improve perceived responsiveness. The Ollama client already returns `IAsyncEnumerable<string>`.
**Estimate:** M (1–2 days) · **Blocking:** No

### P2.2 · `splitbrain_git_status` / `git_diff` / `git_commit`
Close the agent loop: after `agent_task` applies changes, the agent should be able to commit. Full git surface: status, diff (staged/unstaged), commit with message, optional push.
**Estimate:** M · **Blocking:** No

### P2.3 · `splitbrain_check_build`
Run `dotnet build` (or equivalent) and return structured pass/fail + errors. Closes the generate→apply→build loop.
**Estimate:** S · **Blocking:** No

### P2.4 · `splitbrain_find_symbol`
Find class/function/interface by name via grep/ripgrep. Fast, deterministic. Complement to `search_codebase` (semantic/fuzzy).
**Estimate:** S · **Blocking:** No

### P2.5 · Progress notifications for `agent_task`
Emit `notifications/progress` at each iteration boundary. Caller gets live iteration counter without polling.
**Estimate:** M · **Blocking:** No

### P2.6 · Per-tool model routing policy
Declare preferred model type per tool: analysis→`qcoder:latest`, codegen→`qwen2.5-coder:7b-instruct`, reasoning→`deepseek-r1:7b`. Needs `RoutingHint` field on `InferenceRequest`.
**Estimate:** M · **Blocking:** No

---

## P3 — Semantic + Differentiation · [ ] OPEN

### P3.1 · Real semantic search (replace LLM-in-loop)
Replace `search_codebase`'s LLM-in-loop with Nomic Embed + SQLite-vec similarity. Background indexer + query-time embedding.
**Estimate:** L (3–5 days) · **Blocking:** No

### P3.2 · `splitbrain_multi_agent_task`
Dual-node killer feature. Implementer (Fast) + Reviewer (Deep) in a loop. Requires dedicated routing for each role.
**Estimate:** L · **Blocking:** No

### P3.3 · `splitbrain_plan_task`
Human-approve-before-execute: decompose goal without executing. Routes to reasoning node.
**Estimate:** S · **Blocking:** No

### P3.4 · Tool rename to `splitbrain_*` prefix
All 7 existing tools renamed (review_code → splitbrain_review_code, etc.). Breaking change for callers.
**Estimate:** S (once ready to cut) · **Blocking:** Coordinate with callers

### P3.5 · MCP Prompts (3 templates)
`code-review-structured`, `test-generation-comprehensive`, `refactor-preserve-behavior`. Appear in client UIs.
**Estimate:** S · **Blocking:** No

### P3.6 · Full JSON Schema on all tool inputSchema
Enums for constrained values, min/max for numbers. Enables client-side validation + UI generation.
**Estimate:** M · **Blocking:** No

---

## Deferred / Won't Implement

- `Orchestrator.Hosting` DI extension: deferred to Phase 2 architectural review
- `DashboardState.cs` concurrency fixes: deferred (separate audit needed)
- `InferenceNodeBase.cs` shared base: deferred (requires extensive re-testing)
- HTTPS enforcement: documented (reverse proxy pattern); no code needed

---

*Updated 2026-06-03 after Phase 0 (PR #18) and Phase 1 (PR #19) implementation.*
