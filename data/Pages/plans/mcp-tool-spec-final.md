# SplitBrain.AI — Definitive MCP Tool Surface Specification
## Council-reviewed 2026-06-03 · Sources: Internal audit + 2 external agents + live test data

**Total planned surface: 30 tools · 7 resources · 3 prompts**  
**Current surface: 7 tools (4 broken) · 3 resources (unverified) · 0 prompts**

---

## Existing Tools — Fix Before Adding Anything New

### ✅ `review_code` — OPERATIONAL
- **MCP Name:** `review_code` → rename to `splitbrain_review_code` (Phase 2)
- **Annotation:** `readOnlyHint: true`
- **Routing:** Fast node (Node A), latency matters
- **Model:** `qcoder:latest` (analysis model — works well)
- **Fix needed:** Remove hardcoded model string; add logging parity with other tools; parse LLM output into structured `Issues[]` array (not just `Summary`)
- **Parameters:** `code`, `language`, `focus` (bugs|performance|security|readability|architecture), `idempotencyKey?`

### ❌ `refactor_code` — BROKEN (4-min timeout)
- **MCP Name:** `refactor_code` → rename to `splitbrain_refactor_code` (Phase 2)
- **Annotation:** `destructiveHint: true` (modifies code)
- **Routing:** Quality node (Node B / Deep), then C, then A
- **Model:** `qwen2.5-coder:7b-instruct` (instruction-following required for code gen)
- **Fixes needed:** 
  1. `OllamaNodeB.TimeoutSeconds = 5` (eliminates 4-min hang) 
  2. Strip markdown fences from model output
  3. Add per-tool 30s CancellationToken with structured error
  4. Add raw response logging before deserialization
  5. Enforce JSON output contract in prompt OR parse plain text correctly
- **Parameters:** `code`, `language`, `goal` (readability|performance|solid|naming|extract_method|reduce_complexity), `idempotencyKey?`

