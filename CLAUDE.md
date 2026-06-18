# Oloraculo Durable Context

This file is the compact resume point for Codex sessions and compatibility
clients.

## Current Architecture

- Solution: `Oloraculo.sln`.
- Web app: `Oloraculo.Web` targeting `net9.0`.
- Tests: `Oloraculo.Web.Tests` using xUnit and in-memory SQLite/fake HTTP handlers.
- UI: Blazor Server/Razor Components with MudBlazor and `InteractiveServer`.
- Persistence: EF Core 9 with SQLite. No migrations are currently present; schema is created/extended at runtime.
- Data: CSV seed files under `Oloraculo.Web/Data`.
- Prediction core: `Oloraculo.Web/Predictors` and `Oloraculo.Web/Probability`.
- Simulation core: `Oloraculo.Web/Services/Simulation`.

## Important Runtime Behavior

- `Program.cs` runs CSV import on startup.
- `RankingRefreshOnStartup` defaults to `true` and can make network calls during normal app startup.
- `--export-readme-snapshots` enters CLI mode, runs snapshot export, and returns without starting the web server.
- The README generated block is between `<!-- oloraculo:snapshots:start -->` and `<!-- oloraculo:snapshots:end -->`.

## External Integrations

- FIFA ranking raw Wikipedia module.
- International-football.net Elo table.
- GitHub raw goalscorers CSV.
- API-Football v3, requiring `ApiFootballApiKey` for live fixture/context refresh.
- OpenRouter chat completions, requiring `OpenRouterApiKey` for availability classification.
- ESPN/TalkSport availability URLs configured in `appsettings.json`.

## Safety Rules

- Keep API keys out of git. Use `appsettings.Development.json`, user secrets, or environment variables.
- Local config files are readable in this repo. Do not paste or commit key values unless explicitly asked.
- Do not run data refresh or README export without stating expected side effects.
- Do not claim model quality from a single prediction. Use evaluation metrics, calibration checks, and tests.
- Every status update, report, audit, and final handoff must include the recommended next step and why it matters. If there is no recommended next step, say so and explain why.
- Preserve user edits in the worktree.

## Default Verification

```powershell
dotnet test Oloraculo.sln
```

Use broader gates when relevant:

```powershell
dotnet build Oloraculo.sln
dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --collect:"XPlat Code Coverage"
python tooling\goal_strength_calibration.py
```

## Current Automation Layer

- Codex-native surfaces are primary.
- `.agents/skills` contains repo-scoped Codex skills: `oloraculo-architecture-map`, `oloraculo-aws-r2-ops`, `oloraculo-feed-hotpath`, `oloraculo-cockpit-parity`, `oloraculo-quant-evidence`, `oloraculo-release-gate`, `oloraculo-mcp-tooling`, and `oloraculo-security-boundary`.
- `.codex/agents` contains project-scoped Codex custom subagents: architecture, AWS runtime, R2 archive, Rust hotpath, feed status, cockpit UI, quant evidence, security/risk, release verification, and MCP tooling.
- `.codex/config.toml` configures project-scoped Codex MCP servers. `oloraculo-context`, `context7`, browser tooling, and Serena are enabled; credential-backed MCPs stay disabled by default.
- `tools/mcp/oloraculo_context_server.py` exposes committed source-of-truth docs, backlog phases, guardrails, and routing through the local `oloraculo-context` MCP.
- `tools/codex/check-oloraculo-codex.ps1` validates the Codex-native layer.
- `opencode.json`, `.opencode/*`, and `.claude/*` are compatibility mirrors for other clients, not the authoritative Codex layer.
- Polymarket Combo Lab workflow lives in `docs/source-of-truth/POLYMARKET_COMBO_LAB.md`; the all-sports sports scalp cockpit workflow lives in `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`.
- Serena is configured with `--project-from-cwd` and `--language-backend LSP`; restart Codex after MCP/config changes.
