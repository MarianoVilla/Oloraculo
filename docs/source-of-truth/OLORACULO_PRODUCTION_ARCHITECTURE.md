# Oloraculo Production Architecture

This is the new source of truth for the clean production trading system.

## Decision

`prediction/Oloraculo` is the forward production target.

`polytrade` is now a **read-only donor/reference** only. It contains useful
lessons, Rust patterns, feed notes, AWS scripts, and venue probes, but it is too
messy to remain the runtime source of truth for this project.

Any useful idea from Polytrade must be copied conceptually into Oloraculo as a
clean, owned, tested implementation. Do not make the Oloraculo production system
depend on Polytrade runtime state, paths, services, data lake files, dirty
worktree files, or scripts.

## Production split

| Layer | Owner | Runtime | Notes |
| --- | --- | --- | --- |
| Cockpit/UI | Oloraculo | .NET Blazor | Read-only operator surface, no order side effects. |
| Sports scalp scanner | Oloraculo | Rust-first hotpath, .NET display | Full-depth VWAP, hedge targets, blockers. |
| CLOB collectors | Oloraculo AWS | Rust-first | Resident WebSocket books/trades, bounded hot cache. |
| External feed collectors | Oloraculo AWS | Rust-first / .NET workers | Databet, widgets, OddsPapi, GRID, Polymarket CLOB, and archive status; no Python runtime services. |
| Archive | Oloraculo AWS → R2/S3-compatible | Rust-first batchers/materializers | Raw compressed batches, Parquet/ZSTD query layers. |
| Execution | Oloraculo AWS | Rust-first | Disabled until exact approval/WAL/risk gates exist under Oloraculo. |
| Polytrade | Reference only | none | Mine for lessons; do not run as production dependency. |

## Core trading workflow

The strategy is the sports two-leg scalp workflow:

```text
entry side + hedge side < 1.00
locked_profit_per_share = 1 - entry - hedge
roi = locked_profit_per_share / (entry + hedge)
required_hedge = 1 / (1 + target_roi) - entry
```

The scanner must always identify:

- entry side;
- opposite hedge side;
- executable entry VWAP for target shares;
- executable/current hedge VWAP where available;
- breakeven / 2% / 5% / 8% / 10% / 12% hedge prices;
- 400-share ladder by default;
- trigger path;
- failure plan;
- blockers.

## AWS direction

The AWS production host should run the live hotpath because it should have lower
and more reliable latency than a Windows laptop for CLOB WebSocket books,
scanner updates, and eventual execution.

But the AWS stack must be **Oloraculo-owned**, not Polytrade-owned:

1. Clean Terraform or deployment scripts under Oloraculo.
2. Clean service names, paths, users, logs, and data directories.
3. No dependency on `/home/.../polytrade` paths.
4. No execution until Oloraculo has its own exact approval, WAL,
   reconciliation, kill-switch, and risk gates.
5. AWS status checks use SSM. SSH is not routine ops.

Initial Oloraculo AWS services:

- `oloraculo-clob-hotpath`: resident CLOB WebSocket book/trade collector.
- `oloraculo-sports-scalp-scanner`: full-depth two-leg scalp planner.
- `oloraculo-feed-status`: sanitized external feed status aggregator.
- `oloraculo-r2-archiver`: raw batch upload and verified local prune.
- `oloraculo-cockpit-api`: read-only snapshot endpoint for the Blazor cockpit.

## Local storage rule

Local disk is hot cache only.

Do not keep raw books/snapshots on the Windows disk as a permanent archive.

Oloraculo local keeps:

- current live dashboard state;
- last-good in-memory snapshot;
- short-lived test fixtures;
- bounded hot cache only if explicitly configured.

Permanent history belongs in Cloudflare R2/S3-compatible object storage.

Preferred lake layout:

