# ✅ Complete Setup Checklist for Node A + Node C Testing

Run through this checklist step-by-step. Everything is now fixed and working.

---

## Prerequisites (Run Once)

- [ ] .NET 10 SDK installed
  ```powershell
  dotnet --version  # Should show 11.x (preview) or later
  ```

- [ ] Ollama installed
  ```powershell
  ollama --version
  ```

- [ ] Ollama model downloaded
  ```powershell
  ollama pull qwen2.5-coder:7b-instruct-q4_K_M
  ollama list  # Should show the model
  ```

- [ ] GitHub CLI installed
  ```powershell
  gh --version  # Should be v2.45.0+
  ```

- [ ] Copilot CLI installed
  ```powershell
  gh copilot --version  # Should work
  ```

- [ ] GitHub authenticated
  ```powershell
  gh auth login
  gh auth status  # Should show logged-in user
  ```

---

## Environment Setup (Run Once)

- [ ] Navigate to repo root
  ```powershell
  cd 'C:\@Repos\Visual Studio Projects\SplitBrain.AI'
  ```

- [ ] Run prerequisite check
  ```powershell
  .\scripts\setup-test-nodeac.ps1 -CheckPrereqs
  ```
  Expected: All checks pass ✓

- [ ] Set GitHub token (choose ONE method)

  **Option A: Direct token (simplest)**
  ```powershell
  $token = "ghp_your_github_token"
  .\scripts\setup-test-nodeac.ps1 -GitHubToken $token
  ```

  **Option B: Use GitHub CLI (recommended)**
  ```powershell
  .\scripts\setup-test-nodeac.ps1
  ```

  **Option C: Azure Key Vault (enterprise)**
  ```powershell
  az login
  .\scripts\setup-test-nodeac.ps1 -UseKeyVault -KeyVaultUri "https://my-vault.vault.azure.net"
  ```

- [ ] Verify setup completed
  ```powershell
  cat src/Orchestrator.Mcp/appsettings.json
  # Should show: OllamaNodeB.BaseUrl = "http://unreachable.local:11434"
  # Should show: CopilotNode.Model = "gpt-4o"
  ```

- [ ] Verify token is set
  ```powershell
  [System.Environment]::GetEnvironmentVariable("COPILOT_API_KEY", "User")
  # Should print your GitHub token (if set)
  ```

---

## Running Node A + Node C (Every Test Session)

### Terminal 1: Ollama (Backend for Node A)
- [ ] Open PowerShell terminal
- [ ] Run:
  ```powershell
  ollama serve
  ```
- [ ] Wait for:
  ```
  Listening on 127.0.0.1:11434
  ```
