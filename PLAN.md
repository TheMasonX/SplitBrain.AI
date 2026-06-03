# SplitBrain.AI — Master Plan (Revised)
<!-- Council-reviewed 2026-06-03. Incorporates deep audit of all 7 tools, gap analysis, and MCP platform review. -->

## Vision

SplitBrain.AI is a two-machine inference orchestrator that acts as a **first-class MCP sub-agent platform** — an external agent (Claude, Copilot, a CI script) can hand it a goal and have it execute, review, and commit code changes autonomously using its own time and context window, with full read access and opt-in write access.

---

## Current State (as of 2026-06-03)

### Tool Status

**7 tools registered. 4 broken. 3 working.**

| Tool | Status | Root Cause |
|------|--------|-----------|
| `review_code` | ✅ Working | Returns plain-text summary in `Summary` field |
| `refactor_code` | ❌ 4-min timeout | NodeB BaseUrl = `unreachable.local`; TimeoutSeconds=240 before fallback |
| `generate_tests` | ❌ 4-min timeout | Same root cause; also: output path `Tests.{language}` is invalid |
| `agent_task` | ❌ Partial | Diff generated but never applied to disk; Meta field is null |
| `apply_patch` | ✅ Working | Pure local FS, correct path guard |
| `run_tests` | ✅ Working | `--no-build` hardcoded; allowedRoot check correct |
| `search_codebase` | ⚠️ Partial | Glob matching is extension-only stub; LLM-in-loop for "search" |

### Key Structural Issues

- **Zero file I/O tools** — no read_file, write_file, list_files, get_structure
- **Zero git tools** — no status, diff, commit
- **No write-access gate** — apply_patch writes unconditionally; agent_task doesn't write at all
- **No tool prefix** — tool names collide in multi-server MCP environments
- **No MCP annotations** — missing `readOnlyHint`/`destructiveHint`
- **NodeB timeout = 240s** — tools that prefer NodeB hang for 4 minutes before fallback

---

## Phase 0: Immediate Hotfixes (this week — NOT a sprint, just fixes)

These are one-line or few-line changes that unblock everything else.

### P0.1 Fix NodeB timeout cascade
**File:** `src/Orchestrator.Mcp/appsettings.json`
```json
"OllamaNodeB": {
  "BaseUrl": "http://unreachable.local:11434",
  "TimeoutSeconds": 5   // was 120 — fast-fail, don't wait 4 minutes
}
```
**Impact:** `refactor_code` and `generate_tests` will respond in ~10s (via NodeA fallback) instead of 4 minutes.

### P0.2 Fix agent_task diff application
**File:** `src/Orchestrator.Mcp/Tools/AgentTaskTool.cs`  
When `workingDirectory` is provided and agent loop succeeds, auto-apply the diff using the existing `UnifiedDiffApplier`. Gate behind a new `applyChanges` bool parameter (default false).

### P0.3 Fix AgentTaskResponse.Meta null
**File:** `src/Orchestrator.Mcp/Tools/AgentTaskTool.cs`  
```csharp
Meta = new Meta { TaskId = result.TaskId ?? Guid.NewGuid().ToString("N"), Node = "agent" }
```

### P0.4 Add fence-stripping to refactor_code and generate_tests
Both tools say "no markdown fences" but the model ignores this. Strip ` ``` ` blocks from `result.Text` before returning. Add to a shared `ResponseCleaner.StripFences(string)` helper.

### P0.5 Fix generate_tests output path
```csharp
Path = $"{DetectClassName(code) ?? "Tests"}.{GetTestExtension(language, framework)}"
// e.g. "UserServiceTests.cs" not "Tests.csharp"
```

### P0.6 Fix search_codebase glob
Replace `MatchGlob` with `Microsoft.Extensions.FileSystemGlobbing.Matcher` (already in the BCL on .NET 10). One-line change.

---

## Phase 1: Foundation — "Every Tool Works, File I/O Exists"

**Goal:** All 7 registered tools respond correctly. 6 new file/git tools added. Write gate implemented.  
**Target:** 4 weeks  
**Branch:** `feature/phase-1-foundation`

### P1 Critical Path

```
Phase 0 hotfixes (fast-fail NodeB, fence-strip, glob fix)
  ↓
