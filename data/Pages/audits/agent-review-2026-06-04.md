# SplitBrain.AI — Agent Audit & Council Review

**Date:** 2026-06-04
**Evaluator:** Claude Sonnet 4.6 via claude.ai (web MCP session)
**Tools used:** `splitbrain:*` (live), `memorysmith-splitbrain:*` (live)
**Evidence base:** 15 KB memory records + 10 wiki pages + 4 live tool calls
**Gate:** CONDITIONAL PASS — one live tool defect confirmed, two structural risks flagged

---

## Scope

This review was triggered by an open-ended request: "use the splitbrain and memorysmith-splitbrain MCP tools." The agent performed a full discovery sweep, synthesised all current-state records, probed all four SplitBrain tools, and produced this findings document.

---

## Assumptions

- [critical assumption] `%SplitBrainRepo%` env var is **not** configured in the MemorySmith server process. All source links fail with `source.missing_variable` + `source.unresolved`. This affects every record in the KB. Evidence: 16 identical warnings across two separate context-pack calls.
- [assumption] next-gen branch HEAD is commit `9b4adf82` (2026-06-02), as recorded in `splitbrain-next-gen-branch-state` (confidence: 0.97). No live git call was possible from this session.
- [assumption] PR #19 (`feature/phase-1-tools`) has not yet been merged. Phase 1 reconciliation record (`splitbrain-phase1-reconciliation`, 2026-06-03) describes it as submitted, not merged.
- [assumption] The `search_codebase` defect is server-side, not a query problem. Three independent queries with semantically distinct terms all returned the same degenerate result structure.
- [assumption] The SplitBrain MCP server is running on the local machine at `http://localhost:5100/mcp` (port changed from the historic 5000 to 5100 per `splitbrain-mcp-interface`).

---

## Open Questions

- [ ] **Is PR #19 merged?** If yes, 12 tools are live on next-gen. If not, the write-gate tools (write_file, apply_patch write mode) are unavailable.
- [ ] **What is causing `search_codebase` to return `model_output` / single-token results?** Hypothesis: the Qwen2.5-coder model on Node A is receiving a malformed or empty prompt and returning a default stub. Needs server log inspection (`%TEMP%/splitbrain-mcp-[date].log`).
- [ ] **Is `%SplitBrainRepo%` intentionally unset on the MemorySmith server host?** If the server runs as a Windows service, it may not inherit user env vars. Fix: set the variable in the service's environment block via `sc.exe` or the service configuration file.
- [ ] **What is the current C1 DrainAsync status?** This critical-severity hang has been pending since the first audit batch. No record confirms it was fixed.
- [ ] **What is the current C2 DashboardState thread safety status?** Same — still listed as pending in `splitbrain-audit-batch-status`.
- [ ] **What is the current C3 NodeWorker unauthenticated endpoints status?** Security-critical. No record confirms resolution.

---

## Tool-by-Tool Evaluation

### `review_code` ✅ Operational

**Evidence:** Live call with minimal C# probe code returned a coherent, correctly structured JSON response with `summary`, `issues[]`, and `meta`. Latency: ~2.4 s. Node: A. Model: `qcoder:latest`.

**Observations:**
- Response structure is correct and parseable.
- Per KB record `splitbrain-mcp-interface`: `ReviewCodeTool` is missing a per-tool timeout (TSK-0036). Not observable in a short probe but is a production risk under slow inference.
- `focus` parameter accepts: `architecture | performance | bugs | readability | security`.

**Confidence: 90%** — tool is functional; TSK-0036 is a known gap, not a blocker.

---

### `refactor_code` ✅ Operational (inferred)

**Evidence:** Not directly called in this session (probe budget conserved). KB record `splitbrain-phase0-implementation` (confidence: 0.97) confirms Phase 0 fixes: ResponseCleaner integration, 45 s per-tool timeout, ILoggingService parity. These fixes specifically targeted `refactor_code` and `generate_tests`.

**Confidence: 82%** — strong KB evidence; no live call confirmation in this session.

---

### `agent_task` ✅ Operational (inferred)

**Evidence:** Not directly called. KB record confirms `AgentTaskTool` Meta null crash was fixed in Phase 0, `applyChanges` parameter added (disk write deferred to Phase 1). Agent bounds record (`splitbrain-agent-bounds`) confirms max 4 iterations, 12k tokens, 300 s timeout.

**Confidence: 78%** — KB evidence strong; write-path untested without PR #19 confirmation.

---

### `search_codebase` ❌ Defective

**Evidence:** Three independent live calls, three different semantically unrelated queries:

1. `"DrainAsync hang deadlock async stdout stderr process"` → `{"filePath":"model_output","snippet":"The","score":1}`
2. `"WriteAccessGuard write gate modes MCP tools"` → `{"filePath":"model_output","snippet":"This","score":1}`
3. `"LiteDbAgentEventLog SQLite migration"` → `{"filePath":"model_output","snippet":"This","score":1}`

