---
name: self-review
description: "Run an agent self-review for skill and prompt improvements. Use periodically to audit skill design, token-cost hotspots, and workflow upgrades across the SplitBrain.AI customizations."
argument-hint: "Review scope, urgency, and output format"
user-invocable: true
disable-model-invocation: false
---

# Self Review

Inherits from `task-core-loop`.

## Added Context
Use this to periodically review and improve:
- skill design and overlap
- agent prompt clarity
- token-cost hotspots
- opportunities for script extraction for repeated terminal commands
- chat tool usage parity across prompt, runtime, MCP, and coding-agent surfaces

Prefer MCP tooling first for reads/writes where available; propose extraction only for repeated operations not already covered by MCP tools.

## Additional Procedure
1. Review active skills for duplication and missing shared abstractions.
2. Identify loops that should move from chat polling to script-driven waits.
3. Propose prompt updates that reduce ambiguity and token use.
4. For repeated manual content generation, propose reusable scripts or MCP tool patterns.
5. Treat chat tool usage parity as a first-class requirement: document any intentional mode differences and flag undocumented drift.
6. Update or create `Data/Tasks/` records when a recommendation is significant enough to merit execution tracking.
7. Mark each recommendation as now, next, or later with confidence.

## Output
- Improvement shortlist with impact and effort.
- Proposed edits/scripts with evidence.
