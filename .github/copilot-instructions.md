# Copilot Instructions

## Project Guidelines
- User prefers NUnit over xUnit for test projects.

## Knowledge Hub — Where to Find Things

All project knowledge is organized under `Docs/` at the repo root. The `src/SplitBrain.Meta/` project (net8.0, no code) exists solely to surface `Docs/` and `Scripts/` inside Visual Studio via `<None Include>` links.

### Docs Directory Map

| Path | Purpose |
|---|---|
| `Docs/Memories/` | **Active knowledge base.** Distilled, up-to-date facts about architecture, decisions, and current state. Start here for context. |
| `Docs/Plans/` | Architecture and implementation plans. `MasterPlanV4.md` is the canonical blueprint (mirrored at `Plans/MasterPlanV4.md` repo root). |
| `Docs/Reviews/` | External and automated review reports against the plans (e.g. deep-research critiques). Cross-reference with codebase before trusting. |
| `Docs/ProgressReports/` | Snapshot reports of implementation progress per phase. |
| `Docs/UserDocs/` | End-user and operator documentation (setup, deployment, configuration). |
| `Docs/*.md` (root-level) | Older operational docs (Copilot CLI fixes, node test guides). Treat as historical; canonical replacements live in subdirectories. |

### Key Files to Read First
- `Docs/Memories/` — current distilled knowledge (check this before any planning session)
- `Docs/Plans/MasterPlanV4.md` — canonical architecture blueprint
- `Plans/MasterPlanV4.md` — repo-root copy kept in sync with Docs version

### SplitBrain.Meta
`src/SplitBrain.Meta/SplitBrain.Meta.csproj` is a **documentation-only utility project** targeting net8.0 with no source files. Its sole purpose is to link `Docs/**` and `Scripts/**` into the Visual Studio Solution Explorer so they are browsable and searchable without being in the build graph.