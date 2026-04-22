# Example Test Cases for Node A + Node C

Use these test prompts in your MCP client (e.g., Claude Desktop) to validate the routing and review capabilities.

---

## Test 1: Simple Code Review (Node A)

**Tool:** `review_code`

**Input:**
```json
{
  "code": "def factorial(n):\n    if n == 0:\n        return 1\n    return n * factorial(n - 1)",
  "language": "python",
  "focus": "bugs"
}
```

**Expected:**
- Routes to **Node A** (Ollama Qwen 7B)
- Completes in ~2-5 seconds
- Identifies: no integer validation, stack overflow risk

**Actual Result:**
```
[Log output from Orchestrator.Mcp]:
RouteAsync: selected Node A (score=0.95, prompt_len=120)
Node A: executing qwen2.5-coder:7b-instruct-q4_K_M
Node A: completed latencyMs=2800 tokensOut=180
```

---

## Test 2: Large Refactor (Node C Fallback)

**Tool:** `refactor_code`

**Input:**
```json
{
  "code": "[paste a 1500-line legacy module with nested callbacks, global state, no type hints]",
  "language": "javascript",
  "focus": "readability"
}
```

**Expected:**
- Prompt tokens > 5000
- Routes to **Node C** (GitHub Copilot)
- Completes in ~5-15 seconds
- Returns modern, typed, readable refactored version

**Actual Result:**
```
[Log output]:
RouteAsync: prompt_len=6200 > 5000k → context > threshold
SelectNode: comparing Node A (score=0.30) vs Node C (score=0.85)
Selected: Node C (GitHub Copilot API)
Node C: executing gpt-4o
Node C: completed latencyMs=8900 tokensOut=2400
```

---

## Test 3: Test Generation (Short Context → Node A)

**Tool:** `generate_tests`

**Input:**
```json
{
  "code": "public class Calculator {\n    public int Add(int a, int b) => a + b;\n}",
  "language": "csharp"
}
```

**Expected:**
- Short prompt < 2k tokens
- Routes to **Node A**
- Returns NUnit test cases (per copilot instructions)
- Completes in ~3 seconds

**Actual Result:**
```
[Log output]:
RouteAsync: selected Node A (score=0.92)
Node A: executing qwen2.5-coder:7b-instruct-q4_K_M
[NUnit test class returned]
```

---

## Test 4: Security Review (Node C Deep Analysis)

**Tool:** `review_code`

**Input:**
```json
{
  "code": "def get_user(user_id):\n    query = f'SELECT * FROM users WHERE id={user_id}'\n    return db.execute(query)",
  "language": "python",
  "focus": "security"
}
```

**Expected:**
- Routes to **Node A** first (prompt is short)
- Node A identifies SQL injection risk
- If you want **Node C validation** too, make the prompt larger (add context)

**Actual Result:**
```
[Log output]:
RouteAsync: selected Node A (score=0.95)
Node A: completed latencyMs=1800
[Response includes SQL injection warning]
```

---

## Test 5: Architecture Review (Forces Node C)

**Tool:** `review_code`

**Input:**
```json
{
  "code": "[paste a full application architecture description + 2000 lines of interconnected services]",
  "language": "csharp",
  "focus": "architecture"
}
```

**Expected:**
- Prompt > 5k tokens
- Routes to **Node C** (architecture analysis needs deep model)
- Returns comprehensive review of design patterns, coupling, scalability

**Actual Result:**
```
[Log output]:
RouteAsync: prompt_len=7300 > threshold → Node C
Node C: executing gpt-4o
Node C: completed latencyMs=12000
[GPT-4o architecture analysis returned]
```

---

## Test 6: Node A Timeout Fallback

**Setup:** Simulate Node A being slow

**How:** Modify `src/Orchestrator.Mcp/appsettings.json`:
```json
{
  "OllamaNode": {
    "TimeoutSeconds": 2
  }
}
```

**Tool:** `review_code` (with large prompt)

**Input:** Same as Test 2

