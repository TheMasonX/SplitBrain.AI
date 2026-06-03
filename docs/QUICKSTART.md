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
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/call","params":{"name":"review_code","arguments":{"code":"def divide(a,b): return a/b","language":"python","focus":"bugs"}}}'
```

## Discover Available Tools

```bash
curl -X POST http://localhost:5000/mcp \
  -H "Content-Type: application/json" \
  -d '{"jsonrpc":"2.0","id":1,"method":"tools/list","params":{}}'
```

Expected: 12 tools including `splitbrain_read_file`, `splitbrain_write_file`, etc.

## Typical Agent Workflow

```
splitbrain_query_allowed_root  → confirm write mode
splitbrain_list_files          → discover files (**/*.cs)
splitbrain_read_file           → read the file to review
review_code                    → structured review
refactor_code                  → get the refactored version
splitbrain_write_file          → write it back (requires write gate)
run_tests                      → verify nothing broke
```

## Enable File Mutations

By default, all write operations are **disabled** for safety.

**Option A — Per-call (recommended):** Set `Mcp:WriteAccess:Mode: PerCallEnable` in appsettings.json.  
Then pass `enable_write: true` in each write tool call.

**Option B — Always on:** Set `Mcp:WriteAccess:Mode: AlwaysEnabled`.  
Use only on isolated development machines.

Query the current mode at runtime:
```
splitbrain_query_allowed_root  → { write_mode: "Disabled", ... }
```

## Node B (Deep Inference)

If you have a secondary GPU machine:
```bash
# On Machine B, set NODE_B_BACKEND env var and configure LlamaCppNode
export NODE_B_BACKEND=llamacpp

# Start the MCP server on Machine A with Node B in topology config
```

See `data/Pages/guides/llamacpp-gtx1080-setup.md` for GTX 1080 flags.

## Troubleshooting

If `refactor_code`, `generate_tests`, or `agent_task` time out, see `docs/DIAGNOSTICS.md`.
The most common cause is Node B configured to an unreachable address with a long timeout.
