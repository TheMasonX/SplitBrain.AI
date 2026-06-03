# SplitBrain.AI — Setup & Configuration Guide

## Prerequisites

### Both Nodes

| Requirement | Details |
|---|---|
| **OS** | Windows 10/11 (64-bit) |
| **.NET SDK** | [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Ollama** | [ollama.com/download](https://ollama.com/download) |
| **Git** | For cloning the repository |

### Node A — Fast Inference
- **GPU:** RTX 5060 (or equivalent, VRAM >= 8 GB)
- **Role:** Interactive / low-latency workloads

### Node B — Deep Inference
- **GPU:** GTX 1080 (or equivalent, VRAM >= 8 GB)
- **Role:** Long-context reasoning and deep inference

---

## 1. Clone the Repository

```powershell
git clone https://github.com/TheMasonX/SplitBrain.AI.git
cd SplitBrain.AI
```

---

## 2. Pull Ollama Models

Run the following on each node as appropriate:

```powershell
# Fast coder model (Node A)
ollama pull qwen2.5-coder:7b-instruct-q4_K_M

# Deep inference / fallback (Node B)
ollama pull deepseek-coder-v2

# Embeddings (both nodes)
ollama pull nomic-embed-text
```

---

## 3. Configure Node Settings

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

> **Note:** Replace `<other-node-ip>` and `<port>` with the actual address of the peer node on your local network.
> Valid `Role` values: `"NodeA"` (fast/latency-sensitive) or `"NodeB"` (deep/long-context).

---

## 4. Build & Run

```powershell
dotnet build

# On each node — runs the HTTP inference worker (health + inference endpoints)
dotnet run --project src\Orchestrator.NodeWorker

# On Node A only — starts the MCP stdio server for AI client connections
dotnet run --project src\Orchestrator.Mcp
```

---

## 5. Verify Node Health

Once both nodes are running, confirm connectivity:

```powershell
# Check MCP endpoint health
Invoke-RestMethod http://localhost:5100/health

# Confirm Ollama is serving models
Invoke-RestMethod http://localhost:11434/api/tags
```

---

## Notes

- Ollama must be running (`ollama serve`) **before** starting SplitBrain.AI on each node.
- Node roles are asymmetric: Node A prioritises latency, Node B prioritises context depth.
- Agent iteration, token, and safety limits are configured in `appsettings.json`.
- This document will be updated as configuration files are finalised.

---

## 6. (Optional) Deploy Project Wiki

A MemorySmith wiki service provides a browsable wiki and code search for the
SplitBrain.AI project.

### Prerequisites

- [MemorySmith](https://github.com/TheMasonX/MemorySmith) cloned to `C:\Users\norrt\source\repos\MemorySmith`
- .NET 10 SDK (same as project)

### Deploy

```powershell
# Deploy the wiki service on port 6769
.\scripts\Deploy-WikiService.ps1

# The service installs and starts automatically.
# Open http://127.0.0.1:6769 in your browser.
```

### Code Search

After deployment, warm the code-search index to enable semantic code search
across all SplitBrain.AI source code:

```powershell
.\scripts\Warm-CodeSearchIndex.ps1
```

If the ONNX embedding model is not yet downloaded:

```powershell
.\scripts\Install-CodeSearchModel.ps1
```

### Manage the Service

```powershell
# Check status
.\scripts\Get-WikiServiceStatus.ps1

# Stop and remove
.\scripts\Stop-WikiService.ps1

# Full uninstall (stops service + removes artifacts)
.\scripts\Remove-WikiService.ps1 -RemoveArtifacts
```

### Wiki Content

The wiki lives under `data/` in this repo:
- `data/Pages/` — Markdown wiki pages
- `data/Memories/Core/` — Structured memory records
- `data/Memories/Working/` — Active session notes
- `data/codesearch-config.json` — CodeSearch settings for indexing the project
