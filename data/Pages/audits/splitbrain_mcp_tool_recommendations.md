# SplitBrain.AI — MCP Tool Surface Analysis & Recommendations

**Date:** 2026-06-02  
**Source:** Live tool testing + NexGenMasterPlan.md (v1.0, April 2026, 1,259 lines)  
**Current surface:** 6 tools · 3 resources · 0 prompts  
**Proposed surface:** 19 tools · 7 resources · 3 prompts  

---

## Part 1 — What the Architecture Already Reveals

The NexGenMasterPlan is one of the most carefully designed home AI orchestration blueprints I've seen at this scale. A few things stand out immediately that frame every recommendation:

**The existing tool surface is incomplete by design.** The plan explicitly names `CodeReview`, `Refactor`, `GenerateTests`, `RunAgent`, `Embed`, and `Chat` as MCP tools — but the testing shows only `review_code` is operational. The others are registered but broken (likely a response schema deserialization issue — see Part 2). So the first job is not designing new tools; it's making the planned ones work.

**The embedding infrastructure is already in place but unexploited.** Nomic Embed Text is a first-class citizen in the model registry and fallback chains, but there's no `search_code` tool or `embeddings://search` resource that actually puts that embedding capability to work. You're paying the VRAM cost of loading Nomic and not using it through MCP.

**The agent system is the most powerful and least exposed primitive.** The `agent_task` tool is supposed to run a bounded Plan → Implement → Review → Test loop, but it's currently broken and also underspecified in terms of observability. A caller has no way to inspect a running agent's state, cancel it, or get progress updates mid-execution. That's a gap even once it's repaired.

**The routing engine is invisible to MCP callers.** The VRAM-aware, queue-aware, latency-aware scoring algorithm is an engineering achievement, but a client using MCP tools has no way to ask "what's the best node for this task?" or "why did my last call go to Node B?" Both of those are useful primitives for debugging and for clients that want to make routing-aware decisions.

---

## Part 2 — Fix the Broken Tools First

Before adding anything new, three of the six existing tools need to be repaired. They all fail identically: 4-minute hard timeout with no error response.

### Root Cause Hypothesis

`review_code` works because it returns a **text summary** — plain string analysis. The three broken tools (`refactor_code`, `generate_tests`, `agent_task`) all require **code generation as output** — they need to return modified source code, test files, or agent-patched implementations.

The hypothesis is a response contract mismatch: the Orchestrator.Mcp handler likely expects a structured JSON payload like `{ "code": "..." }` but `qcoder:latest` returns plain text or markdown-fenced output. The deserializer blocks waiting for a valid response that never arrives.

### Diagnostic Steps

**Step 1 — Read the raw Ollama response before deserialization.**

Add a log statement immediately after the Ollama call completes, before any JSON parsing:

```csharp
// In your Ollama inference handler (SplitBrain.MCP or SplitBrain.Agents)
var rawResponse = await ollamaNode.GenerateAsync(request, ct);
_logger.LogDebug("Raw Ollama response for {ToolName}: {Response}", toolName, rawResponse);
// Then attempt deserialization...
```

Check `%TEMP%/splitbrain-mcp-[date].log`. If you see the log entry, the bug is in parsing. If you see nothing after the request is dispatched, the model itself is hanging.

**Step 2 — Test the Ollama endpoint directly.**

Create a `.http` file (or use curl) to send the exact prompt your `refactor_code` handler would send. If Ollama responds locally but the MCP tool times out, the bug is definitely in the handler. Example:

```http
POST http://localhost:11434/api/generate
Content-Type: application/json

{
  "model": "qcoder:latest",
  "prompt": "Refactor this C# code for readability:\npublic string FormatName(string first, string last, bool upper) {\n  if (upper == true) { return first.ToUpper() + \" \" + last.ToUpper(); }\n  else { return first + \" \" + last; }\n}\nReturn only the refactored code, no explanation.",
  "stream": false
}
```

**Step 3 — Enforce JSON output format.**

If the handler expects structured JSON, the system prompt for these tools must explicitly instruct the model to respond only in JSON. Add this to the generative tool prompts:

```
Respond ONLY with valid JSON matching this schema and nothing else — no preamble, no markdown fences, no explanation:
{"code": "<refactored source code here>", "explanation": "<one sentence summary>"}
```

**Step 4 — Consider a model swap for generative tools.**

`qcoder:latest` performs well on analysis tasks (review_code). It may not follow structured output instructions reliably. Consider routing generative tools to a different model:

