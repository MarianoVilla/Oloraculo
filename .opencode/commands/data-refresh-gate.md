---
description: Plan or run an Oloraculo data/feed refresh with explicit config and side-effect checks.
agent: feed-status-integrator
---

Run a data or feed refresh gate for: $ARGUMENTS

Use `oloraculo-feed-hotpath` and `oloraculo-security-boundary`.

Procedure:

1. Read `docs/source-of-truth/DATA_AND_SECRETS.md`, `docs/source-of-truth/COMMANDS.md`, and relevant tests.
2. Run `git status --short` before any refresh.
3. Identify required API keys/config by presence only. Do not print key values.
4. State expected side effects before running any command or UI action.
5. Run the narrowest safe command or stop with a plan if manual/API-key action is required.
6. Inspect changed files and report warnings/errors.
7. Verify with focused tests or `dotnet test Oloraculo.sln` when behavior changed.
