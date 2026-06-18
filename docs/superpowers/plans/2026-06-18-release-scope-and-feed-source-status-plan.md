# Release Scope And Feed Source Status Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Oloraculo production layer reviewable from Git, then replace config-only feed status with source-specific, fakeable, measured status for Databet, OddsPapi/Pinnacle, GRID, Polymarket CLOB, and the object archive while keeping the runtime read-only and secret-safe.

**Architecture:** Treat release hygiene as the first gate: classify every dirty or untracked file as tracked production evidence, ignored/generated local state, or deferred reference material before claiming readiness. Keep the public feed JSON schema stable. Add a feed adapter seam where each source emits a sanitized `FeedAdapterReport`; `/snapshot.json` and Blazor render only cached or bounded status reports and never perform order placement, cancellation, approval, signing, redemption, or live control. Real probes are read-only, timeout-bounded, redacted, and disabled unless configured.

**Tech Stack:** .NET 9 Blazor Server, xUnit, `HttpClient` fake handlers, Rust hotpath tests, PowerShell release/security scripts, Docker local smoke, source-of-truth Markdown docs, existing Oloraculo Codex skills and MCP context.

---

## Non-Negotiable Boundaries

- [ ] Work only inside `C:\Users\gianz\prediction\Oloraculo`.
- [ ] Do not read, print, commit, or summarize real secret values. Presence checks are allowed.
- [ ] Do not add live order placement, cancel, approval, signing, redemption, relayer submit, or heartbeat.
- [ ] Keep SofaScore deprecated. Active feed rows are Databet sportsbook, Databet widgets, OddsPapi/Pinnacle, GRID, Polymarket CLOB, and object archive.
- [ ] Keep `/snapshot.json` read-only and sanitized.
- [ ] Use `apply_patch` for manual file edits.
- [ ] Do not revert unrelated dirty files.

## Findings Folded Into This Plan

- The repo is broadly dirty: 44 tracked modified files and more than 500 untracked files.
- `oloraculo-dev.out.log` is tracked and huge; it should not be part of release evidence.
- `.gitignore` currently ignores `tools\release\*.ps1` through the broad `[Rr]elease/` rule.
- `Oloraculo.Web/Feeds/FeedStatusModels.cs` already has a good canonical row schema and adapter-state mapping.
- `Oloraculo.Web/Feeds/FeedStatusService.cs` still hardcodes config/planned rows and does not compose source-specific adapters.
- `RuntimeStatusSnapshot` and `/snapshot.json` are schema-ready, but not source-health ready.
- Object archive readiness is config/readiness only; it must not become `READY` until a measured archive health probe exists.
- Existing Polymarket API endpoint work should follow `docs/superpowers/plans/2026-06-18-polymarket-api-best-practices-plan.md`.

## File Structure

Create:

- `docs/source-of-truth/RELEASE_SCOPE_AUDIT.md`
- `tools/release/check-release-scope.ps1`
- `Oloraculo.Web/Feeds/FeedStatusOptions.cs`
- `Oloraculo.Web/Feeds/FeedStatusProbeContext.cs`
- `Oloraculo.Web/Feeds/IFeedStatusAdapter.cs`
- `Oloraculo.Web/Feeds/FeedStatusSourceCatalog.cs`
- `Oloraculo.Web/Feeds/FeedStatusHealthStore.cs`
- `Oloraculo.Web/Feeds/FeedStatusProbeWorker.cs`
- `Oloraculo.Web/Feeds/Adapters/DatabetSportsbookFeedStatusAdapter.cs`
- `Oloraculo.Web/Feeds/Adapters/DatabetWidgetsFeedStatusAdapter.cs`
- `Oloraculo.Web/Feeds/Adapters/OddsPapiPinnacleFeedStatusAdapter.cs`
- `Oloraculo.Web/Feeds/Adapters/GridFeedStatusAdapter.cs`
- `Oloraculo.Web/Feeds/Adapters/PolymarketClobFeedStatusAdapter.cs`
- `Oloraculo.Web/Feeds/Adapters/ObjectArchiveFeedStatusAdapter.cs`
- `Oloraculo.Web/ComboLab/Scalp/PolymarketClobStatusEvaluator.cs`
- `Oloraculo.Web/Archive/IObjectArchiveHealthProbe.cs`
- `Oloraculo.Web.Tests/Feeds/FeedStatusSourceCatalogTests.cs`
- `Oloraculo.Web.Tests/Feeds/FeedStatusAdapterCompositionTests.cs`
- `Oloraculo.Web.Tests/Feeds/FeedStatusProbeWorkerTests.cs`
- `Oloraculo.Web.Tests/Feeds/DatabetSportsbookFeedStatusAdapterTests.cs`
- `Oloraculo.Web.Tests/Feeds/DatabetWidgetsFeedStatusAdapterTests.cs`
- `Oloraculo.Web.Tests/Feeds/OddsPapiPinnacleFeedStatusAdapterTests.cs`
- `Oloraculo.Web.Tests/Feeds/GridFeedStatusAdapterTests.cs`
- `Oloraculo.Web.Tests/Feeds/PolymarketClobFeedStatusAdapterTests.cs`
- `Oloraculo.Web.Tests/ComboLab/PolymarketClobStatusEvaluatorTests.cs`
- `Oloraculo.Web.Tests/Archive/ObjectArchiveHealthProbeTests.cs`

