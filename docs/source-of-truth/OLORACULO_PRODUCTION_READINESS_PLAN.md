# Oloraculo Production Readiness Plan

Date: 2026-06-17

This plan is the concrete checklist for turning the current Oloraculo workspace
into a production-ready, Oloraculo-owned AWS/R2 sports scalp system. It is based
on the current source-of-truth docs, code audit, and delegated domain audits for
AWS/R2, feed/hotpath, cockpit/quant evidence, and security/release.

## Production Definition

Oloraculo is production ready only when all of these are true:

- The release contents are tracked, reviewed, reproducible, and covered by CI.
- The deployed runtime is Oloraculo-owned, AWS-hosted, and operated through SSM
  runbooks rather than Polytrade paths or manual laptop state.
- The cockpit is read-only, secret-safe, and consumes an Oloraculo snapshot API.
- Feed status is real: source freshness, rows, joins, parse health, entitlement
  state, and redacted errors are measured instead of inferred from config alone.
- Polymarket CLOB collection is resident and first-class, with freshness,
  crossed/thin/no-book blockers, full-depth liquidity, and latency metrics.
- R2/S3-compatible storage is the durable archive, with compressed raw batches,
  manifests, hash/size verification, retry state, and prune only after verified
  upload.
- The cockpit visually matches or improves the designed monitor while remaining
  data-honest: no static production-looking panels, no hidden blockers, and no
  order affordances.
- Evidence exists before execution: paper/shadow tracking, markouts, fillability
  denominator, realized hedge replay, PnL/EV/hour, and calibration reports.
- Live order code stays absent or disabled until Phase 7 has exact approval,
  WAL, reconciliation, kill switch, cancel-all proof, tiny caps, and explicit
  operator arming.

## What We Have

| Area | Current assets | Production-readiness state |
| --- | --- | --- |
| Governance | `docs/source-of-truth/*`, `AGENTS.md`, Codex agents, Codex skills, `oloraculo-context` MCP | Strong local operating layer, but most assets are currently untracked and must be reviewed before release. |
| Application | .NET 9 Blazor Server app, prediction services, probability models, data import/export, test suite | Mature for World Cup prediction workflows; not yet a deployed trading runtime. |
| Sports scalp UI | `SportsScalp.razor`, auto-refresh, no-order banners, Gamma/CLOB public-read scanner display | Useful local cockpit; still directly fetches Gamma/CLOB and needs AWS snapshot mode. |
| Scalp math | Full-depth 400-share VWAP, hedge targets, ROI blockers, tests | Good core math; missing partial hedge/equalization and stronger stale/crossed/no-timing blockers. |
| Combo Lab | Watch-only checkpoints, live lock inputs, hashes, freshness gates, `LiveOrderPath = none` | Strong analysis boundary and evidence ledger foundation. |
| Rust hotpath | `rust/oloraculo_hotpath` with deterministic book/math/scalp snapshot code | Clean Phase 1 skeleton; no resident collector, no network IO, no archive, no schema compatibility gate yet. |
| Feed status | Canonical sanitized schema across `.NET`, Rust hotpath primitives, and `/snapshot.json` | Contract is frozen and tested; rows still mostly report config/planned state, not real source health. |
| R2/S3 archive | `S3ObjectArchiveService`, manifest sidecar, SHA256/size metadata verification, optional evidence checkpoint archive | Upload primitive exists; no compressed raw batcher, retry queue, durable manifest table, retention/prune job, or Parquet layers. |
| AWS deployment | `Dockerfile`, `deploy/aws/README.md`, env example, `/healthz`, `/snapshot.json` | Container skeleton exists; no IaC, pinned service units, SSM scripts, rollback drill, or deployed proof. |
| Security boundary | Secret ignore rules, redaction helpers, no-order UI language, disabled credential-backed MCPs by default | Strong analysis-only posture. Needs CI secret scan and regression tests on every release. |
| Verification | `dotnet test`, Rust unit tests, MCP smoke, Codex health, release-scope check, local container smoke | Local gates exist, but CI does not yet run the full stack and screenshot gates are missing. |
| Live-path donor evidence | Read-only AWS inventory of NativePM/Polytrade live router, executor, private websocket, order heartbeat, open-order snapshot, paper/shadow, evidence, and monitor services | Useful donor concepts, but not Oloraculo production dependency; see `docs/source-of-truth/OLORACULO_LIVE_PATH_DONOR_AUDIT.md`. |

## What We Need

