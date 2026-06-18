# Oloraculo Feed Status Contract

Date: 2026-06-18

This is the canonical sanitized feed/archive status contract shared by the
.NET app, Rust hotpath primitives, `/snapshot.json`, and cockpit UI.

## Status Envelope

`/snapshot.json` exposes a read-only status envelope:

- `schema_version`: currently `1`.
- `generated_at_utc`: UTC timestamp for the response.
- `mode`: `READ_ONLY_STATUS_ONLY`.
- `archive`: sanitized object archive readiness.
- `feeds`: canonical feed status snapshot.

The endpoint is status-only. It is not live-order authority and does not expose
private keys, signed orders, wallet material, raw tokens, or signed URLs.

## Feed Snapshot

The feed snapshot fields are:

- `schema_version`: currently `1`.
- `as_of_utc`: UTC timestamp used for freshness calculations.
- `generated_at_utc`: alias of `as_of_utc`.
- `mode`: `SANITIZED_STATUS_ONLY`.
- `rows`: list of feed rows.
- readiness counts: `ready_count`, `planned_count`,
  `missing_config_count`, `not_implemented_count`, `blocked_count`.

## Feed Row

Every row must include:

- `source_id`: stable machine id such as `databet_sportsbook`,
  `databet_widgets`, `oddspapi_pinnacle`, `grid`, `polymarket_clob`,
  or `object_archive`.
- `source`: human-readable source name.
- `role`: what the source contributes.
- `readiness` and `state`: one of `READY`, `PLANNED`, `CONFIG_MISSING`,
  `NOT_IMPLEMENTED`, or `BLOCKED`.
- `present`: measured data is currently present. Credential/config presence
  alone must not set this to `true`.
- `auth_present`: `true`, `false`, or `null` when auth is not required or not
  knowable.
- `config_present`: required config is available.
- `latest_recv_ts_utc`: latest received data time.
- `age_ms`: freshness age relative to the snapshot timestamp.
- `rows_last_minute`: recent throughput.
- `join_coverage`: source join or mapping coverage when applicable.
- `last_error_redacted`: safe, redacted error text.
- `blocker`: first blocker for compact UI.
- `blockers`: full blocker list.
- `detail`: safe operational detail.
- `secret_policy`: display rule, usually `PRESENCE_ONLY_NO_VALUES`.

## State Mapping

Deterministic adapter states map as follows:

| Adapter state | Readiness | Present | Blocker |
| --- | --- | --- | --- |
| `MissingConfig` | `CONFIG_MISSING` | `false` | `AUTH_CONFIG_MISSING` |
| `Down` | `BLOCKED` | `false` | `SOURCE_DOWN` |
| `Stale` | `BLOCKED` | `true` | `STALE_SOURCE` |
| `Empty` | `BLOCKED` | `true` | `EMPTY_SOURCE` |
| `ParseError` | `BLOCKED` | `true` | `PARSE_ERROR` |
| `Blocked` | `BLOCKED` | `true` | `SOURCE_BLOCKED` |
| `Planned` | `PLANNED` | `false` | `COLLECTOR_NOT_ENABLED` |
| `NotImplemented` | `NOT_IMPLEMENTED` | `false` | `NOT_IMPLEMENTED` |
| `Ready` with measured timestamp and rows/backlog count | `READY` | `true` | none |
| `Ready` without measured timestamp or rows/backlog count | `BLOCKED` | `false` | `MEASURED_DATA_MISSING` |

Source-specific adapters may override the default blocker when a more precise
safe reason is known. Current source-specific blockers include:

- Databet sportsbook/widgets: `AUTH_CONFIG_MISSING`, `COLLECTOR_NOT_ENABLED`,
  `ENTITLEMENT_DENIED`, `SOURCE_DOWN`, `EMPTY_SOURCE`, `PARSE_ERROR`.
- OddsPapi/Pinnacle: `AUTH_CONFIG_MISSING`, `COLLECTOR_NOT_ENABLED`,
  `ENTITLEMENT_DENIED`, `SOURCE_DOWN`, `EMPTY_SOURCE`,
  `PINNACLE_COVERAGE_MISSING`, `PARSE_ERROR`.
- GRID: `AUTH_CONFIG_MISSING`, `GRID_PROBE_NOT_IMPLEMENTED`,
  `COLLECTOR_NOT_ENABLED`, `ENTITLEMENT_DENIED`, `SOURCE_DOWN`,
  `EMPTY_SOURCE`, `PARSE_ERROR`.
- Polymarket CLOB: `LIVE_COLLECTOR_PENDING`, `NO_CLOB_TOKENS`,
  `CLOB_FETCH_FAILED`, `STALE_CLOB`, `NO_ORDER_BOOK`, `NO_BID`, `NO_ASK`,
  `CROSSED_BOOK`, `INSUFFICIENT_DEPTH`.
- Object archive: `OBJECT_ARCHIVE_CONFIG_INCOMPLETE`, `ARCHIVER_DISABLED`,
  `ARCHIVER_HEALTH_UNVERIFIED`, `ARCHIVE_LIST_DENIED`, `ARCHIVE_BACKLOG`,
  `ARCHIVE_STALE`.
- Central status mapper: `MEASURED_DATA_MISSING` when an adapter claims ready
  without enough measured freshness evidence.

## Active Sources

Current active/planned status rows are Databet sportsbook, Databet widgets,
OddsPapi/Pinnacle, GRID, Polymarket CLOB, and object archive.

SofaScore is not an active Oloraculo feed. Do not add SofaScore credentials or
rows unless a future source decision explicitly reactivates it.

## Probe Execution

`/snapshot.json` and Blazor screens read cached/bounded status only. They do
not run live orders, approvals, cancels, signing, redemption, relayer submit,
or private user operations.

Background probes are optional and read-only. They are enabled only through
`Oloraculo:FeedStatus:EnableBackgroundProbes=true`, run source adapters with
`AllowNetwork=true`, redact failures, and write sanitized reports into the
in-memory health store. Inline snapshot reads call adapters with
`AllowNetwork=false`, so external HTTP status comes from cached probe results
or honest planned/config rows.

The object archive feed row must not set `present=true` or `READY` from
bucket/key presence alone. `READY` requires a measured recent manifest health
snapshot with acceptable local backlog.

## Verification

Current contract evidence:

- `.NET`: `Oloraculo.Web.Tests/Feeds/*FeedStatus*Tests.cs`,
  `Oloraculo.Web.Tests/Archive/ObjectArchiveHealthProbeTests.cs`,
  `Oloraculo.Web.Tests/ComboLab/PolymarketClobStatusEvaluatorTests.cs`
- Rust: `rust/oloraculo_hotpath/src/feed_status.rs`
- `/snapshot.json` envelope: `RuntimeStatusSnapshot`
- Shared fixture: `docs/source-of-truth/fixtures/feed_status_snapshot_v1.json`