### ❌ `generate_tests` — BROKEN (4-min timeout)
- **MCP Name:** `generate_tests` → rename to `splitbrain_generate_tests` (Phase 2)
- **Annotation:** `readOnlyHint: true` (generates tests but doesn't write files without write_file)
- **Routing:** Quality node, then C, then A
- **Model:** `qwen2.5-coder:7b-instruct` (instruction-following required)
- **Fixes needed:** Same as `refactor_code` + fix output path from `Tests.{language}` to `{ClassName}Tests.{ext}`
- **Parameters:** `code`, `language`, `framework` (xunit|nunit|pytest|jest), `coverage` (all|happy_path|edge_cases|error_handling), `idempotencyKey?`

### ❌ `agent_task` — BROKEN (timeout + diff not applied + Meta null)
- **MCP Name:** `agent_task` → rename to `splitbrain_agent_task` (Phase 2)
- **Annotation:** `destructiveHint: true` (writes files when `applyChanges: true`)
- **Routing:** Quality node (reasoning model), then C, then A
- **Model:** `deepseek-r1:7b` or similar reasoning model (multi-step requires it)
- **Fixes needed:**
  1. Same timeout fix (NodeB fast-fail)
  2. `AgentTaskResponse.Meta` — populate from agent result (currently `default!` = null crash)
  3. Auto-apply diff when `applyChanges: true` AND write gate allows
  4. Emit `notifications/progress` at each iteration boundary
  5. Cap single-iteration timeout at 30s
- **Parameters:** `goal`, `workingDirectory?`, `context?`, `applyChanges` (bool, default false, requires write gate ≠ Disabled)
- **New field:** Return `agentId` for status polling via `splitbrain_get_task_status`

### ✅ `apply_patch` — OPERATIONAL
- **MCP Name:** `apply_patch` → rename to `splitbrain_apply_patch` (Phase 2)
- **Annotation:** `destructiveHint: true`
- **Routing:** Local (no LLM needed)
- **Fix needed:** Add write-gate check; `dryRun: true` should work without write gate
- **Parameters:** `filePath`, `patch` (unified diff), `allowedRoot`, `dryRun?`

### ✅ `run_tests` — OPERATIONAL
- **MCP Name:** `run_tests` → rename to `splitbrain_run_tests` (Phase 2)
- **Annotation:** `readOnlyHint: true` (reads filesystem, doesn't modify)
- **Routing:** Local (spawns dotnet process)
- **Fix needed:** Add `--build` option (currently hardcodes `--no-build`); add framework selector
- **Parameters:** `projectPath`, `allowedRoot`, `filter?`, `timeoutSeconds?`, `build?` (bool, default false)

### ⚠️ `search_codebase` — PARTIAL (fake glob)
- **MCP Name:** `search_codebase` → rename to `splitbrain_search_codebase` (Phase 2)
- **Annotation:** `readOnlyHint: true`
- **Routing:** LLM-in-loop for ranking (replace with Nomic embedding in Phase 3)
- **Fix needed:** Replace `MatchGlob` with `Microsoft.Extensions.FileSystemGlobbing.Matcher`
- **Parameters:** `query`, `rootPath`, `pattern?`, `topK?`, `idempotencyKey?`

---

## Phase 1 — New Tools (Next Sprint)

### `splitbrain_read_file`
- **Purpose:** Read file contents within allowed root. The most critical missing primitive.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (no LLM)
- **Parameters:** `path` (string), `allowedRoot` (string), `startLine?` (int), `endLine?` (int)
- **Notes:** Return line-numbered output. Cap at 500 lines by default, with `maxLines` override. Reject path traversal.

### `splitbrain_write_file`
- **Purpose:** Write or create a file within allowed root.
- **Annotation:** `destructiveHint: true`
- **Routing:** Local (no LLM)
- **Write gate:** Requires write gate ≠ Disabled
- **Parameters:** `path` (string), `content` (string), `allowedRoot` (string), `createDirectories?` (bool, default false), `dryRun?` (bool)
- **Notes:** Return diff of what changed vs. previous content. If file doesn't exist and `createDirectories: false`, return error.

### `splitbrain_list_files`
- **Purpose:** List files matching a glob pattern with size and modification time metadata.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local
- **Parameters:** `rootPath` (string), `pattern?` (glob, default `**/*`), `maxResults?` (int, default 100), `includeSize?` (bool), `includeModified?` (bool)
- **Notes:** Uses `Microsoft.Extensions.FileSystemGlobbing.Matcher`. Respects `.gitignore` if present.

### `splitbrain_get_structure`
- **Purpose:** Return directory tree as a compact ASCII diagram (like `tree` command).
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local
- **Parameters:** `rootPath` (string), `depth?` (int, default 3), `ignorePatterns?` (string[], default `["bin/","obj/","node_modules/",".git/"]`)
- **Notes:** Return structured JSON + ASCII representation. Cap total items at 500.

### `splitbrain_explain_code`
- **Purpose:** Explain what code does in plain language. Distinct from `review_code` (which finds problems). Most frequent IDE use case.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Fast node (latency-sensitive — developer waiting)
- **Model:** `qcoder:latest` (analysis, not generation)
- **Parameters:** `code` (string), `language` (string), `verbosity?` (`brief`|`detailed`|`eli5`, default `brief`)

### `splitbrain_document_code`
- **Purpose:** Generate inline documentation (XML doc for C#, JSDoc for TS/JS, docstrings for Python, Javadoc for Java).
- **Annotation:** `readOnlyHint: true`
- **Routing:** Quality node (documentation quality matters)
- **Model:** `qwen2.5-coder:7b-instruct`
- **Parameters:** `code` (string), `language` (string), `style?` (`xml`|`jsdoc`|`docstring`|`javadoc`), `includeExamples?` (bool, default false)

### `splitbrain_git_status`
- **Purpose:** Return working tree status (staged/unstaged/untracked files).
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (no LLM — shells out to `git status`)
- **Parameters:** `workingDir` (string), `allowedRoot` (string)
- **Notes:** Return structured JSON: `{ staged: [...], unstaged: [...], untracked: [...] }`. Reject if outside allowedRoot.

### `splitbrain_git_diff`
- **Purpose:** Return working tree diff or diff between refs.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local
- **Parameters:** `workingDir` (string), `allowedRoot` (string), `file?` (string), `staged?` (bool, default false), `ref1?` (string), `ref2?` (string)

### `splitbrain_git_commit`
- **Purpose:** Stage and commit specified files (or all tracked changes) with a commit message.
- **Annotation:** `destructiveHint: true`
- **Routing:** Local
- **Write gate:** Requires write gate ≠ Disabled
- **Parameters:** `workingDir` (string), `message` (string), `allowedRoot` (string), `files?` (string[], null = all tracked changes), `dryRun?` (bool)
- **Notes:** Return commit SHA on success. Include pre-commit diff summary in response.

### `splitbrain_check_build`
- **Purpose:** Run `dotnet build` (or equivalent) and return structured pass/fail with errors.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (no LLM — spawns build process)
- **Parameters:** `projectPath` (string), `allowedRoot` (string), `configuration?` (string, default `Debug`), `restore?` (bool, default false)
- **Notes:** Return `{ success: bool, errors: [...], warnings: [...], meta: { latencyMs, ... } }`

### `splitbrain_get_node_status`
- **Purpose:** Return live health for one or all inference nodes: VRAM, queue depth, loaded models, latency, circuit breaker state.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (reads from INodeHealthCache)
- **Parameters:** `nodeId?` (string, null = all nodes)

### `splitbrain_list_models`
- **Purpose:** Return all models across all nodes with live availability (loaded/available/unavailable) vs. static registry.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local
- **Parameters:** none

---

## Phase 2 — Architecture Tools

### `splitbrain_route_task` (dry run)
- **Purpose:** Run the routing scoring algorithm and return the full decision without executing inference. "Which node/model would handle this, and why?"
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (no LLM — runs scoring)
- **Parameters:** `taskType` (string), `requiredCapabilities?` (string[]), `minContextWindow?` (int), `routingPolicy?` (`LatencyFirst`|`QualityFirst`|`BalancedLoad`)
- **Notes:** Return `{ winnerNodeId, winnerModel, allCandidateScores: [...], policyApplied, decisionMs }`

### `splitbrain_plan_task`
- **Purpose:** Decompose a complex goal into an ordered step list — without executing any steps. Returns the plan for human review before `agent_task` executes it.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Quality node (reasoning model)
- **Model:** `deepseek-r1:7b` or equivalent reasoning model
- **Parameters:** `goal` (string), `context?` (string), `maxSteps?` (int, default 10)

### `splitbrain_get_task_status`
- **Purpose:** Given a taskId, return current status, routing decision, node/model, elapsed time.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (reads task registry)
- **Parameters:** `taskId` (string)
- **Notes:** Returns `{ status: Routing|Inferring|Complete|Failed|Cancelled, ... }`

### `splitbrain_cancel_task`
- **Purpose:** Cancel a running task or agent by taskId. Propagates CancellationToken through pipeline.
- **Annotation:** idempotent
- **Routing:** Local
- **Parameters:** `taskId` (string)

### `splitbrain_inspect_agent`
- **Purpose:** Return live state of a running agent: iteration count, token usage, tool calls made, current role (Planner/Implementer/Reviewer).
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (reads from agent orchestrator)
- **Parameters:** `agentId` (string)

### `splitbrain_get_metrics`
- **Purpose:** Return aggregate performance metrics: total requests, p50/p95/p99 latency by tool type, fallback rate, circuit breaker events, token usage by model. For CI pipelines and monitoring.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (reads from IMetricsCollector)
- **Parameters:** `since?` (DateTime), `tool?` (string filter)

### `splitbrain_find_symbol`
- **Purpose:** Find classes, functions, methods, or interfaces by name across the codebase using text search (not LLM — use grep/ripgrep internally).
- **Annotation:** `readOnlyHint: true`
- **Routing:** Local (no LLM)
- **Parameters:** `name` (string), `rootPath` (string), `language?` (string), `symbolType?` (`class`|`method`|`interface`|`all`)

---

## Phase 3 — Semantic + Differentiation

### `splitbrain_embed_text`
- **Purpose:** Embed text using the Nomic Embed Text model (already loaded in Ollama). Returns a vector. Enables external callers to build their own semantic features.
- **Annotation:** `readOnlyHint: true`
- **Routing:** Fast node (embeddings are fast)
- **Model:** `nomic-embed-text` (already in Ollama)
- **Parameters:** `text` (string), `model?` (string, default `nomic-embed-text`)

### `splitbrain_run_command`
- **Purpose:** Execute an arbitrary shell command within an allowed directory. High risk, high value.
- **Annotation:** `destructiveHint: true`
- **Write gate:** Requires write gate = Enabled (strictest)
- **Parameters:** `command` (string), `workingDir` (string), `allowedRoot` (string), `timeoutSeconds?` (int, default 30), `env?` (dict)
- **Notes:** Never execute with elevated privileges. Block common danger patterns (rm -rf, sudo, etc.).

### `splitbrain_multi_agent_task`
- **Purpose:** Spawn Implementer (Fast node) + Reviewer (Deep node) in an iterative loop. The dual-node killer feature.
- **Annotation:** `destructiveHint: true`
- **Routing:** SPLIT — Implementer pins to NodeRole.Fast, Reviewer pins to NodeRole.Deep
- **Write gate:** Requires write gate ≠ Disabled if writing
- **Parameters:** `goal` (string), `context?` (string), `maxIterations?` (int, default 3), `workingDirectory?` (string), `implementerModel?` (string override), `reviewerModel?` (string override), `applyChanges?` (bool)

---

## MCP Resources (7 total)

| Resource | Status | Purpose |
|----------|--------|---------|
| `nodes://health` | ✅ Exists | Live node health |
| `models://registry` | ✅ Exists | Static model definitions |
| `tasks://history` | ✅ Exists | Past task log |
| `routing://decisions` | 🆕 Phase 2 | Recent routing decisions for debugging |
| `agents://active` | 🆕 Phase 2 | Live running agents |
| `metrics://realtime` | 🆕 Phase 2 | OTel meter data |
| `embeddings://search` | 🆕 Phase 3 | Raw semantic search via Nomic |

---

## MCP Prompts (3 total — Phase 1)

### `code-review-structured`
Pre-fill: language, focus area, severity threshold. Ensures consistent review format.  
```
Review the following {{language}} code focusing on {{focus}} issues.
Severity threshold: {{threshold}} (critical|high|medium|low+).
Return findings as a numbered list ordered by severity.
```

### `test-generation-comprehensive`  
Pre-fill: language, framework, coverage target.  
```
Generate {{coverage}} {{framework}} tests for the following {{language}} code.
Include: setup/teardown, {{coverage}}-path cases, mock patterns appropriate for {{framework}}.
```

### `refactor-preserve-behavior`
Behavioral preservation constraint baked in.  
```
Refactor the following {{language}} code for {{goal}}.
CRITICAL CONSTRAINT: The refactored code must preserve all observable behavior exactly.
Include a before/after comparison of the changed contract if any.
```

---

## Protocol Improvements (all phases)

| Item | Phase | Notes |
|------|-------|-------|
| Tool annotations (readOnlyHint, destructiveHint) | P1 | One-time, add to all `[McpServerTool]` attributes |
| Full JSON Schema on all inputSchema | P2 | Types, enums, min/max, required fields |
| Per-tool 30s CancellationToken timeout | P0 | Structured error instead of hanging |
| Write-access gate (3 modes) | P1 | Disabled (default) / PerCallEnable / Enabled |
| Progress notifications for agent_task | P2 | `notifications/progress` at each iteration |
| SSE streaming for long tool calls | P3 | Forward IAsyncEnumerable from Ollama |
| Tool rename to `splitbrain_*` prefix | P2 | Breaking change — coordinate with callers |
| Per-tool model routing policy | P2 | Analysis→qcoder, gen→qwen2.5-instruct, reasoning→deepseek |
| `allowedRoot` global default in config | P1 | Per-call override still available |

---

## Tool Count Summary

| Phase | Tools Added | Cumulative |
|-------|-------------|------------|
| P0 hotfixes | 0 new (7 fixed) | 7 |
| Phase 1 | +12 new | 19 |
| Phase 2 | +7 new | 26 |
| Phase 3 | +3 new | 29 |
| + 7 resources + 3 prompts = **39 total MCP items** | | |

---

*Synthesized 2026-06-03 from: internal codebase audit, live MCP evaluation (Claude Sonnet 4.6), NexGenMasterPlan-aware agent recommendations.*