| Area | Required work | Why it matters | Acceptance evidence |
| --- | --- | --- | --- |
| Release baseline | Reconcile dirty/untracked files, decide release scope, track the production layer intentionally | Untracked source-of-truth, CI, tooling, and deploy files cannot be production evidence | Clean reviewed diff, CI passes from tracked files only |
| CI and security gates | Add .NET, Rust, MCP smoke, Codex health, release-scope, Docker build, browser screenshots, and secret scanning | Production readiness must be repeatable without this session | CI run with all gates green and artifacts retained |
| Feed contract | Freeze canonical schema across .NET, Rust, UI, `/snapshot.json`: `schema_version`, `present`, `latest_recv_ts`, `age_ms`, `rows_last_minute`, `join_coverage`, `last_error_redacted`, readiness, blockers | Config presence is not source health | Contract tests for missing, down, stale, empty, parse-error, not-implemented, and redacted-error states |
| Source adapters | Add Databet sportsbook/widgets, OddsPapi/Pinnacle, GRID, and Polymarket CLOB status adapters; keep SofaScore deprecated unless a future source decision reactivates it | Operators need honest feed health and entitlement state | Fake adapter tests plus optional live smoke with env/SSM credentials |
| CLOB hotpath | Promote Polymarket CLOB to resident collector/status source with token/book counts, freshness, crossed/thin/no-book blockers, and scanner coverage | Direct REST polling is not production collector-grade | Rust/.NET compatibility tests and snapshot fixture replay |
| Scanner blockers | Block stale quotes, missing required timing, crossed books, invalid books, and insufficient ladder depth | `TRADE_NOW` analysis math must not hide execution risk | Unit tests for every blocker and no-order regression text |
| R2 raw archive | Implement compressed raw batcher, durable manifest rows, retry/failure state, hash/size verification, and no-prune-before-verify | Permanent history belongs in R2, not laptop hot cache | Local fake S3/R2 tests plus real test-bucket smoke |
| R2 materializers | Add Parquet/ZSTD bronze/silver/gold layers after raw replay is trustworthy | Research and evidence need queryable history | Idempotent materializer tests, schema snapshots, DuckDB/httpfs query smoke |
| AWS deployment | Create Oloraculo-owned IaC or scripts, SSM runbooks, service names, directories, logs, image pinning, rollback | Production cannot rely on notes alone | Deployed service smoke through SSM, rollback drill, no Polytrade paths |
| Observability | Add metrics/logs/alerts for health, collector freshness, latency p50/p90/p99, archive failures, disk/inode usage | A live data plane needs operations evidence | `/healthz`, `/snapshot.json`, metrics endpoint/log query, alert test |
| Cockpit integration | Blazor consumes AWS snapshot in production with local fallback for development only | The cockpit must display the production data plane | Tests proving production mode does not call Gamma/CLOB directly |
| Monitor parity | Convert static monitor visuals into live, read-only panels matching or improving `C:\123\monitor-design` | Static production-looking UI is dangerous | Playwright screenshots desktop/mobile, visual comparison, no overlap |
| Operator workflows | Add event grouped market board, candidate drilldown, hedge grid, ladder capacity, blockers, feed freshness, failure plan, position calculator | Operators need decision context, not only a candidate table | Component tests and screenshot evidence |
| Evidence layer | Add paper/shadow lifecycle, markouts, fillability denominator, realized hedge replay, PnL/EV/hour, OOS calibration | No live execution without proof of edge and fillability | Replay/markout reports, strategy cells, calibration snapshots |
| Phase 7 live path | Implement only after explicit approval: supervisor, exact approval, WAL, private reconciliation, kill switch, cancel-all proof, caps, arming | Live trading is the highest-risk boundary | Separate gated release with explicit operator arming and veto checks |
| NativePM/Polytrade donor review | Preserve donor live services as read-only evidence until Oloraculo replacements exist; do not decommission or arm them as Oloraculo | Existing services contain useful patterns but also runtime/path/bypass risks | `docs/source-of-truth/OLORACULO_LIVE_PATH_DONOR_AUDIT.md` plus future Oloraculo-owned tests |

## Priority List

### P0 - Stabilize The Release Boundary

1. Reconcile the dirty worktree and make the production/tooling layer tracked.
2. Add or enforce CI for `.NET`, Rust, MCP smoke, Codex health, release-scope, Docker
   build, secret scanning, and no-order static scans.
3. Keep live-order capability under veto. Assert `WATCH_ONLY`,
   `NO_ORDER_PATH`, and `LiveOrderPath=none` across UI, checkpoints, snapshots,
   Rust, and tests.
4. Add shallow production smoke tests for `/healthz` and `/snapshot.json`, with
   no secret leakage.

Why first: without a reproducible, tracked, secret-safe, analysis-only baseline,
every later "production" claim is fragile or misleading.

### P1 - Make Data Health Real

1. Keep the frozen feed status schema stable across .NET, Rust, UI, and `/snapshot.json`.
2. Add source-specific fake adapters and tests for all planned status states.
3. Implement real per-source status adapters or honest `NOT_IMPLEMENTED` rows.
4. Promote Polymarket CLOB into first-class status from the Oloraculo hotpath.
5. Upgrade SportsScalp blockers for stale, no-timing, crossed, invalid, thin,
   and insufficient ladder-depth books.