- `qcoder:latest` → `review_code`, `explain_code` (analysis → text output)
- `qwen2.5-coder:7b-instruct` → `refactor_code`, `generate_tests`, `document_code` (instruction-following → code output)
- `deepseek-r1:7b` → `agent_task`, `plan_task` (multi-step reasoning)

This is exactly what the routing engine and fallback chains were designed to support — wire each tool type to the model best suited for it.

**Step 5 — Add a per-tool timeout with a structured error response.**

Replace the 4-minute wall timeout with a 30-second configurable timeout that returns a structured error instead of hanging:

```csharp
using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
cts.CancelAfter(TimeSpan.FromSeconds(30));

try {
    var result = await ExecuteToolAsync(request, cts.Token);
    return result;
} catch (OperationCanceledException) {
    _logger.LogWarning("Tool {ToolName} timed out after 30s for task {TaskId}", toolName, taskId);
    return new ToolResult { Error = "Tool timed out. Try a smaller input or check node health." };
}
```

---

## Part 3 — New Tools: Developer Workflow

These fill the gap between code analysis (what `review_code` already does) and the full developer workflow loop.

### `explain_code`

**What it does:** Explains what a piece of code does — its purpose, behavior, and key logic — without evaluating quality. Distinct from `review_code`, which looks for problems.

**Why it matters:** The most frequent use case in an IDE integration. A developer pastes an unfamiliar function and wants "what does this do?" not "what's wrong with it?" Currently there's no MCP tool for this.

**Routing hint:** `LatencyFirst` — callers expect fast responses. Route to `qcoder:latest` on Node A.

**Parameters:**
```json
{
  "code": "string",
  "language": "string",
  "verbosity": "brief | detailed | eli5"
}
```

---

### `document_code`

**What it does:** Generates inline documentation for code — XML doc comments for C#, JSDoc for TypeScript/JavaScript, Python docstrings, or Javadoc for Java.

**Why it matters:** Completely mechanical and tedious for a developer; ideal for an LLM. Natural complement to `refactor_code`. After refactoring, you want documentation. Closes the gap between "the code works" and "the code is maintainable."

**Routing hint:** `QualityFirst` — documentation quality matters more than speed.

**Parameters:**
```json
{
  "code": "string",
  "language": "string",
  "style": "xml | jsdoc | docstring | javadoc",
  "include_examples": "boolean"
}
```

---

### `apply_patch`

**What it does:** Takes a unified diff and applies it to a file on disk within an allowed root directory. The missing "write" side of the current "generate" capability.

**Why this is critical:** Right now, `refactor_code` (when it works) generates a modified version of the code but has no way to actually save it. `generate_tests` can produce test files but can't create them. Without `apply_patch` or `write_file`, the entire generative tool surface produces output that a human must manually copy and paste. That severely limits agent autonomy.

**Security note:** This tool must enforce an allowlist of root paths. The MCP handler should reject any path that traverses outside the configured allowed roots (no `../` escapes).

**Parameters:**
```json
{
  "patch": "string (unified diff format)",
  "root_path": "string (must be within allowed roots)",
  "dry_run": "boolean (validate without applying)"
}
```

---

### `run_tests`

**What it does:** Executes a test suite (via `dotnet test`, `pytest`, `jest`, `cargo test`, etc.) in a specified directory and returns structured results: pass count, fail count, individual test names and outcomes, and stdout/stderr.

**Why it matters:** This closes the `generate_tests → run_tests` loop. Generating tests without running them is half the value. An `agent_task` using `generate_tests` followed by `run_tests` can validate that the generated tests actually pass — or iterate if they don't. This is the engine of the Plan → Implement → Review → Test agent loop.

**Routing hint:** This tool doesn't call Ollama at all — it's a shell execution tool. Should bypass the routing engine entirely and execute directly on the host.

**Parameters:**
```json
{
  "project_path": "string",
  "framework": "dotnet | pytest | jest | cargo",
  "filter": "string (optional test name filter)",
  "timeout_seconds": "number"
}
```

---

### `search_code`

**What it does:** Takes a natural language query and returns semantically similar code snippets from an indexed codebase. Uses the Nomic Embed Text model already in the model registry to embed the query, then searches a pre-built vector index.

**Why this matters now:** You already have Nomic loaded. You're paying the 550MB VRAM cost. There's no MCP surface that uses it for search. This tool makes the embedding investment visible to callers and enables a genuinely useful workflow: "find all places in the codebase where we handle authentication errors" returns relevant code snippets without requiring exact text matching.

