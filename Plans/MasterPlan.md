# Distributed Home AI Orchestrator

## Final Technical Specification (Model-Constrained + Implementable)

---

# 0. Project Metadata

| Field | Value |
| ----- | ----- |
| **Repository** | https://github.com/TheMasonX/SplitBrain.AI |
| **Default branch** | `main` |
| **Root path** | `C:\@Repos\Visual Studio Projects\SplitBrain.AI\` |
| **IDE** | Visual Studio 2026 (18.5.0) |
| **Target framework** | .NET 10 |
| **Shell** | PowerShell |

---

# 1. Core Design Principles

1. **Stability > raw intelligence**
2. **Latency-sensitive tasks are isolated** (Node A never blocks)
3. **All capabilities exposed through MCP** (single system boundary)
4. **Models treated as constrained compute resources**
5. **Agent autonomy is bounded and observable**

---

# 2. Hardware Allocation

| Node       | Device | GPU          | Role                        |
| ---------- | ------ | ------------ | --------------------------- |
| **Node A** | Laptop | RTX 5060 8GB | Interactive + Orchestration |
| **Node B** | Tower  | GTX 1080 8GB | Deep inference + validation |
| **Node C** | Cloud  | —            | GitHub Copilot API (enterprise fallback) |

Node C: **GitHub Copilot API** (cloud inference, enterprise-secure)

---

# 3. Model Strategy (Locked)

## 3.1 Node A — Low Latency

**Primary Model**

* Qwen 2.5 Coder 7B
* Quant: **Q4_K_M (GGUF via Ollama)**

**Purpose**

* Autocomplete
* Chat
* Agent orchestration

---

## 3.2 Node B — Deep Tasks

**Primary Model**

* Qwen 2.5 Coder 7B
* Quant: **Q5_K_M**

**Fallback**

* DeepSeek Coder 6.7B — Q4_K_M

---

## 3.3 Node C — Cloud / Enterprise

**Primary Model**

* GitHub Copilot API (`gpt-4o`)
* Accessed via **`GitHub.Copilot.SDK`** NuGet package (v0.2.2)
  * SDK wraps the GitHub Copilot CLI via stdio/TCP — not a raw HTTP client
  * Requires the Copilot CLI (`gh copilot`) installed and in PATH on the Node C host,
    or `CopilotNode:CliPath` pointing to the binary, or `CopilotNode:CliUrl` targeting a pre-running server

**Purpose**

* Fallback when both local nodes are busy or unavailable
* Deep review, refactor, agent steps at work (enterprise account)

**SDK Session Model**

* Each `ExecuteAsync` call opens a fresh, single-turn `CopilotSession` (infinite sessions disabled)
* Streaming is wired through `AssistantMessageDeltaEvent`; final content from `AssistantMessageEvent`
* Health probe uses `PingAsync()`
* `CopilotClient` is `IAsyncDisposable` — `StopAsync` + `DisposeAsync` called on shutdown

**API Key / Auth Resolution Order**

1. Azure Key Vault — `DefaultAzureCredential` (managed identity, workload identity, Azure CLI, Visual Studio)
   * Config key: `CopilotNode:KeyVaultUri`
   * Secret name: `CopilotNode:KeyVaultSecretName` (default: `CopilotApiKey`)
   * Token passed to SDK as `CopilotClientOptions.GitHubToken`
2. Environment variable `COPILOT_API_KEY` → same `GitHubToken` path
3. GitHub CLI logged-in user (`gh auth login`) — SDK default when no token is supplied
* Raw tokens are **never** stored in config files or source control

**Config Keys (`CopilotNode` section)**

| Key | Purpose | Default |
| --- | ------- | ------- |
| `KeyVaultUri` | Azure Key Vault URI | *(unset)* |
| `KeyVaultSecretName` | Secret name in Key Vault | `CopilotApiKey` |
| `Model` | Chat model identifier | `gpt-4o` |
| `TimeoutSeconds` | Per-request timeout | `60` |
| `CliPath` | Path to Copilot CLI binary | *(SDK default / `COPILOT_CLI_PATH`)* |
| `CliUrl` | URL of pre-running CLI server | *(unset — SDK spawns its own)* |

---

## 3.4 Embeddings (Required)

* Model: `nomic-embed-text`
* Runs on Node A (CPU preferred)

---

## 3.5 Removed Components

* vLLM → **Removed**

  * Reason: poor Pascal support, instability risk

---

# 4. Ollama Configuration (Critical Section)

This is where most real-world failures happen—so we make it explicit.

---

## 4.1 Node A (RTX 5060)

### Environment Variables

```bash
OLLAMA_NUM_PARALLEL=2
OLLAMA_MAX_LOADED_MODELS=1
OLLAMA_FLASH_ATTENTION=1
OLLAMA_KV_CACHE_TYPE=q8_0
OLLAMA_GPU_LAYERS=999
OLLAMA_HOST=0.0.0.0
```

---

### Model Pull + Run

```bash
ollama pull qwen2.5-coder:7b-instruct-q4_K_M
```

---

### Modelfile (Optional Fine-Tune)

```text
FROM qwen2.5-coder:7b-instruct-q4_K_M

