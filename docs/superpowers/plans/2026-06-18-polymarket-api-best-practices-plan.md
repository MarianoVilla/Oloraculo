# Polymarket API Best Practices Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Align Oloraculo's Polymarket integration with the official local docs snapshot so public market discovery, CLOB books, and public account state use documented API shapes while live order capability stays gated.

**Architecture:** Keep Polymarket access centralized in `PolymarketMarketDataService` for this slice. Add canonical endpoint constants and small public models; switch Gamma list calls to documented keyset endpoints; add CLOB `/books` batching for scanner latency; add Data API current positions for read-only account monitoring; document authenticated CLOB order/user-order surfaces as future Rust/server-only gates.

**Tech Stack:** .NET 9, `HttpClient`, `System.Text.Json`, xUnit fake HTTP handlers, local official docs snapshot in `docs/vendor/polymarket-docs`.

---

## File Structure

- Modify: `Oloraculo.Web/OloraculoConfig.cs`
  - Add `PolymarketDataBaseUrl` for `https://data-api.polymarket.com`.
- Modify: `Oloraculo.Web/appsettings.json`
  - Add default Data API host.
- Modify: `Oloraculo.Web/ComboLab/Markets/PolymarketMarketModels.cs`
  - Add Gamma keyset page records, Data API position models, and endpoint constants.
- Modify: `Oloraculo.Web/ComboLab/Markets/PolymarketMarketDataService.cs`
  - Use endpoint constants, keyset pagination, CLOB `/books`, and Data API positions.
- Modify: `Oloraculo.Web/ComboLab/Scalp/SportsScalpScannerService.cs`
  - Hydrate token books in documented `/books` batches instead of one GET per token.
- Modify: `Oloraculo.Web.Tests/TestSupport/TestFixtures.cs`
  - Let fake HTTP tests assert method, path, and JSON body for POST `/books`.
- Modify: `Oloraculo.Web.Tests/ComboLab/PolymarketMarketDataServiceTests.cs`
  - Add tests for keyset URLs, batch books, and Data API positions.
- Modify: `Oloraculo.Web.Tests/ComboLab/SportsScalpScannerServiceTests.cs`
  - Update scanner tests to expect one POST `/books` call.
- Modify: `Oloraculo.Web.Tests/ComboLab/SportsScalpUniverseTests.cs`
  - Update scanner universe test to expect one POST `/books` call.
- Modify: `docs/source-of-truth/POLYMARKET_DOCS_REFERENCE_MAP.md`
  - Add implementation status notes and boundary.

## Task 1: Config And Endpoint Constants

- [ ] Add `PolymarketDataBaseUrl` to `OloraculoConfig` with default `https://data-api.polymarket.com`.
- [ ] Add `PolymarketDataBaseUrl` to `appsettings.json`.
- [ ] Add `PolymarketApiEndpoints` constants in `PolymarketMarketModels.cs`:
  - `GammaEventsKeyset = "/events/keyset"`
  - `GammaMarketsKeyset = "/markets/keyset"`
  - `GammaEventBySlugPrefix = "/events/slug/"`
  - `GammaSportsMarketTypes = "/sports/market-types"`
  - `ClobBook = "/book"`
  - `ClobBooks = "/books"`
  - `DataPositions = "/positions"`
  - `ComboMarkets = "/v1/rfq/combo-markets"`
- [ ] Run `dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter PolymarketMarketDataServiceTests` and expect compile failures until service code uses the new constants.

## Task 2: Keyset Gamma Pagination

- [ ] Add `PolymarketEventPage` and `PolymarketMarketPage` records with `Items` and `NextCursor`.
- [ ] Add `FetchEventsPageAsync(...)` using `/events/keyset`, `after_cursor`, and no `offset`.
- [ ] Update `FetchEventsAsync(...)` to call `FetchEventsPageAsync(...)` once for backward compatibility.
- [ ] Update `FetchWorldCupEventsAsync(...)` to call keyset endpoint with `tag_id=102232`.
- [ ] Add `FetchWorldCupEventsPagedAsync(maxPages, limit, ct)` for callers that need multiple pages.
- [ ] Add `FetchWorldCupMarketsByTypePageAsync(...)` using `/markets/keyset`.
- [ ] Keep old public method names stable so current UI code does not break.

## Task 3: Batch CLOB Books

- [ ] Add `FetchBooksAsync(IEnumerable<string> tokenIds, CancellationToken ct)` using `POST /books`.
- [ ] Payload shape must be an array of objects with `token_id`, matching the official docs.
- [ ] Parse each returned book with existing `ParseBook`.
- [ ] Deduplicate blank/duplicate token IDs before sending.
- [ ] Update `SportsScalpScannerService` to fetch event token books in batches bounded by `MaxTokenBooks`.
- [ ] Preserve `TokenBookRawStatus.FetchFailed`, `MissingToken`, `EmptyBook`, and `Stale` behavior.

## Task 4: Public Data API Positions

- [ ] Add `PolymarketPositionSnapshot` model matching the documented public `/positions` fields used by account status.
- [ ] Add `FetchCurrentPositionsAsync(user, limit, offset, sizeThreshold, ct)` against `PolymarketDataBaseUrl + /positions`.
- [ ] Validate `user` as a non-empty `0x` address string before building URL.
- [ ] Parse array responses and preserve numeric fields as decimals.
- [ ] Do not add live order placement, cancellation, allowance, relayer submit, or heartbeat calls in this slice.

## Task 5: Tests

- [ ] Extend `FakeHttpMessageHandler` to support method-aware request keys such as `POST https://clob.test/books [...]`.
- [ ] Test keyset events URL contains `/events/keyset` and excludes `offset`.
- [ ] Test World Cup keyset requests include `tag_id=102232&related_tags=true`.
- [ ] Test batch books POST body is `[{"token_id":"..."}]` and returned books parse to token IDs.
- [ ] Test Data API positions URL uses `https://data.test/positions?user=...`.
- [ ] Update scanner tests to use batch book responses and keep existing candidate expectations.

## Task 6: Docs

- [ ] Update `POLYMARKET_DOCS_REFERENCE_MAP.md` with implemented public surfaces and future gated authenticated surfaces.
- [ ] Mention that authenticated CLOB order/user-order/heartbeat requires L2 headers and server-side signing per official docs.

## Verification

- [ ] Run `dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --filter "FullyQualifiedName~Polymarket|FullyQualifiedName~SportsScalp"`.
- [ ] Run `dotnet test Oloraculo.sln`.
- [ ] Run `python tools\docs\fetch_polymarket_docs.py` only if docs fetcher changed.
- [ ] Run `python -m py_compile tools\docs\fetch_polymarket_docs.py` only if docs fetcher changed.
- [ ] Run security checks if present: `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1`.

## Scope Boundary

This plan intentionally does not implement order placement, cancellation, approvals, relayer transaction submission, or heartbeat. Those require L2 credentials, server-side HMAC signing, locally signed orders, WAL/reconciliation/arming gates, and the Phase 7 live-order design.
