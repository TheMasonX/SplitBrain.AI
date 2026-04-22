# Fixed: setup-test-nodeac.ps1 Script

## Issues Found & Fixed

### 1. ❌ Unicode Characters Causing Encoding Issues
**Problem:** PowerShell couldn't parse `✓` and `✗` characters
```powershell
# BEFORE
Write-Host "✓ $Message" -ForegroundColor Green
Write-Host "✗ $Message" -ForegroundColor Red
```

**Fix:** Replaced with ASCII-safe characters
```powershell
# AFTER
Write-Host "[OK] $Message" -ForegroundColor Green
Write-Host "[FAIL] $Message" -ForegroundColor Red
```

---

### 2. ❌ Here-String Parsing Conflict
**Problem:** `@"..."@` syntax conflicted with numbered list items
```powershell
# BEFORE
Write-Host @"

Next steps:

1. Start Ollama (Terminal 1):
   ollama serve

2. Start MCP Server (Terminal 2):
   dotnet run --project src/Orchestrator.Mcp

3. Test with Claude Desktop or custom MCP client

To verify prerequisites are installed, run:
   .\setup-test-nodeac.ps1 -CheckPrereqs

"@
```

**Error:** `Unexpected token 'Start' in expression or statement`

**Fix:** Replaced with individual Write-Host calls
```powershell
# AFTER
Write-Host ""
Write-Host "Next steps:"
Write-Host ""
Write-Host "1. Start Ollama (Terminal 1):"
Write-Host "   ollama serve"
Write-Host ""
Write-Host "2. Start MCP Server (Terminal 2):"
Write-Host "   dotnet run --project src/Orchestrator.Mcp"
Write-Host ""
Write-Host "3. Test with Claude Desktop or custom MCP client"
```

---

### 3. ❌ Apostrophe in Double-Quoted String
**Problem:** PowerShell couldn't handle `you've` in double quotes
```powershell
# BEFORE
Write-Host "Make sure you've run: gh auth login"
```

**Error:** `The string is missing the terminator: ".`

**Fix:** Removed apostrophe
```powershell
# AFTER
Write-Host "Make sure you have run: gh auth login"
```

---

### 4. ❌ Plus Symbol Interpretation
**Problem:** `+` in string was being interpreted as operator
```powershell
# BEFORE
Write-Success "Ready to test Node A + Node C!"
```

**Error:** `The string is missing the terminator: ".`

**Fix:** Changed to explicit word
```powershell
# AFTER
Write-Success "Ready to test Node A and Node C!"
```

---

## Testing Results

### ✅ Before Fix
```
At C:\...\setup-test-nodeac.ps1:112 char:30
+     Write-Host "Make sure you've run: gh auth login"
+                              ~~~~~~~~~~~~~~~~~~~~~~~
The string is missing the terminator: '.
```

### ✅ After Fix
```powershell
PS C:\@Repos\Visual Studio Projects\SplitBrain.AI> .\scripts\setup-test-nodeac.ps1 -GitHubToken "test_token"

=== Setting up GitHub Copilot Authentication ===
Setting GitHub token via environment variable...
[OK] COPILOT_API_KEY set (User scope)

=== Configuring appsettings.json ===
[OK] appsettings.json updated
Configuration:
  Node A (Ollama):     http://localhost:11434
  Node B (Disabled):   http://unreachable.local:11434
  Node C (Copilot):    Model=gpt-4o, Timeout=60s

=== Setup Complete! ===

[OK] Ready to test Node A and Node C!
```

---

## Summary of Changes

| Line | Issue | Before | After |
|------|-------|--------|-------|
| 24 | Unicode ✓ | `Write-Host "✓ $Message"` | `Write-Host "[OK] $Message"` |
| 29 | Unicode ✗ | `Write-Host "✗ $Message"` | `Write-Host "[FAIL] $Message"` |
| 112 | Apostrophe | `you've` | `you have` |
| 158-173 | Here-string | `@"..."@` | Multiple `Write-Host` calls |
| 172 | Plus symbol | `Node A + Node C` | `Node A and Node C` |

---

## How to Run the Fixed Script

```powershell
cd 'C:\@Repos\Visual Studio Projects\SplitBrain.AI'

# Check prerequisites
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs

# Setup with token
.\scripts\setup-test-nodeac.ps1 -GitHubToken "your-token"

# Or auto-detect from GitHub CLI
.\scripts\setup-test-nodeac.ps1
```

---

## Documentation

Three new guides created:

1. **`docs/TESTING-NodeA-NodeC.md`** — Complete setup guide with troubleshooting
2. **`docs/TEST-CASES-NodeA-NodeC.md`** — 9 example test scenarios
3. **`docs/SETUP-CHECKLIST.md`** — Step-by-step checklist
4. **`docs/QUICK-START-Script.md`** — Quick reference for the fixed script

---

✅ **Script is now fully functional and ready to use!**