Modify:

- `.gitignore`
- `docs/source-of-truth/FEED_STATUS_CONTRACT.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md`
- `tools/mcp/oloraculo_context_server.py`
- `Oloraculo.Web/Feeds/FeedStatusModels.cs`
- `Oloraculo.Web/Feeds/FeedStatusService.cs`
- `Oloraculo.Web/Program.cs`
- `Oloraculo.Web/OloraculoConfig.cs`
- `Oloraculo.Web.Tests/Feeds/FeedStatusServiceTests.cs`
- `Oloraculo.Web.Tests/Archive/ObjectArchiveServiceTests.cs`
- `tools/release/test-container-smoke.ps1`
- `rust/oloraculo_hotpath/src/feed_status.rs` only if the public JSON fixture changes.
- `docs/source-of-truth/fixtures/feed_status_snapshot_v1.json` only if the public JSON fixture changes.

## Agent Split

- [ ] Release worker owns `.gitignore`, `RELEASE_SCOPE_AUDIT.md`, `tools/release/check-release-scope.ps1`, and release docs.
- [ ] Feed-seam worker owns `Oloraculo.Web/Feeds/*`, source catalog tests, service composition tests, and DI wiring.
- [ ] Source-adapter worker owns `Oloraculo.Web/Feeds/Adapters/*` and the five source adapter test files.
- [ ] CLOB worker owns `PolymarketClobStatusEvaluator`, CLOB status adapter tests, and integration with the existing Polymarket API plan.
- [ ] Archive worker owns `IObjectArchiveHealthProbe`, `ObjectArchiveFeedStatusAdapter`, and archive health tests.
- [ ] Verification worker owns full gate execution, Docker smoke, security scans, MCP smoke, Rust tests, and status reporting.

## Task 1: Capture The Release Scope Baseline

- [ ] Run:

```powershell
git status --short --branch --untracked-files=all
git diff --stat
git check-ignore -v tools\release\check-host-prereqs.ps1 tools\release\test-container-smoke.ps1 oloraculo-dev.out.log
```

- [ ] Create `docs/source-of-truth/RELEASE_SCOPE_AUDIT.md` with these exact sections:
  - `Current State`
  - `Track For Production`
  - `Track After Secret-Safe Review`
  - `Ignore Or Remove From Release Scope`
  - `Deferred Reference Material`
  - `Nested Repo Decision`
  - `Verification Commands`
- [ ] In `Track For Production`, include:
  - `AGENTS.md`
  - `.agents/skills`
  - `.github/workflows/dotnet.yml`
  - `.dockerignore`
  - `.editorconfig`
  - `.mcp.json.example`
  - `Dockerfile`
  - `deploy/aws`
  - `docs/source-of-truth`
  - `docs/llms.txt`
  - `design.md`
  - `Oloraculo.Web/Archive`
  - `Oloraculo.Web/ComboLab`
  - `Oloraculo.Web/Feeds`
  - `Oloraculo.Web/WorldCup`
  - `Oloraculo.Web.Tests/Archive`
  - `Oloraculo.Web.Tests/ComboLab`
  - `Oloraculo.Web.Tests/Feeds`
  - `Oloraculo.Web.Tests/WorldCup`
  - `Cargo.toml`
  - `Cargo.lock`
  - `rust/oloraculo_hotpath`
  - `tools/codex`
  - `tools/docs`
  - `tools/mcp`
  - `tools/opencode`
  - `tools/release`
  - `tools/security`
