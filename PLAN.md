# SplitBrain.AI — Refined Master Plan
<!-- Council-reviewed 2026-06-02. Sources: architecture audit, live tool evaluation, MCP expansion council. -->

## Vision

SplitBrain.AI is a two-machine inference orchestrator that acts as a **first-class MCP sub-agent platform** — an external agent (Claude, Copilot, a CI script) can hand it a goal and have it execute, review, and commit code changes autonomously using its own time and context window, with full read access and opt-in write access.

---

## Current State (as of 2026-06-02)

**7 MCP tools registered. 4 of 7 working.**

| Tool | Status | Root Cause |
|------|--------|-----------|
| `review_code` | Working | Returns plain text summary |
| `refactor_code` | 4-min timeout | JSON schema mismatch vs qcoder output |
| `generate_tests` | 4-min timeout | JSON schema mismatch vs qcoder output |
| `agent_task` | 4-min timeout | Same root cause + diff never applied |
| `apply_patch` | Working | Pure local FS, no LLM |
| `run_tests` | Working | Pure local process |
| `search_codebase` | Partial | Glob matching is extension-only stub |

**Structural issues addressed in PRs #1-#6:**
- DI duplication eliminated (Orchestrator.Hosting)
- DashboardState thread safety fixed
- Idempotency race condition fixed
- NodeWorker auth added
- Tool error handling standardized
- Code duplication eliminated (InferenceNodeBase, TokenEstimator, Meta.FromInferenceResult)
- Real latency scoring, file-size guards, prompt sanitization

---

## Phase 1: Foundation Stabilization

**Goal:** Every registered tool works or returns a structured error within its timeout. Write operations are gated.

**Target:** 4-6 weeks

### Critical Path

```
P0.1 Raw response logging
  -> P0.2 Direct Ollama test (confirm format)
    -> P0.3 Fix JSON deserialization (fence-stripping + format:json)
      -> P0.4 Per-tool configurable timeout + structured error envelope
P0.5 Write-access gate (3-mode config)
  -> P0.6 agent_task diff application to disk
```

### Exit Criteria

1. All 7 tools return within their declared timeout
2. `agent_task` with `apply_changes: true` writes files to disk
3. Write gate enforced: Disabled by default, structured error returned
4. CI test suite covers each tool's happy path + timeout + write-gate
5. `CHANGELOG.md` entry with schema fix and write-gate migration notes

---

## Phase 2: Read Surface + Discovery

**Goal:** An external agent can fully navigate and read the codebase through MCP. Write ops available under the gate.

**Target:** 6-8 weeks after Phase 1

### New Tools

| Tool | Risk | Purpose |
|------|------|--------|
| `splitbrain_read_file` | ReadOnly | Read any file within allowed_root |
| `splitbrain_list_files` | ReadOnly | Real glob listing with size/date metadata |
| `splitbrain_query_allowed_root` | ReadOnly | Self-orientation; returns config + write mode |
| `splitbrain_read_agent_log` | ReadOnly | Per-iteration artifacts for any agent task |
| `splitbrain_write_file` | Write | Full-content write for new/existing files |
| `splitbrain_explain_code` | ReadOnly | "What does this do?" distinct from review |
| `splitbrain_get_node_status` | ReadOnly | Live cluster health |
| `splitbrain_list_models` | ReadOnly | Available models across nodes |

### Protocol Improvements

- MCP tool annotations (`readOnlyHint`, `destructiveHint`) on all tools
- Standard temp-file envelope for payloads >8KB
- All existing tools renamed to `splitbrain_*` prefix

### Documentation

- `docs/QUICKSTART.md` - 5-minute hello world
- `docs/TOOL_REFERENCE.md` - per-tool reference with examples
- `docs/WRITE_SAFETY.md` - write gate modes explained

### Exit Criteria

1. External agent completes `list_files -> read_file -> review_code -> apply_patch -> run_tests` chain end-to-end with `PerCallEnable` write gate
2. All 15+ tools present in `tools/list` with correct annotations
3. Temp-file envelope tested: large outputs written to file, swept after 24h

---

## Phase 3: Operational Polish

**Goal:** Observable, controllable, streamable. CI-pipeline ready.

