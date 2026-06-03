# SplitBrain.AI — MCP Tool Evaluation Report

**Date:** 2026-06-02  
**Evaluator:** Claude Sonnet 4.6 (via claude.ai web)  
**MCP Server:** `http://localhost:5000/mcp` (Orchestrator.Mcp, .NET 10 / ASP.NET Core)  
**Inference Node:** `http://localhost:11434` (Ollama)  
**Model:** `qcoder:latest`  
**Bridge:** `npx mcp-remote` proxy → Claude Desktop  

---

## Executive Summary

| Tool | Status | Calls Attempted | Calls Succeeded | Avg Latency |
|---|---|---|---|---|
| `review_code` | ✅ Operational | 5 | 5 | ~3,830 ms |
| `refactor_code` | ❌ Timing Out | 2 | 0 | >4 min (timeout) |
| `generate_tests` | ❌ Timing Out | 2 | 0 | >4 min (timeout) |
| `agent_task` | ❌ Timing Out | 1 | 0 | >4 min (timeout) |

**Bottom line:** `review_code` is production-ready across multiple languages and focus areas. The remaining three tools — `refactor_code`, `generate_tests`, and `agent_task` — all fail with a consistent 4-minute hard timeout, suggesting a systemic handler or model-routing issue affecting tools that require code generation (as opposed to analysis-only responses).

---

## Tool 1: `review_code`

### Overview
Analyzes source code across five focus areas: `bugs`, `performance`, `security`, `readability`, `architecture`. Tested across C#, Python, and TypeScript.

---

### Test 1.1 — Python / Bugs

**Input:**
```python
def process_orders(orders):
    total = 0
    for o in orders:
        if o['status'] == 'active':
            total = total + o['amount'] * o['discount']
    return total
```

**Focus:** `bugs` | **Language:** `python`

**Result:** ✅ Success — Latency: 2,956 ms

**Summary from MCP:**
- No syntax errors detected
- Flagged potential floating-point arithmetic imprecision when multiplying `amount` by `discount`
- Recommended integer arithmetic or explicit float casting for precision-sensitive use cases

**Assessment:**  
Accurate low-severity catch. The discount multiplication is semantically suspicious (discount should typically divide or be a coefficient < 1 — the model could have flagged the missing bounds check on `discount`), but the float precision note is legitimate. Missed: no null/key-existence guard on `o['status']`, `o['amount']`, or `o['discount']`.

---

### Test 1.2 — Python / Performance

**Input:** Same `process_orders` function.

**Focus:** `performance` | **Language:** `python`

**Result:** ✅ Success — Latency: 1,296 ms *(fastest observed)*

**Summary from MCP:**
- O(n) time complexity, O(1) space — correctly characterized
- Assessed as efficient and well-written

**Assessment:**  
Accurate. No false positives. For this simple loop there's genuinely nothing to optimize, and the model correctly declined to manufacture issues. The fast latency on a short analysis-only response is a good sign — `performance` focus likely hits a more tightly scoped prompt path.

---

### Test 1.3 — C# / Security (Deliberately Vulnerable Code)

**Input:**
```csharp
public class AuthController : ControllerBase
{
    [HttpPost("login")]
    public IActionResult Login(string username, string password)
    {
        var query = $"SELECT * FROM Users WHERE username='{username}' AND password='{password}'";
        var user = _db.ExecuteRaw(query);
        if (user != null)
        {
            var token = username + "_" + DateTime.Now.Ticks;
            return Ok(new { token });
        }
        return Unauthorized();
    }
}
```

**Focus:** `security` | **Language:** `csharp`

**Result:** ✅ Success — Latency: 622 ms *(second fastest)*

**Summary from MCP:**
- SQL Injection via string interpolation
- No password hashing or verification
- Insecure and non-unique token generation

**Assessment:**  
Excellent. This was a deliberately injected triple-vulnerability sample and the model caught all three issues cleanly and concisely. The fast latency suggests the security focus triggers a shorter, more direct output path — the model didn't over-explain, just enumerated findings. No false positives, no missed issues. Best response quality in the test suite.

---

### Test 1.4 — TypeScript / Readability

**Input:**
```typescript
const fetchUserData = async (userId) => {
  const res = await fetch(`/api/users/${userId}`);
  const data = await res.json();
  return data;
};

const renderUser = async (id) => {
  const user = await fetchUserData(id);
  document.getElementById('name').innerText = user.name;
  document.getElementById('email').innerText = user.email;
};
```

**Focus:** `readability` | **Language:** `typescript`

**Result:** ✅ Success — Latency: 2,015 ms

