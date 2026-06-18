---
name: oloraculo-feed-hotpath
description: Build Oloraculo feed status, external adapters, CLOB freshness, and Rust scanner hotpath contracts. Use for Databet, widgets, OddsPapi/Pinnacle, Sofascore, GRID, Polymarket CLOB books/trades, full-depth VWAP, stale blockers, and read-only snapshot contracts.
---

# Oloraculo Feed Hotpath

## Purpose

Use this skill when the cockpit depends on live-ish feed truth or CLOB book
state. It keeps data contracts honest and scanner math deterministic.

## Required Reads

- `docs/source-of-truth/DATA_AND_SECRETS.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_BACKLOG.md`
- `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`
- `rust/oloraculo_hotpath/`
- `Oloraculo.Web/Feeds/` if present

## Feed Status Contract

Use these sanitized fields unless a source-of-truth doc changes them:

- `present`
- `latest_recv_ts`
- `age_ms`
- `rows_last_minute`
- `join_coverage`
- `last_error_redacted`

Return `NOT_IMPLEMENTED` for absent sources. Do not fake green health.

## Procedure

1. Define the contract and redaction behavior before implementing an adapter.
2. Distinguish missing config, missing entitlement, stale data, empty data, parse
   error, source down, and not implemented.
3. Use read-only feed probes and fakes in tests.
4. For CLOB books, use executable bid/ask/depth and mark stale/no-book/thin-depth
   as blockers.
5. Keep pure Rust math free of network, archive, credential, and order effects.

## Tests

- Present/missing/stale/error/not-implemented feed states.
- Redacted error output.
- Empty/crossed/stale/thin CLOB book blockers.
- Full-depth VWAP and 400-share ladder behavior.
- JSON snapshot compatibility when contracts change.

## Vetoes

- Raw credential values or token fragments in UI/logs/docs.
- Last price or midpoint as executable liquidity.
- Hiding missing feeds.
- Order placement, approval, cancel, or wallet calls.
