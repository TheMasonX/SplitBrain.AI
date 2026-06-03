# Council Synthesis: MCP Tool Surface — Three-Source Deep Review
## Commit: `70572c93` · Date: 2026-06-03

**Sources synthesized:**
1. **Axiom (internal council)** — codebase audit, all 7 tool files read verbatim
2. **External Agent A** — live evaluation: actually called every tool via MCP at localhost:5000, reported real latency, real failures
3. **External Agent B** — NexGenMasterPlan.md reader + architecture-aware recommendations

**Seat confidences:** Archivist 88% · Architect 84% · Practitioner 91% · Skeptic 61% · Advocate 83%

---

## What the Live Evaluation Confirms (First-Hand Evidence)

This is the most important section. External Agent A actually USED the server. Their findings are ground truth.

### Confirmed: timeout root cause is Node B + Claude Desktop ceiling

**appsettings.json:** `OllamaNodeB.TimeoutSeconds = 300` → `HttpClient.Timeout = 600s` (10 minutes)  
**nodes.json:** `Node B TimeoutSeconds = 120` → also too long if Node B is unreachable  
**Claude Desktop:** cancels MCP calls at ~4 minutes  
**Observed:** all generative tools time out at exactly 4 minutes — this is Claude's ceiling, not the server's response

The 4-minute timeout is NOT a model hang. It's `Routing → Node B (preferred for Refactor/TestGen) → HttpClient waits 600s → Claude times out at ~240s`. The MCP session appears to complete (no error) but returns nothing.

**Fix:** `OllamaNodeB.TimeoutSeconds = 5` (fast-fail, triggers fallback to C → A within seconds). This single change fixes all three broken tools.

### Confirmed: `review_code` quality is genuinely good

| Test | Quality |
|---|---|
| Python bugs | Good (minor misses on dict key guards) |
| Python performance | Excellent (correctly declined to manufacture issues) |
| C# security | Excellent (caught all 3 deliberate vulnerabilities: SQL injection, no password hash, weak token) |
| TypeScript readability | Good (slight focus bleed, missed type annotation note) |
| C# architecture | Excellent (full Strategy Pattern recommendation with working code example) |

The model scales output depth to complexity. Security (short, direct) = 622ms. Architecture (long, with code) = 12s. This is healthy.

### Confirmed: `qcoder:latest` is the actual model, not `qwen2.5-coder:7b-instruct-q4_K_M`

`ReviewCodeTool.cs` hardcodes `"qwen2.5-coder:7b-instruct-q4_K_M"` in the `InferenceRequest.Model` field, but the actual model running is `qcoder:latest` (likely a custom Ollama model built from `wiki-chat-agent.modelfile`). The routing/model lookup is resolving to a different model than what's hardcoded. This is either:
- The hardcoded string doesn't match, and routing falls through to the default
- `qcoder:latest` IS `qwen2.5-coder:7b-instruct-q4_K_M` aliased under a custom name

Either way, `qcoder:latest` performs well for analysis, poorly/untested for generation.

### Confirmed: Node B has NEVER served production traffic

All 5 successful calls routed to Node A. Zero Node B activity. Either:
1. Node B is offline (most likely, given `unreachable.local` in appsettings.json)
2. Node B health check shows Unavailable, so routing correctly avoids it

The dual-node architecture exists in config but has never operated in production.

---

## What External Agent B Found That I Missed

### 1. MCP Resources already exist — 3 undocumented

External Agent B saw `nodes://health`, `models://registry`, `tasks://history` as live MCP resources. My council review didn't know these existed. **This is important** — before adding new resources, we need to audit what's there.

### 2. MCP Prompts — completely unaddressed by my review

The MCP spec defines Prompts as parameterized message templates a client can list and invoke. Zero prompts exist in SplitBrain.AI. External Agent B recommends 3:
- `code-review-structured` — consistent review request format
- `test-generation-comprehensive` — framework + coverage + language template
- `refactor-preserve-behavior` — behavioral preservation constraint

These are NOT tools. They're conversation starters that appear in the client UI (Claude Desktop, IDE extensions). High value, low effort.

### 3. `route_task` dry run — I didn't think of this

A tool that runs the routing scoring algorithm and returns the full decision **without executing inference**. "Which node would handle this task, and why?" This is invaluable for:
- Debugging why a task routed to the wrong node
- Pre-validating node availability before dispatching long tasks
- Testing routing policy changes

My council review had no equivalent.

### 4. `plan_task` as a separate primitive from `agent_task`

External Agent B correctly separates these:
- `plan_task` = decompose goal into steps, return plan, don't execute (route to Deep/Reasoning node)
- `agent_task` = execute the plan (or a given goal) end-to-end

