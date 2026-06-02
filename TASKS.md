# SplitBrain.AI — Task Tracker
<!-- Council-reviewed 2026-06-02. Cross-referenced: live tool evaluation, architecture audit, MCP expansion audit. -->

## Status Legend
`[x]` Done in PRs #1-#6 | `[ ]` Open | `[~]` In progress | `[!]` Blocking

---

## P0 — Foundation Stabilization · Phase 1 Blocker

### P0.1 · Add raw response logging to broken tools
**Area:** bug/tools · **Estimate:** S (2–4 hours)
**Problem:** `refactor_code`, `generate_tests`, and `agent_task` all hard-timeout at 4 minutes on every call. The response from Ollama (if any) is never surfaced. Without seeing the raw payload we cannot confirm the root cause.
**Fix:** In each of the three tool handlers, add a `_logger.LogDebug("Raw Ollama response [{ToolName}]: {Raw}", toolName, raw)` call immediately after `_routing.RouteAsync` returns, before any JSON parsing or response construction.
**Acceptance:**
- [ ] Log file at `%TEMP%/splitbrain-mcp-*.log` contains raw Ollama text for each broken tool call
- [ ] No change to tool behavior, only added logging

---

### P0.2 · Confirm qcoder:latest response format via direct Ollama test
**Area:** bug/diagnostic · **Estimate:** S (1 hour)
**Problem:** We need ground truth on whether the model returns valid JSON or markdown-fenced plain text for generative tasks.
**Fix:** Add `.http` test files in `tests/http/` with direct Ollama payloads matching each tool's prompt template. Run manually against `http://localhost:11434/api/generate` with `stream: false`. Document the actual response format.
**Acceptance:**
- [ ] `tests/http/refactor_ollama.http`, `tests/http/generate_tests_ollama.http` files committed
- [ ] Findings documented in a `docs/DIAGNOSTICS.md` note

---

### P0.3 · Fix JSON deserialization — strip fences, tolerate plain text, enforce format:json
**Area:** bug/tools · **Estimate:** M (4–8 hours)
**Problem:** Ollama's `qcoder:latest` returns code wrapped in markdown fences (` ```csharp `) or plain text rather than raw JSON. The MCP handlers block forever waiting for a valid JSON payload that never arrives.
**Fix (three-layer):**
1. Add `"format": "json"` to `OllamaGenerateRequest` for all generative tool calls to force structured output
2. Add a fence-stripping pre-processor in the response parsing path (already exists in `SearchCodebaseTool.TryParseResults` — extract to shared utility)
3. Fall back to wrapping plain text in `{"code": "<text>"}` if JSON parse fails after fence-stripping
**Acceptance:**
- [ ] `refactor_code` returns a valid JSON response with the refactored code within 60 seconds
- [ ] `generate_tests` returns a valid JSON response with test file content within 60 seconds
- [ ] Unit test covering fence-stripping + JSON fallback
- [ ] Depends on: P0.1 (confirms exact failure mode before fixing)

---

### P0.4 · Per-tool configurable timeout with structured error envelope
**Area:** reliability/tools · **Estimate:** M (4 hours)
**Problem:** All tools inherit a global 4-minute wall timeout with no structured error response. A caller cannot distinguish a hung tool from a slow one.
**Fix:**
1. Add `TimeoutSeconds` per-tool default in tool descriptors (suggested defaults: `review_code`=60s, `refactor_code`=90s, `generate_tests`=90s, `agent_task`=300s, `search_codebase`=60s, `apply_patch`=30s, `run_tests`=120s)
2. Wrap each tool's `ExecuteCoreAsync` with `CancellationTokenSource.CreateLinkedTokenSource(ct)` + `CancelAfter`
3. On timeout, return: `{"error":"tool_timeout","elapsed_ms":N,"hint":"Tool exceeded Ns. Check node health or reduce input size."}`
4. Expose timeout defaults in `appsettings.json` under `Mcp:ToolTimeouts:{ToolName}`
**Acceptance:**
- [ ] All tools return a structured `tool_timeout` error within their declared timeout (not 4 minutes)
- [ ] Timeout values configurable from `appsettings.json`
- [ ] Integration test: mock slow routing, assert timeout error returned in time

---

