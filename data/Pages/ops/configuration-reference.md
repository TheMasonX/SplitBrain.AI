# Configuration Reference — Updated

> Migrated from `Docs/UserDocs/configuration-reference.md`  
> Updated 2026-06-03 to add OpenTelemetry and NodeWorker auth sections.

Full setup steps are in [Getting Started](../guides/getting-started.md). This page is a quick-reference for all configurable values.

## appsettings.json

| Key | Type | Description | Example |
|-----|------|-------------|---------|
| `Node:Role` | string | Which role this machine plays | `"NodeA"` or `"NodeB"` |
| `Node:OllamaBaseUrl` | string | Local Ollama API endpoint | `"http://localhost:11434"` |
| `Node:PeerNodeUrl` | string | Address of the other node | `"http://192.168.1.50:5100"` |
| `McpServer:Port` | int | Port the MCP server listens on | `5100` |

## OllamaNode Section

| Key | Type | Description | Default |
|-----|------|-------------|---------|
| `OllamaNode:BaseUrl` | string | Ollama server URL | `http://localhost:11434` |
| `OllamaNode:TimeoutSeconds` | int | Per-request timeout | `120` |

## LlamaCppNode Section (Node B llama.cpp backend)

| Key | Type | Description | Default |
|-----|------|-------------|---------|
| `LlamaCppNode:BaseUrl` | string | llama.cpp server URL | `http://localhost:8080` |
| `LlamaCppNode:TimeoutSeconds` | int | Per-request timeout | `180` |
| `LlamaCppNode:ModelLabel` | string | Label for the loaded GGUF model | `"llama-cpp-default"` |
| `LlamaCppNode:NodeId` | string | Node identifier | `"B"` |
| `LlamaCppNode:VramMb` | int | GPU VRAM in MB (for reporting) | `8192` |

## NodeWorker Auth (opt-in bearer token, default off)

| Key | Type | Description | Default |
|-----|------|-------------|---------|
| `NodeWorker:Auth:RequireToken` | bool | Enable bearer token auth on inference endpoints | `false` |
| `NodeWorker:Auth:Token` | string | Static bearer token (leave empty to auto-generate) | `""` |

When `RequireToken: true`, all endpoints except `GET /health` require `Authorization: Bearer <token>`.  
If no token is configured, an ephemeral token is generated at startup and logged at Warning level.  
Alternative: set `SPLITBRAIN_WORKER_TOKEN` environment variable instead of appsettings.

## OpenTelemetry (optional — off by default)

SplitBrain.AI instruments traces and metrics via OpenTelemetry. The OTLP exporter is **only activated when `OTEL_EXPORTER_OTLP_ENDPOINT` is set** — if the env var is absent, no connection is attempted and no startup errors occur.

| Environment Variable | Description | Example |
|---------------------|-------------|---------|
| `OTEL_EXPORTER_OTLP_ENDPOINT` | OTLP collector URL (Jaeger, Grafana Tempo, etc.) | `http://localhost:4317` |

To enable:
```powershell
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
# Then start the MCP server or Dashboard
```

To run without any collector (default): simply leave the env var unset. OTel internal metrics still work — only the export step is skipped.

**Service names:**
- `SplitBrain.Mcp` — MCP server traces and metrics
- `SplitBrain.Dashboard` — Dashboard traces and metrics

## Environment Variables

Standard .NET config hierarchy with `__` as section separator:

```powershell
$env:Node__Role            = "NodeB"
$env:Node__OllamaBaseUrl   = "http://localhost:11434"
$env:Node__PeerNodeUrl     = "http://192.168.1.10:5100"
$env:McpServer__Port       = "5100"

# Backend selection (Node B)
$env:NODE_B_BACKEND        = "llamacpp"   # or "ollama"

# NodeWorker auth token (alternative to appsettings)
$env:SPLITBRAIN_WORKER_TOKEN = "your-secret-token"

# OpenTelemetry export (optional — leave unset to disable OTLP export)
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
```

## Ollama Models

| Model | Pull Command | Used On |
|-------|-------------|---------|
| Qwen 2.5 Coder 7B (Q4) | `ollama pull qwen2.5-coder:7b-instruct-q4_K_M` | Node A |
| Qwen 2.5 Coder 7B (Q5) | `ollama pull qwen2.5-coder:7b-instruct-q5_K_M` | Node B (primary) |
| DeepSeek Coder 6.7B (Q4) | `ollama pull deepseek-coder:6.7b-instruct-q4_K_M` | Node B (fallback) |
| Nomic Embed Text | `ollama pull nomic-embed-text` | Both nodes (embeddings) |

> **Note on embeddings:** SplitBrain.AI uses `ollama pull nomic-embed-text` (the Ollama-hosted version).  
> This is **not** the same as the ONNX model file (`nomic-embed-text-v1.5.onnx`) used by MemorySmith's  
> code search engine. SplitBrain.AI does not use ONNX embedding models directly.

## Agent Bounds (Locked)

| Setting | Value | Notes |
|---------|-------|-------|
| `MaxIterations` | `4` | Locked per spec |
| `MaxTokensPerLoop` | `12000` | Budget ceiling; model context window is 8K |
| `DefaultTimeout` | `300s` | Locked per spec |
