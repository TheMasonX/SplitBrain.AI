# SplitBrain.AI — Architecture Overview

SplitBrain.AI is a distributed home AI orchestrator that routes inference tasks between two local machines and exposes all capabilities through MCP.

## High-Level Diagram

```
┌─────────────── Machine A (Laptop) ──────────────────────┐
│                                                          │
│  ┌──────────────────┐   ┌──────────────────────────┐    │
│  │  Orchestrator.Mcp│   │   SplitBrain.Dashboard   │    │
│  │  Port 5100 /mcp  │   │   Blazor Server UI        │    │
│  │  (HTTP + stdio)  │   │   Nodes/Metrics/Settings  │    │
│  └────────┬─────────┘   └──────────────────────────┘    │
│           │                                              │
│  ┌────────▼──────────────────────────────────────────┐  │
│  │  Orchestrator.Infrastructure                       │  │
│  │  RoutingService · NodeRegistry · InferenceQueue    │  │
│  │  HealthCheckService · MetricsCollector             │  │
│  └──────┬────────────────────────┬────────────────────┘  │
│         │                        │                        │
│  ┌──────▼──────┐         ┌───────▼──────┐                │
│  │  Node A      │         │   Node C     │                │
│  │  NodeClient  │         │  NodeClient  │                │
│  │  .Ollama     │         │  .Copilot    │                │
│  │  RTX 5060    │         │  GitHub SDK  │                │
│  │  qwen 7B Q4  │         │  gpt-4o      │                │
│  └─────────────┘         └──────────────┘                │
└──────────────────────────────┬───────────────────────────┘
                               │  HTTP (port 5050)
┌─────────────── Machine B (Tower) ────────────────────────┐
│                                                          │
│  ┌─────────────────────────────────────┐                │
│  │  Orchestrator.NodeWorker             │                │
│  │  GET /health  POST /inference        │                │
│  │  GET /models  GET /metrics           │                │
│  └──────────────┬──────────────────────┘                │
│                 │  NODE_B_BACKEND=ollama (default)       │
│         ┌───────▼───────┐  or  ┌──────────────────┐    │
│         │  NodeClient   │      │  NodeClient       │    │
│         │  .Ollama      │      │  .LlamaCpp        │    │
│         │  Port 11434   │      │  Port 8080        │    │
│         │  qwen 7B Q5   │      │  qwen3 30B MoE    │    │
│         └───────────────┘      └──────────────────┘    │
│                         GTX 1080 8 GB                   │
└──────────────────────────────────────────────────────────┘
```

## Core Principles

- **Stability over raw intelligence** — bounded deterministic agents, not open-ended loops
- **Latency isolation** — Node A for interactive work, Node B for deep inference
- **MCP-first** — all capabilities exposed as MCP tools, usable from any MCP client
- **Models as constrained compute** — VRAM-aware, queue-aware routing
- **Full observability** — structured logs, health probes, token metrics

## Component Responsibilities

| Component | Responsibility |
|-----------|---------------|
| `Orchestrator.Core` | Models, interfaces (`IInferenceNode`, `IRoutingService`), configs (`NodeConfiguration`, `RoutingOptions`), FluentValidation |
| `Orchestrator.Infrastructure` | NodeRegistry, RoutingService, InferenceQueue, HealthCheckService, MetricsCollector, AgentEventLog (LiteDB → SQLite planned) |
| `Orchestrator.Agents` | Bounded deterministic agent engine (max 4 iterations, 12K token budget, 300s timeout) |
| `Orchestrator.Mcp` | MCP server (port 5100), all MCP tools, idempotency, prompt injection guards |
| `Orchestrator.NodeWorker` | HTTP worker on Machine B — health, inference, model listing, metrics relay |
| `SplitBrain.Dashboard` | Blazor Server UI — node health, VRAM gauges, metrics charts, topology settings |
| `NodeClient.Ollama` | Ollama `/api/generate` transport for Node A and Node B |
| `NodeClient.LlamaCpp` | llama.cpp `/v1/chat/completions` OAI-compatible transport, true SSE streaming |
| `NodeClient.Copilot` | GitHub Copilot SDK transport for Node C |
| `NodeClient.Worker` | HTTP relay client for remote `Orchestrator.NodeWorker` instances |
| `Orchestrator.Tests` | NUnit 4.5 + NSubstitute + FluentAssertions, 221+ tests |

## Related Pages

- [Solution Layout](solution-layout.md)
- [Node Topology](../guides/backend-switching.md)
- [llama.cpp NodeClient Design](llamacpp-nodeclient.md)
- [Getting Started](../guides/getting-started.md)
