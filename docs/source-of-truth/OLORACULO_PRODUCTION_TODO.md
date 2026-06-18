# Oloraculo Production TODO

Date: 2026-06-17

This is the executable TODO list derived from
`docs/source-of-truth/OLORACULO_PRODUCTION_READINESS_PLAN.md`. Use it as the
working board. Do not mark an item complete without the acceptance evidence
listed beside it.

## Active Routing

Current slice: P1 feed status contracts and source-health adapters.

Skills in use:

- `oloraculo-architecture-map`
- `oloraculo-release-gate`
- `oloraculo-security-boundary`
- `oloraculo-mcp-tooling`
- `oloraculo-aws-r2-ops`
- `oloraculo-feed-hotpath`

MCP in use:

- `oloraculo-context` for `route_work`, guardrails, and source-of-truth reads.

Owners:

- Primary: `release-verification-lead`
- Reviewer: `security-risk-sentinel`
- Tooling reviewer: `mcp-toolsmith`
- Donor/live reviewer: `aws-runtime-operator`
- Feed/hotpath reviewer: `feed-status-integrator`

## P0 - Release Boundary

| Status | Item | Why | Acceptance evidence |
| --- | --- | --- | --- |
| [x] | Create production-readiness plan | The repo needed one concrete source of truth for production gaps and priorities | `docs/source-of-truth/OLORACULO_PRODUCTION_READINESS_PLAN.md`; MCP smoke passes |
| [x] | Create executable TODO board | The plan needs a working queue that agents can use without re-auditing | This file exists and is exposed through `oloraculo-context` |
| [x] | Reconcile dirty/untracked worktree | Production evidence must be tracked, reviewable, and reproducible | First-release scope staged; deferred donor/reference folders ignored; `tools/release/check-release-scope.ps1` now fails on ambiguous untracked files |
| [x] | Make health scripts repo-relative and fail on failed checks | CI must run the same checks on Linux that pass locally on Windows | Codex health and release-scope checks pass locally; CI run still pending |
| [x] | Add committed/unignored-file secret scan | P0 requires a repeatable guard against raw tokens, private keys, and wallet material | `tools/security/check-no-raw-secrets.ps1` passes locally; CI run still pending |
| [ ] | Expand CI beyond .NET | Production baseline needs Rust, MCP, tooling health, secret scan, no-order scan, and container smoke | `.github/workflows/dotnet.yml` is patched and local equivalents pass; actual CI run still pending |
| [x] | Add no-order static scan | `WATCH_ONLY` and `NO_ORDER_PATH` must remain guarded while Phase 7 is incomplete | `tools/security/check-no-live-order-path.ps1` passes locally |
| [x] | Add `/healthz` and `/snapshot.json` container smoke | Docker build alone does not prove the runtime endpoint surface starts cleanly | `tools/release/test-container-smoke.ps1` passes locally |
| [x] | Audit NativePM/Polytrade live-path donor services | Future Oloraculo live work needs live router, order heartbeat, user websocket, arming, scalp, paper/shadow, and monitor lessons without inheriting unsafe runtime dependencies | `docs/source-of-truth/OLORACULO_LIVE_PATH_DONOR_AUDIT.md`; no services changed |

Latest local validation:

- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-host-prereqs.ps1` passed after installing GitHub CLI and repairing Docker Desktop.
- `python tools\mcp\test_oloraculo_context_server.py` passed.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1` passed.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1` passed.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\codex\check-oloraculo-codex.ps1` passed.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1` passed with Codex-only tooling requirements and non-Codex mirror paths blocked.
- `cargo test` passed: 12/12 Rust tests.
- `dotnet test Oloraculo.sln` passed: 346/346 .NET tests.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\test-container-smoke.ps1` passed after fixing typed-HTTP-client constructor ambiguity and enforcing secret-safe `/snapshot.json` checks.
- `git diff --check` and `git diff --cached --check` passed after normalizing release-scope whitespace.
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1` passed with required tracked files/prefixes, blocked donor/reference prefixes, and no untracked-file drift.
- `dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter "FullyQualifiedName~Oloraculo.Web.Tests.Feeds.FeedStatusServiceTests|FullyQualifiedName~ObjectArchiveServiceTests.FeedStatus"` passed after freezing the feed-status and `/snapshot.json` schema, adding the shared golden fixture, broadening redaction, and preventing object archive config from claiming measured readiness.
- `cargo test -p oloraculo_hotpath feed_status` passed after aligning Rust feed-status JSON with the canonical schema and adding non-ASCII-safe redaction truncation.
- `.dockerignore` now excludes local tooling/reference/generated material from the image build context; `tools\release\test-container-smoke.ps1` passed with a ~38 KB context instead of multi-GB local state.

Remote CI status:

- GitHub CLI is installed and authenticated.
- `origin` points to `https://github.com/MarianoVilla/Oloraculo`.
- Remote CI cannot verify this workflow patch until the staged release scope is committed and pushed.