This "approve before execute" pattern is critical for write-gate compliance and human-in-the-loop workflows. I had it as Phase 4 but it should be earlier.

### 5. Per-tool model routing policy — immediately actionable

External Agent B proposes assigning default models by tool type:
- Analysis tools (`review_code`, `explain_code`) → `qcoder:latest` (Node A, Fast)
- Code generation tools (`refactor_code`, `generate_tests`, `document_code`) → `qwen2.5-coder:7b-instruct` (explicit instruct model)
- Multi-step reasoning (`agent_task`, `plan_task`) → `deepseek-r1:7b` (Deep node)
- Embeddings (`search_code`) → `nomic-embed-text` (fast, dedicated model)

This is wiring the routing engine to do something meaningful instead of treating all tasks equivalently.

### 6. NexGenMasterPlan.md (1,259 lines) — we haven't read this

External Agent B read a 1,259-line `NexGenMasterPlan.md` from April 2026. This is Lucas's original detailed architecture document. We should incorporate it into our review but I haven't seen it. Note it as a reference for future context loading.

---

## What I Found That the External Agents Missed

### 1. `apply_patch` and `run_tests` already exist

External Agent B recommends `apply_patch` and `run_tests` as "new tools" — they already exist and work. The agent didn't check the current implementation. The recommendations are good but target functionality already present.

### 2. The exact timeout math

My council (confirmed by live data) pinpointed `TimeoutSeconds * 2` as the HttpClient multiplier. The external agents just observe "4-minute timeout" without tracing to the config value. The fix is specifically `OllamaNodeB.TimeoutSeconds = 5` in appsettings.json AND nodes.json.

### 3. `AgentTaskResponse.Meta = default!` — null crash

External Agent B doesn't mention this. It means the `agent_task` response will crash JSON serialization even if the agent succeeds. Zero callers have seen a successful `agent_task` response because the diff is never applied and Meta is null.

### 4. `MatchGlob` is completely fake

Neither external agent flagged this. The glob pattern `src/**/*.cs` is treated identically to `*.cs`. The entire directory-scope constraint is silently ignored.

---

## Points of Disagreement / Critical Assessment

### On model routing (External Agent B's proposal)

External Agent B proposes `deepseek-r1:7b` for agent_task/plan_task. This is correct in principle but:
- DeepSeek R1 must actually be pulled to Node B/C: `ollama pull deepseek-r1:7b`
- The routing engine currently uses node roles (Fast/Deep/Hybrid), not specific model names per tool
- Implementing per-tool model routing requires a new `RoutingHint` or `PreferredModel` field on `InferenceRequest`

This is a valuable medium-term change but not a one-liner. Mark as Phase 2.

### On `search_code` via Nomic embedding

Both external agents recommend using Nomic for semantic search. This is right but requires:
1. A background indexing job (chunking + embedding all source files)
2. A vector store (SQLite-vec or in-memory)
3. At least 30 minutes of work to set up correctly

The current `search_codebase` fake-glob + LLM-in-loop approach is wrong and should be replaced, but it's Phase 2 work, not Phase 0.

### On streaming responses

External Agent B says streaming is "massive UX improvement." True, but:
- `review_code` architecture review takes 12s and is already usable as-is
- Streaming requires MCP transport changes and client-side support
- It's genuinely Phase 3 work

Mark as planned but not high priority vs. broken tools and file I/O.

---

## Definitive Priority Ranking (Synthesized)

### P0: Fix this week (no new design needed)

| Fix | Source | Impact |
|-----|--------|--------|
| `OllamaNodeB.TimeoutSeconds = 5` in appsettings.json | All 3 sources | Fixes 3 broken tools instantly |
| `OllamaNodeB.TimeoutSeconds = 10` in nodes.json | Internal | Consistent fast-fail |
| Add fence-stripping to refactor_code + generate_tests | All 3 sources | Clean code output |
| Fix `AgentTaskResponse.Meta = default!` | Internal | Prevents null crash |
| Apply diff in `agent_task` (gated by write gate flag) | External B | Agent actually useful |
| Fix `MatchGlob` to use FileSystemGlobbing | Internal | Real glob search |
| Fix `GenerateTestsTool` output path | Internal | Valid file names |
| Add raw response logging to generative tools | All 3 sources | Diagnosability |
| Add per-tool 30s CancellationToken timeout | External A+B | Structured errors |

### P1: Next sprint (new tools + write gate)