**Implementation note:** Needs a background indexing step that builds a vector index from the codebase at startup (or incrementally on file change). The index can live as an in-process `IVectorStore` or be persisted to LiteDB (already planned for Phase 5).

**Parameters:**
```json
{
  "query": "string (natural language)",
  "root_path": "string",
  "top_k": "number (default 5)",
  "file_extensions": ["cs", "ts", "py"] 
}
```

---

### `read_file` / `write_file`

**What they do:** Read or write a file on disk within allowed paths. Foundational filesystem primitives for agent tasks.

**Why they're essential:** Without these, `agent_task` can never read the source file it's supposed to review or write the refactored version back. Every agent task currently operates on code passed as a string parameter — it can't discover or modify files autonomously. These two tools are table stakes for any meaningful agent autonomy.

**Security note:** Same allowlist enforcement as `apply_patch`. The allowed roots should be configured in `appsettings.json` and validated on every call.

---

## Part 4 — New Tools: Orchestration Visibility

These expose the routing engine and cluster state to MCP callers. Currently that information is only visible in the Blazor dashboard; it should also be queryable via MCP.

### `route_task` (dry run)

**What it does:** Given a `TaskRequirements` payload, runs the scoring algorithm and returns the full routing decision — winning node+model, all candidate scores, the policy applied — without actually executing anything.

**Why it matters:** Invaluable for debugging. When a caller wants to understand why their task went to Node B instead of Node A, or wants to pre-validate that a node is available before dispatching a long-running task, this is the answer. Also useful for testing routing policy changes without live inference.

**Parameters:**
```json
{
  "required_capabilities": ["Chat", "CodeGeneration"],
  "min_context_window": 8192,
  "routing_policy": "LatencyFirst | QualityFirst | BalancedLoad"
}
```

---

### `get_node_status`

**What it does:** Returns live node health for one or all nodes: VRAM used/total, queue depth, loaded models, recent latency, circuit breaker state.

**Why it matters:** A caller integrating SplitBrain.AI into an IDE extension or CI pipeline may want to check node health before dispatching a batch of tasks. Currently this information only exists in the Blazor dashboard; exposing it via MCP lets external clients make informed decisions.

---

### `list_models`

**What it does:** Returns all models across all nodes with their availability status: which are currently loaded in VRAM (affinity = 100), which are available to load, which are unavailable.

**Complements `models://registry`:** The resource gives the static model definitions from config. This tool gives the live availability cross-reference. The distinction matters when VRAM is constrained and models are swapped in and out.

---

### `get_task_status`

**What it does:** Given a `taskId` (returned by any tool invocation), returns the current status: `Routing | Inferring | Complete | Failed`, the node and model used, elapsed time, and the routing decision.

**Why it matters:** Long-running `agent_task` calls currently have no progress visibility. A caller dispatching an agent task has no way to check on it. This tool enables polling-based progress monitoring until streaming notifications are implemented.

---

### `get_metrics`

**What it does:** Returns aggregate performance metrics from the OpenTelemetry meters: total requests, p50/p95/p99 latency by tool type, fallback rate, circuit breaker events, and token usage by model.

**Complements the Blazor dashboard:** Dashboard is for humans watching a browser. `get_metrics` is for CI pipelines, alerting integrations, and programmatic monitoring that can't open a browser.

---

## Part 5 — New Tools: Agent Capability

### `plan_task`

**What it does:** Given a complex goal, decomposes it into a step sequence and returns the plan — without executing any steps. The "thinking" phase of the agent loop, exposed as a standalone primitive.

**Why separate from `agent_task`:** Sometimes a caller wants to review and approve a plan before execution, especially for destructive operations (file writes, shell execution). `plan_task` → human review → `agent_task` with the plan attached is a safer and more controllable workflow than fully autonomous execution.

**Routing hint:** `QualityFirst`, routed to DeepSeek R1 for reasoning quality. This is exactly the task DeepSeek was chosen for.

---

### `inspect_agent`

**What it does:** Given an active `agentId`, returns the current state machine state, iteration count, token usage so far, and the tool calls made in each iteration.

**Why it matters:** The current agent system is a black box during execution. An agent that's on iteration 7 of 10 with 45,000 of 50,000 tokens consumed is approaching its limits — a caller should be able to see that and intervene if needed. This also makes the bounded, deterministic agent system observable, which is one of the stated core design goals of the project.

---

