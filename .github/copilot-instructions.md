# Copilot Instructions

## Project Guidelines
- User prefers NUnit over xUnit for test projects.
- User prefers autonomous execution: continue implementing as far as safely possible between prompts, avoid pausing for user input when work can continue, include a markdown progress report file named `{PlanName}_Progress_{TimeStamp}`, summarize completion percentage, and explicitly report issues with `%CONTINUE%`/%`END%` status.

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

### SplitBrain.Meta
`src/SplitBrain.Meta/SplitBrain.Meta.csproj` is a **documentation-only utility project** targeting net8.0 with no source files. Its sole purpose is to link `Docs/**` and `Scripts/**` into the Visual Studio Solution Explorer so they are browsable and searchable without being in the build graph.