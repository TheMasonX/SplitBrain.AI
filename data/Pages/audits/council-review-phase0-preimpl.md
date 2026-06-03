# Council Review: Phase 0 — Pre-Implementation Gate
## Branch: `next-gen` · Commit: `2d4b8072` · Date: 2026-06-03

**Gate decision: APPROVED WITH NOTES**  
**Seat confidences:** Archivist 87% · Architect 84% · Practitioner 91% · Skeptic 67% · Advocate 88%

---

## Scope

Phase 0 is a pure bug-fix sprint — no new tools, no new routes, no API surface changes.

| Fix | File | Risk |
|-----|------|------|
| P0.1 NodeB timeout fast-fail | appsettings.json | Low |
| P0.2 ResponseCleaner (fence-strip + codegen forgiveness) | New: Helpers/ResponseCleaner.cs | Medium |
| P0.3 Apply ResponseCleaner to RefactorCodeTool | RefactorCodeTool.cs | Low |
| P0.4 Apply ResponseCleaner to GenerateTestsTool + fix output path | GenerateTestsTool.cs | Low |
| P0.5 Fix SearchCodebaseTool MatchGlob | SearchCodebaseTool.cs | Low |
| P0.6 Fix AgentTaskTool Meta null | AgentTaskTool.cs | Low |
| P0.7 Add per-tool 30s timeout to all LLM tools | All tool files | Medium |
| P0.8 Add ILoggingService parity to all tools | All tool files | Low |

---

## Seat 1 — Archivist (Source Verification) | 87%

### P0.1 APPROVED: appsettings.json NodeB timeout

`OllamaNodeB.TimeoutSeconds = 300` → `5` is the correct fix. Evidence from live evaluation:  
- Node B never served any of 5 test calls  
- All generative tools timed out at exactly ~240s (Claude Desktop's MCP ceiling)  
- `HttpClient.Timeout = TimeoutSeconds * 2 = 600s` — well above Claude's threshold  

Risk: If Node B IS reachable but slow (model loading), 5s may cause spurious fast-fail. **Mitigation:** 5s is calibrated to detect genuinely unreachable nodes (TCP timeout ~3s on LAN) while allowing the routing health cache to catch slow-but-reachable nodes. If Node B is loading a model, the `/health` endpoint returns `{"status":"loading model"}` which routes it to Degraded before any inference is attempted.

Also note: `nodes.json` (previously read, contains real LAN IP) has `TimeoutSeconds = 120` — also needs updating to 10s (slightly more generous than appsettings.json for actual LAN inference).

### P0.2 NEEDS DESIGN DECISION: ResponseCleaner scope

Looking at `AgentOrchestrator.ExtractDiff` (already implemented in the codebase):
```csharp
private static string ExtractDiff(string response) {
    // Handles: ```diff, ```, raw diff headers
}
```

And `SearchCodebaseTool.TryParseResults` (already implemented):
```csharp
private static List<SearchResult> TryParseResults(string modelText, int topK) {
    // Strips markdown fences before JSON parsing
}
```

These show the PATTERN but need to be generalized. The ResponseCleaner should handle:
1. Code fence extraction (largest block wins for codegen)  
2. Preamble stripping ("Here's the refactored code:\n\n```")  
3. Outro stripping ("```\nI made the following changes:")  
4. Language-tagged fence handling  
5. Fallback: return original text (never empty if input non-empty)  

**NOT in scope**: ResponseCleaner should NOT try to parse or validate the code. It's a text extraction tool, not a code analyzer.

### P0.6 APPROVED: AgentTaskTool Meta fix

`AgentTaskResponse.Meta = default!` (null) confirmed. `AgentResult` has no `TaskId`, `NodeId`, or `LatencyMs`. Correct fix: generate a fresh `taskId`, use `"agent"` as the virtual node ID, sum step latencies for `LatencyMs`. This is accurate enough for observability.

---

## Seat 2 — Architect (Structure) | 84%

### ResponseCleaner placement

`src/Orchestrator.Mcp/Helpers/ResponseCleaner.cs` — correct namespace: `Orchestrator.Mcp.Helpers`.

The extractor should be `static` (no DI, no state), pure function inputs/outputs. Methods:

```csharp
public static class ResponseCleaner
{
    // Extract code from model output — for refactor_code, generate_tests
    public static string ExtractCode(string raw, string language);
    
    // Strip fences only — for tools that want code but accept prose
    public static string StripFences(string raw);
    
    // Extract JSON array from model output — already in SearchCodebaseTool
    public static bool TryExtractJson<T>(string raw, out T? result);
}
```

### Per-tool timeout placement

The 30s timeout should be in `ExecuteCoreAsync`, wrapping the `_routing.RouteAsync` call via a linked `CancellationTokenSource`. Do NOT wrap the `IdempotencyHelper.ExecuteAsync` — that would cancel idempotency state updates on timeout, leaving a `Processing` slot that never transitions to `Complete` or `Failed`.

Correct pattern:
```csharp
private async Task<string> ExecuteCoreAsync(..., CancellationToken ct) {
    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(TimeSpan.FromSeconds(30));   // configurable via appsettings
    try {
        var result = await _routing.RouteAsync(..., cts.Token);
        ...
    } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
        // Our timeout, not the caller's cancellation
        return MakeTimeoutError(toolName, taskId);
    }
    // But DO let OperationCanceledException propagate when caller cancelled
}
```

### ILoggingService parity

`ReviewCodeTool` already injects `ILoggingService`. Other tools need it injected. Pattern is: constructor injection, `LogRequestAsync` + `LogInferenceAsync` + `LogResponseAsync` calls. The LoggingService calls are `await`-ed but fire-and-forget from the tool's perspective (if logging fails it shouldn't fail the tool).

