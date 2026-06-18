# World Cup Edge Lab Commands

Run commands from the repo root unless noted.

## Safe Default Gates

```powershell
dotnet restore Oloraculo.sln
dotnet build Oloraculo.sln
dotnet test Oloraculo.sln
```

Use `dotnet test Oloraculo.sln` as the normal end-of-work verification gate.

## Production Readiness Gates

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-host-prereqs.ps1
dotnet test Oloraculo.sln
cargo test
python tools\mcp\test_oloraculo_context_server.py
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\codex\check-oloraculo-codex.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\opencode\check-oloraculo-opencode.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\test-container-smoke.ps1
```

Use these after changing production architecture, feed/hotpath, R2/AWS,
agents/skills/MCP, or release tooling. Add Docker, browser screenshot, and AWS
SSM smoke checks before claiming deployed production readiness.

## Coverage

```powershell
dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --collect:"XPlat Code Coverage"
```

Coverage artifacts are generated under `TestResults/` and should not be committed unless explicitly requested.

## Run App

```powershell
dotnet run --project Oloraculo.Web
```

This creates/updates the local SQLite database and can refresh ranking CSVs when `RankingRefreshOnStartup` is true.

## README Snapshot Export

```powershell
dotnet run --project Oloraculo.Web -- --export-readme-snapshots
```

This command is side-effecting. It can refresh external data, save snapshots/evaluations, update local SQLite state, and rewrite the generated block in `README.md`.

## Calibration Helper

```powershell
python tooling\goal_strength_calibration.py
python tooling\goal_strength_calibration.py --windows 3 5 8 12 all
```

Use this when touching goal strength, Poisson scoreline behavior, or recent-form window choices.

## Codex Health

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/codex/check-oloraculo-codex.ps1
```

Use this after changing Codex agents, skills, config, or MCP context files.

## OpenCode Compatibility Health

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/opencode/check-oloraculo-opencode.ps1
```

Use this after changing compatibility mirror files while `.opencode/*` remains
available.

## Combo Lab Workflow Commands

```text
/combo-bet-lab <task>
/polymarket-market-map <fixture or event>
/betting-risk-gate <candidate>
```

These commands are analysis-only. They must not place orders, touch wallet keys,
or mutate NativePM live-control files.
