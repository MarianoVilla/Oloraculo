# Oloraculo Live Path Donor Audit

Date: 2026-06-17

This audit reconciles the existing NativePM/Polytrade live-path services with
the Oloraculo production architecture. It is based on read-only local source
review, read-only AWS/SSM service inventory, and delegated design review.

No secrets were printed or committed. No AWS services were stopped, started,
deleted, armed, or reconfigured.

## Current Decision

Keep NativePM/Polytrade live-path components as donor evidence for now. Do not
decommission them while Oloraculo is still missing its own live router,
authenticated user websocket, order heartbeat, arming state, scalp scanner
promotion path, fill guard, WAL, and reconciliation gates.

Do not run those donor services as the Oloraculo production dependency either.
The future production live path must be Oloraculo-owned, Rust-first, disabled by
default, and covered by Oloraculo tests and release gates.

## Verdict

NativePM already demonstrates several concepts Oloraculo will need:

- resident Rust services;
- live router promotion from scanner/evidence streams;
- private user websocket and order heartbeat freshness;
- open-order snapshot reconciliation;
- WAL and idempotency journals;
- fail-closed live-enable file;
- tiny per-order caps;
- Prometheus/Grafana-style monitor components;
- paper/shadow, evidence, markout, and monitor patterns.

It is not Oloraculo-ready as-is:

- source and release evidence are not owned by this repo;
- services are tied to `/opt/nativepm`, `/var/lib/nativepm`, `/etc/nativepm`,
  and Polytrade-era runtime state;
- the observed live executor unit includes risky bypass flags that would be
  unacceptable as Oloraculo defaults;
- the fill guard concept exists but was not active in the observed service
  state;
- Polytrade arm/scalp/book/sharp-monitor services are root/Python/env-file
  services and should be treated as donor-only, not ported as production shape;
- there is no Oloraculo-owned runbook, test suite, or replay harness proving
  these services satisfy Oloraculo Phase 7 gates.

## Service Inventory

| Service | Observed state | What it proves | Oloraculo decision |
| --- | --- | --- | --- |
| `nativepm-live-router` | Active donor service | Route promotion can be blocked by universe/timing/evidence rules. It has useful concepts for scanner-to-intent streams, heartbeat cadence, depth caps, and blocker accounting. | Keep as donor evidence. Rebuild as `oloraculo_live::router`, accepting only approved, WAL-recorded intents. |
| `nativepm-exec-live` | Enabled but inactive/fail-closed because live-enable file is absent | Resident executor shape, WAL/idempotency, private stream, heartbeat, open-order snapshot, caps, and fail-closed arming are the right families of controls. | Do not arm as Oloraculo. Rebuild with no auto-approval default, no unresolved-inventory bypass default, exact approval artifacts, replay tests, and Oloraculo release gates. |
| `nativepm-user-ws` | Active donor service | Authenticated private/user websocket belongs server-side and feeds reconciliation/fill status, not browser controls. | Rebuild as `oloraculo_live::user_ws`; expose only sanitized freshness and status to Blazor. |
| `nativepm-heartbeat` | Active donor service | Order heartbeat freshness is a first-class live veto. | Rebuild as `oloraculo_live::heartbeat` beside reconciliation. Stale heartbeat must block all routing. |
| `nativepm-open-orders-snapshot` | Periodic donor worker | Open-order REST snapshots are needed to cross-check private stream state. | Rebuild as `oloraculo_live::reconcile` with tests for mismatch, stale snapshot, and unresolved inventory. |
| `nativepm-fill-guard` | Present but disabled/inactive in observed state | A fill guard is mandatory if live orders ever exist. | Required for Phase 7. It must be active and tested before any Oloraculo live gate can arm. |
| `nativepm-paper-shadow` | Active donor service | Shadow lifecycle and promotion/evidence timing are useful before real execution. | Rebuild earlier than live execution as Oloraculo Phase 6 evidence. |
| `nativepm-evidence-daemon` and `nativepm-markout-resolver` | Active/periodic donor services | Candidate evidence and markouts are necessary for proving edge. | Rebuild as Oloraculo evidence pipeline before Phase 7. |
| `nativepm-trader-monitor` | Active local monitor service | Existing monitor/backend patterns may help visual parity and ops status. | Use as a donor pattern only. Oloraculo cockpit remains read-only and consumes Oloraculo snapshots. |
| `nativepm-scanner` | Active donor service | Resident scanner with status cadence and candidate streams is the correct production shape. | Rebuild as `oloraculo-sports-scalp-scanner`; no execution permission. |
| `nativepm-maker-lab` | Active donor service | Lab/evidence workflow exists, but observed manual-allow flags need review. | Donor only. Oloraculo must not inherit missing-approval bypasses. |
| `polytrade-arm` | Active root service | Shows an arming workflow exists in the old runtime. | Do not port as-is. Rebuild arming in Rust with TTL, nonce, caps, kill-switch awareness, and explicit operator approval. |
| `polytrade-scalp` | Active root/Python service | Shows old scalp automation/runtime conventions. | Donor only. Oloraculo scalp service must be Rust-first analysis/scanner, not root Python production. |
| `polytrade-book` | Active root service | Shows book collection shape. | Donor only. Rebuild as Oloraculo CLOB collector/hotpath. |
| `polytrade-sharp-monitor` | Active root/Python service | Shows monitor/reporting ideas. | Donor only. Do not make it an Oloraculo dependency. |

