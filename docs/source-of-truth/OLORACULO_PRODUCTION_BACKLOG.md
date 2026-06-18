# Oloraculo Production Backlog

This backlog turns the new production architecture into executable phases. Check
items off only when implemented under Oloraculo, tested under Oloraculo, and free
of Polytrade runtime dependency.

## Phase 0 — Boundary Lock

- [x] Declare `prediction/Oloraculo` as the clean production target.
- [x] Declare Polytrade read-only donor/reference only.
- [x] Keep Oloraculo cockpit read-only; no live order path.
- [x] Add local no-bloat storage rule: local disk is hot cache only.
- [x] Add full-depth 400-share VWAP math to the Oloraculo scanner.
- [x] Add auto-refreshing `SportsScalp` dashboard.

## Phase 1 — Clean Rust Hotpath Skeleton

- [x] Create an Oloraculo-owned Rust workspace/crate for hotpath components.
- [x] Add CLOB market/book/trade data models.
- [x] Add deterministic full-depth book state and VWAP helpers.
- [x] Add two-leg sports scalp planner output matching the Blazor cockpit model.
- [x] Add no-side-effect JSON snapshot writer/API contract.
- [x] Add unit tests for book deltas, reconnect snapshots, VWAP, hedge targets,
  and blockers.

Phase 1 implementation: `rust/oloraculo_hotpath`. It is dependency-free,
network-free, archive-free, credential-free, and order-path-free. It owns exact
integer price/share math, full-depth 400-share VWAP, hedge target math, blockers,
and a read-only JSON snapshot contract.

## Phase 2 — Clean Feed Status Contracts

- [ ] Define sanitized feed status model: `present`, `latest_recv_ts`, `age_ms`,
  `rows_last_minute`, `join_coverage`, `last_error_redacted`.
- [ ] Add Databet sportsbook/widgets status adapter using Polytrade docs as donor
  only; no token display.
- [ ] Add OddsPapi/Pinnacle status adapter; key presence only.
- [ ] Add GRID status adapter; entitlement/result counts only.
- [ ] Add Polymarket CLOB status adapter from the Oloraculo hotpath.

## Phase 3 — R2/Object Archive

- [ ] Define object-storage env var names with placeholder-only docs.
- [ ] Add raw batch format: `.ndjson.zst` / `.jsonl.zst`, 100-500 MB target.
- [ ] Add manifest rows: stream, path, byte count, row count, recv range, SHA256,
  upload status.
- [ ] Upload to R2/S3-compatible endpoint.
- [ ] Verify object size/hash before local prune.
- [ ] Add retention policy: local hot cache 1-7 days; permanent history in R2.
- [ ] Add Parquet/ZSTD bronze materializer for trades, book deltas, top10.
- [ ] Add silver/gold research layers for price curves, decay features, hedge
  simulations, match cards, and PnL reports.

## Phase 4 — AWS Oloraculo Deployment

- [ ] Create Oloraculo-owned AWS deployment/runbooks; no Polytrade path reliance.
- [ ] Use SSM for routine ops; no SSH as normal path.
- [ ] Install services with Oloraculo names and directories:
  - `oloraculo-clob-hotpath`
  - `oloraculo-sports-scalp-scanner`
  - `oloraculo-feed-status`
  - `oloraculo-r2-archiver`
  - `oloraculo-cockpit-api`
- [ ] Add health endpoint and `/snapshot.json` consumed by Blazor.
- [ ] Measure CLOB WS and REST latency p50/p90/p99 before making latency claims.
- [ ] Add disk/inode guards for local hot cache.

## Phase 5 — Cockpit Integration

- [ ] Replace local direct Gamma/CLOB polling with Oloraculo AWS snapshot source
  when available.
- [ ] Keep local fallback for development only.
- [ ] Add event-level market board grouping.
- [ ] Add candidate drilldown: market board, hedge grid, ladder capacity,
  blockers, feed freshness, and failure plan.
- [ ] Add position screenshot calculator workflow as a cockpit panel.

## Phase 6 — Evidence Before Execution

- [ ] Add paper/shadow candidate tracking.
- [ ] Add markouts at 1s, 5s, 10s, 30s, 60s.
- [ ] Add queue-loss and fillability denominator.
- [ ] Add realized hedge simulations from actual book paths.
- [ ] Add per-market-cell PnL/EV/hour reports.

## Phase 7 — Live Order Path, Disabled Until Explicitly Approved

- [ ] Resident Rust supervisor.
- [ ] Fresh quote intent.
- [ ] Exact approval generated and validated field-for-field.
- [ ] WAL before any side effect.
- [ ] Private/reconciliation gate.
- [ ] Kill switch and cancel-all proof.
- [ ] Event timing gate: live or starts within 2h; missing timing fails closed.
- [ ] Tiny caps and explicit operator live gate.

Until Phase 7 is complete and explicitly armed, Oloraculo remains analysis-only.

## Polytrade donor extraction queue

Extract only concepts, tests, and protocol knowledge from these donor areas:

- Polymarket CLOB/Gamma parsing and WS lessons.
- NativePM Rust monitor endpoint patterns.
- `pmexec` signing/order lifecycle tests, only when Oloraculo is ready for its
  own execution crate.
- Databet sportsbook/widgets protocol notes.
- OddsPapi/Pinnacle de-vig and line extraction notes.
- GRID entitlement/status lessons.
- AWS SSM deployment patterns, not scripts as runtime dependencies.

Every extracted item needs a new Oloraculo file, test, and owner.
