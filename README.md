# **SplitBrain.AI**
### *Distributed Home AI Orchestrator — MCP Server*

SplitBrain.AI transforms a pair of local machines into a coordinated, latency‑aware AI cluster. It routes tasks between **fast**, **deep**, and **cloud** inference nodes, exposes all capabilities through a single **MCP endpoint**, and executes **bounded, deterministic agents** with full observability.

---

## **Features**

- **Multi‑Node Architecture**
  Node A (local Ollama — RTX 5060), Node B (remote worker — GTX 1080),
  Node C (GitHub Copilot API — cloud).

- **Intelligent Routing Engine**
  VRAM‑aware, queue‑aware, context‑aware scoring based on node roles
  (Fast / Deep / Hybrid / Standby) with fallback chains.

- **Unified MCP Interface**
  Code review, refactoring, test generation, codebase search, patch
  application — all exposed as MCP tools on port 5100.

- **Real‑Time Dashboard**
  Blazor Server UI with live node health, latency/throughput charts,
  VRAM gauges, log viewer, and topology editor.

- **Bounded Agent System**
  Deterministic state machine with strict iteration, token, and safety limits.

- **Idempotent Tool Execution**
  TTL‑based deduplication cache prevents duplicate MCP tool calls.

- **Observability Built‑In**
  Structured logs, OpenTelemetry, node health cache, metrics collector,
  and failure replay.

- **Project Wiki**
  MemorySmith-powered browsable wiki and code search on port 6769.

---

## **Architecture**

```
SplitBrain.AI
│
├── MCP Server (port 5100)
│   ├── Routing Service
│   ├── Agent Engine
│   ├── MCP Tools (review, refactor, test-gen, search, patch)
│   └── Queue System
│
├── Dashboard (port 5000)
│   └── Blazor Server — health, metrics, logs, topology
│
├── Wiki (port 6769)
│   └── MemorySmith — browsable docs + semantic code search
│
├── Node A → Fast inference (Ollama, RTX 5060)
├── Node B → Deep inference (Worker/Gr, llama.cpp or Ollama)
└── Node C → Cloud inference (GitHub Copilot API)
```

---

## **Core Principles**

- Stability over raw intelligence  
- Latency‑sensitive tasks isolated  
- All capabilities exposed through MCP  
- Models treated as constrained compute resources  
- Agent autonomy is bounded and observable  

---

## **Use Cases**

- Local code review and refactoring  
- Test generation and validation  
- UI automation (Playwright / FlaUI)  
- Multi‑node model orchestration  
- Deterministic agent workflows  

---

## **Status**

Actively developed. Architecture and model strategy are locked; implementation is progressing through the defined phases.