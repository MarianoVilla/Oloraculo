# Active World Cup Edge Lab Context

This file is the short source-of-truth checkpoint for current project operation.

## Current State

- World Cup Edge Lab is a .NET 9 Blazor Server app for 2026 FIFA World Cup predictions.
- The app has working xUnit coverage for probability, predictors, services, imports, snapshots, rankings, availability, and README export.
- The repo now has a Codex-native AI operating layer: `AGENTS.md`, `CLAUDE.md`, `.agents/skills`, `.codex/agents`, `.codex/config.toml`, `tools/codex/check-oloraculo-codex.ps1`, and the custom `oloraculo-context` MCP under `tools/mcp`. `.claude/*` and `.opencode/*` remain compatibility mirrors only.
- The Polymarket Combo Lab is analysis-only and tracked in `docs/source-of-truth/POLYMARKET_COMBO_LAB.md`.
- The official Polymarket docs snapshot lives under `docs/vendor/polymarket-docs`; use `docs/source-of-truth/POLYMARKET_DOCS_REFERENCE_MAP.md` first for fast lookup.
- The canonical feed/archive status contract is `docs/source-of-truth/FEED_STATUS_CONTRACT.md`; `/snapshot.json` is sanitized status-only, not a live monitor snapshot or order authority.
- The active sports trading cockpit source of truth is `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`; its readable HTML companion is `docs/polymarket-sports-scalp-cockpit.html`.
- The clean production architecture source of truth is `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`; Polytrade is now read-only donor/reference only.
- The clean production backlog is `docs/source-of-truth/OLORACULO_PRODUCTION_BACKLOG.md`.
- The production-readiness plan and priority list is `docs/source-of-truth/OLORACULO_PRODUCTION_READINESS_PLAN.md`.
- The executable production TODO board is `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md`.
- The live-path donor audit is `docs/source-of-truth/OLORACULO_LIVE_PATH_DONOR_AUDIT.md`; it keeps NativePM/Polytrade as donor evidence while Oloraculo owns future live execution.
- C123 reference material now lives under `docs/reference/c123`, with optional read-only probes in `tools/c123-market-validation`, optional archive/query utilities in `tools/c123-market-archive`, and a non-workspace Rust scanner reference in `tools/c123-sports-scout-reference`. These are donor/reference assets until ported into Oloraculo-owned typed services with tests.

## Default Gate

```powershell
dotnet test Oloraculo.sln
```

## High-Risk Surfaces

- Probability math and calibration: `Oloraculo.Web/Probability`, `Oloraculo.Web/Predictors`, evaluation tests.
- Side-effecting data refresh: `RankingRefreshService`, `ApiFootballService`, `AvailabilityNewsService`, `CsvImportService`.
- README publishing: `ReadmeSnapshotExportService` and the generated block in `README.md`.
- Startup behavior: `Program.cs`, especially `RankingRefreshOnStartup`.
- Local config and generated state: `appsettings.Development.json`, `.mcp.json`, `.env`, `secrets.json`, `*.db`.
- Polymarket combo lab and sports scalp cockpit: market identity, payoff masks, two-leg hedge math, executable CLOB freshness/depth, auto-refresh monitor state, and Oloraculo-owned future live boundaries.
- Production migration: useful Polytrade ideas may be extracted, but production runtime, AWS services, archive layout, scanner logic, and cockpit contracts must be Oloraculo-owned and tested here.
- Production readiness: work must follow `docs/source-of-truth/OLORACULO_PRODUCTION_READINESS_PLAN.md`; the first unbuilt production priorities are a tracked/reproducible release baseline, real source-status adapters, CLOB blockers, and AWS snapshot consumption.

## Operating Principle

Prefer small, reversible slices with explicit evidence. Treat every generated prediction as a model output that needs calibration/evaluation context, not as truth by assertion.

Every status update, report, audit, and final handoff must include the recommended next step and why it matters. If there is no useful next step, say that explicitly and explain why.