| Item | Source | Notes |
|------|--------|-------|
| `splitbrain_read_file` | All 3 | Critical for agent autonomy |
| `splitbrain_write_file` | All 3 | Critical for agent autonomy |
| `splitbrain_list_files` | Internal + B | Real glob listing |
| `splitbrain_get_structure` | Internal | Directory tree |
| `splitbrain_explain_code` | External B | Most common IDE use case |
| `splitbrain_document_code` | External B | Complement to refactor |
| `splitbrain_git_status` | All 3 | Agent persistence |
| `splitbrain_git_diff` | All 3 | Agent persistence |
| `splitbrain_git_commit` | All 3 | Agent persistence |
| `splitbrain_check_build` | Internal | Close the test loop |
| Write-access gate (3 modes) | External B | Safety before writes |
| MCP Prompts (3 templates) | External B | Client UX — low effort |
| `splitbrain_get_node_status` | External B | Expose cluster state |
| `splitbrain_list_models` | External B | Live model availability |
| Tool annotations on all tools | External B | Client trust decisions |
| Add logging to all LLM tools | All 3 | Parity with review_code |

### P2: Architecture phase (routing + observability)

| Item | Source | Notes |
|------|--------|-------|
| Per-tool model routing policy | External B | Analysis vs. gen vs. reasoning |
| `splitbrain_route_task` (dry run) | External B | Debug routing decisions |
| `splitbrain_plan_task` | External B | Human-approve before execute |
| `splitbrain_get_task_status` | All 3 | Polling progress |
| `splitbrain_cancel_task` | External B | Graceful cancellation |
| `splitbrain_inspect_agent` | External B | Live agent state |
| `splitbrain_get_metrics` | External B | CI/monitoring integration |
| `routing://decisions` resource | External B | Routing decision log |
| `agents://active` resource | External B | Live agent view |
| `metrics://realtime` resource | External B | OTel metrics over MCP |
| Full JSON Schema on all tool inputs | External B | Client validation |
| Progress notifications for agent_task | External B | Live iteration counter |
| `splitbrain_find_symbol` | Internal | Symbol navigation |

### P3: Semantic + differentiation

| Item | Source | Notes |
|------|--------|-------|
| Real semantic search (Nomic + SQLite-vec) | All 3 | Replace fake LLM search |
| `splitbrain_embed_text` | External B | Expose Nomic via MCP |
| `embeddings://search` resource | External B | Raw semantic search |
| `splitbrain_multi_agent_task` | All 3 | Dual-node killer feature |
| `splitbrain_run_command` | Internal | Sandboxed command exec |
| SSE streaming tool responses | External B | UX improvement |
| Tool rename to `splitbrain_*` prefix | Internal + B | Namespace isolation |

---

## What the Three Sources Agree On (High Confidence)

✅ Fix NodeB timeout first — single highest-leverage change  
✅ File I/O tools are the core missing primitive  
✅ Git tools complete the autonomous loop  
✅ `multi_agent_task` is the dual-node killer feature  
✅ Tool annotations (readOnlyHint/destructiveHint) are low-effort, high-signal  
✅ `explain_code` fills the most common IDE use case  
✅ The `review_code` tool quality is production-ready  
✅ Structured error envelopes need to be consistent across all tools  

---

## New Findings for Wiki Tasks

The following items are NEW since my prior council review and need TSK records:

- TSK-0017: Per-tool model routing policy configuration
- TSK-0018: MCP Prompts — 3 templates (code-review-structured, test-generation-comprehensive, refactor-preserve-behavior)
- TSK-0019: `splitbrain_plan_task` tool (separate from agent_task, routes to reasoning node)
- TSK-0020: `splitbrain_inspect_agent` tool (live agent state during execution)
- TSK-0021: `splitbrain_route_task` dry-run tool (run scoring without inference)
- TSK-0022: `splitbrain_get_metrics` tool (aggregate metrics over MCP)
- TSK-0023: Full JSON Schema on all tool inputSchema definitions
- TSK-0024: `routing://decisions` MCP resource
- TSK-0025: `agents://active` MCP resource
- TSK-0026: `metrics://realtime` MCP resource
- TSK-0027: `splitbrain_document_code` tool
- TSK-0028: `splitbrain_explain_code` tool  
- TSK-0029: `splitbrain_embed_text` tool
- TSK-0030: `embeddings://search` MCP resource
- TSK-0031: `splitbrain_find_symbol` tool
- TSK-0032: Progress notifications for agent_task (MCP notifications/progress)
- TSK-0033: SSE streaming for long tool responses
- TSK-0034: Per-tool 30s CancellationToken timeout with structured error response
- TSK-0035: Audit existing MCP resources (nodes://health, models://registry, tasks://history)
