# Copilot CLI — Correct Installation

Source: Merged from `COPILOT-CLI-CORRECT-INSTALL.md`, `COPILOT-CLI-FIX.md`, `COPILOT-CLI-INSTALLATION.md`, `FINAL-COPILOT-FIX.md`, `ISSUE-RESOLVED.md`, `FIX-COPILOT-CLI-NPM-ISSUE.md`
Status: **Unconsolidated** — candidate for `Docs/Memories/` if Copilot CLI usage becomes a regular part of the system

---

## The Mistake to Avoid

The old docs (and some AI-generated instructions) say:
```powershell
npm install -g @github/gh-copilot   # ❌ WRONG — npm package no longer exists
```
This fails with `404 Not Found` from the npm registry.

## Correct Method (as of 2025)

Copilot CLI is distributed as a **GitHub CLI extension**, not an npm package.

```powershell
# Step 1: Install GitHub CLI (if not present)
winget install GitHub.cli       # or: choco install gh

# Step 2: Authenticate
gh auth login

# Step 3: Install the extension
gh extension install github/gh-copilot

# Step 4: Verify
gh copilot --version
```

## Key Facts
- The binary is downloaded per-platform automatically by `gh extension install`
- Auth is handled by `gh auth` — no separate API key needed for basic CLI use
- The extension lives at `~/.local/share/gh/extensions/gh-copilot/` on Windows

## Relation to SplitBrain.AI
The `CopilotInferenceNode` uses the **GitHub Copilot SDK** (NuGet: `GitHub.Copilot.SDK`), not the CLI.
The CLI is only relevant for manual testing or if a `CliPath` is configured in `CopilotProviderConfig`.
