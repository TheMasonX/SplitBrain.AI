# **SplitBrain.AI**
### *Distributed Home AI Orchestrator*

SplitBrain.AI transforms a pair of local machines into a coordinated, latency‑aware AI cluster. It routes tasks between a **fast, low‑latency node** and a **deep, high‑context node**, exposes all capabilities through **MCP**, and executes **bounded, deterministic agents** with full observability.

---

## **Features**

- **Dual‑Node Architecture**  
  Node A handles interactive workloads; Node B handles deep inference and long‑context reasoning.

- **Intelligent Routing Engine**  
  VRAM‑aware, queue‑aware, context‑aware scoring ensures tasks always land on the optimal node.

- **Unified MCP Interface**  
  Code review, refactoring, test generation, UI automation, and more — all exposed as MCP tools.

- **Bounded Agent System**  
  Deterministic state machine with strict iteration, token, and safety limits.

- **Observability Built‑In**  
  Structured logs, node health, token metrics, and failure replay.

- **Ollama‑Backed Models**  
  Qwen 2.5 Coder 7B (Q4/Q5), DeepSeek fallback, and Nomic embeddings.

---

## **Architecture Overview**

```
SplitBrain.AI
│
├── MCP Server
│   ├── Routing Service
│   ├── Agent Engine
│   ├── API Layer
│   └── Queue System
│
├── Node A → Fast inference (RTX 5060)
└── Node B → Deep inference (GTX 1080)
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