Why next: the cockpit cannot be trusted until every green/yellow/red indicator
means measured source behavior, not config presence or optimistic scaffolding.

### P2 - Build AWS And R2 Production Foundations

1. Implement the R2 raw lake: compressed batches, manifests, retry state,
   verification, retention, and safe prune.
2. Add local hot-cache disk/inode/age guards.
3. Create AWS IaC or deterministic deploy scripts plus SSM runbooks.
4. Deploy the initial read-only services: `oloraculo-cockpit-api`,
   `oloraculo-clob-hotpath`, `oloraculo-feed-status`,
   `oloraculo-sports-scalp-scanner`, and `oloraculo-r2-archiver`.
5. Add latency p50/p90/p99 and archive/collector observability.

Why here: after data contracts are honest, the next bottleneck is reliable
runtime and durable history. R2/AWS must become tested infrastructure, not docs.

### P3 - Make The Cockpit Production-Grade

1. Switch Blazor production mode to the Oloraculo AWS snapshot source; keep local
   Gamma/CLOB fallback for development only.
2. Rebuild `/sports-scalp` and/or `/monitor` to match or improve the visual
   design at `C:\123\monitor-design`, using live snapshot data.
3. Add event-level grouped market board, candidate drilldown, hedge grid,
   ladder capacity, blockers, feed freshness, failure plan, and position
   calculator.
4. Add Playwright desktop/mobile screenshots and visual parity checks.

Why here: the monitor is where operator trust is won or lost. It must be both
visually exact and operationally honest.

### P4 - Prove Edge Before Execution

1. Add paper/shadow candidate lifecycle.
2. Add markouts at 1s, 5s, 10s, 30s, and 60s.
3. Add queue/fillability denominator and partial-fill accounting.
4. Add realized hedge replay from actual book paths.
5. Add PnL/EV/hour reporting by market cell and OOS calibration/ablation gates.

Why here: visible depth and theoretical ROI are not enough. Production execution
requires evidence that the strategy would actually fill, hedge, and survive
fees, latency, queue loss, and stale data.

### P5 - Live Execution, Explicitly Separate

1. Start Phase 7 only after P0-P4 are green and the user explicitly approves it.
2. Implement resident Rust supervisor, exact approval, WAL, private stream
   reconciliation, kill switch, cancel-all proof, event timing gate, tiny caps,
   and operator arming.
3. Ship it as a separate gated release with a release-verification veto.

Why last: this is the only phase that can create real financial side effects.
Until then, Oloraculo remains analysis-only even when it says `TRADE_NOW`.

## Execution Roadmap

| Slice | Deliverables | Primary owners | Required tests |
| --- | --- | --- | --- |
| 0. Release baseline | Reviewed tracked production layer, CI expansion, secret scan, no-order scan | `release-verification-lead`, `security-risk-sentinel` | `dotnet test`, `cargo test`, MCP smoke, Codex health, release-scope, Docker build, secret scan |
| 1. Feed contracts | Canonical schema, `.NET`/Rust/UI compatibility, generic adapter-state mapping | `feed-status-integrator`, `rust-hotpath-engineer` | Contract fixtures, redaction tests, `/snapshot.json` schema tests |
| 2. CLOB collector status | Resident CLOB status model, freshness/blockers, scanner source contract | `rust-hotpath-engineer`, `feed-status-integrator` | Fake CLOB book tests, stale/crossed/thin blockers, snapshot compatibility |
| 3. R2 archive | Raw batcher, manifest store, retry, verify/prune, retention | `r2-archive-lake-engineer`, `aws-runtime-operator` | Fake S3/R2 tests, test-bucket upload/hash/prune failure tests |
| 4. AWS deploy | IaC/scripts, SSM runbooks, service config, image pinning, rollback | `aws-runtime-operator`, `release-verification-lead` | Container smoke, SSM health smoke, rollback drill |
| 5. Cockpit parity | Snapshot consumer, live monitor panels, grouped board, drilldown, calculator | `cockpit-ux-engineer`, `quant-evidence-scientist` | Playwright visual screenshots, component tests, no direct production CLOB calls |
| 6. Evidence | Shadow lifecycle, markouts, replays, fillability, PnL/EV/hour, calibration | `quant-evidence-scientist`, `security-risk-sentinel` | Replay fixtures, markout reports, calibration/OOS gates |
| 7. Live gate | Approval/WAL/reconciliation/kill switch/caps/arming | `security-risk-sentinel`, `release-verification-lead` | Separate explicit live-readiness test suite and manual arming checklist |

## Agent And Skill Routing

