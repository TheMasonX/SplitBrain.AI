---
name: wiki-hygiene-audit
description: "Run a repeatable hygiene audit over Docs/Memories and Docs/ for drift, stale facts, broken source links, and cleanup candidates. Use for dogfooding and knowledge quality maintenance."
argument-hint: "Scope (memories/plans/docs) and cleanup strictness"
user-invocable: true
disable-model-invocation: false
---

# Wiki Hygiene Audit

Inherits from `task-core-loop`.

## Added Context
Use this for dogfooding and knowledge quality maintenance across the SplitBrain.AI project wiki.

## Audit Targets
- Stale or contradictory memory records in `Docs/Memories/`.
- Source links that no longer resolve or point to moved files.
- Redundant or overlapping content across `Docs/` that should be consolidated.
- Memories incorrectly storing future plans that belong in wiki pages or plans.
- Unconsolidated fragments in `Docs/Unconsolidated/` that haven't been promoted.

## Additional Procedure
1. Start from `Docs/Memories/` then expand to `Docs/Plans/`, `Docs/Unconsolidated/`, `Docs/UserDocs/`.
2. Cross-check linked code and documentation content for drift.
3. Identify candidates for merge, deprecation, or rewrite.
4. Apply minimal fixes and capture evidence for each change.
5. Create follow-up task records in `Data/Tasks/` for unresolved hygiene debt.

## Output
- Hygiene findings by severity.
- Proposed consolidations/deprecations.
- Residual backlog items.