### P0.5 · Write-access gate — 3-mode config + per-call enable_write
**Area:** security/write-safety · **Estimate:** M (6 hours)
**Problem:** Any MCP caller can invoke `apply_patch` and `run_tests` (write/exec operations) with no authorization layer. No kill switch exists.
**Fix:**
```json
// appsettings.json
"Mcp": {
  "WriteAccess": {
    "Mode": "Disabled",
    "AllowedWriteTools": ["splitbrain_apply_patch", "splitbrain_write_file"]
  }
}
```
Mode semantics:
- `Disabled` (default): all Write tools return `{"error":"write_access_disabled","gate":"global"}` and are absent from `tools/list`
- `PerCallEnable`: Write tools require `"enable_write": true` param; missing = same error
- `AlwaysEnabled`: trust-all mode for solo dev workflows
**Acceptance:**
- [ ] `Mode=Disabled`: 100% of Write tool calls rejected; Write tools absent from `tools/list`
- [ ] `Mode=PerCallEnable`: calls with `enable_write: true` succeed; without it, clear error returned
- [ ] Default config ships as `Disabled`
- [ ] Error messages are self-healing: include hint for how to enable

---

### P0.6 · Fix agent_task diff application to disk
**Area:** bug/agent · **Estimate:** M (4–6 hours)
**Problem:** `agent_task` produces a unified diff in its response but never applies it. Every sub-agent workflow silently fails at the last mile.
**Fix:**
1. Add `apply_changes: bool` parameter to `agent_task` tool
2. When `apply_changes: true` AND write access is enabled (P0.5 gate), call `UnifiedDiffApplier.Apply` for each file in the diff at loop completion
3. Return `applied_files: ["path1", "path2"]` in the response
4. Add `allowed_root: string?` parameter (required when `apply_changes: true`)
**Acceptance:**
- [ ] `agent_task` with `apply_changes: true` creates/modifies files on disk
- [ ] `read_file` round-trip confirms the change persists
- [ ] `agent_task` without `apply_changes` or with write gate disabled: diff returned but nothing written (same behavior as today)
- [ ] Depends on: P0.5 (write gate), P0.3 (broken tool fix)

---

## P1 — Read Surface + Discovery · Phase 2

### P1.1 · Add splitbrain_read_file tool
**Area:** feature/filesystem · **Estimate:** S (3 hours)
**Problem:** Agents cannot read file content. Every file-aware workflow (read → review → patch) is impossible.
**Fix:** New tool. Enforces `allowed_root`. Emits temp-file envelope (P1.8) for payloads >8KB.
**Params:** `path: string, allowed_root: string?`
**Response:** `{"content": "...", "path": "...", "bytes": N}` or temp-file envelope
**Acceptance:**
- [ ] Returns file content within allowed_root
- [ ] Rejects paths outside allowed_root with self-describing error
- [ ] Files >8KB written to temp file; summary and preview returned inline

---

### P1.2 · Add splitbrain_list_files tool
**Area:** feature/filesystem · **Estimate:** S (3 hours)
**Problem:** Agents cannot discover file structure; broken glob in `search_codebase` is the only approximation.
**Fix:** New tool using `Microsoft.Extensions.FileSystemGlobbing.Matcher` (already transitively available). Enforces `allowed_root`.
**Params:** `path: string, pattern: string? (default **/*), allowed_root: string?`
**Response:** `{"files": [{"path": "...", "size_bytes": N, "modified_utc": "..."}]}`
**Acceptance:**
- [ ] Real glob matching (`src/**/*.cs` correctly scoped to src/)
- [ ] Max 1000 results; if exceeded, returns `truncated: true`
- [ ] Enforces allowed_root

---

### P1.3 · Add splitbrain_query_allowed_root tool
**Area:** feature/discovery · **Estimate:** XS (1 hour)
**Problem:** Agents can't self-orient — they don't know what paths are allowed or what write mode is configured.
**Fix:** No-arg tool returning configured state.
**Response:** `{"allowed_roots": ["..."], "write_mode": "Disabled|PerCallEnable|AlwaysEnabled", "write_tools_enabled": [...]}`
**Acceptance:**
- [ ] Returns accurate reflection of current config
- [ ] Callers can use this before attempting write ops

---

### P1.4 · Add splitbrain_read_agent_log tool
**Area:** feature/observability · **Estimate:** M (4 hours)
**Problem:** `agent_task` is a black box. Callers cannot inspect iteration plans, reviews, or test output.
**Fix:** Queries `LiteDbAgentEventLog` by `task_id`. Returns per-iteration artifacts.
**Params:** `task_id: string, max_iterations: int? (default 10)`
**Response:** `{"task_id": "...", "iterations": [{"index": N, "role": "...", "state": "...", "prompt_preview": "...", "response_preview": "..."}]}`
**Acceptance:**
- [ ] Returns per-step data for a completed agent_task
- [ ] Truncates large prompts/responses to 500 chars with `truncated: true`

