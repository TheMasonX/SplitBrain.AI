# 📚 Node A + Node C Testing Guide — Complete Documentation

## 🎯 Start Here

**Choose based on where you are:**

### 🔧 "I just want to know what was fixed"
→ **[RESOLUTION-SUMMARY.md](RESOLUTION-SUMMARY.md)** — Your issues → Our fixes
- UseBasicParsing added
- Graceful error handling
- Safe command detection
- Professional error messages

### 🚀 "I want to set up and run it NOW"
→ **[SETUP-CHECKLIST.md](SETUP-CHECKLIST.md)** — Step-by-step checklist
- Prerequisites check
- Environment setup
- Running the servers
- Verification steps

### 📋 "I want the quick version"
→ **[QUICK-START-Script.md](QUICK-START-Script.md)** — 5-minute reference
- Setup script commands
- How to run (3 terminals)
- Configuration reference

### 📖 "I want the full technical guide"
→ **[TESTING-NodeA-NodeC.md](TESTING-NodeA-NodeC.md)** — Complete walkthrough
- Detailed prerequisites
- Configuration options
- Monitoring & debugging
- Troubleshooting

### 🧪 "I want test examples"
→ **[TEST-CASES-NodeA-NodeC.md](TEST-CASES-NodeA-NodeC.md)** — 9 example scenarios
- Simple code review (Node A)
- Large refactor (Node C)
- Timeout fallback
- Batch processing
- Success criteria

### 🐛 "I want technical details"
→ **[SCRIPT-IMPROVEMENTS.md](SCRIPT-IMPROVEMENTS.md)** — Technical deep-dive
- UseBasicParsing details
- Safe command detection pattern
- Error collection approach
- Before/after comparison