PARAMETER temperature 0.2
PARAMETER top_p 0.9
PARAMETER num_ctx 16384
PARAMETER repeat_penalty 1.1
PARAMETER stop "<|im_end|>"
```

---

### Launch

```bash
ollama serve
```

---

## 4.2 Node B (GTX 1080)

### Environment Variables

```bash
OLLAMA_NUM_PARALLEL=1
OLLAMA_MAX_LOADED_MODELS=1
OLLAMA_FLASH_ATTENTION=0
OLLAMA_KV_CACHE_TYPE=q8_0
OLLAMA_GPU_LAYERS=999
OLLAMA_HOST=0.0.0.0
```

---

### Model Pull

```bash
ollama pull qwen2.5-coder:7b-instruct-q5_K_M
```

---

### Conservative Modelfile

```text
FROM qwen2.5-coder:7b-instruct-q5_K_M

PARAMETER temperature 0.1
PARAMETER top_p 0.9
PARAMETER num_ctx 12288
PARAMETER repeat_penalty 1.15
```

---

### Notes (Important)

* Flash attention disabled (Pascal instability)
* Lower context to avoid VRAM fragmentation
* Single concurrency enforced

---

# 5. Distributed Architecture

---

## 5.1 Core Components

```text
MCP Server (System Boundary)
│
├── Routing Service
├── Agent Engine
├── API Layer
├── Queue System
│
├── Node A Client → Ollama (fast)
├── Node B Client → Ollama (deep)
└── Node C Client → GitHub Copilot API (cloud, enterprise-secure)
```

---

## 5.2 Node Interface

```csharp
public interface IInferenceNode
{
    string NodeId { get; }
    NodeCapabilities Capabilities { get; }

    Task<InferenceResult> ExecuteAsync(InferenceRequest request);
    Task<NodeHealth> GetHealthAsync();
}
```

---

## 5.3 Health Model

```text
Healthy
Degraded (slow / high VRAM)
Unavailable
```

Heartbeat: every **2 seconds**

Timeouts:

* Node A: 5s
* Node B: 20s

---

# 6. Intelligent Routing System

---

## 6.1 Task Types

```csharp
enum TaskType
{
    Autocomplete,
    Chat,
    Review,
    Refactor,
    TestGeneration,
    AgentStep
}
```

---

## 6.2 Scoring Function

```csharp
score =
    (0.35 * availableVramRatio) +
    (0.25 * (1 / queueDepth)) +
    (0.20 * modelFitScore) +
    (0.10 * latencyPenalty) +
    (0.10 * contextFitScore);
