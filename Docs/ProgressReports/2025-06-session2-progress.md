# Progress Report — Session 2 (June 2026)

**Branch:** `next-gen`
**Build status:** ✅ Clean
**Tests:** ✅ 218 / 218 passing (NUnit 4.5.1 · NSubstitute 5.3.0 · FluentAssertions 8.9.0)

---

## Summary of Work Completed

### 1 — VRAM Radial Gauge (Home.razor) ✅

Replaced the `<progress>` element in each node card with a proper **ApexCharts RadialBar gauge** per node.

**Key implementation details:**
- `SeriesType.RadialBar` with `PlotOptionsRadialBar`
- `Hollow.Size = "60%"` (type `string`), `Track.Background = "#2e3148"`, `Track.Width = 100` (type `double?`, not `string`)
- Color-coded thresholds: green `#22c55e` (≤70%), yellow `#f59e0b` (≤90%), red `#ef4444` (>90%)
- `BuildVramGaugeOptions(string color)` static factory method — one options object per gauge, reused per render
- `@using ApexCharts` directive added at top of razor file

**Model added:**
```csharp
// MetricsChartModels.cs
internal sealed record VramGaugePoint(decimal Pct);
```

**CSS added** (`app.css`):
```css
.sb-vram-gauge-wrap { display: flex; flex-direction: column; align-items: center; margin: 4px 0; }
.sb-vram-label     { font-size: 11px; color: var(--sb-muted); margin-top: -8px; }
```

**ApexCharts API discovery note:** The assembly cannot be inspected via PowerShell reflection in this Blazor context (missing Blazor runtime dependencies at load time). Correct property names were discovered by creating a throwaway `_ApexProbe.cs` file with deliberately wrong names, reading the compiler `CS1061` errors, then deleting the probe file. Findings:
- `PlotOptionsRadialBar.Hollow` → type `Hollow`, not `HollowSize`
- `Hollow.Size` → `string` (e.g. `"60%"`)
- `PlotOptionsRadialBar.Track` → type `Track`, not `RadialBarTrack`
- `Track.Width` → `double?` (e.g. `100`), not `string`; `StrokeWidth` is obsolete

---

### 2 — Settings Page — Topology Management ✅

`Settings.razor` completely rewritten (601 lines) to provide full in-session topology management:

- **Node list panel:** displays all registered nodes with role badge, provider badge, and expand-to-edit affordance
- **Add node form:** provider-agnostic outer form with provider-specific sub-panels (Ollama: endpoint URL + model name; Worker: base URL; CopilotSdk: no extra fields)
- **Remove node:** with confirmation guard
- **Fallback chain editor:** per-node ordered list; add/remove fallback targets
- **View-model:** `NodeForm` with `ToConfig()` / `From(NodeConfiguration)` helpers; seeded from `IOptionsSnapshot<RoutingOptions>` on page init
- **CSS:** `.sb-btn`, `.sb-btn-danger`, `.sb-form`, `.sb-input`, `.sb-select`, `.sb-panel`, `.sb-section-header`, `.sb-settings-actions`, `.sb-chain-*` all added to `app.css`

> **Open question:** `Settings.razor` currently applies changes to the in-memory `INodeRegistry` only. It does **not** write back to `appsettings.json` or any persistent store. A config-persistence layer (JSON round-trip or user secrets) is needed for settings to survive restart.
> **Confidence:** 95% — confirmed by reading the save handler; no file I/O present.

---

### 3 — RoutingService Refactor ✅

`RoutingService` primary constructor changed from concrete node injection to registry-driven:

```csharp
public RoutingService(
    INodeRegistry registry,
    Func<string, IInferenceQueue> queueFactory,
    ILogger<RoutingService> logger,
    INodeHealthCache? healthCache = null,
    IOptions<RoutingOptions>? routingOptions = null)
```

- **Role-based scoring (§6.2):** `NodeRole.Fast/Deep/Hybrid/Standby` replaces hardcoded `"A"/"B"/"C"` node-id strings in `roleFit` and `contextFit` terms
- **Hard rules (§6.3):** Autocomplete → Fast node; queue depth > threshold → skip overloaded node
- **CopilotSdk:** no local VRAM; treated as 0.75 synthetic VRAM ratio
- **Compat constructor** retained for backward compatibility (wraps NodeA/NodeB/NodeC args)
- **Both `Program.cs`** files updated to use registry path

---

### 4 — RoutingServiceRegistryTests (16 tests) ✅

New test class `RoutingServiceRegistryTests` covering the primary registry constructor path:

