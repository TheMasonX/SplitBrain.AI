---
name: council
description: "Run a SplitBrain.AI LLM council review for high-impact decisions. Use when evaluating architecture tradeoffs, orchestration design, schema changes, retrieval/search behavior, agent governance, or long-term infrastructure decisions."
argument-hint: "Decision topic and scope to review"
user-invocable: true
disable-model-invocation: false
---

# LLM Council Review for SplitBrain.AI

Inherits from `task-core-loop`.

Use this skill to run a structured, evidence-first, multi-perspective decision review.

## Subagent Permission Policy

- Default to a single-agent, self-simulated council analysis.
- Do not invoke subagents for council seats unless the user gives explicit permission in the current request.
- If explicit permission is not present, proceed with self-simulated seats and document that choice in the report.

## Outcome

Produce a council report that includes:
- A one-sentence decision statement
- Seat-by-seat recommendations with confidence percentages
- Explicit disagreement and dissent
- Risks, assumptions, open questions
- Acceptance criteria and validation gates before implementation

## Use When

Use this workflow when decisions affect:
- Orchestration architecture or node client dispatch strategy
- Memory record shape or schema fields in the wiki service
- Retrieval quality, ranking behavior, or search configuration
- Chat or agent write behavior and evidence traceability
- Dashboard or MCP interface conventions
- Long-lived architecture or migration cost
- Model provider selection or inference pipeline design

Do not use this workflow for quick lookups, one-off formatting, or routine implementation decisions.

## Inputs

Collect these inputs before running the workflow:
- Decision topic and one-sentence decision question
- Scope of impact: orchestration, wiki/schema, retrieval/search, chat/agent, dashboard, or mixed
- Primary evidence from `Docs/Memories/`, `Docs/Plans/`, and source code
- Any known stale docs, assumptions, or unresolved constraints

## Procedure

1. Classify the decision.
If this is low impact (cleanup, formatting, direct retrieval), skip council and do a normal edit or lookup.
If this is high impact, continue.

2. Build a bounded evidence pack.
Prefer MCP wiki tools and local project evidence first.
Minimum pack should include:
- Relevant memories in `Docs/Memories/`
- Relevant plans in `Docs/Plans/`
- Source-linked code evidence when claims depend on implementation
- Tests and benchmarks when behavior is affected
- If tests or benchmarks are not possible in exceptional circumstances, document why, add alternative evidence, and define a follow-up validation gate

3. Select council seats.
Use at least 3 seats (default) and expand toward all 6 for major architecture decisions:
- Source-Grounded Archivist
- Data Model Architect
- Retrieval Specialist
- Human Learning Advocate
- Skeptical Reviewer
- Synthesizer

4. Run independent seat reviews.
Give each seat the same evidence and require:
- Findings
- Risks
- Recommendations
- Assumptions
- Open questions
- Confidence percentage

Subagent usage note: only run seats via subagents when explicit user permission is provided. Otherwise run all seats in-process.

5. Branch on disagreement.
If seats materially disagree, do not flatten to consensus.
Record the disagreement and identify missing evidence that would change the outcome.

6. Synthesize a decision.
The Synthesizer must separate:
- What changes now
- What is deferred
- What evidence gates must be passed before implementation

7. Record the result.
Write or update the decision report with dissent visible, confidence values, and validation criteria.

8. Gate implementation.
Only proceed to code changes or broad migrations after acceptance criteria are explicit and verifiable.

## Decision Branches

- Branch A: Convention-first vs schema-first
If the concept is immature or primarily documentation/process, prefer convention-first plus validation probes.
Promote to schema only after patterns are stable and machine parsing or enforcement is clearly needed.

- Branch B: Retrieval/search sensitive changes
If ranking or recall quality may change, prefer measurable retrieval checks (tests/benchmarks) and explicit rollback notes.
If measurements are not feasible in an exceptional case, require documented rationale plus a time-boxed follow-up measurement plan.

- Branch C: Chat/agent write behavior
If trust or safety boundaries are affected, require evidence traceability and approval-gated write behavior.

- Branch D: Evidence weakness
If evidence is thin or stale, defer implementation and define what evidence must be gathered.

## Completion Checks

A council review is complete only when all are true:
- Decision statement is explicit
- Evidence links are listed and source-grounded
- Each seat includes confidence and blocking concerns
- Dissent is visible, not merged away
- Acceptance criteria are testable or reviewable
- If tests/benchmarks are omitted, exception rationale and follow-up validation gates are explicit
- Open questions are documented with next-step owners or gates

## Report Template

Use this structure in the final output:

```markdown
# Council Review: <Decision>

## Decision
<one sentence>

## Evidence Reviewed
- <memories/plans/source links>
- <tests/validation commands>

## Findings
| Seat | Recommendation | Confidence | Blocking concern |
|---|---|---:|---|
| Source-Grounded Archivist | ... | 0.85 | ... |
| Data Model Architect | ... | 0.80 | ... |
| Retrieval Specialist | ... | 0.78 | ... |
| Human Learning Advocate | ... | 0.82 | ... |
| Skeptical Reviewer | ... | 0.74 | ... |

## Synthesis
<what changes now vs later>

## Dissent
<unresolved disagreement>

## Acceptance Criteria
- <gate 1>
- <gate 2>

## Open Questions
- <question 1>
- <question 2>
```

## Prompt Pattern

Use this prompt form for each seat:

```text
You are the <seat name> council seat for SplitBrain.AI.
Review <topic> using the supplied wiki and code evidence.
Do not implement code.
Return findings, risks, recommendations, assumptions, open questions, and confidence percent.
Prefer source-linked project wiki evidence over generic advice.
```

## References

- `Docs/Memories/` — current distilled knowledge
- `Docs/Plans/MasterPlanV4.md` — canonical architecture blueprint
- Source code under `src/Orchestrator.*/`, `src/SplitBrain.Dashboard/`, `src/NodeClient.*/`