**Summary from MCP:**
- Descriptive function names ✓
- async/await usage readable ✓
- Missing try/catch error handling
- `getElementById` works but `querySelector` or framework-specific selectors preferred

**Assessment:**  
Solid. The readability praise is warranted. The error handling note is technically a bug/robustness issue rather than a readability issue — slight focus bleed. The `querySelector` suggestion is stylistic and valid. Missed: no TypeScript type annotations on parameters (notable for a TS-specific review).

---

### Test 1.5 — C# / Architecture (Complex Input)

**Input:** `ReportEngine` class with if/else branching across PDF, Excel, CSV report types (~30 lines).

**Focus:** `architecture` | **Language:** `csharp`

**Result:** ✅ Success — Latency: 12,261 ms *(highest observed)*

**Summary from MCP:**
- Identified code duplication across report branches
- Flagged lack of abstraction and nested if/else chains
- Called out magic strings for report types
- Recommended: Strategy Pattern, enum for report types, extract methods
- Provided a complete refactored code sample demonstrating the pattern

**Assessment:**  
Best qualitative response in the batch. The architecture focus unlocked the richest output — a full strategy pattern decomposition with working code. This is a meaningful, actionable review, not a checklist. The latency spike (12s) is directly correlated with output length; not a concern. Notable: this is the only response that included inline code samples in the summary, suggesting the model scales output depth to complexity of the input.

---

### `review_code` — Aggregate Findings

| Test | Language | Focus | Latency | Quality |
|---|---|---|---|---|
| 1.1 | Python | bugs | 2,956 ms | Good — minor misses |
| 1.2 | Python | performance | 1,296 ms | Excellent |
| 1.3 | C# | security | 622 ms | Excellent |
| 1.4 | TypeScript | readability | 2,015 ms | Good — minor focus bleed |
| 1.5 | C# | architecture | 12,261 ms | Excellent — best output depth |

**Latency pattern:** Scales with output length, not input length. Security/performance (short outputs) = fastest. Architecture (long outputs with code) = slowest. This is expected and healthy behavior.

**Cross-language capability:** C#, Python, TypeScript all handled correctly with no degradation.

---

## Tool 2: `refactor_code`

### Test 2.1 — Python / reduce_complexity

**Input:** Duplicated `get_report()` function with repeated if/elif branches for `monthly`/`weekly` types.

**Goal:** `reduce_complexity` | **Language:** `python`

**Result:** ❌ Timeout — No response after 4 minutes

---

### Test 2.2 — C# / readability

**Input:**
```csharp
public string FormatName(string first, string last, bool upper) {
    if (upper == true) { return first.ToUpper() + " " + last.ToUpper(); }
    else { return first + " " + last; }
}
```

**Goal:** `readability` | **Language:** `csharp`

**Result:** ❌ Timeout — No response after 4 minutes

---

### `refactor_code` — Assessment