| Test | Coverage area |
|---|---|
| `Constructor_WhenRegistryIsNull_Throws` | Null guard |
| `Constructor_WhenQueueFactoryIsNull_Throws` | Null guard |
| `RouteAsync_WhenNoNodesRegistered_ThrowsInvalidOperation` | Empty registry |
| `RouteAsync_SingleFastNode_AlwaysRoutesToIt` | Basic routing |
| `SelectNode_Autocomplete_PrefersFastNode` | §6.3 hard rule |
| `SelectNode_Autocomplete_WhenNoFastNode_ReturnsAnyNode` | §6.3 degraded fallback |
| `SelectNode_ReviewTask_PrefersDeepOverFast` | §6.2 role scoring |
| `SelectNode_RefactorTask_PrefersDeepOverFast` | §6.2 role scoring |
| `SelectNode_TestGenerationTask_PrefersDeepOverFast` | §6.2 role scoring |
| `SelectNode_ChatTask_PrefersFastOverDeep` | §6.2 role scoring |
| `SelectNode_ReviewTask_HybridPreferredOverFast` | §6.2 role scoring |
| `SelectNode_StandbyNode_NeverChosenWhenBetterAvailable` | Standby weight=0.1 |
| `SelectNode_UnavailableNodeSkipped_RoutesToHealthyAlternative` | Health cache |
| `SelectNode_AllUnavailableInCache_StillReturnsANode` | All-degraded fallback |
| `SelectNode_CopilotSdkProvider_TreatedAsPartiallyAvailable` | Cloud VRAM ratio |
| `RouteAsync_WhenPrimaryFails_FallbackChainUsed` | Fallback chain |
| `SelectNode_QueueOverloaded_FallsBackToLessLoadedNode` | Queue threshold |
| `SelectNode_LargePrompt_PrefersDeepOrHybridNode` | Context-fit scoring |
| `RouteAsync_CancelledToken_ThrowsOperationCancelled` | Cancellation |

All 16 pass. Total test suite: **218 tests, 0 failed**.

---

## Assumptions

| # | Assumption | Confidence |
|---|---|---|
| A1 | `NodeHealth.CheckedAt` is the correct property name (not `LastChecked`) | 99% — confirmed via `get_symbols_by_name` |
| A2 | `Track.Width = 100` (double, meaning 100%) is the correct API for full-width track | 95% — confirmed by successful compilation and visual intent |
| A3 | `VramGaugePoint.Pct` is in the range 0–100 (not 0–1) | 90% — consistent with how existing VRAM % is computed in Home.razor; radial bar label formatter uses `v.toFixed(0)+'%'` which would be wrong if 0–1 |
| A4 | Settings in-memory only (no appsettings.json round-trip needed yet) | 85% — no persistence spec in MasterPlanV4 for this milestone |
| A5 | `NodeQueue(capacity: 32)` in test queue factory is adequate for threshold tests | 90% — threshold is 2, tests enqueue 5 items, capacity 32 is well above both |
| A6 | Compat ctor (NodeA/NodeB/NodeC) can remain until topology is fully registry-driven | 90% — both Program.cs still reference legacy node types; removing them now would break startup |

---

## Unknowns / Open Questions

| # | Question | Impact | Notes |
|---|---|---|---|
| U1 | How should `Settings.razor` persist topology changes? | High | Options: write `appsettings.json` directly, use `IWritableOptions<T>`, or introduce a `ITopologyRepository`. Not spec'd in MasterPlanV4. |
| U2 | Should the radial gauge animate on live VRAM updates (SignalR push)? | Medium | Currently Home.razor re-renders on `StateHasChanged()`; ApexCharts will re-render the full chart. Could use `UpdateSeriesAsync` for smooth animation if needed. |
| U3 | Is the `QueueFallbackThreshold` constant (currently 2) configurable? | Low | Hardcoded in RoutingService. Could be exposed via `RoutingOptions`. |
| U4 | Do we need a `RoutingServiceCompatTests` class for the old 3-node ctor path? | Low | The compat ctor is tested indirectly by `ScoringFunctionTests` (prior session). Explicit coverage is low-priority. |
| U5 | `FluentAssertions` commercial license warning in test output | Low | Warning is cosmetic (non-commercial use is free). No action needed unless project commercialises. |

---

## Next Suggested Steps

1. **Config persistence** — Implement `IWritableOptions<RoutingOptions>` or a `TopologyRepository` so `Settings.razor` can save to `appsettings.json`. *(Unblocks U1)*
2. **Remove legacy node singletons** — Once all topology entries are driven via `INodeRegistry`, delete `NodeAInferenceNode`/`NodeBInferenceNode` and remove compat ctor from `RoutingService`. *(Clears A6)*
3. **Live VRAM push** — Wire `DashboardHub` SignalR events to update `DashboardState.NodeHealthMap` and confirm `ApexChart` re-renders correctly on push. *(Addresses U2)*
4. **Metrics page live data** — `Metrics.razor` currently uses static sample data. Wire to `IMetricsCollector.GetRecentAsync()` via SignalR or polling. *(Phase 3 from MasterPlanV4)*
5. **Agent task list** — `Tasks.razor` page is a placeholder. Implement task queue view backed by `IAgentEventLog`.

---

*Report generated by GitHub Copilot — June 2026.*
