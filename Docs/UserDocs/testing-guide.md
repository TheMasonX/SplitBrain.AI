# Testing with Node A + Node C (No Local Ollama Required)

This guide shows how to run **Node A** (local Ollama-based) and **Node C** (GitHub Copilot API) for testing MCP tools without Node B.

---

## Prerequisites

### Node A (Local Ollama)
- **Ollama** installed and running on `localhost:11434`
  ```bash
  ollama pull qwen2.5-coder:7b-instruct-q4_K_M
  ollama serve
  ```

### Node C (GitHub Copilot API)
- **GitHub Copilot CLI** installed and in PATH
  ```bash
  # Install Copilot CLI as a GitHub CLI extension (CORRECT method)
  gh extension install github/gh-copilot

  # Or use your existing gh CLI
  gh version  # requires v2.45.0+

  # Authenticate with GitHub
  gh auth login
  gh copilot auth login
  ```

- **API Key** — one of these:
  1. **Azure Key Vault** (enterprise preferred)
     - Set env var: `AZURE_TENANT_ID`, `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` (or use `DefaultAzureCredential` with managed identity)
     - Config in `appsettings.json`:
       ```json
       {
         "CopilotNode": {
           "KeyVaultUri": "https://my-vault.vault.azure.net/",
           "KeyVaultSecretName": "CopilotApiKey"
         }
       }
       ```
  2. **Environment Variable** (development)
     - Set: `export COPILOT_API_KEY="your-github-token"`
     - The token must have `copilot` scope

---

## Configuration

### Step 1: Update `appsettings.json`

Edit `src/Orchestrator.Mcp/appsettings.json` to **skip Node B**:

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
    "BaseUrl": "http://<invalid-unreachable-ip>:11434",
    "TimeoutSeconds": 300
  },
  "CopilotNode": {
    "Model": "gpt-4o",
    "TimeoutSeconds": 60
  }
}
```

**Note:** Node B will report `Unavailable` and all large-context tasks will automatically route to **Node C (GitHub Copilot)** as the fallback.

### Step 2: Set GitHub Token (One of These)

#### Option A: Environment Variable (Simplest for Dev)
```powershell
# PowerShell
$env:COPILOT_API_KEY = "ghp_your_github_token_here"

