# Council Review: feature/phase-1-tools — Complete Branch Review
## PR #19 · Commit: `2396a5bf` → `feature/phase-1-tools` → `next-gen`
## Date: 2026-06-03 · Gate: PROCEED WITH FIXES

**This review determines whether merging feature/phase-1-tools → next-gen → main leaves zero outstanding work.**

---

## 1. Seat 1 — Archivist (Completeness) | 86%

### ✅ Confirmed complete
- All feature/batch-* content: in next-gen base (`9b4adf82`) — confirmed present in feature branch base `f49d6d1a`
- Phase 0 fixes (PR #18): merged to next-gen, in feature branch base
- Phase 1 tools (PR #19): 5 new tools + write gate + ResponseParser

### HIGH: `AgentTaskTool.applyChanges` is a TODO stub
The `applyChanges` parameter was added in Phase 0 as a forward reference to "Phase 1 write gate." Now that WriteAccessGuard exists, the parameter is wired to nothing — it logs a warning but doesn't apply the diff. A caller setting `applyChanges=true` gets a misleading experience: no error, no diff applied, just a silent warning in logs.

**Fix:** Wire `applyChanges` to:
1. Check WriteAccessGuard (`_writeGuard.CheckWrite("agent_task", enableWriteParam: true)`)
2. If allowed and workingDirectory is set, parse the diff for file paths and apply using UnifiedDiffApplier
3. Return applied file list in response

### MEDIUM: `Meta.FromInferenceResult` inconsistency
`ExplainCodeTool` (Phase 1) uses `Meta.FromInferenceResult(taskId, result)`. All Phase 0 tools (RefactorCode, GenerateTests, SearchCodebase) still manually construct Meta. After adding `Meta.FromInferenceResult` to SharedModels.cs, the inconsistency is visible and should be cleaned up.

### LOW: Missing wiki memory on feature branch
`data/Memories/Working/splitbrain-phase1-reconciliation.json` was pushed to next-gen (`f0373113`) but not to feature/phase-1-tools. The feature branch needs it to be complete.

### LOW: TASKS.md not ported
PR #15 had a well-structured `TASKS.md` at the repo root. Not included in PR #19.

---

## 2. Seat 2 — Architect (Structure & DI) | 84%

### AgentTaskTool DI change
When WriteAccessGuard is injected into AgentTaskTool, its constructor gains a new parameter. The DI registration in Program.cs uses automatic constructor injection via `WithTools<AgentTaskTool>()`, so adding the parameter is automatically resolved as long as `WriteAccessGuard` is registered (which it is, as a singleton from Phase 1).

### Patching namespace
`UnifiedDiffApplier` lives in `Orchestrator.Mcp.Patching`. AgentTaskTool is already in `Orchestrator.Mcp.Tools`. The using statement `using Orchestrator.Mcp.Patching;` needs to be added to AgentTaskTool.

### Diff parsing architecture
The agent produces unified diffs with `+++ b/relative/path` conventions. A simple regex over `+++ b/` lines extracts relative paths. `UnifiedDiffApplier.Apply(original, patch)` applies the full unified diff to a single file's content. For the common single-file agent change, this works correctly. For multi-file diffs, the same full diff is passed to each file's Apply call — UnifiedDiffApplier will apply only the relevant hunks for each file.

### MEDIUM: `feature/phase-1-tools` branch base is `f49d6d1a`, not current `f0373113`
The branch was created from `f49d6d1a`. Current next-gen HEAD is `f0373113`. The difference is a single file (`data/Memories/Working/splitbrain-phase1-reconciliation.json`). This doesn't cause conflicts — it's an additive file — but the feature branch should include it for completeness.

---

## 3. Seat 3 — Practitioner (Agent Usability) | 88%

### `applyChanges=true` + `write gate in Disabled mode` — expected behavior
When a caller sets `applyChanges=true` but the write gate is `Disabled` (default), the tool should return a clear error in the response — not silently apply nothing. The current implementation logs a warning but still returns success. Fix: include `writeGateError` in the response when write is blocked.

### `splitbrain_explain_code` — ResponseCleaner.StripFences applied
Correctly strips markdown fences from explanations. Works as designed.

### File tools allowedRoot vs workingDirectory naming
`apply_patch`, `splitbrain_write_file`, `splitbrain_read_file` all use `allowedRoot`. `agent_task` uses `workingDirectory`. These are different parameters that serve the same security purpose. When `applyChanges=true` in `agent_task`, the `workingDirectory` is also the allowedRoot for the applied diff. Consistent.

### `splitbrain_list_files` — excludes bin/obj/.git
Correct defaults. Pattern is properly scoped. ✅

---

## 4. Seat 4 — Skeptic (Risk & Security) | 72%

### Path traversal in ApplyDiff
When applying diffs in agent_task, the extracted file path (from `+++ b/`) must be validated against `workingDirectory`. An adversarial diff with `+++ b/../../../../etc/passwd` must be rejected. The implementation must use `Path.GetFullPath` + prefix check, same pattern as `ApplyPatchTool`.

### Multi-file diff edge case
If the agent generates a diff touching 5+ files, and one file's patch fails to apply, the tool should log per-file failures but continue with the others. Don't abort the entire apply on the first failure.

### TASKS.md — no security concern
Adding documentation only. ✅

### `applyChanges=false` (default) — no writes
Default behavior: write gate not checked, diff returned in response for caller to apply manually. This is the safe default. ✅

---

## 5. Seat 5 — Advocate (Value & Priority) | 90%

### Highest value: wire `applyChanges`
This is the missing piece that makes `agent_task` genuinely useful as an autonomous agent. Without it, agents must do extra work to call `apply_patch` manually. With it, a single `agent_task` call can plan, implement, and commit changes.

### TASKS.md adds discoverability
Good reference for any developer picking up the project.

### `Meta.FromInferenceResult` consistency
Minor but reduces cognitive friction when reading tool code. 6 tools × 6 lines replaced by 1 line each.

---

## Gate Decision: PROCEED WITH FIXES

Required before merging:
1. Wire `applyChanges` in AgentTaskTool (HIGH)
2. Add `Meta.FromInferenceResult` to Phase 0 tools (MEDIUM)
3. Add TASKS.md and missing wiki memory file (LOW)

All changes go directly to `feature/phase-1-tools`. No new branches.
