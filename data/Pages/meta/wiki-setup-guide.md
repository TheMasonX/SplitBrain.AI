# MemorySmith Wiki Setup Guide for a New Project

**Purpose:** Step-by-step record of everything done to create a MemorySmith wiki for SplitBrain.AI. Use as a template for automating wiki creation (wizard / CLI tool) for new projects.

---

## Phase 0 — Prerequisites

Before creating a wiki, gather the following information about the project:

### Information Checklist

- [ ] **Project name and one-line description**
- [ ] **Repository URL** (GitHub/GitLab)
- [ ] **Active branch** (e.g. `next-gen`, `main`)
- [ ] **Solution file name** and location
- [ ] **Projects list** (from `.sln` / `.slnx`) — name, purpose, key dependencies
- [ ] **Architecture decisions** already made (locked decisions → Core memories)
- [ ] **Current state** of implementation (Working memories)
- [ ] **Existing docs directories** — list all `Docs/`, `docs/`, `Plans/`, `README.md` locations
- [ ] **Tech stack** — frameworks, test library, charting, storage
- [ ] **Hardware / deployment targets** (for embedded systems, GPU projects, etc.)
- [ ] **MCP / API surface** (if applicable)
- [ ] **Key open items / known issues**

### MemorySmith Installation

Ensure MemorySmith is running with its `DataPath` pointed at `{repo}/data/`. In `MemorySmith.App/appsettings.json`:

```json
{
  "MemorySmith": {
    "DataPath": "../{project-repo}/data/",
    "WikiName": "{Project Name}",
    "CodeSearch": {
      "Enabled": true,
      "Projects": [
        { "Name": "src", "Path": "../{project-repo}/src/" }
      ]
    }
  }
}
```

---

## Phase 1 — Read the MemorySmith Data Format

Before creating any files, read the format spec. Key facts:

### Memory Format (`.json`)

- Location: `data/Memories/{Status}/`
- Status folders: `Core/` (2), `Working/` (1), `Deprecated/` (3), `Unconsolidated/` (0)
- Filename: `{kebab-case-id}.json` — must match `Id` field exactly
- **No YAML front matter** — pure JSON
- Required fields: `Id`, `Content`, `Status`
- Optional: `Title`, `Confidence` (0.0-1.0), `Tags[]`, `References[]`, `Conflicts[]`, `SourceLinks[]`, `UsageCount`, `LastUpdated`
- `Content`: 100-300 tokens of plain text (no markdown sub-headings)
- `SourceLinks[].Uri`: may use `%VarName%` tokens from `data/vars.json`

### Page Format (`.md`)

- Location: `data/Pages/{subdirectory}/`
- **No YAML front matter** — pure Markdown starting with `# Title`
- Optional sidecar: `.page.json` with `{"minimumRole": "Authenticated"}` for access control
- Valid roles: `"Anonymous"` (default), `"Authenticated"`, `"Admin"`

### Infrastructure Files

| File | Purpose |
|------|---------|
| `data/vars.json` | Path variables for SourceLinks (`%VarName%` expansion) |
| `data/Policies/tag-policy.json` | Tag governance (mode, namespaces, allowlist, blocklist) |
| `data/Graph/code-search/archive/.gitkeep` | Ensures archive dir is tracked; live DB is gitignored |
| `data/Proposals/.gitkeep` | Placeholder for maintenance proposals |
| `data/Tasks/.gitkeep` | Placeholder for task records |
| `data/Memories/{Status}/.gitkeep` | Ensures empty status dirs are tracked |

---

## Phase 2 — Read Existing Project Docs

Before creating wiki content, read what exists:

1. **List all doc directories** in the repository (GitHub: `get_file_contents` on root → list dirs)
2. **Read key existing files** in parallel:
   - `README.md` → project overview → drives the `{project}-project-overview` Core memory
   - `docs/Memories/*.md` or similar → existing informal memories → convert to `.json`
   - `Docs/UserDocs/deployment.md` → deployment guide → migrate to `data/Pages/ops/deployment.md`
   - `Docs/UserDocs/configuration-reference.md` → config page
   - Any design decision docs → Core memories
   - Any progress reports → `data/Pages/history/` index page

3. **Note which files to move** vs. create fresh. Moving = creating new file at new path with same/enhanced content. The old files stay unless deleted separately.

---

## Phase 3 — Create Directory Structure

Create all directories with `.gitkeep` files to ensure they're tracked by git:

