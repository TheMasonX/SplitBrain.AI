---
name: task-core-loop
description: "Shared core workflow for SplitBrain.AI task execution: scope, evidence, minimal-change implementation, validation, and tracking updates."
argument-hint: "Task goal, scope boundaries, and validation target"
user-invocable: false
disable-model-invocation: false
---

# Task Core Loop

Use this internal skill as the shared base for implementation-focused workflows.

## Core Steps
1. Confirm scope and non-goals.
2. Gather source-of-truth evidence from code, wiki memories (`Docs/Memories/`), and active docs (`Docs/Plans/`, `Docs/Reviews/`).
3. Implement the smallest reversible slice.
4. Run the narrowest validation that can fail — prefer `dotnet test` for .NET projects, targeted test filters.
5. Record evidence, assumptions, confidence, and open questions.
6. Update tracker and any durable memory notes affected by new facts.

## Core Guardrails
- Keep changes minimal and focused to the stated slice.
- Prefer `Data/Tasks/` file-based traceability for implementation work when tasks exist.
- Prefer MCP tools (`mcp_memorysmithwi_memorysmith_*`) for wiki operations.
- Use `mcp_splitbrain_mc_*` tools for code review and test generation when beneficial.
- Avoid repetitive polling loops when snapshot checks are available.
- Default to in-process analysis; invoke subagents only with explicit user permission in the current request.

## Core Outputs
- Scope summary.
- Evidence list (file paths, command results, test outcomes).
- Validation result.
- Residual risks and next step.
