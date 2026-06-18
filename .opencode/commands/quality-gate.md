---
description: Run the appropriate focused Oloraculo quality gate for the current diff or requested scope.
agent: release-verification-lead
---

Run the production quality gate for: $ARGUMENTS

Use `oloraculo-release-gate`.

Procedure:

1. Read `CLAUDE.md`, `AGENTS.md`, and `docs/source-of-truth/COMMANDS.md`.
2. Run `git status --short` and inspect the diff for unrelated churn, generated files, and accidental key commits.
3. Identify the narrowest relevant tests for the changed surface.
4. Run focused tests first, then broaden only if risk warrants it.
5. Run `dotnet build Oloraculo.sln` when Razor/config/tooling changed.
6. Run `dotnet test Oloraculo.sln` before any verified/done claim.

Report exact commands, observed results, pass/fail counts, artifacts, and residual risks.
