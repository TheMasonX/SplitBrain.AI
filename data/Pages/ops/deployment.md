# Deployment Reference

> Migrated from `Docs/UserDocs/deployment.md`

## Prerequisites

### Both Nodes

| Requirement | Details |
|-------------|---------|
| **OS** | Windows 10/11 (64-bit) |
| **.NET SDK** | [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Ollama** | [ollama.com/download](https://ollama.com/download) |
| **Git** | For cloning the repository |

### Node A — Fast Inference
- **GPU:** RTX 5060 (or equivalent, VRAM ≥ 8 GB)
- **Role:** Interactive / low-latency workloads

### Node B — Deep Inference
- **GPU:** GTX 1080 (or equivalent, VRAM ≥ 8 GB)
- **Role:** Long-context reasoning and deep inference

## Clone the Repository

```powershell
git clone https://github.com/TheMasonX/SplitBrain.AI.git
cd SplitBrain.AI
```

## Pull Ollama Models

```powershell
# Fast coder model (Node A)
ollama pull qwen2.5-coder:7b-instruct-q4_K_M

# Deep inference / fallback (Node B)
ollama pull deepseek-coder-v2

# Embeddings (both nodes)
ollama pull nomic-embed-text
```

## Configure Node Settings

Edit `appsettings.json` on each node:

```json
{
  "Node": {
    "Role": "NodeA",
    "OllamaBaseUrl": "http://localhost:11434",
    "PeerNodeUrl": "http://<other-node-ip>:<port>"
  },
  "McpServer": {
    "Port": 5100
  }
}
```

Valid `Role` values: `"NodeA"` (fast/latency-sensitive) or `"NodeB"` (deep/long-context).

## Build & Run

```powershell
dotnet build

# Node B — runs the HTTP inference worker
dotnet run --project src\Orchestrator.NodeWorker

# Node A — starts the MCP stdio server
dotnet run --project src\Orchestrator.Mcp

# Node A — optional Dashboard
dotnet run --project src\SplitBrain.Dashboard
```

## Verify Node Health

```powershell
# Check MCP endpoint health
Invoke-RestMethod http://localhost:5100/health

# Confirm Ollama is serving models
Invoke-RestMethod http://localhost:11434/api/tags
```

## Notes

- Ollama must be running (`ollama serve`) **before** starting SplitBrain.AI on each node.
- Node roles are asymmetric: Node A prioritises latency, Node B prioritises context depth.
- Agent iteration, token, and safety limits are configured in `appsettings.json`.