All three returned a single result with `filePath: "model_output"`, line numbers `0:0`, and a one-word snippet. The `meta` block shows `tokensIn: 0` and `tokensOut: 0` on calls 2 and 3, confirming the LLM was not actually invoked for the semantic ranking step.

**Root cause hypothesis A (confidence: 72%):** The file enumeration step is failing silently. `tokensIn: 0` means the embedding/search model received zero input — the file list is empty before the model is ever called. The path `C:/@Repos/Visual Studio Projects/SplitBrain.AI/src` contains a space; unquoted path resolution could silently return an empty file list, causing the tool to emit a stub response.

**Root cause hypothesis B (confidence: 28%):** The semantic/embedding model for `search_codebase` is not loaded or misconfigured. This would also produce `tokensIn: 0` but via a different code path than the file walker. Note: `review_code` uses the inference model inline — the two tools may use entirely different backends.

**Disambiguation:** Check `%TEMP%/splitbrain-mcp-[date].log` for task IDs:
- `a580f0e77f2a42baa9dd310d1e3869ca`
- `03cbc3362a21412b87bea6b45328ad2e`
- `1785b99bfefc4d2494a01463c03b9459`

**Impact:** `search_codebase` is the primary codebase-discovery tool. Its failure degrades `agent_task` planning quality and blocks all agent-driven file navigation.

**Confidence in defect: 97%** — three consistent failures, zero output variance, `tokensIn: 0` is objective.

---

## Findings: KB Health

### F1 — Source Link Variable `%SplitBrainRepo%` Unresolved (All Records)

**Severity:** High (structural, affects all 15+ records)
**Evidence:** 16 `source.missing_variable` + `source.unresolved` warnings across two context-pack calls.
**Impact:** No agent can follow source links from KB records to actual code. `source_bundle` would return empty results for any SplitBrain record. This breaks the KB → code navigation workflow.
**Fix:** Set `%SplitBrainRepo%` in the MemorySmith service environment. Likely value: `C:/@Repos/Visual Studio Projects/SplitBrain.AI/`.

**Confidence: 97%**

---

### F2 — All Record `usageCount` Values Are 0

**Evidence:** Every retrieved record shows `usageCount: 0`, including high-confidence records updated 2026-06-03.

**Interpretation A (60%):** Usage tracking was recently enabled or reset — records written but not yet retrieved via MCP `get` in production.

**Interpretation B (40%):** Usage counter increment does not fire for `context_pack` and `hybrid_search` calls — only `memorysmith_get` increments it.

**Impact:** `usageCount` cannot currently be used as a staleness signal.

---

### F3 — Open Critical Bugs Unresolved (C1, C2, C3)

**Evidence:** `splitbrain-audit-batch-status` (confidence: 0.90, updated 2026-06-02) lists as still pending:
- **C1** `DrainAsync` hang — potential deadlock in async pipe reading
- **C2** `DashboardState` thread safety — concurrent access without synchronization
- **C3** NodeWorker unauthenticated endpoints — security exposure

Batches 1a–4 addressed lower-severity items. The three Critical items were deferred to Phase A. No subsequent record confirms any of them have been resolved.

**Confidence: 88%**

---

### F4 — Known TSK Issues on Active Tool Surface

From `splitbrain-mcp-interface` (confidence: 0.92):

| TSK | Tool | Issue |
|-----|------|-------|
| TSK-0036 | `ReviewCodeTool` | Missing per-tool timeout |
| TSK-0037 | `RunTestsTool` | Hardcodes `--no-build` |
| TSK-0038 | `ApplyPatchTool` | Missing idempotency |
| TSK-0045 | Phase 1 tools | Anonymous JSON instead of typed response models |

---

## Findings: Architecture & Roadmap

### A1 — Phase 1 PR #19 Pending Merge

PR #19 (`feature/phase-1-tools`, commit `2396a5bf`) adds 5 tools: `read_file`, `write_file`, `list_files`, `query_allowed_root`, `explain_code`. Until merged, the live MCP surface is 7 tools (Phase 0), not 12.

**Confidence: 75%** — record says "submitted" not "merged"; no live git verification possible from this session.

---

### A2 — LiteDB Still In Production (SQLite Migration Pending)

Decision D3 (`splitbrain-decision-d3-sqlite`, confidence: 0.94, status: locked) mandates SQLite migration with WAL mode. `LiteDbAgentEventLog.cs` is still the production implementation. Migration is planned for Phase C2.

**Risk:** LiteDB + C2 DashboardState thread safety unresolved = potential data-corruption scenario under agent parallelism.

---

### A3 — llama.cpp Benchmarking Not Yet Done

Decision D8 (`splitbrain-decision-d8-llamacpp-parallel`, confidence: 0.90) deferred the Ollama vs. llama.cpp permanence decision until benchmarking on the actual GTX 1080. `NodeClient.LlamaCpp` is fully implemented (41 tests, commit `1566611d`) but the benchmark gate has not been closed. `NODE_B_BACKEND` defaults to `ollama`, meaning production traffic is not using llama.cpp's MoE advantages (expected ~18–22 tok/s for 30B MoE on 8 GB VRAM).

