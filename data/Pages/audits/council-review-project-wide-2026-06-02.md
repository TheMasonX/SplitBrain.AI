# Council Review: SplitBrain.AI — Project-Wide Audit
## Commit: `b26eb3bd` · Branch: `next-gen` · Date: 2026-06-02

**Gate Decision: PROCEED WITH SPRINT 1 FIXES**  
**Seat Confidences:** Archivist 79% · Architect 76% · Retrieval 74% · Skeptic 48% · Advocate 68%

---

## 1. Seat 1 — Archivist (Completeness & Documentation) | 79%

### HIGH: LlamaCpp provider missing from both dispatch factories
`Mcp/Program.cs` and `Dashboard/Program.cs` both have a `Func<NodeConfiguration, IInferenceNode>` switch that throws `InvalidOperationException` for `NodeProviderType.LlamaCpp`. The `LlamaCppInferenceNode`, `ILlamaCppClient`, and `LlamaCppClientOptions` are fully implemented in `NodeClient.LlamaCpp` but are never registered in either orchestrator host. Any `nodes.json` entry with `"Provider": "LlamaCpp"` will crash at topology dispatch time. This is the single highest-impact bug — it makes the entire llama.cpp infrastructure unusable from the Orchestrator or Dashboard until fixed.

**Fix:** Add `LlamaCppInferenceNode` keyed singleton registration + dispatch arm to both `Program.cs` files, following the `WorkerInferenceNode` pattern.

### HIGH: `installer/SplitBrainAI.iss` deploy-script params mismatch
The `[Run]` section calls `setup-node-a.ps1` and `setup-node-b.ps1` with `-SkipDotNet -SkipOllama -SkipModels` but neither script defines those parameters. PowerShell will throw "A parameter cannot be found that matches parameter name 'SkipDotNet'" on every service-install step. The entire service registration portion of the installer silently fails.

### MEDIUM: `CurStepChanged` collapses three-way backend to binary
At `ssInstall`, the code `if RbOllama.Checked then CBackend := 'ollama' else CBackend := 'llamacpp'` overwrites the three-valued `CBackend` (`'llamacpp-native'`/`'llamacpp-docker'`) with a bare `'llamacpp'` string. `IsLlamaCppNative()` and `IsLlamaCppDocker()` both return False after this fires, so `GetLlamaCppSetupArgs` produces the wrong `-Mode` argument.

### LOW: `SetupIconFile=` is a blank placeholder
Minor cosmetic gap. Project has no `.ico` asset.

### LOW: `data/Tasks/` directory doesn't exist
The wiki refers to task tracking but no task records exist. Need to bootstrap.

---

## 2. Seat 2 — Architect (Structure & Patterns) | 76%

### MEDIUM: `NodeWorker/Program.cs` missing `UseWindowsService()`
`Mcp/Program.cs` calls `builder.Host.UseWindowsService()` correctly. `NodeWorker/Program.cs` does not. `setup-node-b.ps1` registers `Orchestrator.NodeWorker.exe` as a Windows service via `sc.exe create`. Without `UseWindowsService()`, the SCM stop signal arrives as process termination rather than graceful `StopApplication` — unclean shutdown, incomplete log flush, possible data loss in the metrics store.

### MEDIUM: Queue capacity hardcoded by NodeId string comparison
In both `Mcp/Program.cs` and `Dashboard/Program.cs`:
```csharp
queueFactory: nodeId => new NodeQueue(capacity: nodeId == "A" ? 64 : 32)
```
This hardcodes "A" as the fast-node name, silently giving all Worker and LlamaCpp nodes a capacity of 32 regardless of `MaxConcurrentRequests` in `NodeConfiguration`. The config field exists but is ignored. Fix: `topologyConfig.Nodes.FirstOrDefault(n => n.NodeId == nodeId)?.MaxConcurrentRequests ?? 32`.

### MEDIUM: `Dashboard/Program.cs` Worker nodes lack resilience pipeline
Ollama nodes get retry + circuit breaker + per-node timeout via `AddResilienceHandler`. Worker nodes only get `AddHttpClient` with a doubled `Timeout`. A GTX 1080 Worker that's slow or flaky (e.g., during model load) will have no retry and no circuit breaking. Fix: add the same resilience handler pattern used for Ollama to Worker node clients.

### LOW: Redundant `CopilotSdk when config.NodeId == "C"` arm in Dashboard dispatch
Both `CopilotSdk` arms in `Dashboard/Program.cs` return `sp.GetRequiredService<NodeCInferenceNode>()`. The `when` guard is dead code. Fix: remove the guarded arm.

### LOW: Solution file not yet renamed (`Orchestrator.slnx` → `SplitBrain.slnx`)
Decision D4 (locked 2026-06-01) specifies the solution rename. The file is still `Orchestrator.slnx`. Project prefix renames (`Orchestrator.*` → `SplitBrain.*`) are deferred; solution file rename is Sprint 1 scope.