File I/O tools (read_file, write_file, list_files)
  ↓  
Write-access gate (3-mode config)
  ↓
Git tools (status, diff, commit)
  ↓
Build check tool
  ↓
Fix agent_task to use file I/O tools internally
```

### New Tools — File I/O

```csharp
[McpServerTool(Name = "splitbrain_read_file", ReadOnly = true)]
Task<string> ReadFileAsync(
    string path,
    string allowedRoot,
    int? startLine = null,
    int? endLine = null)

[McpServerTool(Name = "splitbrain_write_file", Destructive = true)]
Task<string> WriteFileAsync(
    string path,
    string content,
    string allowedRoot,
    bool createDirectories = false,
    bool dryRun = false)

[McpServerTool(Name = "splitbrain_list_files")]
Task<string> ListFilesAsync(
    string rootPath,
    string pattern = "**/*",
    int maxResults = 100,
    bool includeSize = true,
    bool includeModified = true)

[McpServerTool(Name = "splitbrain_get_structure")]
Task<string> GetStructureAsync(
    string rootPath,
    int depth = 3,
    string[]? ignorePatterns = null)   // defaults: ["bin/","obj/","node_modules/",".git/"]
```

### New Tools — Git

```csharp
[McpServerTool(Name = "splitbrain_git_status")]
Task<string> GitStatusAsync(string workingDir)

[McpServerTool(Name = "splitbrain_git_diff")]
Task<string> GitDiffAsync(
    string workingDir,
    string? file = null,
    bool staged = false)

[McpServerTool(Name = "splitbrain_git_commit", Destructive = true)]
Task<string> GitCommitAsync(
    string workingDir,
    string message,
    string[]? files = null,    // null = stage all tracked changes
    string allowedRoot = "")
```

### New Tools — Build

```csharp
[McpServerTool(Name = "splitbrain_check_build")]
Task<string> CheckBuildAsync(
    string projectPath,
    string allowedRoot,
    string configuration = "Debug",
    bool restore = false)
```

### Write-Access Gate (3 modes)

Config: `SplitBrain:WriteAccess:Mode`  
- `"Disabled"` (default) — all write operations return `WRITE_DISABLED` error
- `"PerCallEnable"` — write ops require `applyChanges: true` parameter
- `"Enabled"` — write ops execute without extra flag

Each write tool checks the gate before executing. The gate status is returned in `Meta.WriteMode` on every response.

### Phase 1 Exit Criteria

1. All 7 existing tools respond within declared timeout
2. `agent_task` with `applyChanges: true` writes files to disk
3. `read_file` → `refactor_code` → `write_file` chain works end-to-end
4. `git_commit` creates a commit in the working directory
5. Write gate enforced in Disabled mode (default)
6. CI test suite covers each tool's happy path + write-gate rejection + timeout

---

## Phase 2: Read Surface — "Agent Can Navigate and Understand the Codebase"

**Goal:** An external agent can fully navigate, read, and understand any codebase through MCP alone.  
**Target:** 6 weeks after Phase 1

### New Tools

| Tool | Purpose |
|------|---------|
| `splitbrain_find_symbol` | Find class/function/method by name across rootPath |
| `splitbrain_explain_code` | "What does this do?" — distinct from review |
| `splitbrain_get_node_status` | Live cluster health (Node A/B/C status, VRAM, queue) |
| `splitbrain_list_models` | Available models across all nodes |
| `splitbrain_generate_docs` | Generate docstrings/JSDoc/XML doc from code |

### Protocol Improvements

- All tools renamed to `splitbrain_*` prefix (breaking change — version bump)
- MCP tool annotations (`readOnlyHint`, `destructiveHint`) on all tools
- Standard error envelope on ALL tools (currently inconsistent):
  ```json
  { "success": false, "error": { "code": "X", "message": "...", "retryable": false }, "meta": {...} }
  ```
- Temp-file envelope for outputs >8KB: write to `%TEMP%/splitbrain/{taskId}.json`, return path in response
- `allowedRoot` global default in appsettings, per-call override

### Phase 2 Exit Criteria

1. External agent completes `list_files → read_file → refactor_code → write_file → check_build → run_tests → git_commit` chain end-to-end
2. All tools use `splitbrain_*` prefix
3. All tools have MCP annotations
4. Consistent error envelope across all tools

---

## Phase 3: Operational Polish — "Production-Ready, Observable, Streamable"

**Goal:** Observable, controllable, streamable. CI-pipeline ready.  
**Target:** 8 weeks after Phase 2

### New Capabilities

| Item | Purpose |
|------|---------|
| `splitbrain_embed_text` | Expose Nomic Embed (already on Node A/B via Ollama) |
| `splitbrain_get_task_status` | Poll running task state (for long agent loops) |
| `splitbrain_cancel_task` | Propagate cancellation to in-flight inference |
| `splitbrain_run_command` | Sandboxed arbitrary command execution (with allowedRoot) |
| Real semantic search | Replace LLM-in-loop with Nomic embed + SQLite-vec similarity |
| SSE streaming | Progressive output for long reviews/refactors (not blocked 240s wait) |
| `notifications/progress` | Agent iteration counter via MCP progress notifications |
| `routing://decisions` resource | Routing decision log readable over MCP |

