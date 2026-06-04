# MCP Tool Runtime Audit — 2026-06-04

## Context
Ran automated tool tests against both MCP servers to verify that all documented tools respond correctly.

## Servers Tested

### MemorySmith Wiki (localhost:6769)
- **Transport:** Standard HTTP/JSON (no session needed)
- **Tools exposed:** 12 (all documented)
- **Overall result:** ✅ 8/12 working as expected, 4 minor issues

### SplitBrain Orchestrator MCP (localhost:5100)
- **Transport:** Streamable HTTP (SSE format, requires `Accept: application/json, text/event-stream`)
- **Session model:** Required — initialize handshake first, then pass `Mcp-Session-Id` header
- **Tools exposed:** 7 out of 12 documented
- **Overall result:** ⚠️ Phase 1 tools missing (5 tools not deployed)

## Key Findings

1. **Phase 1 tools not deployed:** `ReadFileTool`, `ListFilesTool`, `WriteFileTool`, `QueryAllowedRootTool`, `ExplainCodeTool` are registered in `Program.cs` but the running binary was built before they were added. Needs rebuild & restart.
2. **Source bundle auth:** `memorysmith_source_bundle` rejects anonymous callers. Need to decide whether to open up or document as expected.
3. **%SplitBrainRepo% variable missing:** Source links in wiki records reference an undefined variable. Need to configure it in the wiki environment.

## Links
- Full report: `logs/MCP-Tool-Test-Report_20260604.md`
- Source: `src/Orchestrator.Mcp/Program.cs`