### `cancel_task`

**What it does:** Cancels a running task or agent by `taskId`. Propagates a `CancellationToken` through the Polly pipeline and Ollama inference call.

**Why it matters:** MCP supports cancellation natively via `notifications/cancelled`. Without a `cancel_task` tool, a caller that changes their mind about a long-running agent has no recourse except closing the connection and hoping the server cleans up. Given the bounded agent's iteration and token limits, a cancellation mechanism is important for graceful resource recovery.

---

### `multi_agent_task`

**What it does:** Spawns two agents with distinct roles — a Reviewer and an Implementer — that iterate against each other. The Implementer generates code; the Reviewer critiques it; the Implementer revises based on the critique; repeat for N iterations or until the Reviewer approves.

**Why this is architecturally natural:** The dual-node system was designed for exactly this use case. Route the Implementer to Node A (fast, Qwen Q5) and the Reviewer to Node B (deep, DeepSeek R1). The fast node generates; the quality node validates. This is the highest-value use case for the dual-node architecture and isn't exposed anywhere in the current tool surface.

**Parameters:**
```json
{
  "goal": "string",
  "context": "string",
  "max_iterations": "number (default 3)",
  "implementer_model": "optional model override",
  "reviewer_model": "optional model override"
}
```

---

### `git_diff` / `git_status` / `git_commit`

**What they do:** Local git operations within an allowed repository path. `git_diff` returns the working tree diff (or diff between refs). `git_status` returns staged/unstaged/untracked files. `git_commit` stages and commits specified files with a message.

**Why they matter for agents:** An agent that can read files, refactor code, run tests, and then commit the result is genuinely useful as an autonomous coding assistant. Without git operations, the agent can only produce output — it can't close the loop. These three operations are the minimum viable git surface for agent workflows.

---

## Part 6 — New Resources

Resources are the read-only data streams MCP exposes to callers. The three current resources (`nodes://health`, `models://registry`, `tasks://history`) cover the basics. Four more would complete the picture:

### `routing://decisions`

The routing engine produces a full `RoutingDecision` record for every task — node selected, model selected, all candidate scores, policy applied, decision duration. Currently this lives in Serilog logs and the Blazor dashboard. Exposing it as a resource lets MCP callers replay recent routing decisions and debug why a task landed where it did.

### `agents://active`

A live view of all currently executing agents: their `agentId`, goal, current iteration, node+model assigned, and token usage. Pairs with `inspect_agent` for detailed per-agent drill-down.

### `metrics://realtime`

The OpenTelemetry meter data exposed as a resource. Enables external monitoring tools to pull the same metrics the Blazor dashboard shows. Particularly useful for integrating SplitBrain.AI into an existing home lab observability stack (Grafana, Prometheus, etc.).

### `embeddings://search`

Exposes the Nomic embedding index as a queryable resource. A caller passes a query string and gets back semantically similar document chunks. Distinct from the `search_code` tool — this resource is a raw semantic search endpoint that can be used against any indexed corpus (code, documentation, logs, notes).

---

## Part 7 — MCP Prompts (the Missing Primitive)

The NexGenMasterPlan doesn't mention MCP Prompts at all — this is the most underutilized MCP primitive. Prompts are reusable, parameterized templates that a client (Claude Desktop, IDE extensions) can list and invoke by name. They're not tool calls; they're message templates that get injected into the conversation.

Three prompts would immediately improve the caller experience:

### `prompts/code-review-structured`

Generates a structured code review request pre-filled with language, focus area, and severity threshold parameters. A caller selects this prompt from the IDE, fills in the parameters, and gets a consistently formatted review every time.

### `prompts/test-generation-comprehensive`

A test generation template that pre-fills framework, coverage target (happy path, edge cases, all), and language. Ensures that every test generation request includes the constraints the model needs to produce useful output.

### `prompts/refactor-preserve-behavior`

A refactoring template that explicitly includes a behavioral preservation constraint. The most common failure mode in LLM-assisted refactoring is subtle semantic changes. Making "preserve observable behavior" a first-class parameter in the prompt template addresses this directly.

---

## Part 8 — Protocol-Level Improvements

Beyond new tools, several MCP protocol features are either missing or underused:

### Streaming tool responses

The NexGenMasterPlan uses `Streamable HTTP` transport, which supports SSE streaming. But tool responses currently return in one block. For `review_code` (especially the architecture focus that takes 12+ seconds), streaming tokens as they arrive would dramatically improve perceived responsiveness. The `Ollama.GenerateAsync` method already returns `IAsyncEnumerable<string>` — the MCP layer just needs to forward that stream.

