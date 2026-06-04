# Copilot Instructions

## Project Guidelines
- User prefers NUnit over xUnit for test projects.
- User prefers autonomous execution: continue implementing as far as safely possible between prompts, avoid pausing for user input when work can continue, include a markdown progress report file named `{PlanName}_Progress_{TimeStamp}`, summarize completion percentage, and explicitly report issues with `%CONTINUE%`/%`END%` status.
- Keep `Docs/Memories` stable: it is the live project wiki. Do not treat `Docs/Unconsolidated/` as authoritative without verification.
- Do not stop work because extra changed files are present. Files under `Docs/` are expected in normal workflows and should be included with related changes unless the user explicitly asks to exclude them.

## Execution Standards
- Use evidence-backed updates: include file paths, commands, test results, or page references for non-trivial claims.
- Use `Data/Tasks/` as the first-class planning artifact for audits and implementation. Audit findings should map to explicit task records and status transitions.
- Consolidate workflow guidance around `.github/agents/smith.agent.md` first, then backfill related docs/instructions so supporting guidance follows the Smith contract.
- Prefer using `.github/skills/` skills for repeatable workflows over ad hoc instructions.

## Knowledge Hub — Where to Find Things

All project knowledge is organized under `Docs/` at the repo root. The `src/SplitBrain.Meta/` project (net8.0, no code) exists solely to surface `Docs/` and `Scripts/` inside Visual Studio via `<None Include>` links.

### Docs Directory Map

| Path | Purpose |
|---|---|
| `Docs/Memories/` | **Active knowledge base.** Distilled, up-to-date facts about architecture, decisions, and current state. Start here for context. |
| `Docs/Unconsolidated/` | **Unconsolidated memories inbox.** Raw knowledge fragments (gotchas, fixes, lessons) land here before being distilled. Do not treat Unconsolidated/ as authoritative — these are raw material. Periodic "dreaming" sessions consolidate them into `Docs/Memories/`. |
| `Docs/Plans/` | Architecture and implementation plans. `MasterPlanV4.md` is the canonical blueprint (mirrored at `Plans/MasterPlanV4.md` repo root). |
| `Docs/Reviews/` | External and automated review reports against the plans (e.g. deep-research critiques). Cross-reference with codebase before trusting. |
| `Docs/ProgressReports/` | Snapshot reports of implementation progress per phase. |
| `Docs/UserDocs/` | End-user and operator documentation (setup, deployment, configuration, testing guides). |

### Key Files to Read First
- `Docs/Memories/` — current distilled knowledge (check this before any planning session)
- `Docs/Plans/MasterPlanV4.md` — canonical architecture blueprint
- `Plans/MasterPlanV4.md` — repo-root copy kept in sync with Docs version
- `.github/agents/smith.agent.md` — primary development agent definition
- `.github/skills/` — reusable workflow skills

### SplitBrain.Meta
`src/SplitBrain.Meta/SplitBrain.Meta.csproj` is a **documentation-only utility project** targeting net8.0 with no source files. Its sole purpose is to link `Docs/**` and `Scripts/**` into the Visual Studio Solution Explorer so they are browsable and searchable without being in the build graph.

### Reviewing Plans and Progress
When reviewing plans or progress reports, cross-reference with the actual codebase to verify that the documented state matches reality.
Plans may be aspirational and not yet fully implemented, while progress reports may be snapshots that have since evolved.
Always check the latest code for the true source of truth.
Always include any relevant code file paths in your notes.
Always note assumptions and open questions that arise during review to facilitate discussion.
Give confidence levels where appropriate.