### 🚨 "I hit the npm 404 error for Copilot CLI"
→ **[FIX-COPILOT-CLI-NPM-ISSUE.md](FIX-COPILOT-CLI-NPM-ISSUE.md)** — How to fix it
- Why npm package doesn't exist anymore
- Correct installation: `gh extension install github/gh-copilot`
- What you saw (and why it's correct)
- Detailed guide: [COPILOT-CLI-CORRECT-INSTALL.md](COPILOT-CLI-CORRECT-INSTALL.md)

---

## Quick Navigation

| Document | Purpose | Time |
|----------|---------|------|
| **RESOLUTION-SUMMARY.md** | Your issues → our fixes (START HERE!) | 3 min |
| **FIX-COPILOT-CLI-NPM-ISSUE.md** | npm 404 error & fix (gh extension method) | 2 min |
| **COPILOT-CLI-CORRECT-INSTALL.md** | Detailed modern installation guide | 5 min |
| **SETUP-CHECKLIST.md** | Step-by-step checklist | 15-20 min |
| **QUICK-START-Script.md** | Fast reference + commands | 2-3 min |
| **TESTING-NodeA-NodeC.md** | Full technical guide | 30+ min |
| **TEST-CASES-NodeA-NodeC.md** | Validate with examples | 10 min |
| **SCRIPT-IMPROVEMENTS.md** | Technical deep-dive | 3 min |
| **FINAL-SCRIPT-STATUS.md** | Status overview | 2 min |
| **COMPLETE-RESOLUTION.md** | All fixes summarized | 5 min |

---

## What's Been Set Up For You

### ✅ Fixed Script
**Location:** `scripts/setup-test-nodeac.ps1`

```powershell
# Prerequisite check
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs

# Setup with token
.\scripts\setup-test-nodeac.ps1 -GitHubToken "ghp_..."

# Or auto-detect from GitHub CLI
.\scripts\setup-test-nodeac.ps1
```

**What it does:**
- Verifies Ollama, GitHub CLI, Copilot CLI installed
- Sets `COPILOT_API_KEY` environment variable
- Updates `appsettings.json` to disable Node B
- Configures Node C timeout (60s)

---

### ✅ Configuration Files Updated
**Location:** `src/Orchestrator.Mcp/appsettings.json`

After running the setup script:
```json
{
  "OllamaNode": {
    "BaseUrl": "http://localhost:11434"
  },
  "OllamaNodeB": {
    "BaseUrl": "http://unreachable.local:11434"
  },
  "CopilotNode": {
    "Model": "gpt-4o",
    "TimeoutSeconds": 60
  }
}
```

**Key Changes:**
- Node A: Points to local Ollama (enabled)
- Node B: Set to unreachable (disabled → falls back to Node C)
- Node C: GitHub Copilot configured (enabled)

---

### ✅ MCP Server (Node A + Router + Node C)
**Location:** `src/Orchestrator.Mcp`

```powershell
# Start the MCP server
cd C:\@Repos\Visual Studio Projects\SplitBrain.AI
dotnet run --project src/Orchestrator.Mcp
```

**What it does:**
- Hosts Node A inference client (Ollama)
- Hosts Node C inference client (GitHub Copilot)
- Routes requests intelligently between them
- Exposes tools via MCP protocol

---

## Running Everything (3 Terminals)

### Terminal 1: Ollama (Backend)
```powershell
ollama serve
# Wait for: Listening on 127.0.0.1:11434
```

### Terminal 2: MCP Server
```powershell
cd 'C:\@Repos\Visual Studio Projects\SplitBrain.AI'
dotnet run --project src/Orchestrator.Mcp
# Wait for: MCP server listening on stdio
```

### Terminal 3: Client Testing
Use Claude Desktop or custom MCP client to send review requests.

---

## How Routing Works

```
Request comes in (code review, refactor, etc.)
    ↓
RoutingService analyzes:
  - Prompt size
  - Node health
  - Queue depth
  - Task type
    ↓
Decision:
  ┌─────────────────────────────────────────┐
  │ Small + healthy Node A → Node A (fast)  │
  │ Large or Node A down → Node C (deep)    │
  └─────────────────────────────────────────┘
    ↓
Response in 2-15 seconds
```

---

## Key Concepts

### Node A (Local Ollama)
- **Model:** Qwen 2.5 Coder 7B (Q4 quantized)
- **GPU:** RTX 5060 8GB (can use CPU fallback)
- **Speed:** 2-5 seconds per request
- **When used:** Small prompts < 5k tokens
- **Requires:** Ollama running on localhost:11434

### Node C (GitHub Copilot)
- **Model:** GPT-4o
- **Cloud:** Yes (GitHub's infrastructure)
- **Speed:** 5-15 seconds per request
- **When used:** Large prompts, deep analysis, Node A unavailable
- **Requires:** GitHub token + Copilot CLI

### Node B (Disabled for This Test)
- **Status:** Set to unreachable (`http://unreachable.local:11434`)
- **Effect:** Removed from routing decisions
- **Purpose:** Focus on Node A + Node C testing

---

## Testing Workflow

**1. Setup** (once)
```powershell
.\scripts\setup-test-nodeac.ps1 -CheckPrereqs
.\scripts\setup-test-nodeac.ps1 -GitHubToken "your-token"
```

**2. Start servers** (each test session)
- Terminal 1: `ollama serve`
- Terminal 2: `dotnet run --project src/Orchestrator.Mcp`

**3. Run tests** (Terminal 3)
- Use Claude Desktop or custom MCP client
- Send code review / refactor / test generation requests
- Monitor logs in Terminals 1 & 2

**4. Verify** 
- Check Terminal 2 logs for routing decision
- Verify response quality and latency
- See examples in TEST-CASES-NodeA-NodeC.md

---

## Success Criteria

✅ All of these should be true:
- [ ] Ollama running on localhost:11434
- [ ] MCP server starts without errors
- [ ] Can send requests via MCP client
- [ ] Small code reviews route to Node A (< 5 seconds)
- [ ] Large refactors route to Node C (5-15 seconds)
- [ ] Node health monitoring works
- [ ] Queue system handles concurrent requests

---

## Troubleshooting Quick Reference

| Issue | Solution |
|-------|----------|
| Ollama not found | `ollama serve` in Terminal 1 |
| MCP won't start | `dotnet --version` to verify SDK installed |
| Token error | Re-run: `.\scripts\setup-test-nodeac.ps1 -GitHubToken "token"` |
| Wrong node selected | Check logs, verify `appsettings.json` |
| Timeout errors | Increase `CopilotNode:TimeoutSeconds` in config |
| Script won't run | `powershell -ExecutionPolicy Bypass -File .\scripts\setup-test-nodeac.ps1` |

See **[TESTING-NodeA-NodeC.md](TESTING-NodeA-NodeC.md)** for detailed troubleshooting.

---

## What to Do Next

### Short Term (This Week)
1. Run setup script
2. Start Ollama + MCP server
3. Test with 3-4 code review examples
4. Verify routing is working correctly
5. Check logs and response quality

### Medium Term (Next Week)
1. Test various code snippets (Python, C#, JavaScript)
2. Monitor performance metrics
3. Experiment with different focus areas (bugs, security, architecture)
4. Try timeout scenarios
5. Batch process multiple requests

### Long Term (Next Month)
1. Enable Node B for deep tasks
2. Run full integration tests
3. Optimize routing weights
4. Set up monitoring/dashboard
5. Prepare for production deployment

---

## Files & Locations

```
SplitBrain.AI/
├── docs/
│   ├── SETUP-CHECKLIST.md ..................... Start here ✅
│   ├── QUICK-START-Script.md ................. Fast reference 🚀
│   ├── TESTING-NodeA-NodeC.md ............... Full guide 📖
│   ├── TEST-CASES-NodeA-NodeC.md ........... Examples 🧪
│   ├── SCRIPT-FIX-SUMMARY.md ................. Fix details 🐛
│   └── THIS FILE (INDEX) ..................... You are here 📍
│
├── scripts/
│   └── setup-test-nodeac.ps1 ................. Setup automation ⚙️
│
└── src/
    ├── Orchestrator.Mcp/ ..................... MCP server
    │   ├── Program.cs ........................ DI + routing setup
    │   ├── appsettings.json .................. Configuration ✨
    │   └── Tools/ ........................... ReviewCode, RefactorCode, etc.
    │
    ├── NodeClient.Ollama/ .................... Node A (local)
    │   ├── OllamaClient.cs
    │   ├── NodeAInferenceNode.cs
    │   └── NodeBInferenceNode.cs
    │
    └── NodeClient.Copilot/ .................. Node C (cloud)
        ├── CopilotClient.cs ................. SDK integration
        ├── NodeCInferenceNode.cs
        └── CopilotClientOptions.cs
```

---

## Quick Links

- **GitHub Repo:** https://github.com/TheMasonX/SplitBrain.AI
- **Master Plan:** `Plans/MasterPlan.md`
- **Current Branch:** `main`

---

## Support

For issues:
1. Check **[TESTING-NodeA-NodeC.md](TESTING-NodeA-NodeC.md)** troubleshooting section
2. Review **[SCRIPT-FIX-SUMMARY.md](SCRIPT-FIX-SUMMARY.md)** if script fails
3. See **[TEST-CASES-NodeA-NodeC.md](TEST-CASES-NodeA-NodeC.md)** for expected behavior
4. Check logs in Terminal 2 for routing decisions

---

**🎉 Ready to test Node A + Node C!**

Choose a guide above and start with SETUP-CHECKLIST.md.