**Target:** 8-10 weeks after Phase 2

### New Tools and Features

| Item | Purpose |
|------|--------|
| `splitbrain_get_task_status` | Poll running task state |
| `splitbrain_cancel_task` | Propagate cancellation to in-flight inference |
| `splitbrain_embed_text` | Expose Nomic Embed (currently loaded but unexposed) |
| Streaming SSE responses | Progressive output for long reviews |
| `notifications/progress` | Agent iteration counter emitted via SSE |
| `routing://decisions` resource | Routing decision log readable over MCP |
| `metrics://realtime` resource | OTel meter data over MCP |
| MCP Prompts (3 templates) | IDE-friendly parameterized templates |

---

## Phase 4: Architectural Differentiation

**Goal:** The dual-node architecture has a compelling use case that REQUIRES two nodes.

**Target:** Post-Phase-3

### Key Feature: multi_agent_task

The dual-node "killer app." One tool invocation spawns two agents:
- **Implementer** -> pinned to `NodeRole.Fast` (Node A, Qwen Q5) - generates code fast
- **Reviewer** -> pinned to `NodeRole.Deep` (Node B, DeepSeek R1) - deep reasoning critique

Iterate until Reviewer approves or max iterations reached. The fast node generates; the quality node validates. This is the highest-value use case for the dual-node architecture.

### Additional Phase 4 Work

| Item | Purpose |
|------|--------|
| `splitbrain_search_code` | True semantic search via Nomic (not LLM-based) |
| `plan_task` | Decompose goal into step list, without executing |
| `inspect_agent` | Live iteration state for running agents |
| README rewrite | Lead with the problem statement + dual-node story |

---

## Architecture Decisions (Locked)

| Decision | Choice | Rationale |
|----------|--------|----------|
| MCP transport | NuGet `ModelContextProtocol` SDK | Keep; layer tool catalog on top |
| Node clients | Separate projects | Distinct deps (Copilot pulls Azure.Identity) |
| Persistence | SQLite (migrating from LiteDB) | Aligns with MemorySmith |
| Solution name | `SplitBrain.slnx` | Rename from Orchestrator.slnx |
| Tool naming | `splitbrain_verb_noun` | All tools prefixed |
| Write safety | 3-mode gate (Disabled default) | Per-call explicit enable |
| Deployment | 2 machines minimum | A=laptop+App, B=tower+Worker |
| Semantic Kernel | Keep | Persistent session / agent planning |

---

## Two-Machine Deployment Model

```
Machine A (Laptop - coding agent host)
+-- SplitBrain.App (merged MCP + Dashboard)
|   +-- /mcp endpoint  <- external agents connect here
|   +-- /hubs/dashboard  <- live SignalR dashboard
|   +-- Ollama (Node A - fast inference, qcoder/Qwen)
+-- MemorySmith (companion - knowledge management via MCP)

Machine B (Tower - inference offload)
+-- SplitBrain.Worker (Node B relay)
    +-- Ollama (Node B - deep inference, DeepSeek R1)
```

Callers on any machine (Claude Desktop, CI, scripts) connect to Machine A's `/mcp` endpoint and get full access to both inference nodes transparently.

---

## Open Questions Remaining

1. Should `write_file` create new files, or only overwrite existing ones within allowedRoot?
2. What is the `allowed_root` default for `read_file`/`list_files`? Required per-call, or global config default?
3. Should `agent_task` with `apply_changes: true` auto-run tests, or leave that to the caller?
4. Is 24-hour temp-file TTL sufficient, or add a `delete_temp_file` tool?
5. Does `read_agent_log` need `SensitiveRead` risk tier (it contains LLM prompts/responses)?

---

## Related Documents

- `TASKS.md` - task tracker with acceptance criteria
- `docs/DIAGNOSTICS.md` - tool timeout debugging guide
- `Audit_MCP_Tool_Expansion.md` - full MCP surface audit
- `Audit_SplitBrain_vs_MemorySmith.md` - first-pass architecture audit
- `Council_Review_Pre_Implementation.md` - pre-implementation council
- `Council_Review_Pass2_Findings.md` - second-pass findings council
