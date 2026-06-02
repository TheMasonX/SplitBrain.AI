# Consolidate Fallback Graph Validation Logic — Progress Report (2026-06-24 00:00:01)

## Summary of Completed Work

### 1) Shared fallback graph validator introduced
- **File:** `src/Orchestrator.Infrastructure/Configuration/FallbackGraphValidator.cs`
- Added reusable cycle detection utility:
  - `FallbackGraphValidator.TryGetFirstCyclePath(...)`
- Detects first directed cycle and returns human-readable path (`A -> B -> A`).

### 2) Coordinator refactored to use shared validator
- **File:** `src/Orchestrator.Infrastructure/Configuration/TopologyRoutingSaveCoordinator.cs`
- Replaced coordinator-local DFS cycle logic with `FallbackGraphValidator` call.
- Preserved existing validation and exception semantics:
  - unknown source/target checks unchanged
  - self-reference check unchanged
  - cycle exception message format preserved

### 3) Settings page refactored to reuse shared validator
- **File:** `src/SplitBrain.Dashboard/Components/Pages/Settings.razor`
- Removed page-local cycle DFS implementation.
- Cycle warning panel and pre-save guard now both use shared validator.
- Result: one validation implementation for both UI and backend, less drift risk.

### 4) New tests for shared validator
- **File:** `src/Orchestrator.Tests/Infrastructure/FallbackGraphValidatorTests.cs`
- Added tests:
  - `TryGetFirstCyclePath_WhenGraphIsAcyclic_ReturnsFalse`
  - `TryGetFirstCyclePath_WhenGraphHasDirectCycle_ReturnsPath`
  - `TryGetFirstCyclePath_WhenGraphHasMultiNodeCycle_ReturnsPath`

### 5) Existing coordinator tests remain valid and green
- **File:** `src/Orchestrator.Tests/Infrastructure/TopologyRoutingSaveCoordinatorTests.cs`
- Existing path-clarity and cycle validation tests passed unchanged after refactor.

---

## Validation Results

- Build: ✅ successful
- Targeted tests (`FallbackGraphValidatorTests` + `TopologyRoutingSaveCoordinatorTests`): ✅ **14/14 passing**
- Full `Orchestrator.Tests` suite: ✅ **238/238 passing**

---

## Assumptions (with confidence)

1. **Assumption:** Shared validator should be `public` in Infrastructure so both Dashboard and coordinator can consume the same implementation.
   - Confidence: **0.93**

2. **Assumption:** Returning the first detected cycle is sufficient for current operator troubleshooting.
   - Confidence: **0.84**

3. **Assumption:** Retaining current exception messages avoids regressions in existing UI/test expectations.
   - Confidence: **0.90**

---

## Unknowns (with confidence)

1. **Unknown:** Whether future UX needs all detected cycles, not just first cycle.
   - Confidence unresolved: **0.73**

2. **Unknown:** Whether validator should move to `Orchestrator.Core` for stricter architectural layering.
   - Confidence unresolved: **0.68**

3. **Unknown:** Whether extremely large fallback graphs need optimized cycle diagnostics or limits.
   - Confidence unresolved: **0.62**

---

## Open Questions (with confidence)

1. Should fallback validation return structured diagnostics (issues list) instead of exception strings for richer Blazor rendering?
   - Confidence useful: **0.82**

2. Should cycle path output be normalized deterministically for snapshot-style UI tests?
   - Confidence useful: **0.71**

3. Should we add UI component tests (bUnit) to verify warning panels for stale references + cycles together?
   - Confidence useful: **0.79**

---

## Issues Encountered

- No blocking implementation issues.
- Existing FluentAssertions commercial-license warning remains informational only.

---

## Net Outcome

Fallback validation logic is now consolidated and reusable across backend persistence and Blazor UI checks, reducing drift and maintenance risk while keeping full test coverage green.
