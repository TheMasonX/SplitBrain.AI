---
name: task-delivery-sprint-loop
description: "Run a task-backed implementation loop from active task selection through validation evidence and task status updates. Use when delivery should be anchored to Data/Tasks records."
argument-hint: "Task ID/key, change slice, and validation scope"
user-invocable: true
disable-model-invocation: false
---

# Task Delivery Sprint Loop

Inherits from `task-core-loop`.

## Added Context
Use this when delivery should be anchored to `Data/Tasks/` records, not ad hoc notes.

Use file-based task records under `Data/Tasks/` — read and update them directly.

## Additional Inputs
- Task id or key (e.g., `tsk-0001-*`).
- Target status transition (for example: Backlog -> InProgress -> Done).
- Validation evidence required before completion.

## Additional Procedure
1. Load task context from `Data/Tasks/<task-id>.json` before edits.
2. Move task status to `InProgress` when implementation starts.
3. Apply core steps from `task-core-loop`.
4. Update the task record with a summary of work done, evidence, and findings.
5. Transition task status only after validation evidence is captured.

## Done Criteria
- Task record reflects current status accurately.
- Evidence is captured in the task record or a linked progress report.
- Code and wiki notes are synchronized with the delivered slice.
