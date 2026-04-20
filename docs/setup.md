# Configuration Reference

Full setup steps are in [DEPLOYMENT.md](../DEPLOYMENT.md). This page is a quick-reference for all configurable values.

---

## appsettings.json

| Key | Type | Description | Example |
|---|---|---|---|
| `Node:Role` | string | Which role this machine plays | `"NodeA"` or `"NodeB"` |
| `Node:OllamaBaseUrl` | string | Local Ollama API endpoint | `"http://localhost:11434"` |
| `Node:PeerNodeUrl` | string | Address of the other node | `"http://192.168.1.50:5100"` |
| `McpServer:Port` | int | Port the MCP server listens on | `5100` |

---

## Ollama Models

| Model | Pull Command | Used On |
|---|---|---|
| Qwen 2.5 Coder 7B (Q4) | `ollama pull qwen2.5-coder:7b-instruct-q4_K_M` | Node A |
| DeepSeek Coder V2 | `ollama pull deepseek-coder-v2` | Node B (fallback) |
| Nomic Embed Text | `ollama pull nomic-embed-text` | Both nodes |

---

## Environment Variables (optional overrides)

Environment variables override appsettings.json using the standard .NET configuration hierarchy with __ as the section separator:

```powershell
$env:Node__Role            = "NodeB"
$env:Node__OllamaBaseUrl   = "http://localhost:11434"
$env:Node__PeerNodeUrl     = "http://192.168.1.10:5100"
$env:McpServer__Port       = "5100"
```