---

### P1.5 · Add splitbrain_write_file tool
**Area:** feature/filesystem · **Write-gated**
**Estimate:** S (3 hours)
**Problem:** Patch (P0.6) can only modify existing files. New files require a full-content write.
**Fix:** New tool. Gated by P0.5 (write access). `enable_write: true` required in PerCallEnable mode.
**Params:** `path: string, content: string, allowed_root: string, enable_write: bool?`
**Response:** `{"written": true, "path": "...", "bytes": N}`
**Acceptance:**
- [ ] Creates new files and overwrites existing within allowed_root
- [ ] Atomic write (temp + rename pattern)
- [ ] Depends on: P0.5

---

### P1.6 · Add MCP tool annotations to all tools
**Area:** feature/protocol · **Estimate:** S (2 hours)
**Problem:** Clients can't make trust decisions. Every tool looks identical in terms of safety.
**Fix:** Add `[McpServerTool(..., ReadOnly = true)]` or `McpAnnotations` for each tool:
- ReadOnly: `review_code`, `explain_code`, `search_codebase`, `get_node_status`, `list_models`, `query_allowed_root`, `read_file`, `list_files`, `read_agent_log`
- Destructive: `apply_patch`, `write_file`, `run_tests`, `agent_task` (with apply_changes)
**Acceptance:**
- [ ] `tools/list` response includes `annotations.readOnlyHint` and `annotations.destructiveHint`
- [ ] Verified against MCP spec annotation schema

---

### P1.7 · Add splitbrain_get_node_status + splitbrain_list_models tools
**Area:** feature/observability · **Estimate:** S (3 hours)
**Problem:** Callers can't check cluster health or model availability before dispatching long tasks.
**Fix:** Expose `INodeRegistry` health cache and `IModelRegistry` over MCP.
**`get_node_status` response:** `{"nodes": [{"id":"A","status":"Healthy","queue_depth":0,"latency_ms":120,"vram_used_mb":4096}]}`
**`list_models` response:** `{"models": [{"name":"qcoder:latest","node_ids":["A"],"loaded":true,"vram_mb":4096}]}`
**Acceptance:**
- [ ] Returns live data from the health cache, not stale config

---

### P1.8 · Standard temp-file envelope for large payloads
**Area:** feature/output · **Estimate:** M (4 hours)
**Problem:** Large reviews and agent diffs returned inline break terminal parsing and MCP response size limits.
**Fix:** Add `TempFileService` that writes to `/tmp/splitbrain/sb-{tool}-{utciso}-{hash8}.txt`. Background sweep reaps files >24h. Threshold: 8KB.
**Standard envelope when output > 8KB:**
```json
{
  "summary": "≤2KB inline",
  "output_file": "/tmp/splitbrain/sb-review_code-2026-06-02T...-.txt",
  "output_bytes": 14823,
  "preview": "first 2048 bytes"
}
```
**Acceptance:**
- [ ] `review_code` (architecture focus, 12s responses) writes to temp file and returns envelope
- [ ] Sweep job reaps 25h-old files; verified in test
- [ ] `/tmp/splitbrain/` path configurable via `Mcp:TempFileDirectory`

---

### P1.9 · Documentation: QUICKSTART.md + TOOL_REFERENCE.md + WRITE_SAFETY.md
**Area:** docs · **Estimate:** M (6 hours)
**Problem:** No onboarding path for external developers or agents. The tool surface is undiscoverable without reading source.
**Deliverables:**
- `docs/QUICKSTART.md` — 5-minute hello world with `review_code`
- `docs/TOOL_REFERENCE.md` — per-tool: purpose, params, response schema, example call, error codes, composition hints
- `docs/WRITE_SAFETY.md` — 3-mode write gate explained with curl examples

---

### P1.10 · Add explain_code tool
**Area:** feature/tools · **Estimate:** S (3 hours)
**Problem:** Most common IDE use case ("what does this do?") has no dedicated tool. `review_code` looks for problems; explanation is a different task.
**Params:** `code: string, language: string, verbosity: "brief|detailed|eli5"`
**Routing:** LatencyFirst (caller is waiting at keyboard)
**Acceptance:**
- [ ] Returns explanation in ≤30 seconds on Node A
- [ ] Does not mention code quality or suggest improvements (distinct from `review_code`)

---

## P2 — Operational Polish · Phase 3