- [ ] In `Track After Secret-Safe Review`, include:
  - `Oloraculo.Web/appsettings.Production.json`
  - `deploy/aws/oloraculo.aws.env.example`
  - every `*.env.example`
- [ ] In `Ignore Or Remove From Release Scope`, include:
  - `oloraculo-dev.out.log`
  - `.serena/`
  - `.pytest_cache/`
  - `**/__pycache__/`
  - `polytrade-agent/.venv/`
  - `polytrade-agent/audit-output.md`
  - local runtime databases and hot-cache files already covered by `.gitignore`
- [ ] In `Deferred Reference Material`, include:
  - `polytrade-agent/README1.MD`
  - `polytrade-agent/task1.md`
  - `docs/reference/c123`
  - `docs/vendor/polymarket-docs`
  - `tools/polyfill-rs`
- [ ] State that deferred reference material can remain in the workspace but must be split from the first production release unless deliberately reviewed and tracked.

## Task 2: Fix Ignore Hygiene

- [ ] Patch `.gitignore` so the release directory rule is root-scoped:

```gitignore
/[Rr]elease/
/[Rr]eleases/
```

- [ ] Add log ignores:

```gitignore
*.log
*.out.log
*.err.log
```

- [ ] Add local tool/cache ignores:

```gitignore
.serena/
.pytest_cache/
**/__pycache__/
polytrade-agent/.venv/
polytrade-agent/audit-output.md
```

- [ ] Verify `tools/release` is no longer ignored:

```powershell
git check-ignore -v tools\release\check-host-prereqs.ps1 tools\release\test-container-smoke.ps1
```

Expected result: no output.

- [ ] Verify the local log is ignored once untracked:

```powershell
git check-ignore -v oloraculo-dev.out.log
```

Expected result: the new log rule is reported.

## Task 3: Remove The Tracked Runtime Log From Release Scope

- [ ] Confirm the file is tracked:

```powershell
git ls-files -- oloraculo-dev.out.log
git status --short -- oloraculo-dev.out.log
```

- [ ] Remove it from the Git index while preserving the local file:

```powershell
git rm --cached -- oloraculo-dev.out.log
```

- [ ] Verify:

```powershell
git status --short -- oloraculo-dev.out.log
git check-ignore -v oloraculo-dev.out.log
```

Expected result: the file is not a modified tracked file, and the ignore rule applies.

## Task 4: Add A Release Scope Guard Script

- [ ] Create `tools/release/check-release-scope.ps1`.
- [ ] The script must fail when:
  - `tools/release/check-host-prereqs.ps1` is ignored.
  - `tools/release/test-container-smoke.ps1` is ignored.
  - any tracked file ends with `.env`, `.out.log`, `.err.log`, or `.log`.
  - any tracked path starts with `.serena/`, `.pytest_cache/`, or `polytrade-agent/.venv/`.
  - `.env` or `pmkey.txt` is tracked.