## Required Oloraculo Components

Build these under Oloraculo before any live-order discussion:

- `rust/oloraculo_clob_hotpath`: resident public CLOB/Gamma collector, book
  cache, status, snapshot writer, latency metrics.
- `rust/oloraculo_sports_scalp_scanner`: analysis-only scanner that emits
  candidates and fresh quote intents; it never signs, places, cancels, approves,
  or mutates live-control state.
- `rust/oloraculo_archive`: raw `.ndjson.zst` or `.jsonl.zst` batcher, manifest
  store, retry queue, verified prune, and later Parquet/ZSTD materializers.
- `contracts/`: JSON schemas for feed status, sports scalp snapshots, scalp
  intents, live status, and evidence receipts.
- `Oloraculo.Web/Runtime/Snapshots/`: production snapshot clients with local
  development fallback only.
- `Oloraculo.Web/Live/`: read-only live status models and clients. No order
  buttons or arming controls until Phase 7 is explicitly approved.

Phase 7, only after P0-P6 are green and explicitly approved, should add:

- `rust/oloraculo_live/src/main.rs`: resident supervisor.
- `config.rs`: fail-closed config and secret-presence validation without value
  logging.
- `types.rs`: fresh quote intent, approval draft, approved intent, WAL record,
  and live status types.
- `approval.rs`: exact approval generation and field-for-field validation.
- `wal.rs`: append-before-side-effect, idempotency, and replay.
- `risk.rs`: caps, event allowlist, timing gate, quote freshness, private-stream
  freshness, and stale-data vetoes.
- `router.rs`: the only path from approved intent to venue adapter.
- `venue/polymarket.rs`: the only module allowed to call signing/order/cancel
  endpoints.
- `user_ws.rs`: authenticated private/user websocket state.
- `heartbeat.rs`: order heartbeat and private-stream freshness snapshot.
- `reconcile.rs`: REST/private-stream/open-order reconciliation gate.
- `arming.rs`: fail-closed operator arming with TTL, nonce, caps, evidence
  receipt checks, and kill-switch awareness.
- `kill_switch.rs` and `cancel_all.rs`: fail-closed kill path plus cancel-all
  proof.

## Live Representation Rules

`live-router` should be a Rust module inside `oloraculo_live` first, not a
Blazor service. It routes only approved, WAL-recorded intents.

`order heartbeat` belongs beside reconciliation in Rust. It observes private
stream/open-order freshness and blocks routing when stale or divergent.

`user websocket` means authenticated Polymarket private/user websocket. It must
never be exposed as a browser control path; Blazor may display only sanitized
freshness, status, and blocker counts.

`arming service` is Rust-owned, default `DISARMED`, with TTL, nonce, tiny caps,
event allowlist, kill-switch awareness, P0-P6 evidence receipt, and explicit
operator gate.

`scalp service` remains analysis-only. It emits candidates/intents; it never
signs, places, cancels, approves, or mutates live-control state.

## Required Tests

Add Rust tests before Phase 7 can be considered:

- approval exactness;
- WAL-before-adapter-call;
- idempotency replay;
- stale quote veto;
- private websocket stale veto;
- heartbeat stale veto;
- reconciliation mismatch;
- unresolved inventory veto;
- kill switch;
- cancel-all proof;
- event timing gate;
- tiny caps;
- arming TTL and nonce;
- fail-closed missing config;
- no approval bypasses enabled by default.

Add .NET tests proving:

- production Blazor uses snapshot clients, not direct Gamma/CLOB polling;
- live status rendering is read-only;
- no secret values or token fragments appear in UI or snapshots;
- `TRADE_NOW` remains analysis-only until Phase 7 is explicitly armed.

Add static gates:

- keep `tools/security/check-no-live-order-path.ps1` for today's watch-only
  folders;
- add a future `tools/security/check-live-order-is-gated.ps1` that requires the
  live crate to contain approval, WAL, reconciliation, heartbeat, arming,
  kill-switch, caps, and fail-closed tests.

## Priority Impact

This audit changes the live-path priority from "delete old runtime when unused"
to "hold old runtime as donor/evidence until Oloraculo replacements exist."

Production priority remains unchanged:

1. P0 release boundary and CI evidence.
2. P1 real feed status and CLOB blockers.
3. P2 R2/AWS foundations.
4. P3 monitor parity and snapshot-driven cockpit.
5. P4/P6 evidence, paper/shadow, markouts, fillability, replay.
6. Phase 7 Oloraculo-owned live execution, only after explicit approval.