### LOW: `NodeWorker` has no OpenTelemetry
`Mcp/Program.cs` and `Dashboard/Program.cs` both configure OTLP traces and metrics. `NodeWorker/Program.cs` doesn't. Worker telemetry is invisible to any Grafana/Jaeger collector. Defer to Sprint 2 (add logging, not critical for V1).

---

## 3. Seat 3 — Retrieval (Data Flow & Config) | 74%

### HIGH: `setup-node-b.ps1` dynamic env var assignment bug
In `Set-PersistentEnv`:
```powershell
$env:($Name) = $Value
```
This is syntactically invalid for dynamic env var assignment in PowerShell. It will not throw but silently assigns to a variable named literally `($Name)` in process scope, not the intended env var. The `[System.Environment]::SetEnvironmentVariable($Name, $Value, "Machine")` call above is correct for machine-scope; the in-process update fails. Fix: `Set-Item "Env:$Name" $Value` or just `[System.Environment]::SetEnvironmentVariable($Name, $Value, 'Process')`.

### MEDIUM: `Setup-LlamaCpp.ps1` pinned fallback tag `"b5695"` is approximate
The GitHub Releases API fallback URL uses hardcoded tag `"b5695"` which is described as "approximate". This will produce a broken download URL when that tag doesn't exist on the releases page. Fix: use a properly resolved tag from the API response error, or use the rolling `latest` URL pattern from GitHub Releases.

### MEDIUM: `Setup-LlamaCpp.ps1` `$extractOk` logic bug
`$extractOk = $true` is set AFTER the conditional block that set it to `$false` on download failure, unconditionally overwriting it. The extraction-failure path never propagates. Fix: restructure to only set `$extractOk = $true` inside the success block.

### LOW: `setup-node-b.ps1` does not check `$LASTEXITCODE` after `ollama pull`
Node A checks `if ($LASTEXITCODE -ne 0) { throw }`. Node B silently ignores pull failures. Fix: add identical error handling.

### LOW: `setup-node-a.ps1` Invoke-WebRequest missing `-UseBasicParsing`
First readiness check uses `Invoke-WebRequest` without `-UseBasicParsing`. On headless Windows Server Core (no IE engine initialized), this throws. Fix: add `-UseBasicParsing`.

### LOW: `PrepareToInstall` references `{tmp}\installer-scripts\Test-Prerequisites.ps1`
The function runs before `{app}` exists and attempts to execute a script from `{tmp}`. The `[Files]` section copies scripts to `{app}\installer-scripts` during `ssInstall`, which happens after `PrepareToInstall`. The `Exec` call silently fails, and prereq checks are entirely bypassed. Fix: inline a lightweight prereq check in `PrepareToInstall` using direct PowerShell commands.

---

## 4. Seat 4 — Skeptic (Risk & Security) | 48%

### HIGH: `POST /inference` is completely unauthenticated
`NodeWorker/Program.cs` exposes `POST /inference`, `GET /models`, `GET /metrics`, `GET /health` with no authentication. Any machine that can reach port 5050 can submit arbitrary inference requests and read metrics. For a local LAN deployment this may be acceptable, but it's a zero-effort attack surface. **Recommendation:** Add opt-in bearer token auth (disabled by default) controlled by `NodeWorker:RequireAuthToken: false` in `appsettings.json`. If `RequireAuthToken: true`, generate a random token on first run and store in config.

### MEDIUM: `COPILOT_API_KEY` stored in User-scope env var by installer
`Write-Config.ps1` stores the GitHub Copilot token as `[System.Environment]::SetEnvironmentVariable("COPILOT_API_KEY", $CopilotToken, "User")`. This is visible to any process running as the same user and will appear in `Set-Variable` dumps. For developer machines this is standard practice; for multi-user systems it's a concern. **Recommendation:** Document this explicitly. Consider using Windows Credential Manager as an opt-in alternative.

### MEDIUM: `NodeCInferenceNode.CreateAsync` blocks at startup with `.GetAwaiter().GetResult()`
Both `Mcp/Program.cs` and `Dashboard/Program.cs` call `NodeCInferenceNode.CreateAsync(...).GetAwaiter().GetResult()` during DI registration — synchronous blocking of the startup thread while resolving the API token. If Azure Key Vault is configured and takes >10s, the service appears hung. **Recommendation:** Defer to a `IHostedService.StartAsync` or lazy init. Deferred to Sprint 2 (requires more refactoring).

### LOW: No HTTPS enforcement in `NodeWorker/Program.cs`
`Dashboard/Program.cs` calls `app.UseHttpsRedirection()` in non-development environments. `NodeWorker` and `Mcp` do not. Inference traffic on port 5050/5100 is plaintext. **Recommendation:** For LAN-only deployment, this is acceptable. Document explicitly that users who want HTTPS should put a reverse proxy (Caddy/nginx) in front.

### LOW: `ApplyPatchTool.cs` patches arbitrary files
`ApplyPatchTool` presumably accepts a file path. The prior audit found `allowedRoot` enforcement was added to `RunTestsTool`. Whether `ApplyPatchTool` has path-scope enforcement should be verified. If not, it can write to arbitrary paths. **Recommendation:** Verify and add `allowedRoot` to `ApplyPatchTool` in Sprint 2.