Both calls failed identically. The timeout is consistent and not input-dependent (the C# test was a 4-line trivial function). This strongly suggests the issue is in the **tool handler itself** — either the `refactor_code` prompt template, the Ollama model routing for this tool, or a response-parsing stage that never completes. The tool is registered and reachable (no connection error), but the processing pipeline hangs before returning.

**Likely cause:** `refactor_code` requires the model to generate modified source code as output. If the output parser expects a specific structured format (e.g., JSON with a `code` field) and the model returns plain text or vice versa, the handler may deadlock waiting for a format that never arrives.

---

## Tool 3: `generate_tests`

### Test 3.1 — C# / xunit / all coverage

**Input:** `Calculator` class with `Add`, `Subtract`, `Divide` (including divide-by-zero guard).

**Framework:** `xunit` | **Coverage:** `all` | **Language:** `csharp`

**Result:** ❌ Timeout — No response after 4 minutes

---

### Test 3.2 — Python / pytest / edge_cases

**Input:**
```python
def clamp(value, min_val, max_val):
    return max(min_val, min(max_val, value))
```

**Framework:** `pytest` | **Coverage:** `edge_cases` | **Language:** `python`

**Result:** ❌ Timeout — No response after 4 minutes

---

### `generate_tests` — Assessment

Same failure pattern as `refactor_code`. The `clamp` test is about as minimal as it gets — 1 function, 1 line of logic — so the timeout is definitely not caused by input complexity. Like `refactor_code`, this tool requires code generation as output. Same root cause hypothesis applies: a structured output contract mismatch between the Ollama model response and the MCP handler's response parser.

---

## Tool 4: `agent_task`

### Test 4.1 — Add input validation to a Python function

**Goal:** `Add input validation to this Python function: def divide(a, b): return a / b`  
**Context:** `Python 3.11, no external dependencies, should raise ValueError for bad inputs`

**Result:** ❌ Timeout — No response after 4 minutes

---

### `agent_task` — Assessment

`agent_task` runs a multi-step Plan → Implement → Review → Test loop (up to 4 iterations, 12k tokens). A timeout here could mean it never completed even the first iteration, or it got stuck between stages. Given that `refactor_code` and `generate_tests` (simpler, single-step tools) also fail, the `agent_task` failure is unsurprising — its implementation likely shares the same code generation output path.

---

## Root Cause Analysis

### Why `review_code` succeeds while others fail

`review_code` returns a **summary string** — plain text analysis. The other three tools all require **code generation**: modified source code, test file output, or agent-patched implementations. 

The working hypothesis is that `qcoder:latest` (or the Orchestrator.Mcp prompt construction for these tools) produces output in a format the handler cannot parse, causing it to wait indefinitely for a valid structured response that never arrives.

### Possible failure vectors (ranked by likelihood)

1. **Response schema mismatch** — The handler expects `{ "code": "...", ... }` JSON but the model returns plain text or markdown fences. The deserializer blocks forever.
2. **Prompt template incomplete** — The system prompt for `refactor_code`/`generate_tests`/`agent_task` may be missing or malformed, causing the model to produce an unusable response.
3. **Model capability gap** — `qcoder:latest` may not be well-suited for instruction-following code generation tasks (as opposed to code analysis). A more capable model like `codellama:13b` or `deepseek-coder` might resolve this.
4. **Token budget / context window** — The model may be hitting a context limit mid-generation and returning an empty or partial response that the parser rejects.
5. **ASP.NET handler not awaiting correctly** — An async deadlock in the .NET handler (e.g., `.Result` called on an async method) would cause a 4-minute timeout with no error propagation.

---

## Recommendations

### Immediate

- **Add response logging before deserialization** in `refactor_code`, `generate_tests`, and `agent_task` handlers. Log the raw string from the Ollama response to the Serilog sink at `%TEMP%/splitbrain-mcp-[date].log`. This will immediately reveal whether the model is returning something and the parser is failing, or the model isn't returning at all.
- **Test the Ollama endpoint directly** with the same prompt these handlers send via `curl` or a quick `.http` file. If Ollama responds locally but the MCP tool times out, the bug is in the handler. If Ollama hangs too, the bug is the model/prompt.

### Short-Term

- **Switch model for generative tools.** `qcoder:latest` appears to handle code analysis well but may struggle with structured code generation. Try `deepseek-coder:6.7b-instruct` or `codellama:13b-instruct` for `refactor_code` and `generate_tests`. Keep `qcoder:latest` for `review_code` where it's performing well.
- **Add a per-tool timeout with fallback error response** in the MCP handlers (e.g., 30s hard timeout that returns a structured error rather than hanging the entire connection).
- **Validate JSON output contract.** If the handler expects structured JSON, add an explicit instruction in the prompt: `"Respond only with valid JSON matching this schema: ..."` to ensure the model conforms.

### Longer Term

- **Implement model routing by tool type.** Analysis tools (`review_code`) and generation tools (`refactor_code`, `generate_tests`) likely perform better with different models. The orchestration layer is well-positioned to support per-tool model assignment.
- **Add integration tests** that hit each MCP tool with a minimal payload and assert a non-timeout response. Gate CI on these tests before deployment.

---

## Appendix: Raw Metadata

| Call | Tool | taskId | Node | Model | Latency |
|---|---|---|---|---|---|
| 1.1 | review_code | e6812f1a | A | qcoder:latest | 2,956 ms |
| 1.2 | review_code | f4890ca9 | A | qcoder:latest | 1,296 ms |
| 1.3 | review_code | 2ea31ce9 | A | qcoder:latest | 622 ms |
| 1.4 | review_code | 0a2eb467 | A | qcoder:latest | 2,015 ms |
| 1.5 | review_code | 315ac96e | A | qcoder:latest | 12,261 ms |
| 2.1 | refactor_code | — | — | — | >4 min timeout |
| 2.2 | refactor_code | — | — | — | >4 min timeout |
| 3.1 | generate_tests | — | — | — | >4 min timeout |
| 3.2 | generate_tests | — | — | — | >4 min timeout |
| 4.1 | agent_task | — | — | — | >4 min timeout |

*All successful calls routed to Node A. No Node B activity observed — single-node routing may be in effect or Node B is not registered.*

---

*Report generated by Claude Sonnet 4.6 via SplitBrain.AI MCP integration — 2026-06-02*
