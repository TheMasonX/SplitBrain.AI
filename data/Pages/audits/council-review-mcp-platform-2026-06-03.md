# Council Review: SplitBrain.AI MCP Platform — Deep-Dive
## Branch: `next-gen` · Commit: `a6350ff7` · Date: 2026-06-03

**Scope:** What to do next; broken tool root causes; missing MCP surface; flexibility gaps  
**Gate:** PROCEED — comprehensive action plan follows  
**Seat Confidences:** Archivist 82% · Architect 79% · Practitioner 85% · Skeptic 55% · Advocate 78%

---

## 1. Seat 1 — Archivist (What's Broken and Why) | 82%

### Root cause of 4-minute timeouts (P0)

Reading the actual source code, the timeout cascade is:

```
refactor_code, generate_tests → TaskType.Refactor / TestGeneration
  → RoutingService scores NodeB highest for these task types (modelFit=1.0)
  → NodeB BaseUrl = "http://unreachable.local:11434" (default appsettings.json)
  → HttpClient.Timeout = OllamaClientOptions.TimeoutSeconds * 2 = 240s
  → 4-minute hang waiting for unreachable Node B
  → Fallback to C (Copilot) if configured, then A
```

The `appsettings.json` ships with `OllamaNodeB.BaseUrl = "http://unreachable.local:11434"`. Node B is scored highest for Refactor/TestGeneration but is unreachable. The tool hangs for `TimeoutSeconds * 2 = 240s` before the fallback chain kicks in.

**Fix:** `OllamaNodeB.TimeoutSeconds` should be `5` (fast-fail) not `120`. Or set `OllamaNodeB.BaseUrl` to `http://localhost:11434` in the default config and remove the `unreachable.local` placeholder. The DIAGNOSTICS approach (raw response logging → direct Ollama test) is the right debugging path but misdiagnoses the root cause as "JSON schema mismatch" when it's actually "network timeout before response."

### `agent_task` timeout (different root cause)

`AgentTaskTool` bypasses routing — it uses `IAgentOrchestrator.RunAsync` directly. The 4-minute timeout here is likely the agent loop itself: if the agent tries to call an LLM that returns bad output, it may spin through all 4 iterations (each with up to 3-minute inference timeout) before giving up.

### `search_codebase` partial failure

`MatchGlob` is extension-only. Pattern `src/**/*.cs` is treated identically to `*.cs`. Directory prefixes are silently ignored. For a large codebase, this means the tool searches everything instead of the specified path. The LLM-based "semantic search" is a fake semantic search — it just dumps file contents into a prompt and asks the model to score relevance. This will fail on codebases larger than the model's context window.

### Other confirmed bugs in tool layer

- `ReviewCodeResponse.Issues = []` — always empty; LLM output never parsed into structured issues
- `RefactorCodeTool` says "no markdown fences" but wraps input in fences (inconsistent contract)
- `GenerateTestsTool` output path is `Tests.{language}` (e.g. `Tests.csharp`) — invalid extension
- `AgentTaskResponse.Meta = default!` — null, crashes JSON serializer on `Meta.TaskId`
- Only `ReviewCodeTool` has structured logging; 5 other LLM tools are invisible in logs
- `InferenceRequest.UseFallback` is declared but never read by RoutingService (dead field)
- `run_tests` uses `--no-build` hardcoded — fails on unbuilt projects

---

## 2. Seat 2 — Architect (Structural Gaps) | 79%

### Missing file I/O tools — the core gap

The project calls itself a "coding agent platform" but has zero file I/O tools. A caller (Claude Desktop, CI pipeline) cannot:
- Read a file to understand context before reviewing it
- Write a new file as output from generation
- List what files exist in a directory
- Create a new file from scratch

Every coding task requires: `read → understand → generate → write → test`. SplitBrain.AI only covers the middle three steps for code already passed inline. To be a true sub-agent, it needs file I/O.

### Missing Git tools — no persistence layer

After running `apply_patch`, there's no way to commit the change. After running `run_tests`, there's no way to revert if tests fail. The "Plan → Implement → Review → Test → Commit" loop exists in `agent_task` but git operations are not exposed to external callers.

### Tool naming inconsistency

Tool names don't follow the `splitbrain_*` prefix convention documented in PLAN.md. Current names: `review_code`, `refactor_code`, etc. These collide with generic names in an MCP namespace with many servers. Renaming to `splitbrain_review_code` etc. is important for disambiguation in multi-server MCP environments.

### No write-access gate

