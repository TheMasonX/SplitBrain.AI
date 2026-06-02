# Configuration Reference

> Migrated from `Docs/UserDocs/configuration-reference.md`

Full setup steps are in [Getting Started](../guides/getting-started.md). This page is a quick-reference for all configurable values.

## appsettings.json

| Key | Type | Description | Example |
|-----|------|-------------|---------|
| `Node:Role` | string | Which role this machine plays | `"NodeA"` or `"NodeB"` |
| `Node:OllamaBaseUrl` | string | Local Ollama API endpoint | `"http://localhost:11434"` |
| `Node:PeerNodeUrl` | string | Address of the other node | `"http://192.168.1.50:5100"` |
| `McpServer:Port` | int | Port the MCP server listens on | `5100` |

## OllamaNode Section (appsettings.NodeB.json)

| Key | Type | Description | Default |
|-----|------|-------------|---------|
| `OllamaNode:BaseUrl` | string | Ollama server URL | `http://localhost:11434` |
| `OllamaNode:TimeoutSeconds` | int | Per-request timeout | `120` |

## LlamaCppNode Section (appsettings.NodeB.json)

| Key | Type | Description | Default |
|-----|------|-------------|---------|
| `LlamaCppNode:BaseUrl` | string | llama.cpp server URL | `http://localhost:8080` |
| `LlamaCppNode:TimeoutSeconds` | int | Per-request timeout | `180` |
| `LlamaCppNode:ModelLabel` | string | Label for the loaded model | `"llama-cpp-default"` |
| `LlamaCppNode:NodeId` | string | Node identifier | `"B"` |
| `LlamaCppNode:VramMb` | int | GPU VRAM in MB (for reporting) | `8192` |

## Environment Variables

Standard .NET config hierarchy with `__` as section separator:

```powershell
$env:Node__Role            = "NodeB"
$env:Node__OllamaBaseUrl   = "http://localhost:11434"
$env:Node__PeerNodeUrl     = "http://192.168.1.10:5100"
$env:McpServer__Port       = "5100"

# Backend selection (overrides compile-time default)
$env:NODE_B_BACKEND        = "llamacpp"   # or "ollama"
```

## Ollama Models

| Model | Pull Command | Used On |
|-------|-------------|---------|
| Qwen 2.5 Coder 7B (Q4) | `ollama pull qwen2.5-coder:7b-instruct-q4_K_M` | Node A |
| Qwen 2.5 Coder 7B (Q5) | `ollama pull qwen2.5-coder:7b-instruct-q5_K_M` | Node B (primary) |
| DeepSeek Coder 6.7B (Q4) | `ollama pull deepseek-coder:6.7b-instruct-q4_K_M` | Node B (fallback) |
| Nomic Embed Text | `ollama pull nomic-embed-text` | Both nodes |

## Agent Bounds (Locked)

| Setting | Value | Notes |
|---------|-------|-------|
| `MaxIterations` | `4` | Locked per spec |
| `MaxTokensPerLoop` | `12000` | Budget ceiling; model context window is 8K |
| `DefaultTimeout` | `300s` | Locked per spec |

These do NOT change without explicit architectural review.
