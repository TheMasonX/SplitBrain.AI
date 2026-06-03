# SplitBrain.AI — Quick Start

## Prerequisites
- .NET 10 SDK
- Ollama running at `http://localhost:11434`
- A model loaded (e.g. `ollama pull qcoder:latest`)

## Start the MCP Server
```bash
cd src/Orchestrator.Mcp
dotnet run
# Listening on http://localhost:5000/mcp
```

## Your First Tool Call

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{
    "jsonrpc": "2.0",
    "id": 1,
    "method": "tools/call",
    "params": {
      "name": "review_code",
      "arguments": {
        "code": "def divide(a, b): return a / b",
        "language": "python",
        "focus": "bugs"
      }
    }
  }'
```

## Tool Discovery

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

## File Operations (Write Gate)

By default, write operations (`apply_patch`, `write_file`, `run_tests`) are disabled.

To enable per-call: set `Mcp:WriteAccess:Mode: PerCallEnable` in appsettings.json.  
Then include `"enable_write": true` in each write tool call.

See [WRITE_SAFETY.md](WRITE_SAFETY.md) for full details.

## Typical Agent Chain

```
splitbrain_list_files → find files to review
splitbrain_read_file  → read the file content  
review_code          → get structured review
refactor_code        → get refactored version
splitbrain_write_file → write the new version
run_tests            → verify nothing broke
```