PLAN.md specifies a 3-mode write gate (Disabled / PerCallEnable / Enabled). Currently all file writes in `apply_patch` and agent loop happen without any gate. `Disabled` should be the default, requiring explicit `apply_changes: true` opt-in.

### Hardcoded model in ReviewCodeTool

Only `ReviewCodeTool` hardcodes a model (`qwen2.5-coder:7b-instruct-q4_K_M`). Other tools leave `Model` blank and rely on routing. This inconsistency means `review_code` will always try to use that specific model string, which must exactly match an Ollama model name. If the model is pulled under a different tag, `review_code` fails silently.

---

## 3. Seat 3 — Practitioner (What an Agent Actually Needs) | 85%

This seat speaks from the perspective of an external AI agent (Claude, Copilot, a CI script) trying to use SplitBrain.AI to autonomously modify code.

### The minimal viable agent workflow

```
1. Get orientation: "what files exist in this project?"
2. Read relevant files: "show me the UserService class"
3. Understand structure: "find where IUserRepository is implemented"  
4. Generate a change: "refactor this method for SOLID"
5. Preview the diff: "what would change?"
6. Apply the change: "write it back to disk"
7. Verify: "build the project" + "run tests"
8. Commit: "commit these changes with message X"
9. Report: "here's what I did"
```

SplitBrain.AI currently supports steps 4, 6 (apply_patch), 7 (run_tests). Steps 1, 2, 3, 5, 8 are completely absent.

### What a caller must do WITHOUT these tools

Without `read_file`, the caller must:
- Paste file contents into every `review_code` / `refactor_code` call manually
- Cannot read their own output (the patched file) to verify the change
- Cannot navigate the project structure to find relevant context

This makes SplitBrain.AI a "code processor" not a "code agent." The distinction matters enormously for autonomous use.

### The `allowedRoot` friction problem

Every file-touching tool requires an `allowedRoot` parameter. For an autonomous agent, specifying this on every call is tedious. A better design: configure `allowedRoot` globally in appsettings, with per-call override. The tool should inherit the global default.

### Missing: structured error types the caller can act on

Tool errors currently return varied JSON shapes. A caller needs a consistent error envelope to:
- Know if an error is retryable
- Know if it's a configuration error vs a transient failure
- Know what parameter was wrong

`apply_patch` returns `{ Success: false, Error: { Code, Message, Retryable } }` — good. `refactor_code` just throws an exception which becomes an MCP error with no structure. All tools should use the same error envelope.

---

## 4. Seat 4 — Skeptic (Risk and Security) | 55%

### File I/O without AllowedRoot enforcement is dangerous

Any new file read/write tool MUST enforce `allowedRoot`. An agent calling `read_file("/etc/passwd")` should be rejected. `apply_patch` already does this correctly. The pattern must be consistent.

### LLM-in-the-loop for file operations is wrong

