# Audit Summary — 2026-06-01

Two audits were performed on 2026-06-01 comparing SplitBrain.AI (next-gen branch, commit `1141eaea`) against MemorySmith (master, commit `c4d7a28a`) as a reference for preferred MCP style.

## First-Pass Audit — Architecture vs MemorySmith

**36 findings** across 5 severity levels. Key issues:

| Severity | Count | Examples |
|----------|-------|---------|
| Critical (P0) | 4 | DI duplication across two hosts; PackageReference/ProjectReference mixup; async blocking during DI; no unified tool catalog |
| High (P1) | 10 | Inconsistent naming; Core has NuGet deps; no Storage layer; no tool risk model; no tool enable/disable config; MCP strategy divergence |
| Medium (P2) | 11 | No benchmarks; fake glob; LiteDB vs SQLite; response format inconsistency; hardcoded models |
| Low (P3) | 7 | Docs dir case; empty plan file; ghost meta project; no service commands |
| Info (P4) | 4 | Duplicate plan docs; sentinel URL; PowerShell MCP client |

**Remediation roadmap:** 5 phases (A–E), 22-28 days estimated (council-revised).

## Second-Pass Deep-Dive Audit

**48 new findings** from line-by-line code audit (3C/13H/19M/13L). Key new issues:

| ID | Severity | Finding |
|----|----------|---------|
| C1 | Critical | fire-and-forget `DrainAsync` hang |
| C2 | Critical | thread-unsafe `DashboardState` |
| C3 | Critical | unauthenticated NodeWorker endpoints |
| H5 | High | idempotency race condition (partially fixed) |
| H7 | High | 5 tools without error handling |
| H10 | High | `TryResolveGhCliToken` pipe-buffer deadlock |
| M15 | Medium | `allowedRoot` bypassable in RunTestsTool |

## Implementation Status

### Implemented (merged to next-gen at `9b4adf82`)

| Batch | Finding | Fix |
|-------|---------|-----|
| 1a | H5 (partial) | `IdempotencyHelper.cs` — atomic Failed-slot retry |
| 1b | H10 | `NodeCInferenceNode.cs` — async read before WaitForExit, 5s timeout |
| 2 | H (deduplication) | New `Routing/InferenceNodeFactory.cs` with null guard |
| 3 | M15 | `RunTestsTool.cs` — `allowedRoot` required, always enforced |
| 4 | Security | `GenerateTestsTool.cs` — `SanitizeParam` + `SanitizeForFence` |

### Pending

| ID | Severity | Finding | Planned Phase |
|----|----------|---------|---------------|
| C1 | Critical | DrainAsync hang | Batch 1a (load-test gate) |
| C2 | Critical | DashboardState thread-unsafe | Batch 1a |
| C3 | Critical | Unauthed NodeWorker endpoints | Batch 1b |
| H7 | High | Tools missing error handling | Batch 1b |
| D3 | Decision | LiteDB → SQLite | Phase C2 |
| D4 | Decision | Rename solution to SplitBrain.slnx | Phase C1 |

## Architecture Decisions Made

| ID | Decision |
|----|---------|
| D1 | Keep ModelContextProtocol NuGet (not hand-rolled) |
| D2 | Keep NodeClient projects separate |
| D3 | Migrate LiteDB → SQLite |
| D4 | Rename solution to SplitBrain.slnx |
| D5 | Keep Semantic Kernel |
| D6 | Two-machine deployment topology |
| D7 | Batch request support — deferred |
| D8 | Add llama.cpp as parallel backend (implemented) |

## Pre-Implementation Council Reviews

Two council reviews were performed (5-seat: Archivist, Architect, Retrieval, Skeptic, Advocate):

1. **Pre-Implementation Council Review** — reviewed Phase A-E plan. Gate: PROCEED WITH CHANGES. Estimate revised 14-19 → 22-28 days.
2. **NodeClient.LlamaCpp Pre-Impl Review** — PROCEED WITH CHANGES (3 blocking items resolved before coding).
3. **NodeClient.LlamaCpp Gated Review** — CONDITIONAL PASS (critical ProjectReference missing, found and fixed).
4. **NodeClient.LlamaCpp Full Review** — PASS (0 Critical/High, 3 Medium, 4 Low).

Full reports available in `Docs/Reviews/` and workspace audit markdown files.
