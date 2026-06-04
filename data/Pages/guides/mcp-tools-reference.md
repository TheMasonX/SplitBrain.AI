# MCP Tools Reference Guide

> **Audience:** Developers and AI agents connecting to the SplitBrain.AI MCP server.
> **Service Endpoint:** `http://localhost:5100/mcp` (streamable HTTP)
> **Last Updated:** 2026-06-03

This guide covers every MCP tool exposed by the SplitBrain Orchestrator. Tools are grouped by category. Each entry includes the tool name, description, parameters, response shape, routing behaviour, and usage notes.

---

## Table of Contents

1. [Quick Start](#quick-start)
2. [Code Analysis Tools](#code-analysis-tools)
3. [Code Generation Tools](#code-generation-tools)
4. [File System Tools](#file-system-tools)
5. [Build & Test Tools](#build--test-tools)
6. [Agent System](#agent-system)
7. [Utility Tools](#utility-tools)
8. [Write Access Gate](#write-access-gate)
9. [Idempotency](#idempotency)
10. [Error Handling](#error-handling)

---

## Quick Start

### Prerequisites

- The MCP server must be running: `dotnet run --project src/Orchestrator.Mcp`
- At least one inference node configured (see [Getting Started](getting-started.md))
- For write operations: write gate must be enabled (disabled by default)

### Test the Connection

```json
{
  "tool": "splitbrain_query_allowed_root",
  "params": {}
}
```

Expected response:
```json
{
  "write_mode": "Disabled",
  "allowed_write_tools": [],
  "hint": "Write ops disabled. Set Mcp:WriteAccess:Mode in appsettings.json."
}
```

---

## Code Analysis Tools

These tools read and analyse code without modifying it. They are read-only and safe to call without write access.

### `review_code`

Reviews code for architecture, performance, bugs, readability, or security issues.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `code` | string | ✅ | Source code to review |
| `language` | string | ✅ | Programming language (e.g. `csharp`, `python`, `typescript`) |
| `focus` | string | ✅ | Review focus: `architecture` \| `performance` \| `bugs` \| `readability` \| `security` |
| `idempotencyKey` | string | ❌ | Deduplication key (5-minute TTL) |

**Routing:** Fast node (Node A) via `TaskType.Review` — low latency.
**Response shape:** `ReviewCodeResponse` with `Summary` (string), `Issues[]` (array of `ReviewIssue`), and `Meta`.

**Example usage per focus area:**

| Focus | Best For | Typical Latency |
|-------|----------|-----------------|
| `bugs` | Catching null refs, logic errors, race conditions | 1-3s |
| `security` | SQL injection, XSS, hardcoded secrets | 1-3s |
| `architecture` | Design pattern violations, SRP violations | 3-12s |
| `performance` | O(n²) loops, allocation hotspots | 2-5s |
| `readability` | Naming, complexity, missing error handling | 1-3s |

### `splitbrain_explain_code`

Explains what code does without evaluating quality. Routes to the fast node for low latency.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `code` | string | ✅ | Source code to explain |
| `language` | string | ✅ | Programming language |
| `verbosity` | string | ❌ | `brief` \| `detailed` (default) \| `eli5` |
| `idempotencyKey` | string | ❌ | Deduplication key |

**Routing:** Fast node (Node A) via `TaskType.Chat` with `QueuePriority.High`.
**Timeout:** 45 seconds.
**Response shape:** `{ explanation: string, meta: {...} }` — markdown fences stripped automatically.

**Verbosity levels:**

| Level | Use Case | Example Output Length |
|-------|----------|-----------------------|
| `brief` | Quick context, inline tooltips | 2-3 sentences |
| `detailed` | Deep understanding, debugging | Paragraph + edge case notes |
| `eli5` | Onboarding, non-technical stakeholders | Simple analogies |

### `search_codebase`

Semantically searches the codebase using glob pattern matching + LLM ranking.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `query` | string | ✅ | Natural language or symbol name to search |
| `rootPath` | string | ✅ | Absolute path to the codebase root |
| `pattern` | string | ❌ | Glob pattern (default `**/*`, e.g. `src/**/*.cs`) |
| `topK` | int | ❌ | Max results (1-20, default 10) |
| `idempotencyKey` | string | ❌ | Deduplication key |

**How it works:**
1. Uses `Microsoft.Extensions.FileSystemGlobbing.Matcher` (real glob, not fake) to collect files matching the pattern
2. Excludes `bin/`, `obj/`, `.git/`, `node_modules/`
3. Skips files >512 KB
4. Sends top 50 candidates to LLM for semantic ranking
5. Returns ranked results with file paths, line ranges, snippets, and relevance scores

**Timeout:** 60 seconds.
**Response shape:** `SearchCodebaseResponse` with `Results[]` (array of `SearchResult`).

---

## Code Generation Tools

These tools generate or transform source code using LLM inference. They are potentially slow and may require write access to apply results.

### `refactor_code`

Refactors code for a requested goal while preserving behaviour.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `code` | string | ✅ | Source code to refactor |
| `language` | string | ✅ | Programming language |
| `goal` | string | ✅ | `readability` \| `performance` \| `solid` \| `naming` \| `extract_method` \| `reduce_complexity` |
| `idempotencyKey` | string | ❌ | Deduplication key |

**Routing:** Quality node (Node B / Deep), fallback to C, then A.
**Timeout:** 45 seconds (configurable via `ToolOptions:RefactorTimeoutSeconds`).
**Output cleaning:** Markdown fences, preamble prose, and trailing explanations are automatically stripped by `ResponseCleaner.ExtractCode()`.
**Response shape:** `RefactorCodeResponse` with `Summary` (clean source code) and `Meta`.

> **Note:** The returned code is a **suggestion**. Use `splitbrain_read_file` to read the original, then use `apply_patch` or `splitbrain_write_file` to apply changes after review.

### `generate_tests`

Generates unit tests for provided source code.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `code` | string | ✅ | Source code to generate tests for |
| `language` | string | ✅ | Programming language |
| `framework` | string | ✅ | `xunit` \| `nunit` \| `pytest` \| `jest` |
| `coverage` | string | ❌ | `happy_path` \| `edge_cases` \| `error_handling` \| `all` (default) |
| `idempotencyKey` | string | ❌ | Deduplication key |

**Routing:** Quality node (Node B / Deep), fallback to C, then A.
**Timeout:** 60 seconds (test generation can be slow).
**Output cleaning:** Same `ResponseCleaner.ExtractCode()` as `refactor_code`.
**Test file naming:** Automatically derives filename from the primary class in the source code (e.g., `UserService` → `UserServiceTests.cs`). Falls back to `GeneratedTests.{ext}`.
**Response shape:** `GenerateTestsResponse` with `Files[]` (array of `GeneratedTestFile`) and `Meta`.

**Framework → Extension mapping:**

| Framework | Extension |
|-----------|-----------|
| C# / NUnit | `.cs` |
| C# / xUnit | `.cs` |
| Python / pytest | `.py` |
| TypeScript / jest | `.ts` |
| JavaScript / jest | `.js` |
| Java / JUnit | `.java` |

---

## File System Tools

These tools interact with the local filesystem. Read tools are unrestricted; write tools require write access.

### `splitbrain_read_file`

Reads file contents with security scope enforcement.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `path` | string | ✅ | Absolute path to the file |
| `allowedRoot` | string | ✅ | Security scope — rejects paths outside this directory |

**Small files (<8 KB):** Content returned inline.
**Large files (>8 KB):** Written to `%TEMP%/splitbrain/` — response includes temp path and 2 KB preview.

### `splitbrain_write_file`

Writes content to a file within allowedRoot. Creates or overwrites.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `path` | string | ✅ | Absolute path to write |
| `content` | string | ✅ | Full text content |
| `allowedRoot` | string | ✅ | Security scope |
| `enableWrite` | bool | ❌ | Authorise mutation when gate is `PerCallEnable` (default false) |

**Write gating:** Requires write gate ≠ `Disabled`. In `PerCallEnable` mode, pass `enableWrite: true`.
**Atomic writes:** Writes to `.tmp` then renames to avoid partial-write on crash.
**Directory creation:** Auto-creates parent directories.

### `splitbrain_list_files`

Lists files matching a glob pattern within a security-scoped directory.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `path` | string | ✅ | Absolute path to the directory |
| `allowedRoot` | string | ✅ | Security scope |
| `pattern` | string | ❌ | Glob pattern (default `**/*`, e.g. `src/**/*.cs`) |
| `maxResults` | int | ❌ | Max results (default 500) |

**Excluded directories:** `bin/`, `obj/`, `.git/`, `node_modules/`.
**Response shape:** `{ files: [{ path, size_bytes, modified_utc }], count, truncated }`.

### `apply_patch`

Applies a unified diff patch to a file on disk within the allowed root directory.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `filePath` | string | ✅ | Absolute path to the file to patch |
| `patch` | string | ✅ | Unified diff patch content (output of `diff -u`) |
| `allowedRoot` | string | ✅ | Security scope |
| `dryRun` | bool | ❌ | Validate without writing (default false) |
| `enableWrite` | bool | ❌ | Authorise mutation when gate is `PerCallEnable` (default false) |

**Patch format:** Standard unified diff (`diff -u`). The `UnifiedDiffApplier` parses hunks and applies add/remove operations with cumulative line-count delta. Context/removed lines must match the target file exactly.

**Workflow:**
1. Use `splitbrain_read_file` to read the original
2. Generate a diff (manually or via `refactor_code` / `agent_task`)
3. Call `apply_patch` with `dryRun: true` to validate
4. Call `apply_patch` with `dryRun: false` and `enableWrite: true` to apply

---

## Build & Test Tools

### `run_tests`

Runs a test suite and returns structured pass/fail results.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `projectPath` | string | ✅ | Absolute path to `.csproj` or solution file |
| `allowedRoot` | string | ✅ | Security scope |
| `filter` | string | ❌ | Test filter expression (e.g. `FullyQualifiedName~MyTest`) |
| `timeoutSeconds` | int | ❌ | Timeout (1-120, default 30) |

**Current behaviour:** Runs `dotnet test` with `--no-build`. For projects requiring a rebuild first, build manually before calling this tool.
**Output parsing:** Regex-based extraction of pass/fail/skipped counts and individual failure details.
**Response shape:** `RunTestsResponse` with `Summary` (total/passed/failed/skipped/duration), `Failures[]` (test name, error message, stack trace), and optional `Error`.

---

## Agent System

### `agent_task`

Runs a bounded autonomous agent loop (Plan → Implement → Review → Test) for a natural-language goal.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `goal` | string | ✅ | High-level goal in natural language |
| `workingDirectory` | string | ❌ | Absolute path for the agent to patch and test |
| `context` | string | ❌ | Additional context (stack trace, spec excerpt, etc.) |
| `applyChanges` | bool | ❌ | Apply generated diffs to disk (default false). Requires write gate |
| `idempotencyKey` | string | ❌ | Deduplication key |

**Outer timeout:** 300 seconds (entire agent loop, all iterations).
**Per-step tracking:** Each Plan/Implement/Review/Test iteration is logged with role, tokens, and response preview.

**Agent Loop (max 4 iterations, 12K tokens):**

| Phase | Role | Node | Task Type | Purpose |
|-------|------|------|-----------|---------|
| Plan | Architect | Node A | `TaskType.Chat` | Decompose goal into ordered steps |
| Implement | Coder | Node A | `TaskType.Refactor` | Generate code changes |
| Review | Reviewer | Node B | `TaskType.Review` | Check for "APPROVED" |
| Test | Tester | Node B | `TaskType.TestGeneration` | Generate + run tests in sandbox |

**Abort conditions:**
- Max iterations (4) reached
- Token budget exhausted (12,000)
- 2+ consecutive failures
- No diff produced after implement step
- Repeated identical diff (no state change)

**Diff application (when `applyChanges=true`):**
1. Write gate must not be `Disabled` (`applyChanges` acts as per-call authorisation)
2. `workingDirectory` must be set
3. Parses `+++ b/path` lines from unified diff
4. Path traversal security: rejects paths outside `workingDirectory`
5. Atomic write (`.agentpatch.tmp` + rename)
6. Per-file failure logging (doesn't abort the batch)

**Response shape:** `AgentTaskResponse` with:
- `Success`, `FinalState`, `Summary`, `Diff`
- `AppliedFiles[]` — files actually written (empty if not applied)
- `WriteGateError` — non-null when write gate blocked
- `TotalIterations`, `TotalTokens`, `AbortReason`
- `Steps[]` — detailed per-iteration breakdown

---

## Utility Tools

### `splitbrain_query_allowed_root`

Returns the current write-access mode and allowed write tools. Call this first to understand what file mutations are permitted.

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| *(none)* | | | |

**Response shape:** `{ write_mode, allowed_write_tools[], hint }`.

| Mode | Meaning |
|------|---------|
| `Disabled` | All write tools blocked. Set `Mcp:WriteAccess:Mode` in `appsettings.json` |
| `PerCallEnable` | Each write call requires `enableWrite: true` |
| `AlwaysEnabled` | All write calls permitted unconditionally |

---

## Write Access Gate

The write access gate is a **defence-in-depth** mechanism that prevents accidental or unauthorised file mutations.

### Configuration

In `appsettings.json`:

```json
{
  "Mcp": {
    "WriteAccess": {
      "Mode": "PerCallEnable",
      "AllowedWriteTools": ["apply_patch", "splitbrain_write_file", "agent_task"]
    }
  }
}
```

### Mode Behaviour

| Mode | Effect | Use Case |
|------|--------|----------|
| `Disabled` | All write tools return `write_access_disabled` | Shared/production environments, code review agents |
| `PerCallEnable` | Each call must pass `enableWrite: true` | Interactive development, controlled mutations |
| `AlwaysEnabled` | No gate check | Trusted automation, CI/CD |

### Affected Tools

- `apply_patch` — uses `enableWrite` parameter
- `splitbrain_write_file` — uses `enableWrite` parameter
- `agent_task` — uses `applyChanges` parameter (acts as write authorisation)

---

## Idempotency

Idempotency prevents duplicate execution when a client retries a tool call (e.g., on network timeout).

### How It Works

1. On first call with an `idempotencyKey`, the tool executes normally and caches the result (5-minute TTL)
2. On subsequent calls with the same key within the TTL, the cached response is returned immediately
3. If a call with the same key is already in-flight, concurrent requests are rejected (prevents double execution)
4. If the original call failed, the slot is atomically reclaimed and retried

### Supported Tools

All LLM-backed tools support idempotency via the optional `idempotencyKey` parameter:

| Tool | Parameter | Notes |
|------|-----------|-------|
| `review_code` | `idempotencyKey` | ✅ |
| `refactor_code` | `idempotencyKey` | ✅ |
| `generate_tests` | `idempotencyKey` | ✅ |
| `search_codebase` | `idempotencyKey` | ✅ |
| `agent_task` | `idempotencyKey` | ✅ |
| `splitbrain_explain_code` | `idempotencyKey` | ✅ |
| `apply_patch` | ❌ Not yet supported | See TSK-0038 |

### Best Practice

Always generate a deterministic `idempotencyKey` from the tool inputs. For example:
- SHA-256 hash of `code + language + focus` for `review_code`
- SHA-256 hash of `code + language + goal` for `refactor_code`

This ensures retries return cached results rather than re-executing.

---

## Error Handling

All tools return errors in this structure:

```json
{
  "error": {
    "code": "error_code",
    "message": "Human-readable description",
    "retryable": true,
    "details": {}
  },
  "meta": {
    "taskId": "...",
    "node": "A",
    "model": "qcoder:latest",
    "latencyMs": 1234
  }
}
```

### Common Error Codes

| Code | Meaning | Retryable |
|------|---------|-----------|
| `tool_timeout` | Tool exceeded its per-tool timeout | ✅ Usually yes |
| `validation_error` | Input validation failed (FluentValidation) | ❌ Fix input |
| `PATH_VIOLATION` | File path outside `allowedRoot` | ❌ Fix path |
| `FILE_NOT_FOUND` | Target file does not exist | ❌ Check path |
| `PATCH_FAILED` | Unified diff could not be applied | ❌ Fix patch |
| `PATCH_FAILED` | Context lines don't match target | ❌ Regenerate patch |
| `write_access_disabled` | Write gate blocking mutation | ❌ Enable write gate |
| `internal_error` | Unexpected server error | ✅ Retry |
| `TEST_RUN_FAILED` | dotnet test process failed | ❌ Fix test code |

### Timeout Behaviour

Tools that have per-tool timeouts return a structured `tool_timeout` error instead of hanging:

```json
{
  "error": {
    "code": "tool_timeout",
    "message": "refactor_code timed out after 45s. Try a smaller input or check node health.",
    "retryable": true
  },
  "meta": {
    "taskId": "abc123",
    "node": null
  }
}
```

| Tool | Timeout |
|------|---------|
| `review_code` | *(caller timeout only — no per-tool timeout yet)* |
| `refactor_code` | 45s |
| `generate_tests` | 60s |
| `search_codebase` | 60s |
| `splitbrain_explain_code` | 45s |
| `agent_task` | 300s |
| `run_tests` | Configurable (default 30s) |

---

## Tool Summary

| Tool | Status | Category | Latency Sensitive | Write Gate | Idempotent |
|------|--------|----------|-------------------|------------|------------|
| `review_code` | ✅ Operational | Analysis | Yes | No | ✅ |
| `refactor_code` | ✅ Operational | Generation | No | No | ✅ |
| `generate_tests` | ✅ Operational | Generation | No | No | ✅ |
| `search_codebase` | ✅ Operational | Analysis | No | No | ✅ |
| `apply_patch` | ✅ Operational | Filesystem | N/A | ✅ | ❌ TSK-0038 |
| `run_tests` | ✅ Operational | Build/Test | N/A | No | ❌ (local exec) |
| `agent_task` | ✅ Operational | Agent | No | ✅ (via applyChanges) | ✅ |
| `splitbrain_read_file` | ✅ Operational | Filesystem | N/A | No | ❌ (local exec) |
| `splitbrain_write_file` | ✅ Operational | Filesystem | N/A | ✅ | ❌ (local exec) |
| `splitbrain_list_files` | ✅ Operational | Filesystem | N/A | No | ❌ (local exec) |
| `splitbrain_explain_code` | ✅ Operational | Analysis | Yes | No | ✅ |
| `splitbrain_query_allowed_root`| ✅ Operational | Utility | N/A | No | ❌ (local exec) |
| `splitbrain_get_node_status` | ❌ Backlog | Infrastructure | N/A | No | — |
| `splitbrain_list_models` | ❌ Backlog | Infrastructure | N/A | No | — |
| `splitbrain_document_code` | ❌ Backlog | Generation | Yes | No | — |
| `splitbrain_git_status` | ❌ Backlog | Git | N/A | No | — |
| `splitbrain_git_diff` | ❌ Backlog | Git | N/A | No | — |
| `splitbrain_git_commit` | ❌ Backlog | Git | N/A | ✅ | — |
| `splitbrain_check_build` | ❌ Backlog | Build/Test | N/A | No | — |