```
data/
  vars.json
  Memories/
    Core/.gitkeep
    Working/.gitkeep
    Deprecated/.gitkeep
    Unconsolidated/.gitkeep
  Pages/
    assets/.gitkeep
    architecture/
    guides/
    ops/
    audits/
    research/
    history/
    meta/
  Graph/
    code-search/
      archive/.gitkeep
  Policies/
    tag-policy.json
  Proposals/.gitkeep
  Tasks/.gitkeep
```

---

## Phase 4 — Create vars.json

```json
{
  "{ProjectRepo}": "",
  "_note": "Set {ProjectRepo} to the absolute path of your checkout, e.g. C:\\\\Users\\\\you\\\\repos\\\\{project}\\\\ . SourceLinks use %{ProjectRepo}% prefix."
}
```

The variable name should be `{ProjectName}Repo` (e.g., `SplitBrainRepo`, `MemorySmithRepo`).

---

## Phase 5 — Create Core Memories

Core memories are **durable, stable facts** that change rarely. Create one memory per distinct fact — don't pack multiple unrelated facts into one memory.

### Recommended Core Memory Set for a New Project

| Memory ID Pattern | Content Type | Confidence |
|-------------------|-------------|------------|
| `{proj}-project-overview` | What the project is, its purpose, main entry points | 0.95-0.99 |
| `{proj}-solution-layout` | Project list, naming conventions, NuGet deps | 0.90-0.95 |
| `{proj}-architecture-{area}` | One per major architectural area | 0.85-0.95 |
| `{proj}-decision-{id}-{name}` | Each locked architectural decision | 0.90-0.99 |
| `{proj}-testing-framework` | Test library, conventions, test count | 0.90-0.95 |
| `{proj}-data-wiki-policy` | Explains the data/ directory itself | 0.99 |

### Content Guidelines

- **100-300 tokens** — long enough to be useful, short enough to be scannable
- **No markdown formatting** in Content — plain sentences only
- **One fact domain per memory** — routing facts in routing memory, agent bounds in agent bounds memory
- **Locked decisions get `Confidence ≥ 0.95`** — they're stable
- **`Tags`** — use 3-6 plain tags; add decision/architecture/current-state as appropriate
- **`References`** — IDs of memories this one relates to (must match existing memory IDs exactly)
- **`SourceLinks`** — link to specific files/lines in the repo using `%{ProjectRepo}%` prefix

### Writing the Content Field

Good: `"MemorySmith uses MemorySmith.App as the single deployable host. The app hosts the Blazor UI, REST API, file storage registration, background maintenance, and health endpoints in one process."`

Bad: `"## Architecture\n- Uses MemorySmith.App\n- Blazor UI"` (markdown in Content is not rendered)

---

## Phase 6 — Create Working Memories

Working memories capture **current implementation state** — they change as work progresses.

### Recommended Working Memory Set

| Memory ID Pattern | Content Type |
|-------------------|-------------|
| `{proj}-{feature}-status` | Implementation status of a current feature |
| `{proj}-branch-state` | Current branch HEAD, commit series |
| `{proj}-audit-status` | Outstanding audit findings and batch status |
| `{proj}-{hardware}-flags` | Configuration tuning for specific hardware |

### When to Upgrade to Core

When a Working memory becomes stable (feature shipped, decision locked), change `"Status": 1` to `"Status": 2` and move the file from `Working/` to `Core/`.

---

## Phase 7 — Create Pages

Pages hold **longer documentation** that doesn't fit in 100-300 token memories.

### Recommended Page Structure

```
data/Pages/
  architecture/
    overview.md         # ASCII architecture diagram, component table
    solution-layout.md  # Project-by-project breakdown
    {feature}.md        # One page per major feature/design
  guides/
    getting-started.md  # Setup from scratch
    {workflow}.md       # One per user workflow
  ops/
    deployment.md       # Deploy reference (migrated from Docs/UserDocs/)
    configuration-reference.md  # Config table (migrated)
  audits/
    {date}-summary.md   # Per-audit summary with finding table
  research/
    {topic}.md          # Research notes on specific technologies
  history/
    progress-reports.md # Index linking to old session reports
  meta/
    wiki-setup-guide.md # This document
```

### Page Writing Rules

1. Start with `# Title` — no YAML, no front matter
2. Use `## Subheadings` for sections
3. Include **Related Pages** section at the bottom (relative links)
4. For migrated pages: add `> Migrated from {original path}` after the title
5. Keep pages focused — if a page exceeds ~400 lines, split it

---

## Phase 8 — Create tag-policy.json

Copy the MemorySmith template and adapt the `allowlist` for project-specific tags:

