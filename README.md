# World Cup Edge Lab

World Cup Edge Lab is an analysis-only 2026 FIFA World Cup modeling and Polymarket market-research workspace. It maps markets, runs simulations, explains model signals, and surfaces blockers.

It does **not** place, approve, cancel, sign, or route live orders. NativePM/Polytrade material in this repo is donor evidence only; Oloraculo production execution must be an Oloraculo-owned, explicitly armed boundary.

## Video

[Project background and walkthrough](https://youtu.be/cvPeS0qAikw?si=yHv5wKkk5lqgYXhn)

<!-- oloraculo:snapshots:start -->
## Latest Predictions

_The generated snapshot was intentionally cleared during the English-only World Cup Edge Lab cleanup. Re-run the README exporter when you want a fresh English snapshot from the current model state._
<!-- oloraculo:snapshots:end -->

## What It Does

- Imports seed data from CSV files: groups, historical results, FIFA rankings, and Elo ratings.
- Builds match predictions through layered models:
  - uniform baseline
  - FIFA ranking
  - Elo
  - recent form
  - Poisson scoreline model with a Dixon-Coles-style low-score adjustment
  - goal model adjusted by recent context and player availability when available
- Selects the highest usable model as the final edge model, with notes about missing or skipped signals.
- Runs a repeatable Monte Carlo tournament simulation and stores tournament snapshots.
- Saves match predictions and evaluates them later with Brier score, RPS, log loss, and top-pick accuracy.
- Optionally refreshes rankings, API-Football fixture/context data, and availability news classified through OpenRouter.
- Maps World Cup Polymarket markets and Combo RFQ eligibility in the read-only Combo Lab.
- Tracks fixture-burden features and pre-registration blockers before any edge claim.

## Live-Capital Boundary

World Cup Edge Lab is a model, combo, EV, and monitor lab only:

- no wallet keys
- no signatures
- no approvals
- no order endpoints
- no cancellations
- no live-control mutation

Any future real-money action must go through an Oloraculo-owned Rust supervisor with exact approval, WAL/reconciliation, event-eligibility, private-stream freshness, kill switch, caps, and risk gates. That boundary is not armed in this workspace.

## Tech Stack

- .NET 9
- Blazor Server with MudBlazor
- Entity Framework Core 9
- SQLite
- CsvHelper
- xUnit

## Main Screens

- `/` - overview and model ladder
- `/lab` - compare two teams across the prediction ladder
- `/matches` - group-stage fixtures, prediction snapshots, context refresh, and result entry
- `/fixture` - full fixture view
- `/tournament` - run the Monte Carlo tournament simulation
- `/tournament/snapshots` - inspect saved tournament projections
- `/performance` - prediction evaluation metrics
- `/data` - CSV import, rankings refresh, API-Football refresh, and availability refresh
- `/combo-lab` - read-only World Cup Polymarket universe, Combo RFQ eligibility, burden gates, and pre-registration status

## Project Structure

```text
Oloraculo.sln
Oloraculo.Web/
  ComboLab/            Polymarket market mapping, candidate generation, and monitor services
  Components/          Blazor pages, layout, and shared UI
  DAL/                 EF Core DbContext
  Data/                CSV seed data and video notes
  Helpers/             CSV parsing, team-name normalization, crypto helpers
  Models/              Domain, CSV, API-Football, snapshot, and evaluation models
  Predictors/          Model ladder and final selector
  Probability/         Outcome, scoreline, linked-event, and tournament probability math
  Services/            Import, prediction, rankings, API, availability, snapshots, evaluation
    Simulation/        World Cup bracket and Monte Carlo engine
  WorldCup/            Fixture-burden features and tournament-specific research gates
Oloraculo.Web.Tests/   xUnit tests
docs/                 Source-of-truth research and operating notes
```

## Getting Started

Prerequisites:

- .NET 9 SDK

Run the app:

```bash
dotnet restore
dotnet run --project Oloraculo.Web
```

The SQLite database is created automatically on startup, and the CSV seed data is imported when needed.

## Configuration

Settings live in `Oloraculo.Web/appsettings.json` under the `Oloraculo` section.

Important keys:

- `SimulationCount` and `SimulationSeed`
- `RecentResultCount`
- `GoalModelYearsWindow`
- `RankingRefreshOnStartup`
- `FifaRankingsRawUrl`
- `EloRankingsBaseUrl`
- `GoalscorersRawUrl` and `GoalscorerLookbackYears`
- `ApiFootballApiKey`
- `OpenRouterApiKey`
- `AvailabilitySourceUrls`
- `PolymarketGammaBaseUrl`
- `PolymarketClobBaseUrl`
- `PolymarketComboRfqBaseUrl`
- `ObjectArchive` for disabled-by-default R2/S3-compatible checkpoint archive wiring

Keep secrets such as API-Football, OpenRouter, and Firecrawl keys in local development secrets or environment variables. Do not commit real credential values.
Use `OLORACULO_R2_BUCKET`, `OLORACULO_R2_ENDPOINT`, `OLORACULO_R2_ACCESS_KEY_ID`, and `OLORACULO_R2_SECRET_ACCESS_KEY` for the object archive when enabled.

## Testing

```bash
dotnet test Oloraculo.sln
```

## Data Sources

CSV seed data lives in `Oloraculo.Web/Data`:

- `wc2026_groups.csv`
- `historical_results.csv`
- `goalscorers.csv` (optional cache, refreshed from `GoalscorersRawUrl` when missing)
- `fifa_rankings.csv`
- `elo_snapshot.csv`