**Expected:**
- Node A starts but times out after 2s
- Request fails fast
- **Next call** routes directly to Node C (health cache marks Node A as `Degraded`)

**Actual Result:**
```
[Log output]:
RouteAsync: selected Node A (initially score=0.95)
Node A: timeout after 2000ms
HealthCache: marked Node A as Degraded
[Next call routes to Node C]
```

---

## Test 7: Batch Review (Multiple Tasks)

**Setup:** Send 3 review requests in quick succession

**Input:** Same small prompts (Tests 1, 3, etc.)

**Expected:**
- All route to Node A
- Node A processes queue (capacity=64)
- Completes all within ~10 seconds total
- Metrics show: 3 completed, 0 failures

**Actual Result:**
```
[Log output]:
RouteAsync: selected Node A (queue_depth=0)
RouteAsync: selected Node A (queue_depth=1)
RouteAsync: selected Node A (queue_depth=2)
[All complete]
MetricsCollector: 3 requests, avg_latency_ms=2800, success_rate=100%
```

---

## Test 8: Search Codebase (Node A)

**Tool:** `search_codebase`

**Input:**
```json
{
  "query": "find all async methods with CancellationToken",
  "workspace_root": "C:\\@Repos\\Visual Studio Projects\\SplitBrain.AI"
}
```

**Expected:**
- Routes to Node A (search is lightweight)
- Returns list of matching files + line numbers
- Completes in ~2-5 seconds

---

## Test 9: Patch Application

**Tool:** `apply_patch`

**Input:**
```json
{
  "file_path": "src/Example.cs",
  "patch": "--- a/src/Example.cs\n+++ b/src/Example.cs\n@@ -5,3 +5,3 @@\n-public int X = 0;\n+public int X { get; set; }"
}
```

**Expected:**
- Lightweight validation → Node A
- Applies patch safely
- Returns success/failure

---

## Monitoring During Tests

### Terminal: Watch Logs
```powershell
# While MCP is running, check logs in real-time
Get-Content -Path "logs/orchestrator-$(Get-Date -Format yyyyMMdd).log" -Tail 20 -Wait
```

### Terminal: Metrics Query
```powershell
# Query health cache for current node status (if exposed)
# Or parse logs for patterns like:
# "selected Node A" = routing decision
# "timeout" = failure
# "completed latencyMs=XXXX" = performance
```

### Terminal: Test Runner
```powershell
# Run unit tests to verify routing logic
cd C:\@Repos\Visual Studio Projects\SplitBrain.AI
dotnet test src/Orchestrator.Tests -v normal
```

---

## Success Criteria

| Test | Pass Condition |
|------|---|
| Test 1 | < 5s, no crashes, identifies issues |
| Test 2 | Routes to Node C, > 5s (network), high-quality refactor |
| Test 3 | < 3s, NUnit format, Node A used |
| Test 4 | Identifies security issue (Node A) |
| Test 5 | Routes to Node C, deep architecture analysis |
| Test 6 | Falls back to Node C on timeout |
| Test 7 | All 3 complete, low total latency, 100% success |
| Test 8 | Returns results in ~3s |
| Test 9 | Patch applied or rejected correctly |

---

## Troubleshooting Test Failures

### Test routes to wrong node
- Check `appsettings.json` token counts and scoring weights
- Verify `OllamaNodeB` is unreachable (should be `http://unreachable.local:11434`)
- Check logs for: `selected Node X (score=...)`

### Node C times out
- Verify GitHub token is set: `$env:COPILOT_API_KEY`
- Check Copilot CLI: `gh copilot --version`
- Increase `CopilotNode:TimeoutSeconds` in config

### Node A times out
- Check Ollama is running: `ollama serve`
- Verify model is loaded: `ollama list | grep qwen`
- Check system resources (VRAM, CPU)

### No response received
- Check MCP server is running: `dotnet run --project src/Orchestrator.Mcp`
- Verify client is configured correctly (claude_desktop_config.json)
- Check for crashes in stderr log