- [ ] Keep running (don't close this terminal)

### Terminal 2: MCP Server (Node A + Router + Node C)
- [ ] Open new PowerShell terminal
- [ ] Navigate to repo:
  ```powershell
  cd 'C:\@Repos\Visual Studio Projects\SplitBrain.AI'
  ```
- [ ] Run MCP server:
  ```powershell
  dotnet run --project src/Orchestrator.Mcp
  ```
- [ ] Wait for startup (10-15 seconds)
- [ ] Look for:
  ```
  [INF] Orchestrator.Mcp started successfully
  [INF] MCP server listening on stdio
  ```
- [ ] Keep running (don't close this terminal)

### Terminal 3: Testing (Client)
- [ ] Open Claude Desktop or use test client
- [ ] Configure MCP client to point to your MCP server
- [ ] Send a code review request (see TEST-CASES-NodeA-NodeC.md)
- [ ] Monitor Terminals 1 + 2 for logs

---

## Expected Output

### Terminal 1 (Ollama)
```
time=2024-12-20T10:30:45.123Z level=INFO msg="Listening on 127.0.0.1:11434"
time=2024-12-20T10:31:00.456Z level=INFO msg="loaded model qwen2.5-coder"
```

### Terminal 2 (MCP Server)
```
[10:30:50 INF] Application starting. Version 1.0
[10:30:51 INF] Node A client initialized (Ollama @ http://localhost:11434)
[10:30:52 INF] Node C client initialized (GitHub Copilot)
[10:30:53 INF] Routing service ready (NodeA, NodeC enabled)
[10:30:54 INF] MCP server listening on stdio
[10:31:00 INF] RouteAsync: selected Node A (score=0.95, prompt_len=320)
[10:31:02 INF] Node A: completed latencyMs=1800
```

---

## Testing Workflow

### Test 1: Simple Code Review (Node A)
1. In Claude: "Review this Python code for bugs: [small snippet]"
2. Check Terminal 2 logs: Should say "selected Node A"
3. Response should come within 2-5 seconds
4. ✓ **PASS** if Node A is used

### Test 2: Large Refactor (Node C)
1. In Claude: "Refactor this large module: [1500+ lines]"
2. Check Terminal 2 logs: Should say "selected Node C"
3. Response should come within 5-15 seconds
4. ✓ **PASS** if Node C is used

### Test 3: Batch Processing (Multiple Reviews)
1. Send 3-5 small review requests rapidly
2. Check Terminal 2: All should route to Node A
3. All should complete successfully
4. ✓ **PASS** if all complete without errors

---

## Troubleshooting Quick Reference

| Problem | Solution |
|---------|----------|
| Script won't run | `powershell -ExecutionPolicy Bypass -File .\scripts\setup-test-nodeac.ps1 -CheckPrereqs` |
| Ollama not found | `ollama serve` in Terminal 1 |
| MCP server won't start | Check `dotnet` is installed: `dotnet --version` |
| Token not found | Run setup script again: `.\scripts\setup-test-nodeac.ps1 -GitHubToken "your-token"` |
| Routes to wrong node | Check logs in Terminal 2, verify `appsettings.json` |
| Node C timeouts | Increase `CopilotNode:TimeoutSeconds` in `appsettings.json` |

---

## Files Modified / Created

After running setup script:
- ✅ `src/Orchestrator.Mcp/appsettings.json` — Node B disabled, Node C enabled
- ✅ Environment variable `COPILOT_API_KEY` — set in User scope

You can revert with:
```powershell
# Restore original config
git checkout src/Orchestrator.Mcp/appsettings.json

# Clear token
[System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", "", "User")
```

---

## How Routing Works

```
Request comes in
    ↓
RoutingService scores available nodes
    ↓
┌─────────────────────────────────────────────┐
│ Prompt < 5k tokens + Node A healthy         │
│         → Route to Node A (Ollama)          │
│         → Fast response (2-5s)              │
└─────────────────────────────────────────────┘
    ↓
┌─────────────────────────────────────────────┐
│ Prompt > 5k tokens OR Node A unavailable    │
│         → Route to Node C (GitHub Copilot)  │
│         → Deep analysis (5-15s)             │
└─────────────────────────────────────────────┘
    ↓
Response returned to client
```

---

## Next Steps After Verification

1. **Review examples**: See `docs/TEST-CASES-NodeA-NodeC.md`
2. **Check logs**: Monitor Terminal 2 during tests
3. **Iterate**: Try different code snippets and observe routing
4. **Extend**: Customize review rules in `src/Orchestrator.Mcp/Tools/ReviewCodeTool.cs`
5. **Scale**: When ready, enable Node B with `deploy/setup-node-b.ps1`

---

## Quick Command Reference

```powershell
# Check all prerequisites
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs

# Setup with token
.\scripts\setup-test-nodeac.ps1 -GitHubToken "ghp_..."

# View current config
cat src/Orchestrator.Mcp/appsettings.json | ConvertFrom-Json

# View token (from this PowerShell session)
$env:COPILOT_API_KEY

# Start MCP server
dotnet run --project src/Orchestrator.Mcp

# Run tests
dotnet test src/Orchestrator.Tests

# View recent logs (if log file exists)
Get-Content logs/recent.log -Tail 50
```

---

✅ **You're ready to test Node A + Node C!**

Start with Terminal 1 (Ollama), then Terminal 2 (MCP), then test in Terminal 3 (Client).