### Progress notifications

`agent_task` should emit `notifications/progress` at each iteration boundary. The MCP spec defines this:

```json
{"method": "notifications/progress", "params": {"progressToken": "...", "progress": 3, "total": 10}}
```

This gives callers a live iteration counter without polling `get_task_status`.

### Input validation via JSON Schema

Every tool's `inputSchema` should be fully specified with types, required fields, enums for constrained values, and min/max for numeric parameters. This enables IDE clients to generate UI for tool invocation and gives the server an early validation layer before hitting Ollama.

### Tool annotations

The MCP spec supports `annotations` on tool definitions — `readOnlyHint`, `destructiveHint`, `idempotentHint`, `openWorldHint`. These help callers make trust decisions. Mark `review_code`, `explain_code`, `get_node_status`, etc. as `readOnlyHint: true`. Mark `apply_patch`, `write_file`, `git_commit` as `destructiveHint: true`. This is low-effort and high-value for any client doing automated tool selection.

### Per-tool routing policy defaults

Each tool should declare its default routing policy in its definition, so the routing engine can apply the right weights without the caller needing to specify it:

| Tool | Default Policy | Rationale |
|---|---|---|
| `review_code` | `QualityFirst` | Quality matters more than speed for code review |
| `explain_code` | `LatencyFirst` | Developer waiting at keyboard; fast response wins |
| `refactor_code` | `QualityFirst` | Correctness over speed for code modification |
| `generate_tests` | `QualityFirst` | Test coverage quality is the whole point |
| `chat` | `LatencyFirst` | Interactive; latency is the primary experience |
| `embed` | `LatencyFirst` | Embeddings are fast by nature; don't over-route them |
| `agent_task` | `QualityFirst` | Deep multi-step tasks; route to the deep node |
| `plan_task` | `QualityFirst` | Reasoning task; DeepSeek R1 is the right call |
| `search_code` | `LatencyFirst` | Query-time embedding should be fast |

---

## Part 9 — The Dual-Node Use Case Gap

The most important architectural gap: **the dual-node system doesn't have a compelling MCP-accessible use case that actually requires two nodes.**

Right now, all MCP tool calls can theoretically be served by either node (routing just picks the better one). There's no tool that *requires* the deep node for quality, and no tool that *requires* the fast node for latency — the routing engine handles it transparently.

The opportunity is `multi_agent_task`: a workflow that explicitly uses Node A for fast iterative generation and Node B for deep reasoning and critique. This is the killer app for the architecture. It's the use case that makes "dual-node orchestrator" meaningful rather than just "smart load balancer."

Concrete implementation: `multi_agent_task` with `routing_hint: "split"` pins the Implementer role to a `Fast` node and the Reviewer role to a `Deep` node. The MCP caller sees one tool invocation; under the hood it's a choreography across both nodes.

---

## Summary: Priority Order

**P0 — Fix before anything else:**
1. Diagnose `refactor_code`, `generate_tests`, `agent_task` timeouts (raw response logging)
2. Fix response schema contract (enforce JSON output in prompts or fix deserializer)
3. Swap model for generative tools if `qcoder:latest` isn't instruction-following

**P1 — High leverage, low friction:**
4. `explain_code` — fills the most common IDE use case
5. `get_node_status` + `list_models` — make the cluster visible over MCP
6. `read_file` / `write_file` — unlock real agent autonomy
7. Streaming tool responses — massive UX improvement for long reviews
8. Tool annotations (`readOnlyHint`, `destructiveHint`) — one-time, high-signal

**P2 — Complete the developer loop:**
9. `document_code` — natural complement to existing tools
10. `apply_patch` — closes the generate → apply gap
11. `run_tests` — closes the generate_tests → validate loop
12. `get_task_status` + `cancel_task` — observable, controllable agents
13. MCP Prompts (3 templates) — parameterized caller experience

**P3 — Architectural differentiation:**
14. `multi_agent_task` — the killer dual-node use case
15. `search_code` — monetize the Nomic embedding investment
16. `plan_task` + `inspect_agent` — deeper agent control surface
17. `routing://decisions` + `metrics://realtime` resources — close the observability loop
18. Progress notifications for agent tasks
19. Git tools — full autonomous coding loop

---

*Report generated by Claude Sonnet 4.6 via SplitBrain.AI MCP integration — 2026-06-02*
