---
description: Run or plan Oloraculo README snapshot export with side-effect disclosure and verification.
agent: security-risk-sentinel
---

Run a README snapshot export gate for: $ARGUMENTS

Use `oloraculo-security-boundary` and `oloraculo-release-gate`.

Procedure:

1. Read `docs/source-of-truth/COMMANDS.md`, `docs/source-of-truth/DATA_AND_SECRETS.md`, and `Oloraculo.Web/Services/ReadmeSnapshotExportService.cs`.
2. Run `git status --short` and preserve unrelated dirty work.
3. State that `dotnet run --project Oloraculo.Web -- --export-readme-snapshots` can refresh external data, update SQLite state, save snapshots/evaluations, and rewrite `README.md`.
4. Run the command only if the scope asks for an export or the user confirms this side effect.
5. Inspect `git status --short` and `git diff -- README.md Oloraculo.Web/Data` afterward.
6. Run `dotnet test Oloraculo.sln` if code or data behavior changed.

Report exact command output, changed files, and any unavailable external integrations.
