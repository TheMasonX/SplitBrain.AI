# Getting Started

> Migrated and enhanced from `Docs/UserDocs/deployment.md`

## Prerequisites

### Both Machines

| Requirement | Details |
|-------------|---------|
| **OS** | Windows 10/11 64-bit (Linux supported for Machine B) |
| **.NET SDK** | [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) |
| **Ollama** | [ollama.com/download](https://ollama.com/download) (unless using llama.cpp on Node B) |
| **Git** | For cloning |

### Machine A — Fast Node (Node A)
- **GPU:** RTX 5060 or equivalent (≥ 8 GB VRAM)
- **Role:** Interactive / low-latency workloads

### Machine B — Deep Node (Node B)
- **GPU:** GTX 1080 (8 GB VRAM) or equivalent
- **Role:** Long-context reasoning and deep inference
- **Alternative:** Run llama.cpp server for MoE model support (see [llama.cpp Setup](llamacpp-gtx1080-setup.md))

## Step 1: Clone

```powershell
git clone https://github.com/TheMasonX/SplitBrain.AI.git
cd SplitBrain.AI
git checkout next-gen   # active development branch
```

## Step 2: Pull Ollama Models

```powershell
# Machine A — fast coder model
ollama pull qwen2.5-coder:7b-instruct-q4_K_M

# Machine B — deep inference models
ollama pull qwen2.5-coder:7b-instruct-q5_K_M
ollama pull deepseek-coder:6.7b-instruct-q4_K_M   # fallback

# Both machines — embeddings
ollama pull nomic-embed-text
```

## Step 3: Configure Node B

Edit `src/Orchestrator.NodeWorker/appsettings.NodeB.json`:

```json
{
  "Urls": "http://0.0.0.0:5050",
  "OllamaNode": {
    "BaseUrl": "http://localhost:11434",
    "TimeoutSeconds": 120
  }
}
```

## Step 4: Configure Machine A Topology

Edit `nodes.json` (created on first run, or use the Settings page in the Dashboard):

```json
{
  "Nodes": [
    {
      "NodeId": "A", "DisplayName": "Local Fast Node",
      "Provider": "Ollama", "Role": "Fast", "Priority": 10,
      "Ollama": { "Host": "localhost", "Port": 11434,
                  "TimeoutSeconds": 10, "GpuVramTotalMB": 8192 }
    },
    {
      "NodeId": "B", "DisplayName": "Tower Deep Node",
      "Provider": "Worker", "Role": "Deep", "Priority": 50,
      "Worker": { "BaseUrl": "http://192.168.1.XX:5050",
                  "TimeoutSeconds": 30, "GpuVramTotalMB": 8192 }
    }
  ]
}
```

Replace `192.168.1.XX` with Machine B's local IP address.

## Step 5: Build and Run

**Machine B** (start first):
```powershell
ollama serve   # ensure Ollama is running
dotnet run --project src/Orchestrator.NodeWorker -- --environment NodeB
```

**Machine A**:
```powershell
# MCP server (for AI client connections via IDE or MCP client)
dotnet run --project src/Orchestrator.Mcp

# Dashboard (optional — for UI)
dotnet run --project src/SplitBrain.Dashboard
```

## Step 6: Verify

```powershell
# Check Node B worker health
Invoke-RestMethod http://<machine-b-ip>:5050/health

# Check MCP server health  
Invoke-RestMethod http://localhost:5100/health

# Check Ollama is serving models
Invoke-RestMethod http://localhost:11434/api/tags
```

## Alternative: llama.cpp Backend on Node B

To use llama.cpp instead of Ollama on Node B (for MoE model support):

```powershell
# On Machine B — set environment variable before starting NodeWorker
$env:NODE_B_BACKEND = "llamacpp"
dotnet run --project src/Orchestrator.NodeWorker -- --environment NodeB
```

See [llama.cpp GTX 1080 Setup](llamacpp-gtx1080-setup.md) and [Backend Switching](backend-switching.md) for details.

## Related Pages

- [Configuration Reference](../ops/configuration-reference.md)
- [Deployment Details](../ops/deployment.md)
- [Backend Switching Guide](backend-switching.md)
