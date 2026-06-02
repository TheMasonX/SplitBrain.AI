# Build Warning Fixes (June 2026)

## CS8424 — EnumeratorCancellationAttribute on Interface Methods
`[EnumeratorCancellation]` on `CancellationToken` parameters in interface
method declarations produces CS8424. The attribute is **only effective on the
implementing class's async-iterator method**, not the interface.

**Fix:** Remove `[EnumeratorCancellation]` from interface declarations.
Only the concrete implementation needs it.

**Affected files:**
- `src/Orchestrator.Core/Interfaces/IAgentEventLog.cs` — `ReplayAsync`
- `src/Orchestrator.Core/Interfaces/IInferenceNode.cs` — `StreamAsync`

Also remove `using System.Runtime.CompilerServices;` from the interface file
if `[EnumeratorCancellation]` was the only usage.

## CA2024 — reader.EndOfStream in Async Methods
`StreamReader.EndOfStream` in async methods causes CA2024. The property
performs a synchronous `Read()` peek internally, which can block the thread.

**Fix:** Replace `while (!reader.EndOfStream)` with:
```csharp
while (true)
{
    var line = await reader.ReadLineAsync(ct);
    if (line is null) break;
    // ...
}
```

`ReadLineAsync` returns `null` at end of stream in async context.

**Affected files:**
- `src/NodeClient.Ollama/OllamaClient.cs` — streaming response loop
- `src/NodeClient.LlamaCpp/LlamaCppClient.cs` — SSE response loop

## NU1510 — Unnecessary PackageReference
`Microsoft.NET.Sdk.Web` already includes transitive framework references
for common packages. Explicit `PackageReference` entries produce NU1510
when the framework provides them.

**Fix:** Remove the explicit `PackageReference` if types are available from
the SDK.

**Affected files:**
- `src/Orchestrator.NodeWorker/Orchestrator.NodeWorker.csproj` —
  removed `Microsoft.Extensions.Hosting` and `Microsoft.Extensions.Http`

## CS0105 — Duplicate Using Directives
Duplicate `using` for the same namespace in the same file.

**Fix:** Remove the duplicate line.

**Affected files:**
- `src/Orchestrator.Mcp/Tools/GenerateTestsTool.cs` — duplicate `using Orchestrator.Core.Models`
- `src/Orchestrator.Mcp/Tools/ReviewCodeTool.cs` — duplicate `using Orchestrator.Mcp.Idempotency`

## General
`dotnet build` after the fixes produced 0 warnings across 12 projects.