`search_codebase` pipes raw file contents into an LLM prompt and asks it to rank results. This is:
- Slow (LLM latency for what should be text search)
- Token-expensive (50× file contents per search)
- Nondeterministic (results vary by run)
- Context-limited (can't search large codebases)

Real semantic search should use the Nomic embedding model (already on Node A/B via Ollama) to embed file chunks and do vector similarity. The current implementation is a placeholder.

### `agent_task` diff never applied

Looking at `AgentTaskTool.RunAgentTaskAsync`, the tool returns a `Diff` field from the agent result but never calls `apply_patch` or writes anything to disk. The agent generates a diff but doesn't apply it. This is the "diff never applied" issue mentioned in PLAN.md. The caller would need to take the diff from the response and call `apply_patch` themselves — which requires knowing to do this.

### Timeout cascade is silent

The 4-minute NodeB timeout produces no diagnostic in the MCP response. The caller gets a successful response (eventually, from NodeA/C) with no indication that 3 minutes were wasted trying an unreachable node. The response `Meta.Node` will say "A" or "C" but there's no "tried B first, it timed out" signal.

---

## 5. Seat 5 — Advocate (What to Build Next) | 78%

### Tier 1: Fix what's broken (P0 — this week)

| Fix | File | Impact |
|-----|------|--------|
| Set `OllamaNodeB.TimeoutSeconds = 5` in appsettings | appsettings.json | Eliminates 4-min timeouts |
| Remove `--no-build` or add `-c` flag to `run_tests` | RunTestsTool.cs | Tests work on fresh checkouts |
| Fix `GenerateTestsTool` output path (`Tests.{language}` → `{className}Tests.cs`) | GenerateTestsTool.cs | Valid file paths |
| Fix `AgentTaskResponse.Meta = default!` → populate from result | AgentTaskTool.cs | No null serialization crash |
| Strip markdown fences from model output in refactor_code/generate_tests | Both tools | Clean code output |
| Add logging to all LLM tools (not just review_code) | All tools | Observability |
| Fix `MatchGlob` to use real glob (use `Microsoft.Extensions.FileSystemGlobbing`) | SearchCodebaseTool.cs | Correct scoped search |

### Tier 2: Essential missing tools (P1 — next sprint)

```
splitbrain_read_file(path, startLine?, endLine?, allowedRoot)
splitbrain_write_file(path, content, allowedRoot, createDirs?)
splitbrain_list_files(rootPath, pattern, recursive, maxResults)
splitbrain_get_structure(rootPath, depth, ignorePatterns)
splitbrain_find_symbol(name, rootPath, language?)
splitbrain_git_status(workingDir)
splitbrain_git_diff(workingDir, staged?)
splitbrain_git_commit(workingDir, message, files?, allowedRoot)
splitbrain_check_build(projectPath, allowedRoot)
splitbrain_explain_code(code, language, audience?)
```

### Tier 3: Platform completeness (P2)

```
splitbrain_get_node_status()
splitbrain_list_models()
splitbrain_embed_text(text)
splitbrain_run_command(command, workingDir, allowedRoot)
splitbrain_get_task_status(taskId)
splitbrain_cancel_task(taskId)
splitbrain_write_safety_mode()  -- query current write gate
```

### MCP annotations (apply to all tools)

Add `readOnlyHint` and `destructiveHint` to all `[McpServerTool]` attributes:
```csharp
[McpServerTool(Name = "splitbrain_read_file", ReadOnly = true)]
[McpServerTool(Name = "splitbrain_write_file", Destructive = true)]
[McpServerTool(Name = "splitbrain_apply_patch", Destructive = true)]
```

### Rename to `splitbrain_*` prefix

Prevents name collisions when multiple MCP servers are in use. The PLAN.md specifies this.

---

## 6. Comprehensive Gap Table

| Gap | Type | Priority | Fix |
|-----|------|----------|-----|
| NodeB 4-min timeout cascade | Bug | P0 | Set TimeoutSeconds=5 in appsettings |
| `agent_task` agent loop timeout | Bug | P0 | Add per-step timeout; fast-fail on first bad response |
| `apply_patch` not called by agent_task | Bug | P0 | Auto-apply diff with write gate |
| `AgentTaskResponse.Meta = default!` | Bug | P0 | Populate Meta from agent result |
| `MatchGlob` extension-only | Bug | P1 | Use FileSystemGlobbing |
| `GenerateTestsTool` path `Tests.csharp` | Bug | P1 | Use proper extension |
| `RefactorCodeTool` fence inconsistency | Bug | P1 | Strip fences from model output |
| `ReviewCodeResponse.Issues = []` | Gap | P1 | Parse LLM output into structured issues |
| No file read tool | Gap | P0 | `splitbrain_read_file` |
| No file write tool | Gap | P0 | `splitbrain_write_file` |
| No file listing tool | Gap | P0 | `splitbrain_list_files` |
| No git tool | Gap | P1 | `splitbrain_git_*` suite |
| No build check tool | Gap | P1 | `splitbrain_check_build` |
| No symbol finder | Gap | P1 | `splitbrain_find_symbol` |
| No write-access gate | Security | P1 | 3-mode gate from PLAN.md |
| No tool prefix | Protocol | P2 | Rename to `splitbrain_*` |
| No MCP annotations | Protocol | P2 | `readOnlyHint`/`destructiveHint` |
| No real semantic search | Quality | P2 | Nomic embed + vector similarity |
| No streaming responses | Quality | P2 | SSE for long tool calls |
| Hardcoded model in ReviewCodeTool | Quality | P2 | Use routing default |
| Only review_code has logging | Quality | P1 | Add ILoggingService to all tools |
| `UseFallback` dead field | Cleanup | P3 | Remove or implement |
| `run_tests --no-build` hardcoded | Bug | P1 | Add `--build` option |
| NodeB unreachable not surfaced in response | UX | P2 | Add routing path to Meta |

---

## 7. Gate Decision

**PROCEED** — The path is clear. P0 fixes (fast-fail timeout + file I/O tools) unlock the project's core value proposition. Everything else follows from that foundation.