### Real Semantic Search Architecture

Replace `search_codebase`'s current "feed files to LLM, ask for ranking" with:
1. Background indexer: chunk files → embed with `nomic-embed-text` → store in SQLite-vec (`data/Graph/code-search/code-search.db`)
2. Query time: embed query → cosine similarity → return top-K chunks
3. Optionally: LLM re-ranks the top-K for precision

This matches MemorySmith's architecture and reuses the same infrastructure.

---

## Phase 4: Architectural Differentiation — "The Two-Node Killer Feature"

**Goal:** The dual-node architecture has a compelling use case that requires two nodes.  
**Target:** Post-Phase-3

### `splitbrain_multi_agent_task`

The dual-node "killer feature." One tool invocation spawns two concurrent agents:
- **Implementer** → pinned to `NodeRole.Fast` (Node A, Qwen Q4) — generates code fast
- **Reviewer** → pinned to `NodeRole.Deep` (Node B, DeepSeek R1) — deep reasoning critique

Iterate until Reviewer approves or max iterations. The fast node generates; the quality node validates. This is the highest-value use case for the dual-node architecture and the unique differentiation vs. single-node alternatives.

---

## Architecture Decisions (All Locked)

| Decision | Choice | Rationale |
|----------|--------|-----------|
| MCP transport | NuGet `ModelContextProtocol` SDK | Keep; layer tool catalog on top |
| Node clients | Separate projects | Distinct deps; not all deployments need all clients |
| Persistence | SQLite (migrating from LiteDB) | Aligns with MemorySmith |
| Solution name | `SplitBrain.slnx` | Renamed from Orchestrator.slnx |
| Tool naming | `splitbrain_verb_noun` | All tools prefixed; Phase 2 rename |
| Write safety | 3-mode gate (Disabled default) | Per-call explicit enable |
| Deployment | 2 machines minimum | A=laptop+App, B=tower+Worker |
| Semantic Kernel | Keep | Persistent session / agent planning |
| Ollama embeddings | `nomic-embed-text` via Ollama | Already pulled; use for semantic search |
| NodeB timeout | 5s fast-fail | Prevents 4-minute cascade; proper fallback |

---

## Full MCP Tool Surface (Current + Planned)

### Currently Working
| Tool | Phase | Type | Notes |
|------|-------|------|-------|
| `review_code` | 0 | LLM | Works; needs logging, fence handling |
| `apply_patch` | 0 | Local | Works; needs write gate |
| `run_tests` | 0 | Local | Works; needs --build option |