## P1 - Data Health

| Status | Item | Why | Acceptance evidence |
| --- | --- | --- | --- |
| [x] | Freeze canonical feed status schema | Consumers need one contract across .NET, Rust, UI, and `/snapshot.json` | `docs/source-of-truth/fixtures/feed_status_snapshot_v1.json`; `Oloraculo.Web.Tests/Feeds/FeedStatusServiceTests.cs`; `rust/oloraculo_hotpath/src/feed_status.rs`; `docs/source-of-truth/FEED_STATUS_CONTRACT.md` |
| [x] | Add fake status adapters | Missing/down/stale/empty/parse-error states must be deterministic in tests | `dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter "FullyQualifiedName~FeedStatus\|FullyQualifiedName~ObjectArchiveHealth\|FullyQualifiedName~PolymarketClobStatus"` passed locally: 78/78 |
| [x] | Implement honest source status | Config presence is not source readiness | Databet/widgets/OddsPapi/GRID/CLOB/archive adapters report measured, planned, missing, denied, stale, empty, parse-error, or not-implemented status; SofaScore remains deprecated |
| [x] | Add CLOB status blockers | Scanner readiness depends on fresh, uncrossed, fillable books | `PolymarketClobStatusEvaluatorTests` covers stale, fetch-failed, no-token, no-book, no-bid, no-ask, crossed, thin-depth, and ready cases |
| [ ] | Prove real configured source status in AWS/local live smoke | Fakeable adapters prove contracts, not vendor entitlement or runtime reachability | Background probes run with configured secrets in isolated runtime; sanitized `/snapshot.json` shows measured source rows without raw secret leakage |

## P2 - AWS And R2 Foundations

| Status | Item | Why | Acceptance evidence |
| --- | --- | --- | --- |
| [ ] | Implement raw R2 batcher and manifest store | Uploading isolated objects is not a production archive lake | `.ndjson.zst`/`.jsonl.zst` batch tests and manifest row tests |
| [ ] | Add retry and no-prune-before-verify | Archive failure must not destroy local hot cache before R2 verification | Failed upload/hash mismatch tests block prune |
| [ ] | Add AWS SSM deploy/runbooks | Production ops must not rely on manual laptop state or SSH habits | SSM install/restart/log/health/rollback scripts and smoke evidence |
| [ ] | Add disk/inode/hot-cache guards | Local disk is hot cache only | Guard tests plus operator status surface |

## P3 - Cockpit

| Status | Item | Why | Acceptance evidence |
| --- | --- | --- | --- |
| [ ] | Add AWS snapshot source with dev fallback | Production UI must consume Oloraculo-owned runtime data | Tests prove production mode does not call Gamma/CLOB directly |
| [ ] | Match or improve `C:\123\monitor-design` | The monitor must be visually trustworthy and data-honest | Playwright desktop/mobile screenshots and visual review |
| [ ] | Add grouped market board and drilldown | Operators need blockers, hedge grid, ladder capacity, and failure plan in one place | Component tests and screenshots |
| [ ] | Add position calculator | Screenshot/operator positions need exact equalization and outcome PnL | Unit tests for partial hedge and equalization math |

## P4 - Evidence

| Status | Item | Why | Acceptance evidence |
| --- | --- | --- | --- |
| [ ] | Add paper/shadow candidate lifecycle | Need evidence before any execution path | Shadow ledger tests and reports |
| [ ] | Add markouts | Need to know what happened after each candidate | 1s/5s/10s/30s/60s markout reports |
| [ ] | Add fillability denominator | Visible depth is not proof of actual fills | Queue/partial-fill metrics in reports |
| [ ] | Add realized hedge replay | Hedge targets must be tested against actual book paths | Replay fixtures and PnL/EV/hour output |

## P5 - Live Execution

| Status | Item | Why | Acceptance evidence |
| --- | --- | --- | --- |
| [ ] | Keep Phase 7 under veto | Live trading has real financial side effects | No live order path until explicit approval and P0-P4 evidence |
| [ ] | Preserve donor live services until Oloraculo replacements exist | Existing live router, heartbeat, user websocket, open-order snapshot, arming/scalp, paper/shadow, and monitor components may contain necessary operational lessons | No decommissioning unless replacement path and rollback plan are documented |
| [ ] | Create Oloraculo live crate design before implementation | Live router/order heartbeat/user websocket/arming need one Rust-owned boundary, not Blazor services or inherited Polytrade root/Python services | Design doc and tests for approval, WAL, reconciliation, heartbeat, arming, kill switch, caps, and fail-closed config |
| [ ] | Build exact approval/WAL/reconciliation gates only after approval | Execution needs a separate release boundary | Separate live-readiness suite and operator arming checklist |
