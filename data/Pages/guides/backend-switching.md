# Backend Switching — Ollama vs llama.cpp

Node B supports two inference backends. The default is Ollama. Switch to llama.cpp to enable MoE model support (30B+ models on 8 GB VRAM).

## Quick Switch

```powershell
# On Machine B — set before starting NodeWorker
$env:NODE_B_BACKEND = "llamacpp"   # switch to llama.cpp
# $env:NODE_B_BACKEND = "ollama"   # revert to Ollama (default)

dotnet run --project src/Orchestrator.NodeWorker -- --environment NodeB
```

The selection is case-insensitive. Any value other than `llamacpp` uses Ollama.

## What Changes

| Aspect | Ollama (default) | llama.cpp |
|--------|-----------------|-----------|
| Backend process | `ollama serve` | `llama-server` (Docker or bare metal) |
| Port | 11434 | 8080 |
| Config section | `OllamaNode` | `LlamaCppNode` |
| Model management | Pull at runtime | Baked into server startup |
| MoE offloading | Not supported | `--n-cpu-moe N` |
| True SSE streaming | No (fake stream) | Yes |

## appsettings.NodeB.json — Both Sections

Both sections should be present; the active backend reads only its own section:

```json
{
  "OllamaNode": {
    "BaseUrl": "http://localhost:11434",
    "TimeoutSeconds": 120
  },
  "LlamaCppNode": {
    "BaseUrl": "http://localhost:8080",
    "TimeoutSeconds": 180,
    "ModelLabel": "qwen3-coder-30b-a3b",
    "NodeId": "B",
    "VramMb": 8192
  }
}
```

## Benchmarking Checklist (OI-1)

Before permanently switching, run a controlled comparison:

1. Both backends: same 7B model, 10 identical coding prompts × 3 iterations
2. Measure: tok/s, first-token latency, subjective quality
3. Then: llama.cpp with 30B MoE vs Ollama with 7B (quality vs. speed tradeoff)
4. Use `scripts/test-llamacpp-streaming.sh` to verify progressive token delivery

## Related Pages

- [llama.cpp GTX 1080 Setup](llamacpp-gtx1080-setup.md)
- [NodeClient.LlamaCpp Design](../architecture/llamacpp-nodeclient.md)
