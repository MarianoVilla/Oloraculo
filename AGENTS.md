# Oloraculo Agent Guide

This is the project entrypoint for AI agents working in Oloraculo.

## Project

Oloraculo is a .NET 9 Blazor Server app for predicting the 2026 FIFA World Cup. It imports CSV seed data, refreshes optional external data, builds match predictions through a model ladder, evaluates predictions, stores snapshots in SQLite, and can rewrite the generated snapshot block in `README.md`.

## Hard Rules

- Inspect first. Do not assume the current worktree is clean.
- Preserve unrelated user changes. Never revert files you did not change unless the user explicitly asks.
- Local config files are readable in this repo. Do not paste or commit key values unless the user explicitly asks for those values.
- Do not run destructive git or shell commands such as `git reset --hard`, `git clean -f`, force pushes, or recursive forced deletes.
- Treat data refresh and README export as side-effecting operations. Say what files, database rows, or generated docs may change before running them.
- Do not claim predictions are calibrated, UI is correct, data is refreshed, or work is verified without command output, screenshots, or explicit evidence.
- Every status update, report, audit, and final handoff must include the recommended next step and why it matters. If there is no recommended next step, say so and explain why.

## Main Commands

Run from the repo root unless noted.

```powershell
dotnet restore Oloraculo.sln
dotnet build Oloraculo.sln
dotnet test Oloraculo.sln
dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --collect:"XPlat Code Coverage"
dotnet run --project Oloraculo.Web
dotnet run --project Oloraculo.Web -- --export-readme-snapshots
python tooling\goal_strength_calibration.py
python tools\mcp\test_oloraculo_context_server.py
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/codex/check-oloraculo-codex.ps1
```

## Side-Effect Map

- `dotnet test Oloraculo.sln` should be the default verification gate and should not require external API keys.
- `dotnet run --project Oloraculo.Web` creates or updates the local SQLite database and may refresh ranking CSVs when `RankingRefreshOnStartup` is true.
- `dotnet run --project Oloraculo.Web -- --export-readme-snapshots` can refresh rankings/API/availability data, save snapshots/evaluations, and rewrite the generated block in `README.md`.
- UI actions on `/data`, `/matches`, `/fixture`, and `/tournament` can modify local data, snapshots, evaluations, or generated reports.

## Source Of Truth

- `README.md` describes user-facing behavior and routes.
- `docs/source-of-truth/ACTIVE.md` tracks current project operating context.
- `docs/source-of-truth/COMMANDS.md` defines command gates and side effects.
- `docs/source-of-truth/DATA_AND_SECRETS.md` defines data refresh and local config hygiene rules.
- `docs/source-of-truth/POLYMARKET_DOCS_REFERENCE_MAP.md` maps the local official Polymarket docs snapshot in `docs/vendor/polymarket-docs`.
- `.agents/skills` contains repo-scoped Codex skills.
- `.codex/agents` contains project-scoped Codex custom subagents.
- `.codex/config.toml` contains project-scoped Codex MCP configuration.
- `Oloraculo.Web/Program.cs` defines startup behavior and CLI modes.
- `Oloraculo.Web/OloraculoConfig.cs` defines supported config keys.

## Agent Routing

- Use `chief-systems-architect` for audits, roadmap reconciliation, phase planning, and routing.
- Use `aws-runtime-operator` for AWS deployment, SSM runbooks, service topology, health, latency, and disk guards.
- Use `r2-archive-lake-engineer` for R2/S3 archive layout, manifests, upload verification, retention, and Parquet/ZSTD layers.
- Use `rust-hotpath-engineer` for CLOB books, deterministic Rust math, scanner snapshots, and Rust tests.
- Use `feed-status-integrator` for Databet, widgets, OddsPapi/Pinnacle, GRID, Polymarket CLOB freshness, archive status, and sanitized feed health.
- Use `cockpit-ux-engineer` for Blazor cockpit UI, monitor visual parity, responsive layout, refresh UX, and screenshots.
- Use `quant-evidence-scientist` for probability, payoff masks, combo EV, hedge math, markouts, backtests, and PnL evidence.
- Use `security-risk-sentinel` for secrets, local config, side-effect review, live-order boundaries, and risk vetoes.
- Use `release-verification-lead` for build/test gates, CI, config validation, screenshots, MCP validation, and done claims.
- Use `mcp-toolsmith` for Codex MCP servers, `.codex/config.toml`, `.codex/agents`, `.agents/skills`, and tooling health.

## Polymarket Combo Lab Boundary

Oloraculo may compute World Cup fair values, contract payoff masks, combo EV,
and capped analysis sizing. It must not place, approve, cancel, or route live
orders. Existing NativePM/polytrade live-path services are donor evidence only;
any future live-capital path must be Oloraculo-owned, Rust-first, disabled by
default, and pass the Phase 7 approval/WAL/reconciliation/arming gates.

## Verification Standard

Use the narrowest meaningful check first, then broaden when risk warrants it. A normal code change should end with at least `dotnet test Oloraculo.sln`. Data refreshes and README exports must report exact changed files and any external service limitations. Final handoffs must state the next useful step and the reason that step is the highest-leverage continuation.
