# NodeClient.LlamaCpp — Design and Implementation

**Status:** Complete (2026-06-01, `next-gen` branch, commit `1566611d`)  
**Related memory:** `splitbrain-decision-d8-llamacpp-parallel`, `splitbrain-llamacpp-nodeclient-status`

## Why llama.cpp Alongside Ollama?

Ollama wraps llama.cpp internally but hides its low-level flags. Specifically it does not expose `--n-cpu-moe` (MoE expert offloading), `--no-mmap`, `--mlock`, or TurboQuant KV cache flags. These flags enable running 30B MoE models on 8 GB VRAM:

| Flag | Effect |
|------|--------|
| `--n-cpu-moe N` | Keep MoE expert weights for N layers on CPU RAM |
| `--no-mmap` | Load full model into RAM upfront (35% speed boost vs disk paging) |
| `--mlock` | Pin RAM pages, prevents kernel eviction during idle |
| `--cache-type-k turbo4` | TurboQuant 4.25 bits/value KV cache compression |
| `--cache-type-v turbo3` | TurboQuant 3-bit KV cache values |

## API Mapping (Ollama vs llama.cpp)

| Concern | Ollama | llama.cpp server |
|---------|--------|-----------------|
| Generate | `POST /api/generate` | `POST /v1/chat/completions` (OAI-compatible) |
| Prompt shape | Raw string | `[{"role":"user","content":"..."}]` |
| Streaming | NDJSON | SSE (`data: {...}`) |
| Health | `GET /api/tags` → 200 | `GET /health` → `{"status":"ok"}` |
| Models | `GET /api/tags` | `GET /v1/models` |
| Default port | 11434 | 8080 |
| Auth | None | None (`Bearer no-key` accepted) |

## Project Structure

```
src/NodeClient.LlamaCpp/
  NodeClient.LlamaCpp.csproj    # net10.0, Microsoft.Extensions.* only
  LlamaCppClientOptions.cs      # BaseUrl, TimeoutSeconds, ModelLabel, NodeId, VramMb
  ILlamaCppClient.cs            # ExecuteAsync, StreamChunksAsync, IsHealthyAsync,
                                #   GetServerStatusAsync, ListModelsAsync
  LlamaCppClient.cs             # OAI /v1/chat/completions transport, SSE parser
  LlamaCppInferenceNode.cs      # IInferenceNode adapter — NodeId from opts, VramMb from opts
```

## Health State Mapping

```
GET /health response     →  HealthState
─────────────────────────────────────────
"ok"                     →  Healthy     (route normally)
"loading model"          →  Degraded    (server initialising; do not route)
"error" / null           →  Unavailable (server failed or unreachable)
```

## SSE Streaming

`LlamaCppInferenceNode.StreamAsync` is the first node in the project to implement true progressive token delivery. Each SSE `data:` line yields an `InferenceChunk { Content = delta, IsFinal = false }`. A final sentinel chunk `{ IsFinal = true, FinalResult = ... }` follows. `LatencyMs` on the final result = TTLT (time-to-last-token), not TTFT.

## DI Registration (Program.cs, NODE_B_BACKEND=llamacpp)

```csharp
builder.Services.Configure<LlamaCppClientOptions>(
    config.GetSection(LlamaCppClientOptions.Section));  // "LlamaCppNode"

builder.Services.AddHttpClient<ILlamaCppClient, LlamaCppClient>()
    .ConfigureHttpClient((sp, client) => {
        var opts = sp.GetRequiredService<IOptions<LlamaCppClientOptions>>().Value;
        client.Timeout = TimeSpan.FromSeconds(opts.TimeoutSeconds);
    });

builder.Services.AddSingleton<LlamaCppInferenceNode>();
builder.Services.AddSingleton<IInferenceNode>(sp =>
    sp.GetRequiredService<LlamaCppInferenceNode>());
```

## appsettings.NodeB.json (LlamaCppNode section)

```json
{
  "LlamaCppNode": {
    "BaseUrl": "http://localhost:8080",
    "TimeoutSeconds": 180,
    "ModelLabel": "qwen3-coder-30b-a3b",
    "NodeId": "B",
    "VramMb": 8192
  }
}
```

## Open Items (low priority)

| ID | Item |
|----|------|
| F1 | `ChatMessage.Content` should be `string?` (non-nullable but JSON can set null) |
| F5 | `FakeHttpMessageHandler` needs try/catch for synchronous throws |
| F6 | Add cancellation-mid-stream test for `StreamChunksAsync` |
| OI-1 | Runtime streaming proof — run `scripts/test-llamacpp-streaming.sh` on Machine B |

## Related Pages

- [GTX 1080 llama.cpp Setup](../guides/llamacpp-gtx1080-setup.md)
- [Backend Switching Guide](../guides/backend-switching.md)
- [Architecture Overview](overview.md)
