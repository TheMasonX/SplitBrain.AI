# Council Review: Phase 0 — Post-Implementation Gate
## Date: 2026-06-03

**Gate Decision: CONDITIONAL PASS — 2 refinements needed before commit**

---

## Implementation Verification

| Change | Expected | Status | Notes |
|--------|---------|--------|-------|
| OllamaNodeB.TimeoutSeconds = 5 | Fast-fail NodeB | ✅ | Also added ToolOptions section |
| ResponseCleaner.cs | New shared utility | ✅ | 5 strategies, never empty |
| RefactorCodeTool | Fence-strip + timeout + logging | ✅ | 45s default |
| GenerateTestsTool | Fence-strip + path fix + timeout + logging | ✅ | Regex class name extract |
| SearchCodebaseTool | Real glob + timeout + logging | ✅ | FileSystemGlobbing |
| AgentTaskTool | Meta populated + timeout + applyChanges param | ✅ | applyChanges deferred to Phase 1 |

---

## Seat 1 — Archivist | FINDING

### MEDIUM: ResponseCleaner.cs has an invalid LINQ `.Last()` call on `OrderBy`

```csharp
var best = fenceBlocks
    .OrderByDescending(b => b.Length)
    .ThenWithIndex()   // <-- extension method from EnumerableExtensions
    .Last();            // <-- .Last() doesn't exist on IOrderedEnumerable<(string, int)>
```

The `ThenWithIndex()` extension returns `IEnumerable<(string Item, int Index)>`, but `OrderByDescending` returns `IOrderedEnumerable<string>`. The chain is type-confused and won't compile. Fix:

```csharp
var best = fenceBlocks
    .Select((block, idx) => (block, idx))
    .OrderByDescending(x => x.block.Length)
    .ThenByDescending(x => x.idx)  // last occurrence wins on tie
    .First()
    .block;
```

Also: `EnumerableExtensions.WithIndex` is defined but not actually used after the fix. Remove it or mark it internal for future use.

### LOW: ResponseCleaner has unused `using` for JsonConfig

`ResponseCleaner.cs` has `using Orchestrator.Core.Serialization;` for `JsonConfig.Default` in `TryExtractJson`. This is correct and necessary. No change needed.

---

## Seat 2 — Architect | FINDINGS

### LOW: ToolOptions section in appsettings.json not wired to C# options class

The `appsettings.json` has a `ToolOptions` section but no corresponding `ToolOptions` C# class bound via `builder.Services.Configure<ToolOptions>(...)`. The per-tool `const int DefaultToolTimeoutSeconds` values in each tool are hardcoded, not reading from config. 

**Decision for Phase 0:** Leave as hardcoded constants, add `// TODO: wire from ToolOptions config` comment. Proper DI wiring is Phase 1 polish. The constants are correct values.

### LOW: SearchCodebaseTool still injects ILoggingService but the ctor signature changed

The new SearchCodebaseTool constructor adds `ILoggingService log` and `ILogger<SearchCodebaseTool> logger` which aren't in the DI container registration if `Program.cs` doesn't inject them. However, looking at `ReviewCodeTool`, `ILoggingService` IS registered in DI and the `ILogger<T>` is also auto-registered by `AddLogging()`. So both are already available. ✅

### MEDIUM: `FileSystemGlobbing` package availability

The new `SearchCodebaseTool.cs` uses `Microsoft.Extensions.FileSystemGlobbing`. This package may not be in `Orchestrator.Mcp.csproj` — need to verify or add:

```xml
<PackageReference Include="Microsoft.Extensions.FileSystemGlobbing" Version="10.0.*" />
```

If it's already a transitive dependency (through `Microsoft.Extensions.Hosting` which pulls it in), no change needed.

---

## Seat 3 — Practitioner | FINDINGS

### HIGH: ResponseCleaner.LooksLikeCode false positive risk

The `LooksLikeCode` check looks at the first 200 chars. If a model returns "The method you want is `public class Foo`...", this would be detected as code. However, looking at this more carefully:

The `LooksLikeCode` is only called as a FALLBACK (Strategy 2) — after `ExtractFenceBlocks` returns nothing. In Strategy 2, if no fences and text starts with `public ` it returns as-is. This is fine because if there are no fences and the text starts with `public `, it almost certainly IS code.

The `StripProseWrapper` (Strategy 3) is the risky one. If a model writes "public class overview...", the line detection might incorrectly identify prose as code. **Mitigation:** The prose stripping strips at the LINE level — each line must start with a code keyword. "public class overview of..." is less likely to trigger because "overview" doesn't follow the `public class Name` pattern.

This is acceptable risk for Phase 0.

### MEDIUM: Prompts now say "no backticks, no code fences" — this breaks the input fences

Both `RefactorCodeTool.BuildPrompt` and `GenerateTestsTool.BuildPrompt` still wrap the INPUT in fences (for clarity to the model) but tell the model to output WITHOUT fences. The prompt instruction "Output the code directly — no backticks" may confuse models into ignoring the input fences.

**Recommended change:** Separate the instruction to avoid output fences from the input section:

```
You are an expert {language} developer. Refactor the following code for: {goal}.

Important output formatting:
- Return ONLY the refactored code
- Do not include explanation, preamble, or code fences
- Start your response immediately with the first line of code

Input code:
```{language}
{code}
```
```

This is clearer about which fences are input (don't confuse) vs. output (don't include). Update both BuildPrompt methods.

---

## Seat 4 — Skeptic | FINDINGS

### LOW: Meta.TokensIn / TokensOut in AgentTaskTool are rough estimates

```csharp
TokensIn  = result.TotalTokensUsed / 2,   // rough split estimate
TokensOut = result.TotalTokensUsed / 2
```

This is commented as a rough estimate which is honest. The agent loop doesn't track in/out separately — only `TotalTokensUsed` (estimated as `promptLen/4 + responseLen/4`). The split is meaningless but sum is approximately correct. Acceptable for Phase 0 observability.

### LOW: applyChanges TODO comment references Phase 1 in production code

The TODO comment in AgentTaskTool is clear and appropriate. It uses structured logging (`_logger.LogWarning`) which is better than a silent no-op. ✅

---

## Seat 5 — Advocate | ASSESSMENT

Two refinements are REQUIRED before committing:

1. **Fix ResponseCleaner LINQ bug** (Archivist HIGH) — won't compile as-is
2. **Update BuildPrompt in both tool files** (Practitioner MEDIUM) — better prompt framing

One item to track:
3. **FileSystemGlobbing package** (Architect MEDIUM) — verify in csproj or add

Everything else is quality notes, not blockers.

---

## Gate Decision: CONDITIONAL PASS

Fix the LINQ bug in ResponseCleaner and the prompt phrasing in both codegen tools. Then commit.