- [ ] The script must print `release scope check passed` on success.
- [ ] Add the script to `tools/codex/check-oloraculo-codex.ps1` and `tools/opencode/check-oloraculo-opencode.ps1` after existing secret/no-order scans.
- [ ] Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\codex\check-oloraculo-codex.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\opencode\check-oloraculo-opencode.ps1
```

Expected result: all pass after Tasks 2 and 3.

## Task 5: Track The Production Layer Intentionally

- [ ] Use `git status --short --untracked-files=all` and `RELEASE_SCOPE_AUDIT.md` to stage files by category, not as one blob.
- [ ] First release batch: app, tests, source-of-truth docs, release scripts, Docker, AWS docs, Rust hotpath.
- [ ] Second optional batch: local agent/skill/tooling configs after secret-safe review.
- [ ] Deferred batch: `polytrade-agent`, `docs/reference/c123`, `docs/vendor/polymarket-docs`, and `tools/polyfill-rs`.
- [ ] Decide `tools/polyfill-rs` explicitly:
  - Track as vendored source only if the nested `.git` directory is removed and license/provenance is documented.
  - Track as submodule only if the remote is stable and intentional.
  - Ignore if it remains a local donor/reference checkout.
- [ ] Run before any commit:

```powershell
git diff --check
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1
```

## Task 6: Add Feed Status Options And Source Catalog

- [ ] Add `FeedStatusOptions` with defaults:

```csharp
public sealed class FeedStatusOptions
{
    public bool EnableBackgroundProbes { get; set; } = false;
    public bool EnableInlineNetworkProbes { get; set; } = false;
    public int ProbeIntervalSeconds { get; set; } = 15;
    public int ProbeTimeoutMilliseconds { get; set; } = 2500;
    public int DefaultStaleAfterSeconds { get; set; } = 30;
    public Dictionary<string, int> StaleAfterSecondsBySource { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
```

- [ ] Add `FeedStatusSourceDefinition` and `FeedStatusSourceCatalog`.
- [ ] The catalog must return exactly these source IDs in this order:
  - `databet_sportsbook`
  - `databet_widgets`
  - `oddspapi_pinnacle`
  - `grid`
  - `polymarket_clob`
  - `object_archive`
- [ ] Add `FeedStatusSourceCatalogTests`:
  - asserts exactly six IDs in order.
  - asserts no source contains `Sofa`.
  - asserts roles match `docs/source-of-truth/FEED_STATUS_CONTRACT.md`.
- [ ] Run:

```powershell
dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter FeedStatusSourceCatalogTests
```

Expected result after implementation: pass.

## Task 7: Add The Adapter Seam Without Changing Public JSON

- [ ] Extend `FeedAdapterReport` with optional source-specific blockers:

```csharp
IReadOnlyList<string>? Blockers = null
```

- [ ] Update `FeedStatusRow.FromAdapter` so `report.Blockers` overrides the generic blocker mapping only after redaction and null cleanup.
- [ ] Add `FeedStatusProbeContext`:

```csharp
public sealed record FeedStatusProbeContext(
    DateTimeOffset AsOfUtc,
    TimeSpan StaleAfter,
    bool AllowNetwork);
```

- [ ] Add `IFeedStatusAdapter`:

```csharp
public interface IFeedStatusAdapter
{
    string SourceId { get; }
    FeedAdapterReport Probe(FeedStatusProbeContext context);
}
```

- [ ] Add `IFeedStatusHealthStore` and `InMemoryFeedStatusHealthStore`:

```csharp
public interface IFeedStatusHealthStore
{
    bool TryGet(string sourceId, out FeedAdapterReport report);
    void Upsert(FeedAdapterReport report);
}
```

- [ ] Add tests in `FeedStatusAdapterCompositionTests`:
  - fake ready adapter for `polymarket_clob` produces `READY`, `present=true`, no blockers.
  - fake parse-error adapter redacts error and keeps custom blocker `CLOB_PARSE_ERROR`.
  - missing adapter falls back to the honest planned/config row.
  - source order remains catalog order.
- [ ] Keep `FeedStatusService.Snapshot()` available for existing Blazor calls.
- [ ] Do not change `docs/source-of-truth/fixtures/feed_status_snapshot_v1.json` unless public serialized fields change.

## Task 8: Compose Adapters In FeedStatusService

- [ ] Modify `FeedStatusService` constructor to accept:

```csharp
IEnumerable<IFeedStatusAdapter> adapters
IOptions<FeedStatusOptions> feedStatusOptions
```

- [ ] Preserve existing test constructors by forwarding to empty adapter lists and default options.
- [ ] `Snapshot()` must:
  - get `asOf` from `_clock`.
  - iterate `FeedStatusSourceCatalog.All`.
  - use an adapter report when one exists.
  - otherwise use existing fallback logic for secret-backed, public planned, and object archive rows.
  - use per-source stale thresholds from `FeedStatusOptions`.
- [ ] Register options and adapters in `Program.cs`:

```csharp
builder.Services.Configure<FeedStatusOptions>(builder.Configuration.GetSection("Oloraculo:FeedStatus"));
builder.Services.AddSingleton<IFeedStatusHealthStore, InMemoryFeedStatusHealthStore>();
builder.Services.AddSingleton<IFeedStatusAdapter, DatabetSportsbookFeedStatusAdapter>();
builder.Services.AddSingleton<IFeedStatusAdapter, DatabetWidgetsFeedStatusAdapter>();
builder.Services.AddSingleton<IFeedStatusAdapter, OddsPapiPinnacleFeedStatusAdapter>();
builder.Services.AddSingleton<IFeedStatusAdapter, GridFeedStatusAdapter>();
builder.Services.AddSingleton<IFeedStatusAdapter, PolymarketClobFeedStatusAdapter>();
builder.Services.AddSingleton<IFeedStatusAdapter, ObjectArchiveFeedStatusAdapter>();
```

- [ ] Only register `FeedStatusProbeWorker` as hosted service when `EnableBackgroundProbes=true`.
- [ ] Run:

```powershell
dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter "FullyQualifiedName~FeedStatusServiceTests|FullyQualifiedName~FeedStatusAdapterCompositionTests"
```

## Task 9: Add Background Probe Worker

- [ ] Add `FeedStatusProbeWorker` as a `BackgroundService`.
- [ ] The worker must:
  - skip network work when `EnableBackgroundProbes=false`.
  - run each adapter with `AllowNetwork=true` only inside the worker.
  - catch exceptions and upsert a `Down` report with redacted error text.
  - use `ProbeTimeoutMilliseconds`.
  - delay by `ProbeIntervalSeconds`.
- [ ] Add `FeedStatusProbeWorkerTests`:
  - disabled option performs no probes.
  - enabled option writes a fake ready report into the store.
  - throwing adapter becomes `BLOCKED` with redacted error.
  - timeout becomes `SOURCE_DOWN` or `PROBE_TIMEOUT`.
- [ ] Keep `/snapshot.json` and Blazor reads synchronous and cache-backed for performance.

## Task 10: Implement Databet Sportsbook Status Adapter

- [ ] Use local docs first:
  - `docs/reference/c123/vendor/venues-docs/sportsbook_gql_api.md`
  - `docs/reference/c123/vendor/databet.md`
  - `docs/reference/c123/vendor/source-auth-notes.md`
- [ ] Add adapter config to `OloraculoConfig` or a nested source options record:
  - `DatabetSportsbookBaseUrl`
  - `DatabetSportsbookAuthEnvironmentVariable`, default `SPORTSBOOK_XAUTH`
  - `DatabetSportsbookHealthQuery`
- [ ] Adapter behavior:
  - missing auth -> `MissingConfig`, blocker `AUTH_CONFIG_MISSING`.
  - `AllowNetwork=false` and no cached store row -> `Planned`, blocker `COLLECTOR_NOT_ENABLED`.
  - HTTP 401 or 403 -> `Down`, blocker `ENTITLEMENT_DENIED`.
  - non-success HTTP -> `Down`, blocker `SOURCE_DOWN`.
  - success with zero events -> `Empty`, blocker `EMPTY_SOURCE`.
  - malformed JSON -> `ParseError`, blocker `PARSE_ERROR`.
  - success with events -> `Ready`, rows count set, latest receive set to probe completion time.
- [ ] Add fake HTTP tests for every behavior above.
- [ ] Assert test output never contains auth header values.

## Task 11: Implement Databet Widgets Status Adapter

- [ ] Use local docs first:
  - `docs/reference/c123/vendor/venues-docs/databet_widgets_api.md`
  - `docs/reference/c123/vendor/venues-docs/DATABET_SPORT_REFERENCE.md`
- [ ] Add adapter config:
  - `DatabetWidgetsBaseUrl`
  - `DatabetWidgetsTokenEnvironmentVariable`, default `DATABET_WIDGET_TOKEN`
  - `DatabetWidgetsHealthQuery`
- [ ] Adapter behavior:
  - missing token -> `MissingConfig`.
  - network disabled with no cached row -> `Planned`.
  - HTTP 401 or 403 -> `Down`, blocker `ENTITLEMENT_DENIED`.
  - success with zero widgets/snapshots -> `Empty`.
  - malformed JSON -> `ParseError`.
  - success with widget snapshots -> `Ready`.
- [ ] Add fake HTTP tests for every behavior above.
- [ ] Do not display per-session token values.

## Task 12: Implement OddsPapi/Pinnacle Status Adapter

- [ ] Use local docs first:
  - `docs/reference/c123/vendor/oddspapi.md`
  - `docs/reference/c123/vendor/oddspapi-capability-audit.md`
  - `docs/reference/c123/vendor/venues-docs/oddspapi.md`
- [ ] Add adapter config:
  - `OddsPapiBaseUrl`
  - `OddsPapiKeyEnvironmentVariable`, default `ODDSPAPI_KEY`
  - `OddsPapiBookmaker`, default `pinnacle`
- [ ] Adapter behavior:
  - missing key -> `MissingConfig`.
  - network disabled with no cached row -> `Planned`.
  - HTTP 401 or 403 -> `Down`, blocker `ENTITLEMENT_DENIED`.
  - response has no events -> `Empty`.
  - response has events but no Pinnacle bookmaker rows -> `Blocked`, blocker `PINNACLE_COVERAGE_MISSING`.
  - malformed JSON -> `ParseError`.
  - response has Pinnacle rows -> `Ready` with `JoinCoverage`.
- [ ] Add fake HTTP tests for every behavior above.
- [ ] Assert key values are absent from redacted errors and serialized status.

## Task 13: Implement GRID Status Adapter

- [ ] Search local docs before implementation:

```powershell
rg -n "GRID|grid" docs Oloraculo.Web tools
```

- [ ] If no stable local endpoint/options exist, implement the adapter as honest `NotImplemented` unless a configured endpoint is present.
- [ ] Add adapter config:
  - `GridBaseUrl`
  - `GridKeyEnvironmentVariable`, default `GRID_KEY`
  - `GridHealthPath`
- [ ] Adapter behavior:
  - missing key -> `MissingConfig`.
  - key present but no endpoint -> `NotImplemented`, blocker `GRID_PROBE_NOT_IMPLEMENTED`.
  - HTTP 401 or 403 -> `Down`, blocker `ENTITLEMENT_DENIED`.
  - success with zero events -> `Empty`.
  - malformed JSON -> `ParseError`.
  - success with event/telemetry count -> `Ready`.
- [ ] Add fake HTTP tests for each branch.

## Task 14: Implement Polymarket CLOB Status Evaluator

- [ ] Complete or reuse Tasks 1 to 3 from `docs/superpowers/plans/2026-06-18-polymarket-api-best-practices-plan.md` so batch CLOB book reads use documented public endpoints.
- [ ] Add `PolymarketClobStatusEvaluator`.
- [ ] Input model must include:
  - token ID.
  - received timestamp.
  - bid levels.
  - ask levels.
  - best bid.
  - best ask.
  - total ask depth near target.
  - total bid depth near target.
- [ ] Evaluator outputs `FeedAdapterReport`.
- [ ] Add tests for:
  - no sampled tokens -> `Empty`, blocker `NO_CLOB_TOKENS`.
  - fetch failure -> `Down`, blocker `CLOB_FETCH_FAILED`.
  - stale receive timestamp -> `Stale`, blocker `STALE_CLOB`.
  - no book -> `Empty`, blocker `NO_ORDER_BOOK`.
  - no bid -> `Blocked`, blocker `NO_BID`.
  - no ask -> `Blocked`, blocker `NO_ASK`.
  - crossed book -> `Blocked`, blocker `CROSSED_BOOK`.
  - thin depth -> `Blocked`, blocker `INSUFFICIENT_DEPTH`.
  - valid fresh depth -> `Ready`.
- [ ] Never call order, cancel, approval, signing, or authenticated user endpoints in these tests or adapter code.

## Task 15: Implement Polymarket CLOB Status Adapter

- [ ] Adapter behavior:
  - uses only public CLOB book data or cached scanner/hotpath status.
  - `AllowNetwork=false` and no cached row -> `Planned`, blocker `LIVE_COLLECTOR_PENDING`.
  - public CLOB unavailable -> `Down`, blocker `CLOB_FETCH_FAILED`.
  - evaluator blockers map through `FeedAdapterReport.Blockers`.
  - ready status includes latest receive timestamp and token/book counts.
- [ ] Register the adapter as `polymarket_clob`.
- [ ] Add tests proving `/snapshot.json` status for `polymarket_clob` can become `READY` from fake fresh books without any private credential.

## Task 16: Add Object Archive Health Probe And Adapter

- [ ] Add `IObjectArchiveHealthProbe`:

```csharp
public interface IObjectArchiveHealthProbe
{
    ObjectArchiveHealthSnapshot Probe(DateTimeOffset asOfUtc);
}
```

- [ ] Add `ObjectArchiveHealthSnapshot` with:
  - `Configured`
  - `Enabled`
  - `LastVerifiedManifestUtc`
  - `PendingLocalBatchCount`
  - `LastError`
  - `Provider`
- [ ] Default probe returns unverified status, not ready.
- [ ] `ObjectArchiveFeedStatusAdapter` behavior:
  - incomplete config -> `MissingConfig`, blocker `OBJECT_ARCHIVE_CONFIG_INCOMPLETE`.
  - disabled -> `Planned`, blocker `ARCHIVER_DISABLED`.
  - configured but no measured health -> `Planned`, blocker `ARCHIVER_HEALTH_UNVERIFIED`.
  - list/head denied -> `Down`, blocker `ARCHIVE_LIST_DENIED`.
  - pending backlog above threshold -> `Blocked`, blocker `ARCHIVE_BACKLOG`.
  - stale last manifest -> `Stale`, blocker `ARCHIVE_STALE`.
  - recent verified manifest and acceptable backlog -> `Ready`.
- [ ] Add tests in `ObjectArchiveHealthProbeTests`.
- [ ] Keep existing `ObjectArchiveServiceTests` expectation that config alone does not produce `READY`.

## Task 17: Update Runtime And Container Smoke

- [ ] Update `tools/release/test-container-smoke.ps1` to assert `/snapshot.json` contains:
  - `schema_version = 1`
  - `mode = READ_ONLY_STATUS_ONLY`
  - all six source IDs.
  - no `Sofa`.
  - no private key pattern.
  - no bearer token.
  - no `order` route affordance.
- [ ] Add a focused test or smoke assertion that `object_archive.present` remains false unless the archive health probe reports measured success.
- [ ] Do not require live external credentials in container smoke.

## Task 18: Update Docs And MCP Context

- [ ] Update `docs/source-of-truth/FEED_STATUS_CONTRACT.md`:
  - document source-specific blockers.
  - document that background probes are optional and read-only.
  - document that `/snapshot.json` reads cached status and does not run orders.
- [ ] Update `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md`:
  - mark fake adapter seam complete only after Tasks 6 to 9 pass.
  - mark each source adapter complete only after its fake HTTP tests pass.
  - keep "real source status" incomplete until configured probes produce measured status in AWS or a local fake/live smoke.
- [ ] Expose `RELEASE_SCOPE_AUDIT.md` through `tools/mcp/oloraculo_context_server.py`.
- [ ] Run:

```powershell
python tools\mcp\test_oloraculo_context_server.py
```

## Task 19: Full Verification Gate

- [ ] Run focused feed tests:

```powershell
dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter "FullyQualifiedName~FeedStatus|FullyQualifiedName~ObjectArchiveHealth|FullyQualifiedName~PolymarketClobStatus"
```

- [ ] Run full .NET suite:

```powershell
dotnet test Oloraculo.sln
```

- [ ] Run Rust tests:

```powershell
cargo test
```

- [ ] Run MCP smoke:

```powershell
python tools\mcp\test_oloraculo_context_server.py
```

- [ ] Run security and release checks:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\codex\check-oloraculo-codex.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\opencode\check-oloraculo-opencode.ps1
```

- [ ] Run container smoke:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\test-container-smoke.ps1
```

- [ ] Final status review:

```powershell
git status --short --branch --untracked-files=all
git diff --check
```

## Completion Criteria

- [ ] The tracked release scope is documented and guarded by script.
- [ ] `tools/release` scripts are not ignored.
- [ ] tracked runtime logs are removed from release scope.
- [ ] feed source IDs are centralized and tested.
- [ ] source-specific fake adapters cover missing config, down, stale, empty, parse-error, not-implemented/planned, and ready states where applicable.
- [ ] Databet, widgets, OddsPapi/Pinnacle, GRID, CLOB, and object archive rows can only report `READY` from measured adapter evidence.
- [ ] config presence alone never sets `present=true`.
- [ ] `/snapshot.json` remains secret-safe and read-only.
- [ ] full verification gate passes from tracked files.

## Execution Order

1. Tasks 1 to 5: release boundary. Do these before touching feed code.
2. Tasks 6 to 9: adapter seam and cache-backed service composition.
3. Tasks 10 to 13: external source adapters.
4. Tasks 14 to 15: Polymarket CLOB blockers and adapter.
5. Task 16: archive health probe.
6. Tasks 17 to 19: smoke, docs, MCP, and full verification.