```

---

## 6.3 Hard Routing Rules

| Condition                    | Action                           |
| ---------------------------- | -------------------------------- |
| Autocomplete                 | Force Node A                     |
| Context > 5k tokens          | Prefer Node B or C               |
| Node B queue > 2             | fallback to Node A or C          |
| Node B unavailable           | fallback to Node A or C          |
| Both A + B unavailable       | fallback to Node C (Copilot API) |

---

# 7. Queue System

---

## 7.1 Structure

* Node A Queue (priority: high)
* Node B Queue (priority: normal)

---

## 7.2 Behavior

* FIFO + priority override
* Timeout-aware
* Backpressure:

  * Node B overloaded → reroute

---

# 8. MCP Tool Interface (Canonical API)

All capabilities exposed through MCP:

```text
review_code
refactor_code
generate_tests
run_tests
query_ui
apply_patch
search_codebase
```

---

## 8.1 Example Tool

```csharp
[McpServerTool(Name = "review_code")]
public async Task<string> Review(string code, string focus)
{
    var request = new InferenceRequest
    {
        TaskType = TaskType.Review,
        Payload = code,
        Metadata = focus
    };

    return await routingService.RouteAsync(request);
}
```

---

## 8.2 Requirements

* Streaming responses
* Cancellation support
* Structured output (JSON where applicable)

---

# 9. Agent System (Bounded)

---

## 9.1 Framework

* Semantic Kernel

---

## 9.2 State Machine

```text
INIT
→ PLAN
→ IMPLEMENT
→ REVIEW
→ TEST
→ DONE | FAIL
```

---

## 9.3 Limits

* Max iterations: **4**
* Max tokens per loop: **12k**
* Abort if:

  * no code diff
  * repeated failure
  * no state change

---

## 9.4 Model Assignment

| Role      | Node |
| --------- | ---- |
| Architect | A    |
| Coder     | A    |
| Reviewer  | B    |
| Tester    | B    |

---

# 10. UI Automation Layer

---

## 10.1 Tools

* Playwright (Web)
* FlaUI (WPF)

---

## 10.2 Required Translation Layer

Convert UI → semantic JSON:

```json
{
  "screen": "Settings",
  "elements": [
    { "type": "checkbox", "label": "Enable Logs", "checked": true }
  ]
}
```

---

# 11. Observability

---

## 11.1 Structured Logs

```json
{
  "taskId": "...",
  "node": "B",
  "model": "qwen7b",
  "tokensIn": 3200,
  "tokensOut": 700,
  "latencyMs": 2800,
  "success": true
}
```

---

## 11.2 Required Features

* Prompt history (last N)
* Failure replay
* Node health dashboard
* Token + latency metrics

---

# 12. Fault Tolerance

---

| Scenario      | Behavior             |
| ------------- | -------------------- |
| Node B fails  | fallback to Node A   |
| Node B slow   | queue or reroute     |
| Model crash   | restart + retry once |
| Agent failure | halt + surface       |

---

# 13. Security & Isolation

---

Minimum safeguards:

* Run generated code in:

  * isolated process
  * restricted directory
* Kill long-running tasks (>30s)
* No unrestricted shell access

---

# 14. Implementation Phases

---

## Phase 1 — Foundation

* MCP server
* Node clients
* Routing system
* Ollama setup (both nodes)
* Logging + queues

---

## Phase 2 — Stability

* Model tuning
* Fallback handling
* Dashboard

---

## Phase 3 — Agents

* State machine
* Controlled loop
* Code patching

---

## Phase 4 — Validation ⏸ AWAITING INITIAL TESTING APPROVAL

> **Status**: Blocked — work on this phase cannot begin until initial testing approval is granted.

* Playwright (Web)
* FlaUI + serializer (WPF)

---

# 15. Deployment & Automation

---

## 15.1 Node Provisioning Scripts

| Script | Target | Idempotent |
| ------ | ------ | ---------- |
| `deploy/setup-node-a.ps1` | Node A — RTX 5060, Laptop | Yes |
| `deploy/setup-node-b.ps1` | Node B — GTX 1080, Tower  | Yes |

Each script (run as Administrator):

* Installs .NET SDK (Node A) or Runtime only (Node B)
* Installs Ollama
* Applies the Ollama env vars from ss4 (machine-scope, persistent)
* Pulls required models for that node
* Registers a persistent Windows service
* Creates log directory at `C:\ProgramData\SplitBrain.AI\logs\node-{a|b}\`

---

## 15.2 Publish Profiles

| Profile | Project | RID | Output |
| ------- | ------- | --- | ------ |
| `NodeA.pubxml` | `Orchestrator.Mcp`        | `win-x64` | `publish/node-a/` |
| `NodeB.pubxml` | `Orchestrator.NodeWorker` | `win-x64` | `publish/node-b/` |

Both: self-contained, single-file, ReadyToRun, compressed debug symbols.

---

## 15.3 Per-Node Configuration

| File | Project | Purpose |
| ---- | ------- | ------- |
| `appsettings.json`        | Both | Shared defaults (localhost:11434) |
| `appsettings.NodeA.json`  | `Orchestrator.Mcp` | Node A endpoint, 30s timeout |
| `appsettings.NodeB.json`  | `Orchestrator.NodeWorker` | Node B endpoint, 120s timeout |

Key: `OllamaNode:BaseUrl` — always overridable via environment variable.

---

## 15.4 CI Workflow

File: `.github/workflows/ci.yml`

| Job | Depends on | Action |
| --- | ---------- | ------ |
| `build-test`      | —              | Restore, Build, Test (uploads TRX) |
| `publish-node-a`  | `build-test`   | Publishes MCP server artifact       |
| `publish-node-b`  | `build-test`   | Publishes Node Worker artifact      |

No automatic deployment. Artifacts are downloaded manually and installed via setup scripts.

---

## 15.5 Manual Deployment Procedure

### Node A

```
1. Pull CI artifact: node-a-mcp-server
2. Extract to publish\node-a\ in the repo root
3. On Node A (as Administrator):
   .\\deploy\\setup-node-a.ps1

First run  : installs .NET SDK, Ollama, pulls models, registers service
Re-run     : stops service, updates binary, restarts — skips what is already present
```

### Node B

```
1. Pull CI artifact: node-b-worker
2. Copy publish\node-b\ to Node B
3. On Node B (as Administrator):
   .\\deploy\\setup-node-b.ps1

First run  : installs .NET Runtime, Ollama, pulls q5_K_M + fallback model, registers service
Re-run     : stops service, updates binary, restarts — skips what is already present
```