| Topic | Agent | Skill | Notes |
| --- | --- | --- | --- |
| Architecture and phase decisions | `chief-systems-architect` | `oloraculo-architecture-map` | Owns source-of-truth consistency and priority arbitration. |
| AWS runtime and SSM | `aws-runtime-operator` | `oloraculo-aws-r2-ops` | Owns deploy, service names, runbooks, rollback, health. |
| R2/S3 archive lake | `r2-archive-lake-engineer` | `oloraculo-aws-r2-ops` | Owns raw batches, manifests, verify/prune, materializers. |
| Rust CLOB/scanner hotpath | `rust-hotpath-engineer` | `oloraculo-feed-hotpath` | Owns deterministic Rust contracts, resident collector shape, snapshot compatibility. |
| Feed status | `feed-status-integrator` | `oloraculo-feed-hotpath` | Owns canonical status schema, adapters, redaction, source health. |
| Cockpit and monitor parity | `cockpit-ux-engineer` | `oloraculo-cockpit-parity` | Owns Blazor UX, screenshot QA, visual parity with monitor design. |
| Quant evidence | `quant-evidence-scientist` | `oloraculo-quant-evidence` | Owns markouts, replay, fillability, PnL/EV/hour, calibration. |
| Security and live boundary | `security-risk-sentinel` | `oloraculo-security-boundary` | Owns secret hygiene, no-order invariants, Phase 7 veto. |
| Release gates | `release-verification-lead` | `oloraculo-release-gate` | Owns CI, Docker, tests, release evidence, rollback gate. |
| MCP/tooling | `mcp-toolsmith` | `oloraculo-mcp-tooling` | Owns context MCP, agents/skills validation, routing helpers. |

## Acceptance Gates

Before calling any milestone production ready, collect these artifacts:

- `git status --short` reviewed and scoped.
- `dotnet restore Oloraculo.sln`, `dotnet build Oloraculo.sln`, and
  `dotnet test Oloraculo.sln`.
- `cargo test` for the Rust workspace.
- `python tools/mcp/test_oloraculo_context_server.py`.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/codex/check-oloraculo-codex.ps1`.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/release/check-release-scope.ps1`.
- Docker build and local container run with `/healthz` and `/snapshot.json`
  smoke checks.
- Secret scan over committed files, plus verification that ignored local secret
  files remain ignored.
- Browser screenshot checks for cockpit pages on desktop and mobile.
- R2 fake/test-bucket upload with manifest/hash/size verification and a failed
  verification case proving local prune does not happen.
- AWS SSM runbook smoke for health, logs, restart, rollback, and snapshot read.
- Evidence reports for shadow markouts, fillability, replay, PnL/EV/hour, and
  calibration before any live-order discussion.

## Current Highest Risks

- The worktree is dirty and much of the new production/tooling layer is
  untracked, so release evidence is not yet reproducible.
- `/monitor` has strong visual language but is static/demo, which can mislead if
  treated as live readiness.
- `/sports-scalp` has real scanner math but still depends on direct local
  Gamma/CLOB REST calls and lacks production snapshot-source separation.
- Feed status rows use a frozen sanitized schema, but most rows still represent
  config/planned state rather than real source health.
- R2/S3 archive code verifies individual uploads but is not yet a raw lake with
  batching, retry, retention, materialization, or verified prune.
- AWS deployment is a container/runbook skeleton, not an IaC-backed operated
  system.
- The S3-compatible archive path currently expects explicit access keys, while
  docs also imply AWS task-role style credentials. This must be reconciled.
- NativePM/Polytrade live services show useful route, heartbeat, user-websocket,
  arming, paper/shadow, and monitor concepts, but they are not owned by this
  repo and include defaults/paths/runtime shapes that Oloraculo must not inherit
  blindly.
- `TRADE_NOW` is analysis-only, but the label must remain wrapped in no-order
  language and regression tests until live execution is explicitly approved.
- Current edge evidence is visible-depth math, not fill proof, queue proof, or
  realized hedge replay.

## Immediate Next Work

1. Make the production/tooling layer reviewable: track or intentionally exclude
   `.agents/`, `.codex/`, `.github/`, `docs/`, `deploy/`, `rust/`, `tools/`,
   `Dockerfile`, and `.dockerignore`.
2. Add the production-readiness CI gates: Rust, MCP, Codex health, release-scope,
   Docker build, secret scan, and screenshot smoke.
3. Add source-specific feed adapters and fake/live status tests.
4. Promote Polymarket CLOB status from planned/public read to measured
   freshness, book coverage, and blockers.
5. Add R2 archive batch/manifest/retry design tests before touching production
   retention or prune logic.
6. Start the AWS snapshot consumer path in Blazor with local fallback only for
   development.
7. Begin monitor parity with screenshot-driven QA against `C:\123\monitor-design`.
8. Keep the NativePM/Polytrade donor live services available for read-only
   reference while replacing their concepts with Oloraculo-owned components.
