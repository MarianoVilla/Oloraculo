---
description: Read-only Oloraculo planning pass that identifies the next atomic task and evidence gate.
agent: chief-systems-architect
---

Plan the next Oloraculo implementation slice for: $ARGUMENTS

Stay read-only. Do not edit files or run implementation commands.

Procedure:

1. Use `oloraculo-architecture-map`.
2. Read the matching source-of-truth docs.
3. Check `git status --short` if allowed.
4. Identify the smallest slice, owner agent, skill, files, side effects, tests,
   evidence, and unresolved risks.