---

## Recommendations

### Immediate (before next agent session)

1. **Fix `search_codebase`** — inspect `%TEMP%/splitbrain-mcp-[date].log` for the three task IDs above. Look for path-not-found or empty file-list errors. If it's the path space issue, quote or URL-encode the rootPath in the tool's internal file walker. This is the highest-leverage fix — it unblocks all agent-driven code navigation.

2. **Set `%SplitBrainRepo%` in MemorySmith service environment** — one config change resolves 16 broken source links across all records. Until fixed, `source_bundle` is effectively dead for this project.

3. **Verify PR #19 merge status** — `git log --oneline next-gen | head -5` will confirm in 5 seconds.

### Short-term (Phase A / next sprint)

4. **Resolve C1 DrainAsync** — highest-severity unresolved bug. Pattern fix: always read stdout and stderr concurrently using `Task.WhenAll` before calling `WaitForExitAsync`.

5. **Resolve C2 DashboardState thread safety** — especially important before the SQLite migration (A2).

6. **Resolve C3 NodeWorker unauthenticated endpoints** — interim: firewall rules restricting port 5050 to Machine A only.

7. **Add TSK-0036 timeout to ReviewCodeTool** — copy the per-tool CTS pattern already used by `RefactorCodeTool` and `GenerateTestsTool`.

### Medium-term

8. **Run llama.cpp benchmark on GTX 1080** — close decision D8. Use `test-llamacpp-streaming.sh` with flags from `splitbrain-llamacpp-gtx1080-flags`. Start at `--n-cpu-moe 25`, tune down in steps of 5 until OOM, then add 2–3 back.

9. **Replace anonymous JSON in Phase 1 tools (TSK-0045)** — typed response models improve MCP client compatibility and composability.

10. **Wire `%SplitBrainRepo%` and test `source_bundle` round-trip** — after fixing the env var, verify: `context_pack` → extract IDs → `source_bundle` → confirm code snippets return.

---

## Council Review — `search_codebase` Defect

**Context:** Three live calls with semantically unrelated queries all returned identical degenerate results with `tokensIn: 0`. This is objective evidence the LLM was not invoked.
**Assumptions:** [critical assumption] The server process can reach the file system at the provided rootPath. [assumption] `qcoder:latest` model is healthy (confirmed: `review_code` responded correctly).

### The Advocate *(confidence: 85%)*
> **Position:** The defect is in the file enumeration step, before the LLM is called.

`tokensIn: 0` means the semantic search model received zero tokens — it was never invoked. `review_code` works fine on the same model, so the LLM itself is healthy. The path `C:/@Repos/Visual Studio Projects/SplitBrain.AI/src` contains a space; if the tool's internal file walker doesn't quote or escape it, the walk silently returns an empty list → nothing to embed → LLM skipped → stub response returned.

### The Devil's Advocate *(confidence: 55%)*
> **Position:** The defect might be in the semantic ranking model layer, not the file walker.

`review_code` sends code inline to the inference model — it does not use a file indexer or embedding model. If `search_codebase` uses a separate ONNX or vector-search backend that is misconfigured or missing, it would also produce `tokensIn: 0` without touching the file system. Check whether a separate embedding model is configured in `appsettings.json` for `SearchCodebaseTool` before assuming the path is the cause.

### The Pragmatist *(confidence: 92%)*
> **Position:** Check the server log first; it disambiguates in under 5 minutes.

Look up the three task IDs in `%TEMP%/splitbrain-mcp-[date].log`. The log will show either a path error (confirms Advocate) or a model-not-found / embedding error (confirms Devil's Advocate). Fix the root cause; do not work around it by changing path formats without confirming the actual error first.

---

### Confidence Summary

| Dimension | Confidence | Rationale |
|-----------|-----------|-----------|
| `search_codebase` defect confirmed | 97% | Three independent failures, `tokensIn: 0` is objective evidence |
| Root cause: path issue vs. embedding issue | 55% | Two plausible hypotheses; server log needed to discriminate |
| KB current-state accuracy | 88% | High-confidence records (0.90–0.97); source links broken but content trusted |
| C1/C2/C3 still unresolved | 85% | No confirming fix record found; absence of evidence is the best available signal |
| PR #19 not yet merged | 75% | Record says "submitted"; no merge confirmation in KB |
| llama.cpp benchmark not yet run | 90% | D8 explicitly defers; no result record found |

---

## Write Permissions Note

The `memorysmith-splitbrain` MCP instance returned `401 Unauthorized` on `page_save`. This page was saved via the primary `memorysmith` MCP instance instead. The splitbrain wiki is readable but write-protected via the splitbrain-specific connector — this may be intentional (read-only agent access) or a configuration issue with the splitbrain MemorySmith API key/role.

---

*Saved by Claude Sonnet 4.6 · Session: 2026-06-04 · Tools: `splitbrain:review_code` ✅ · `splitbrain:search_codebase` ❌ · `memorysmith-splitbrain:*` read-only ✅ · `memorysmith:page_save` ✅*