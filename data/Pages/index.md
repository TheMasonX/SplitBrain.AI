# SplitBrain.AI — MCP Server Wiki

Welcome to the **SplitBrain.AI project wiki**, powered by MemorySmith.
This wiki serves as the persistent knowledge base for the SplitBrain
multi-node MCP inference orchestration system.

## What is SplitBrain.AI?

**SplitBrain.AI** is an **MCP server** that routes LLM inference requests across
heterogeneous backends:
- **Node A** — Local Ollama
- **Node B** — Remote worker (Ollama or llama.cpp)
- **Node C** — GitHub Copilot API

It provides intelligent load balancing, fallback chains, circuit breakers,
and a suite of MCP tools for code review, refactoring, test generation,
codebase search, and patch application — all surfaced through a single
MCP endpoint.

## Project Structure

| Directory | Purpose |
|-----------|---------|
| `src/Orchestrator.Core/` | Interfaces, models, enums, configuration |
| `src/Orchestrator.Infrastructure/` | Routing, registry, health, metrics, queue |
| `src/Orchestrator.Agents/` | Agent system with Semantic Kernel planner |
| `src/Orchestrator.Mcp/` | **MCP server** — tool registration, idempotency |
| `src/SplitBrain.Dashboard/` | Real-time Blazor dashboard |
| `src/NodeClient.*/` | Client wrappers (Ollama, llama.cpp, Copilot, Worker) |
| `src/Orchestrator.NodeWorker/` | Remote worker HTTP API |
| `src/Orchestrator.Tests/` | NUnit test suite |
| `data/` | Wiki content and memory store |

## Quick Links

- [Architecture overview](architecture/overview.md)
- [Solution layout](architecture/solution-layout.md)
- [Deployment guide](guides/deployment.md)
- [Configuration reference](ops/configuration-reference.md)
- [Testing guide](../Docs/UserDocs/testing-guide.md)

## Key Documents

- [Master Plan v4](../Plans/MasterPlanV4.md) — canonical architecture blueprint
- [Design decisions](../Docs/Memories/design-decisions.md) — ADRs
- [Current state](../Docs/Memories/architecture-current-state.md) — what's implemented

## Service Endpoints

| Service | Port |
|---------|------|
| Wiki (this page) | **6769** |
| MCP Server | **5100** |
| Dashboard | **5000** |
| Node Worker | **5001** |

## MCP Tools Reference

The MCP server (`Orchestrator.Mcp` at port **5100**) exposes **12 operational tools**
across 5 categories. See the [MCP Tools Reference Guide](guides/mcp-tools-reference.md)
for complete documentation, parameters, response shapes, and usage examples.

| Category | Tools | Status |
|----------|-------|--------|
| **Code Analysis** | `review_code`, `splitbrain_explain_code`, `search_codebase` | ✅ Operational |
| **Code Generation** | `refactor_code`, `generate_tests` | ✅ Operational |
| **File System** | `splitbrain_read_file`, `splitbrain_write_file`, `splitbrain_list_files`, `apply_patch` | ✅ Operational |
| **Build & Test** | `run_tests` | ✅ Operational |
| **Agent** | `agent_task` | ✅ Operational |
| **Utility** | `splitbrain_query_allowed_root` | ✅ Operational |
| **Backlog** | `splitbrain_get_node_status`, `splitbrain_list_models`, `splitbrain_document_code`, `splitbrain_git_status`, `splitbrain_git_diff`, `splitbrain_git_commit`, `splitbrain_check_build`, MCP Prompts (3) | 📋 Planned |

## MCP Tools

The MCP server (`Orchestrator.Mcp`) exposes these tools:
- `review_code` — Architecture, performance, bug, readability, or security review
- `refactor_code` — AI-driven code refactoring
- `generate_tests` — Unit test generation
- `search_codebase` — Semantic code search across the repo
- `apply_patch` — Apply suggested code changes
- `run_tests` — Execute test suites
- `agent_task` — Multi-step agent task execution
