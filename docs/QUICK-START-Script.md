# Quick Start: Running Node A + Node C

The setup script is now fixed and ready to use.

## ✅ Working Script Commands

### 1. Check Prerequisites (before you start)
```powershell
cd 'C:\@Repos\Visual Studio Projects\SplitBrain.AI'
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs
```

This verifies:
- ✓ .NET 10 SDK installed
- ✓ Ollama running on localhost:11434
- ✓ GitHub CLI installed
- ✓ Copilot CLI installed

### 2. Setup with GitHub Token (Development)
```powershell
$token = "ghp_your_actual_github_token_here"
.\scripts\setup-test-nodeac.ps1 -GitHubToken $token
```

This:
- Sets `COPILOT_API_KEY` environment variable
- Configures `appsettings.json` to disable Node B
- Sets Node C timeout to 60s

### 3. Setup with Azure Key Vault (Enterprise)
```powershell
.\scripts\setup-test-nodeac.ps1 -UseKeyVault -KeyVaultUri "https://my-vault.vault.azure.net"
```

This:
- Updates `appsettings.json` with Key Vault URI
- Uses `DefaultAzureCredential` for auth
- (Requires `az login` first)

### 4. Setup with GitHub CLI Auth (Simplest for Dev)
```powershell
# First, ensure you're logged in
gh auth login
gh copilot auth login

# Then run setup without token
.\scripts\setup-test-nodeac.ps1
```

This:
- Extracts token from your GitHub CLI session
- Sets `COPILOT_API_KEY` automatically
- No manual token entry needed

---

## 🚀 How to Actually Run It (3 Terminals)

### Terminal 1: Start Ollama
```powershell
ollama serve
```
Wait for: `Listening on 127.0.0.1:11434`

### Terminal 2: Start MCP Server
```powershell
cd 'C:\@Repos\Visual Studio Projects\SplitBrain.AI'
dotnet run --project src/Orchestrator.Mcp
```

Expected output:
```
[INF] Application starting
[INF] Node A client initialized (Ollama @ localhost:11434)
[INF] Node C client initialized (GitHub Copilot)
[INF] Routing service ready
[INF] MCP server listening on stdio
```

### Terminal 3: Test
Use Claude Desktop or another MCP client to send review requests.

Examples:
- **Small code review** → routes to **Node A** (fast, local)
- **Large refactor** → routes to **Node C** (deep analysis, fallback)

---

## 📋 What the Script Does

| Step | Action | Output |
|------|--------|--------|
| `SetEnvironmentVariable` | Sets `COPILOT_API_KEY` in User profile | Can be used by all PowerShell/app sessions |
| `ConvertFrom-Json` | Reads `appsettings.json` | Safely updates JSON config |
| `OllamaNodeB.BaseUrl` | Changes to `unreachable.local` | Disables Node B, routes to Node C |
| `CopilotNode` settings | Sets Model, Timeout | Configures Node C behavior |
| `ConvertTo-Json` | Writes updated config | Ready for next app start |

---

## ✨ Fixes Applied

1. **Removed Unicode characters** (`✓` → `[OK]`, `✗` → `[FAIL]`)
   - PowerShell encoding issues on some systems

2. **Fixed here-strings** (`@"..."@` → individual Write-Host calls)
   - Parsing conflicts with numbered lists

3. **Escaped apostrophes** (`you've` → `you have`)
   - Double-quote string conflicts

4. **Proper function closures**
   - All `{}` blocks now properly closed

---

## Testing the Setup

```powershell
# Verify script works
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs

# Test with dummy token
.\scripts\setup-test-nodeac.ps1 -GitHubToken "dummy_test"

# Verify env var was set
$env:COPILOT_API_KEY  # Should print your token (or nothing if not set in this session)

# Check appsettings.json was updated
cat src/Orchestrator.Mcp/appsettings.json | ConvertFrom-Json | % CopilotNode
```

---

## Expected appsettings.json After Setup

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.Hosting.Lifetime": "Warning"
    }
  },
  "OllamaNode": {
    "BaseUrl": "http://localhost:11434",
    "TimeoutSeconds": 120
  },
  "OllamaNodeB": {
    "BaseUrl": "http://unreachable.local:11434",
    "TimeoutSeconds": 300
  },
  "CopilotNode": {
    "Model": "gpt-4o",
    "TimeoutSeconds": 60,
    "KeyVaultUri": null,
    "KeyVaultSecretName": "CopilotApiKey",
    "CliPath": null,
    "CliUrl": null
  }
}
```

**Key Points:**
- `OllamaNode` points to your local Ollama
- `OllamaNodeB` is set to unreachable (disables Node B)
- `CopilotNode` uses GitHub Copilot with 60s timeout

---

## Troubleshooting

### Script won't run
```powershell
# Try explicitly allowing execution
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\setup-test-nodeac.ps1 -CheckPrereqs
```

### COPILOT_API_KEY not set
```powershell
# Check if it was set in User environment
[System.Environment]::GetEnvironmentVariable("COPILOT_API_KEY", "User")

# Or in this session only
$env:COPILOT_API_KEY
```

### appsettings.json not updating
```powershell
# Check file is writable
icacls "src\Orchestrator.Mcp\appsettings.json" /T

# Backup and retry
Copy-Item "src\Orchestrator.Mcp\appsettings.json" "src\Orchestrator.Mcp\appsettings.json.bak"
.\scripts\setup-test-nodeac.ps1 -GitHubToken "test"
```

---

## Next Steps

1. **Prerequisite check**: `.\scripts\setup-test-nodeac.ps1 -CheckPrereqs`
2. **Setup environment**: `.\scripts\setup-test-nodeac.ps1 -GitHubToken "your-token"`
3. **Start Ollama**: Terminal 1 - `ollama serve`
4. **Start MCP**: Terminal 2 - `dotnet run --project src/Orchestrator.Mcp`
5. **Test**: Terminal 3 - Use Claude Desktop or custom MCP client
6. **See examples**: Read `docs/TEST-CASES-NodeA-NodeC.md`