1. Keep the namespace structure (kind, priority, audience, scope, review-after, stale-risk)
2. Replace `allowlist` with project-relevant tags
3. Set `"mode": "warn"` — allows any tag but warns on non-allowlisted ones
4. Add `"aliases"` for common misspellings or deprecated tag names

---

## Phase 9 — Configure Code Search

Code search is a runtime concern — the index lives at `data/Graph/code-search/code-search.db` (gitignored). Configure in MemorySmith's `appsettings.json`:

```json
{
  "MemorySmith": {
    "CodeSearch": {
      "Enabled": true,
      "EmbeddingBatchSize": 8,
      "HybridVectorWeight": 0.75,
      "HybridLexicalWeight": 0.25,
      "ChunkLineCount": 40,
      "ChunkOverlapLineCount": 8,
      "Projects": [
        {
          "Name": "Orchestrator.Core",
          "Path": "../SplitBrain.AI/src/Orchestrator.Core/",
          "Include": ["**/*.cs"],
          "Exclude": ["**/obj/**", "**/bin/**"]
        },
        {
          "Name": "NodeClient.LlamaCpp",
          "Path": "../SplitBrain.AI/src/NodeClient.LlamaCpp/",
          "Include": ["**/*.cs"]
        }
      ]
    }
  }
}
```

Add one entry per source project you want indexed. "The codesearch projects will be the repo projects" — each `src/` project becomes one entry.

---

## Phase 10 — Commit to Repository

Commit all `data/` files to the project repository in one atomic commit:

```
git add data/
git commit -m "feat: add MemorySmith wiki under data/

- data/Memories/Core/: {N} core memories (architecture, decisions, facts)
- data/Memories/Working/: {N} working memories (current state, in-progress)
- data/Pages/: {N} markdown pages (architecture, guides, ops, audits)
- data/Policies/tag-policy.json: tag governance
- data/vars.json: %{ProjectRepo}% path variable

Existing docs from Docs/ and docs/ migrated into data/Pages/.
MemorySmith DataPath should point at <repo>/data/.
"
```

---

## Automation / Wizard Notes

To automate this for future projects, a wizard would need to:

1. **Input:** Project name, repo URL, solution file path, list of projects
2. **Step 1:** Clone/read the repo to list existing docs
3. **Step 2:** Read existing doc content via GitHub API
4. **Step 3:** Use LLM to extract key facts → generate Core memory JSON (Content field 100-300 tokens)
5. **Step 4:** Use LLM to generate Working memory suggestions from open issues/PRs/current state
6. **Step 5:** Scaffold the page structure with stub pages for each recommended category
7. **Step 6:** Migrate existing docs by prompting LLM to convert to wiki page format
8. **Step 7:** Generate `vars.json` and `tag-policy.json` from templates
9. **Step 8:** Commit everything via GitHub API (push_files)

### Key Decisions for the Wizard

- **Memory vs. Page threshold:** If content is > 300 tokens → Page. If < 300 tokens and a single coherent fact → Memory.
- **Core vs. Working:** If the fact is a decision/architecture/locked spec → Core. If it's current implementation state → Working.
- **Tag selection:** Use the project's domain vocabulary. For a coding project: architecture, routing, testing, decision. For a storage project: schema, migration, performance.
- **SourceLinks:** Extract file paths from the existing docs or infer from code structure.

### Approximate File Count for a Medium Project

| Category | Files |
|----------|-------|
| Core memories | 10-20 |
| Working memories | 3-8 |
| Pages | 10-20 |
| Infrastructure | 8 (gitkeeps, vars, policy) |
| **Total** | **31-56** |

---

## What This Wiki Setup Did for SplitBrain.AI

**Total files created:** ~36  
**Core memories:** 15 (project overview, solution layout, node topology, MCP interface, inference abstraction, routing engine, agent bounds, decisions D1/D2/D3/D6/D8, testing framework, dashboard theme, data wiki policy)  
**Working memories:** 4 (llama.cpp status, audit batch status, branch state, GTX 1080 flags)  
**Pages:** 11 across architecture/, guides/, ops/, audits/, research/, history/, meta/  
**Infrastructure:** 8 (gitkeeps, vars.json, tag-policy.json)

**Existing docs migrated:** `Docs/UserDocs/deployment.md` → `data/Pages/ops/deployment.md`, `Docs/UserDocs/configuration-reference.md` → `data/Pages/ops/configuration-reference.md`. Session progress reports indexed at `data/Pages/history/progress-reports.md`.

**Not deleted:** Original `Docs/` and `docs/` directories remain in the repo. These should be removed in a separate cleanup commit once the wiki is confirmed working with MemorySmith.

---

*Created 2026-06-02. Update this guide as the wizard/automation tooling evolves.*