# Or permanently
[System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", "ghp_...", "User")
```

#### Option B: Azure Key Vault (Enterprise)
```powershell
# Install Azure CLI
choco install azure-cli

# Login
az login

# Store secret
az keyvault secret set --vault-name "my-vault" --name "CopilotApiKey" --value "ghp_..."

# Set env vars (az cli handles auth automatically)
$env:AZURE_TENANT_ID = "your-tenant-id"
```

---

## Running Node A + Node C

### Terminal 1: Start Ollama (Node A Backend)
```powershell
ollama serve
```
Wait for: `Listening on 127.0.0.1:11434`

### Terminal 2: Start MCP Server (Node A + Router + Node C Client)
```powershell
cd C:\@Repos\Visual Studio Projects\SplitBrain.AI
dotnet run --project src/Orchestrator.Mcp
```

Expected output:
```
[INF] Logging initialized
[INF] Orchestrator.Mcp starting...
[INF] Node A client initialized (Ollama @ localhost:11434)
[INF] Node C client initialized (GitHub Copilot)
[INF] Routing service ready
[INF] MCP server listening on stdio
```

### Terminal 3: Test via MCP Client (e.g., Claude Desktop)

Configure Claude Desktop (`%APPDATA%\Claude\claude_desktop_config.json`):
```json
{
  "mcpServers": {
    "splitbrain": {
      "command": "dotnet",
      "args": ["run", "--project", "C:\\@Repos\\Visual Studio Projects\\SplitBrain.AI\\src\\Orchestrator.Mcp"]
    }
  }
}
```

Then in Claude Desktop:
- Request a **code review**: routes to **Node A** (if prompt < 5k tokens, healthy)
- Request a **deep refactor** (large prompt): routes to **Node C** (GitHub Copilot fallback)
- Node A unavailable? Routes to **Node C**

---

## Testing Scenarios

### Scenario 1: Code Review (Node A)
**Prompt:**
```
Review this function for bugs:

def calculate(a, b):
    return a + b / 0

Focus: bugs
Language: python
```

**Expected:** Node A (Ollama Qwen 7B) analyzes locally, returns fast.

### Scenario 2: Large Refactor (Node C Fallback)
**Prompt:**
```
Refactor this 2000-line legacy module for type safety and modern Python patterns.

[paste large file]

Focus: readability
Language: python
```

**Expected:** Prompt > 5k tokens → routes to **Node C (GitHub Copilot)** for deep analysis.

### Scenario 3: Test Generation (Node A First, C as Backup)
**Prompt:**
```
Generate comprehensive unit tests for this module.

[module code]

Language: csharp
```

**Expected:** 
- Node A healthy → Node A generates tests
- Node A timeout/error → falls back to Node C

---

## Monitoring & Debugging

### View MCP Logs
The MCP server logs to `stderr`. Watch for:
- `[INF] RouteAsync: selected Node A` — successful route
- `[ERR] Node A unavailable` — fallback triggered
- `[INF] Node C: executing gpt-4o` — Copilot API call

### Check Node Health
Run in a PowerShell terminal (while MCP is running):
```powershell
# Query MCP for metrics (if exposed; otherwise check logs)
# Or use a test client that calls the routing service directly
```

### Test Individual Tools
Unit tests already cover all tools and routing logic:
```powershell
cd C:\@Repos\Visual Studio Projects\SplitBrain.AI
dotnet test src/Orchestrator.Tests
```

**Key tests:**
- `RoutingServiceTests.RouteAsync_AutocompleteTask_RoutesToNodeA` — autocomplete → Node A
- `RoutingServiceTests.RouteAsync_WithNodeB_LargePrompt_RoutesToNodeB` — large → fallback (B→C in this config)
- `ScoringFunctionTests.SelectNode_WhenNodeBUnavailableInCache_ReturnsNodeA` — Node B unavailable → Node A / Node C

---

## Troubleshooting

### Error: "Ollama not responding"
```
[ERR] Node A: failed to connect to http://localhost:11434
```
**Fix:** Start Ollama in Terminal 1.

### Error: "Copilot API token not found"
```
[ERR] Node C: ResolveApiTokenAsync failed — no token in Key Vault or COPILOT_API_KEY env var
```
**Fix:** 
1. Set `COPILOT_API_KEY` env var, OR
2. Configure Key Vault URI in `appsettings.json`

### Error: "GitHub Copilot CLI not found"
```
[ERR] Node C: CopilotClient failed — gh copilot not in PATH
```
**Fix:** 
1. Install Copilot CLI: `npm install -g @github/gh-copilot`
2. Or set `CopilotNode:CliPath` in config to explicit path

### Error: "Node C timeout (60s)"
```
[ERR] Node C: timeout waiting for response
```
**Possible causes:**
- Copilot API is slow
- Your token is rate-limited
- Network connectivity issue

**Fix:** Increase `CopilotNode:TimeoutSeconds` in config.

---

## Next Steps

1. **Integrate into VS Code / IDE** — use the MCP client extension
2. **Add custom reviewers** — extend `ReviewCodeTool` with domain-specific checks
3. **Phase 3 Ready** — once Node A + C are stable, re-enable Node B for deep tasks
4. **Deploy locally** — use `deploy/setup-node-a.ps1` to register as Windows service

---

## Configuration Reference

Full `appsettings.json` (Node A + Node C only):

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

| Key | Purpose | Default |
|-----|---------|---------|
| `OllamaNode:BaseUrl` | Node A Ollama server | `http://localhost:11434` |
| `OllamaNodeB:BaseUrl` | Node B Ollama (set to unreachable to disable) | `http://<node-b-ip>:11434` |
| `CopilotNode:Model` | Chat model | `gpt-4o` |
| `CopilotNode:TimeoutSeconds` | Request timeout | `60` |
| `CopilotNode:KeyVaultUri` | Azure Key Vault URL | *(unset → use env var)* |
| `CopilotNode:CliPath` | Path to `gh` CLI | *(SDK default)* |
| `CopilotNode:CliUrl` | Pre-running CLI server URL | *(SDK spawns new)* |