### P2.1 · Add splitbrain_get_task_status + splitbrain_cancel_task
**Area:** feature/agent-control · **Estimate:** M (5 hours)
**Problem:** Long-running `agent_task` calls are fire-and-forget. No progress visibility, no cancellation.
**Fix:** Track running tasks in `IAgentEventLog` with `Running/Completed/Cancelled` status. `get_task_status` polls. `cancel_task` propagates `CancellationToken`.
**Params (status):** `task_id: string`
**Params (cancel):** `task_id: string`

---

### P2.2 · Streaming tool responses + agent_task progress notifications
**Area:** feature/protocol · **Estimate:** L (8 hours)
**Problem:** Long tool calls (12s+ reviews, 4-iteration agent loops) return nothing until complete.
**Fix:** Use SSE streaming transport. Emit `notifications/progress` at each agent iteration.
```json
{"method": "notifications/progress", "params": {"progressToken": "...", "progress": 2, "total": 4, "message": "Iteration 2/4: Review"}}
```

---

### P2.3 · Expose Nomic Embed via splitbrain_embed_text tool
**Area:** feature/tools · **Estimate:** M (4 hours)
**Problem:** Nomic Embed Text is loaded in VRAM but not accessible via MCP — wasted 550MB.
**Fix:** New tool routing to `TaskType.Embedding`. Returns `{"embedding": [...float]}`.

---

### P2.4 · Semantic search via Nomic (splitbrain_search_code)
**Area:** feature/tools · **Estimate:** L (10 hours)
**Problem:** `search_codebase` uses LLM for search — expensive and slow. True semantic search via Nomic would be faster and more accurate.
**Fix:** Background indexer builds in-process vector index. `search_code` embeds query + returns nearest code chunks.
**Depends on:** P2.3

---

### P2.5 · MCP resources: routing://decisions + metrics://realtime
**Area:** feature/protocol · **Estimate:** M (5 hours)
**Problem:** Routing decisions and metrics are only visible in the Blazor dashboard.
**Fix:** Expose as MCP resources (read-only, polled by client).

---

### P2.6 · MCP Prompts — 3 parameterized templates
**Area:** feature/protocol · **Estimate:** S (3 hours)
**Prompts:** `code-review-structured`, `test-generation-comprehensive`, `refactor-preserve-behavior`

---

## P3 — Architectural Differentiation · Phase 4

### P3.1 · multi_agent_task — the dual-node killer feature
**Area:** feature/agent · **Estimate:** XL (16 hours)
**Problem:** The dual-node architecture has no compelling use case that REQUIRES two nodes. multi_agent_task changes this.
**Design:** Implementer role pinned to `NodeRole.Fast` (Node A, Qwen Q5), Reviewer role pinned to `NodeRole.Deep` (Node B, DeepSeek R1). N iterations until Reviewer approves or max reached.
**Depends on:** P2.1 (cancel/status), P0.3 (broken tools fixed)

---

### P3.2 · README rewrite — lead with problem statement
**Area:** docs · **Estimate:** S (2 hours)
**Problem:** Current README presumes the reader knows what SplitBrain.AI is. The dual-node differentiation story is invisible.

---

## Already Done in PRs #1-#6 — Do NOT Re-Implement

| PR | What's covered |
|----|----------------|
| PR #1 (Phase A) | DI dedup, async safety, CI fix, cleanup |
| PR #2 (Batch 1a) | DashboardState thread safety, DrainAsync hang, idempotency race |
| PR #3 (Batch 1b) | NodeWorker shared-key auth, tool error handling (5 tools), gh CLI fix |
| PR #4 (Batch 2) | TokenEstimator, Meta factory, InferenceNodeBase, LogStepAsync, factory collapse, hardcoded model removed |
| PR #5 (Batch 3) | Real latency scoring, NodeStatus.Unknown, refusal regex, file-size guard, allowedRoot required |
| PR #6 (Batch 4) | Disposal logging, AggregateException on fallback, typed exceptions, JsonConfig.MakeReadOnly, prompt sanitization |

---

## Merge Order for Outstanding PRs

```
main/next-gen
  └── PR #1 (Phase A Foundation)
        └── PR #2 (Batch 1a: Concurrency)
              └── PR #3 (Batch 1b: Auth/Errors)
                    └── PR #4 (Batch 2: Dedup)
                          └── PR #5 (Batch 3: Robustness)
                                └── PR #6 (Batch 4: Security)
                                      └── this branch (Roadmap Tasks)
```

Merge each into the next in order. All target `next-gen` after the chain is merged.