```text
r2://market-data/
  polymarket/
    raw/clob_ws/date=YYYY-MM-DD/hour=HH/market_slug=.../*.ndjson.zst
    bronze/trades/date=YYYY-MM-DD/market_slug=.../*.parquet
    bronze/book_deltas/date=YYYY-MM-DD/market_slug=.../*.parquet
    bronze/book_top10/date=YYYY-MM-DD/market_slug=.../*.parquet
    silver/price_1s/date=YYYY-MM-DD/*.parquet
    silver/price_1m/date=YYYY-MM-DD/*.parquet
    silver/decay_curves/date=YYYY-MM-DD/*.parquet
    silver/hedge_simulations/date=YYYY-MM-DD/*.parquet
    gold/strategy_results/
    gold/match_cards/
    gold/pnl_reports/
```

Raw replay/audit files are `.ndjson.zst` or `.jsonl.zst` batched around
100-500 MB. Queryable layers are Parquet with ZSTD compression. DuckDB `httpfs`
may be used as a research/query tool against R2, not as production hotpath
runtime.

## Polytrade extraction rules

Allowed:

- read docs/code/tests to understand venue behavior;
- extract protocol shape, endpoint paths, stream names, failure lessons;
- port small Rust patterns only after rewriting and testing under Oloraculo;
- cite donor file paths in migration notes.

Forbidden:

- running Polytrade as production;
- depending on Polytrade services, runtime directories, local lake, dirty files,
  or AWS scripts as the Oloraculo runtime;
- copying secrets, logs containing secrets, `.env`, `.mcp`, private keys,
  wallet material, AWS credentials, or key-like values;
- adding Python production services/materializers/order paths;
- treating Polytrade row counts as live Oloraculo readiness.

## Feed migration source map

Useful Polytrade donor areas, read-only:

- Databet sportsbook/widgets: `docs/ops/venues/databet.py`,
  `docs/ops/specs/sportsbook_gql_api.md`,
  `docs/ops/specs/databet_widgets_api.md`.
- OddsPapi/Pinnacle: `docs/ops/venues/oddspapi.py`,
  `docs/ops/venues/ODDSPAPI.md`.
- GRID: `docs/ops/venues/grid.py`, `docs/ops/venues/GRID.md`.
- Polymarket CLOB/Gamma: `docs/ops/venues/polymarket.py`,
  `nativepm/hotpath/src/*`, `pmexec/src/*`.
- Monitor patterns: `nativepm_trader_monitor.rs`, dashboard specs.

These are donor references only. Oloraculo owns the implementation and tests.

## Live-order boundary

Current Oloraculo stance: **no live order path**.

Before any live order can exist under Oloraculo:

- resident Rust supervisor;
- fresh quote intent;
- exact approval generated and validated field-for-field;
- WAL before side effect;
- reconciliation/private stream gate;
- event timing gate: live/in-play or starts within 2h; missing timing fails closed;
- kill switch;
- tiny caps;
- explicit operator approval for the live gate.

Until then, dashboards may show `TRADE_NOW` as analysis-only executable math, not
permission to place an order.

## Current Rust hotpath skeleton

`rust/oloraculo_hotpath` is the clean Phase 1 Rust crate. It currently provides:

- dependency-free deterministic price/share primitives;
- canonical full-depth bid/ask sorting;
- buy/sell/buy-up-to depth fills;
- 400-share VWAP and worst-price math;
- hedge targets for breakeven, 2%, 5%, 8%, 10%, and 12% ROI;
- two-leg scalp candidate verdicts and blockers;
- read-only JSON snapshot contract.

It does not perform network I/O, write local raw books, read secrets, place
orders, approve orders, cancel orders, or arm live trading.

## Backlog

1. Keep `SportsScalp` Blazor cockpit auto-refreshing and read-only.
2. Add an Oloraculo-owned Rust hotpath crate for CLOB book/trade collection and
   two-leg scalp planning.
3. Add sanitized feed status contracts for Databet, widgets, OddsPapi, GRID,
   Polymarket CLOB, and R2/S3-compatible archive config/health.
4. Add R2 archive writer: compressed raw batch, manifest, hash/size verify,
   prune local only after verified upload.
5. Add Parquet/ZSTD bronze/silver/gold materialization for research.
6. Deploy Oloraculo-owned AWS services via SSM-managed runbooks.
7. Add measured p50/p90/p99 latency reports before claiming AWS latency wins.
8. Only then consider Oloraculo live execution gates.