Wrap logging calls in `try { await _log.LogXxx(...); } catch { }` to prevent logging failures from breaking inference.

---

## Seat 3 — Practitioner (Agent Usability) | 91%

### "Maximally forgiving" — what this means concretely

When Claude (or another agent) calls `refactor_code`:
1. It sends code + language + goal
2. It expects to get back cleaned, ready-to-use code in `response.summary`
3. It does NOT want to parse markdown fences itself

The `ResponseCleaner.ExtractCode` contract:
- Input: `"Here's the refactored version:\n\n```csharp\npublic class Foo { ... }\n```\n\nKey changes:\n- Renamed X to Y"` 
- Output: `"public class Foo { ... }"`

Strategy for multi-block responses (model generates "before" and "after"):
- Take the LAST code fence (most likely to be the final answer)
- OR take the LARGEST code block by character count

Strategy for no-fence responses:
- If the text looks like code (starts with `using`, `import`, `def `, `class `, `public `, `function `, `#include`, etc.) → return as-is, it's clean code
- If the text looks like prose + code mixed → try to find the code-looking region and return it
- If all prose → return as-is (caller sees the explanation)

### GenerateTestsTool output path

Current: `Tests.csharp`, `Tests.python`, `Tests.typescript`  
Target: Language-appropriate test file name

Heuristic to extract class name from code:
```csharp
private static string ExtractPrimaryClassName(string code) {
    // Match: class Foo, class Foo:, public class Foo, etc.
    var m = Regex.Match(code, @"\bclass\s+(\w+)", RegexOptions.Multiline);
    return m.Success ? m.Groups[1].Value : null;
}
```

Then:
```csharp
var className = ExtractPrimaryClassName(code) ?? "Generated";
var ext = language.ToLowerInvariant() switch {
    "csharp" or "cs" => "cs",
    "python" or "py" => "py",
    "typescript" or "ts" => "ts",
    "javascript" or "js" => "js",
    "java" => "java",
    _ => language
};
var path = $"{className}Tests.{ext}";
```

---

## Seat 4 — Skeptic (Risk Scenarios) | 67%

### Risk: ResponseCleaner over-strips legitimate preamble in documentation tools

`document_code` (planned for Phase 1) might legitimately return a brief explanation followed by code with comments. If ResponseCleaner is applied, the explanation would be stripped. **Mitigation:** Make `ExtractCode` only strip outer prose, not embedded comments. The code block itself is preserved verbatim.

### Risk: 30s timeout is too short for large code files

On Node B (GTX 1080) a long inference might legitimately take 45-60s for a 500-line refactor. **Mitigation:** The timeout value should be configurable: `ToolOptions.TimeoutSeconds` in appsettings.json (default 30, max 120). If Node B is the selected node, the health check latency data can inform whether 30s is sufficient.

### Risk: MatchGlob fix needs FileSystemGlobbing package

`Microsoft.Extensions.FileSystemGlobbing` needs to be in `Orchestrator.Mcp.csproj`. If it's not already a transitive dependency, add it. Package is `Microsoft.Extensions.FileSystemGlobbing` on NuGet — lightweight, no extra deps.

### Risk: AgentTaskTool `applyChanges` parameter not in scope

The council review noted that diff application was in Phase 0. Re-reading carefully: the P0.2 specification says "apply diff with write gate." The write gate itself is a Phase 1 item. For Phase 0, fix Meta and add the parameter signature but leave application deferred to Phase 1 when the write gate exists. This is the conservative path. **Decision:** Add `applyChanges` parameter (default false), log a TODO comment, apply Meta fix. Do NOT wire actual diff application yet (no write gate = unsafe).

### Risk: Logging in tools adds I/O overhead

`ILoggingService` writes to disk (file logger at `%TEMP%/splitbrain-mcp-*.log`). All tool calls would now have file I/O. Wrap ALL logging calls in `try { ... } catch { }` so logging failures cannot fail a tool call.

---

## Seat 5 — Advocate (Value Assessment) | 88%

Prioritized by expected user-visible improvement:

| Change | Value if it works | Confidence |
|--------|-------------------|-----------|
| NodeB timeout fix | 3 broken tools start working | 95% |
| ResponseCleaner on refactor_code | Cleaner output, agent-usable | 90% |
| ResponseCleaner on generate_tests | Cleaner output + valid filename | 88% |
| AgentTaskTool Meta fix | agent_task no longer crashes | 95% |
| MatchGlob fix | search_codebase respects patterns | 92% |
| 30s per-tool timeout | Structured errors instead of hangs | 85% |
| Logging parity | Better diagnostics | 75% |

The NodeB timeout fix alone delivers the most value. The ResponseCleaner is the most nuanced change — design it conservatively and test with common model output patterns.

---

## Gate Decision: APPROVED

All 8 changes are technically sound and well-scoped. No architectural red flags. Proceed to implementation.

**Required before commit:**
1. Verify `Microsoft.Extensions.FileSystemGlobbing` is available (add PackageReference if not)
2. Confirm 30s timeout is configurable, not hardcoded
3. ResponseCleaner must never return empty when input was non-empty
4. `applyChanges` parameter added with TODO but no actual disk write yet
5. All logging wrapped in try/catch

**Phase 0 branch:** `feature/phase-0-fixes` off `2d4b8072`