### Currently Broken (P0 fix)
| Tool | Phase | Type | Fix |
|------|-------|------|-----|
| `refactor_code` | 0 | LLM | NodeB timeout + fence strip |
| `generate_tests` | 0 | LLM | NodeB timeout + fence strip + path fix |
| `agent_task` | 0 | Agent | Diff not applied + Meta null |
| `search_codebase` | 0 | LLM+Local | Fake glob + LLM-in-loop |

### Phase 1 (New)
| Tool | Type | MCP Hint |
|------|------|----------|
| `splitbrain_read_file` | Local | ReadOnly |
| `splitbrain_write_file` | Local | Destructive |
| `splitbrain_list_files` | Local | ReadOnly |
| `splitbrain_get_structure` | Local | ReadOnly |
| `splitbrain_git_status` | Local | ReadOnly |
| `splitbrain_git_diff` | Local | ReadOnly |
| `splitbrain_git_commit` | Local | Destructive |
| `splitbrain_check_build` | Local | ReadOnly |

### Phase 2 (New)
| Tool | Type | MCP Hint |
|------|------|----------|
| `splitbrain_find_symbol` | Local | ReadOnly |
| `splitbrain_explain_code` | LLM | ReadOnly |
| `splitbrain_get_node_status` | Local | ReadOnly |
| `splitbrain_list_models` | Local | ReadOnly |
| `splitbrain_generate_docs` | LLM | ReadOnly |

### Phase 3 (New)
| Tool | Type | MCP Hint |
|------|------|----------|
| `splitbrain_embed_text` | LLM | ReadOnly |
| `splitbrain_get_task_status` | Local | ReadOnly |
| `splitbrain_cancel_task` | Local | — |
| `splitbrain_run_command` | Local | Destructive |

### Phase 4 (New)
| Tool | Type | MCP Hint |
|------|------|----------|
| `splitbrain_multi_agent_task` | Agent | Destructive |

**Total planned surface: 26 tools** across 4 phases.

---

## Two-Machine Deployment Model

```
Machine A (Laptop — coding agent host)
+-- SplitBrain.App (MCP + Dashboard merged)
|   +-- /mcp endpoint  ← external agents connect here
|   +-- /hubs/dashboard  ← live SignalR dashboard
|   +-- Node A (Ollama — fast inference, Qwen Q4/Q5)
+-- MemorySmith (companion — knowledge management via MCP)
    +-- Uses nomic-embed-text for semantic code search

Machine B (Tower — inference offload)
+-- SplitBrain.Worker (NodeWorker)
    +-- Node B (Ollama or llama.cpp — deep inference, DeepSeek R1 / Qwen3 30B MoE)
```

Callers (Claude Desktop, CI, scripts) connect to Machine A's `/mcp` endpoint and get full access to both inference nodes transparently.

---

## Open Questions

1. Should `splitbrain_write_file` create new files, or only overwrite existing ones within allowedRoot?
2. What is the `allowedRoot` default? Required per-call (current), or global config default with per-call override?
3. Should `agent_task` with `applyChanges: true` auto-run tests, or leave that to the caller?
4. Is 24-hour temp-file TTL sufficient, or add `splitbrain_delete_temp_file`?
5. Should `splitbrain_run_command` be exposed at all (high risk), or is `check_build` + `run_tests` sufficient?
6. Does `splitbrain_read_agent_log` need `SensitiveRead` risk tier?
7. When should NodeB timeout be 5s vs configurable? Should this be a topology config field?

---

## Related Documents

- `docs/DIAGNOSTICS.md` — tool timeout debugging guide (3-step investigation)
- `data/Pages/audits/council-review-project-wide-2026-06-02.md` — Sprint 1 project audit
- `data/Pages/audits/council-review-mcp-platform-2026-06-03.md` — this review
- `data/Pages/plans/sprint-1-2026-06-02.md` — completed Sprint 1