---

## 5. Seat 5 — Advocate (UX & Value Delivery) | 68%

### HIGH: Installer has no model path input for llama.cpp
`Setup-LlamaCpp.ps1` hardcodes `ModelPath = "C:\Models\qwen3-coder-30b-a3b.gguf"`. Users must manually edit the generated launch scripts after installation to point at their actual model file. This is a significant friction point for new users. **Recommendation:** Add a "Model file path" input to the `NetworkPage` (field [5]) when the llama.cpp backend is selected. Pass it to `GetLlamaCppSetupArgs`.

### MEDIUM: CUDA build version not selectable in installer
The wizard always uses CUDA 12.4. Users with CUDA 13.x drivers (R580+) may want to choose 13.3 for potentially better compatibility. **Recommendation:** Add a simple radio button pair "CUDA 12.4 (recommended)" / "CUDA 13.3 (newest drivers)" to the backend page for Native mode. Low risk, high discoverability.

### MEDIUM: No post-install "quick start" guidance in wizard Finish page
The Finish page only offers "Launch MCP Server now" / "Launch Dashboard now". For new users, there's no pointer to the setup guide or wiki. **Recommendation:** Add a `[CustomMessages]` note in the Finish page description linking to the getting started guide (or the installed `data/Pages/guides/getting-started.md`).

### LOW: `pullmodels` task shows even when llama.cpp is selected
The Ollama model download task appears in the task list regardless of backend selection. When llama.cpp is selected, it auto-skips at runtime, but the checkbox is still visible and confusing. **Recommendation:** Gray out or hide the `pullmodels` task description text dynamically when llama.cpp is selected.

### LOW: `data/Tasks/` doesn't exist — no task tracking in wiki
There is no structured task tracking in the wiki. **Recommendation:** Create `data/Tasks/` with properly formatted MemorySmith task records for all sprint findings.

---

## 6. Finding Summary

| ID | Severity | Area | Finding | Sprint |
|----|----------|------|---------|--------|
| C1 | Critical | C# | LlamaCpp provider unhandled in Mcp + Dashboard dispatch | S1 |
| C2 | Critical | Installer | Deploy scripts called with non-existent params | S1 |
| H1 | High | Installer | `CurStepChanged` collapses CBackend to binary | S1 |
| H2 | High | Deploy | `setup-node-b.ps1` `$env:($Name)` bug | S1 |
| H3 | High | Security | NodeWorker unauthenticated endpoints | S1 (opt-in) |
| H4 | High | UX | No model path input for llama.cpp | S1 |
| M1 | Medium | C# | NodeWorker missing `UseWindowsService()` | S1 |
| M2 | Medium | C# | Queue capacity hardcoded by NodeId string | S1 |
| M3 | Medium | C# | Dashboard Worker nodes lack resilience | S1 |
| M4 | Medium | Setup | `Setup-LlamaCpp.ps1` pinned fallback tag stale | S1 |
| M5 | Medium | Setup | `Setup-LlamaCpp.ps1` `$extractOk` logic bug | S1 |
| M6 | Medium | UX | CUDA build version not selectable | S1 |
| M7 | Medium | Security | `CreateAsync` blocking startup | S2 |
| L1 | Low | Installer | `PrepareToInstall` prereq check skipped | S1 |
| L2 | Low | C# | Redundant CopilotSdk dispatch arm | S1 |
| L3 | Low | Solution | `Orchestrator.slnx` → `SplitBrain.slnx` rename | S1 |
| L4 | Low | Deploy | `setup-node-b.ps1` no `$LASTEXITCODE` check | S1 |
| L5 | Low | Deploy | `setup-node-a.ps1` missing `-UseBasicParsing` | S1 |
| L6 | Low | C# | NodeWorker no OpenTelemetry | S2 |
| L7 | Low | Security | No HTTPS in NodeWorker | Documented |
| L8 | Low | Security | `ApplyPatchTool` path-scope enforcement | S2 |

---

## 7. Approved Items

- `NodeClient.LlamaCpp` implementation: correct and complete
- SSE streaming in `LlamaCppClient`: correct semantics
- `NodeWorker/Program.cs` NODE_B_BACKEND env var gate: clean pattern
- Three-way installer BackendPage (Ollama/Native/Docker): correct architecture
- MemorySmith wiki structure (`data/`): well-organized
- `IdempotencyHelper.cs` atomic retry: correct
- `RunTestsTool` `allowedRoot` enforcement: correct
- `GenerateTestsTool` `SanitizeParam`/`SanitizeForFence`: correct

---

## 8. Gate Decision

**PROCEED WITH SPRINT 1** — C1 and C2 block llama.cpp from being usable end-to-end. H1–H4 and M1–M6 are all small-scope fixes. Total estimated effort: 8–12 hours of changes. No architectural rethink required. Sprint 1 targets all Critical/High + most Medium findings.
