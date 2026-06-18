# Polytrade Universe Audit

Generated: 2026-06-18

## Current Data Flow

- `SportsScalp.razor` calls `SportsScalpScannerService.ScanAsync` every 30 seconds.
- `SportsScalpScannerService` fetches Gamma events through `PolymarketMarketDataService.FetchEventsAsync`, then fetches per-token CLOB books through `FetchBookAsync`.
- The UI renders scalp candidates and a small event universe table from `SportsScalpSnapshot.Events`.
- `ComboLabMonitorService` separately discovers World Cup markets through `/markets/keyset?tag_id=102232&related_tags=true&sports_market_types=...`.
- `LiveComplementaryLockService` separately maps local World Cup fixtures to Gamma World Cup events for read-only lock analysis.

## Current Universe Source

- Sports Scalp is Polymarket-first, but only for the first unpaginated Gamma page.
- World Cup event discovery now uses `/events/keyset?series_id=11433&include_children=true&include_best_lines=true`; market-family discovery still uses tag `102232`.
- Generic event discovery uses documented keyset pagination and filters active events client-side; it still does not request an ordering aligned to near/live trading.
- The Combo Lab market universe is sports-market-type first, not event-first.

## Current Time Filtering

- The scanner uses `DateTimeOffset.UtcNow` for the scan timestamp, but it does not filter events by a near UTC window.
- Old scheduled events can appear because sorting uses `StartTimeUtc ?? DateTimeOffset.MaxValue` without excluding stale starts.
- Local fixtures from the World Cup database are used by Combo Lab lock scans, but Sports Scalp does not currently use external fixtures as the primary universe.

## Current Liquidity Source

- Gamma liquidity and volume fields are not parsed into market snapshots.
- CLOB book top/depth is fetched for candidate token pairs only.
- Missing or failed books are swallowed as `null`, then fillable shares render as `0`, which makes unknown/failure look like real zero depth.
- No liquidity source or quality field is shown in the Sports Scalp universe panel.

## Current Market Discovery Logic

- `PolymarketMarketDataService.ParseMarket` parses `outcomes`, `clobTokenIds`, and `outcomePrices` when they appear as arrays or JSON strings.
- It does not parse token IDs from `tokens`, snake_case aliases, or all liquidity/volume aliases.
- It does not carry `archived`, `acceptingOrders`, `groupItemTitle`, event/market liquidity, or volume fields.
- Market classification is driven by `sportsMarketType`, `marketType`, question, slug, and description; it lacks some title/outcome patterns.

## Why World Cup Markets Are Missing

- Active Gamma events are not paginated, so World Cup events outside the first limited response are invisible.
- Discovery relies on a hardcoded World Cup tag and/or sports market type lists, which can miss active Polymarket events whose tag/type metadata differs.
- The default Sports Scalp event cap can select old high-priority rows before current near/live World Cup rows because no near-window filter exists.

## Why Liquidity Shows Zero

- Gamma liquidity fields are not displayed.
- CLOB failures are converted to `null` and then rendered as `0` fillable shares.
- Empty books, failed book fetches, missing tokens, and confirmed zero depth are not separated.

## Stale Or Mock Data Locations

- `Oloraculo.Web/Data/wc2026_groups.csv` is a static World Cup fixture seed for modeling and enrichment. It should not define the tradable universe.
- Combo Lab lock scans use local fixtures only to match/enrich a selected Gamma event.
- No production Sports Scalp path should show external-only fixtures or static schedules as tradable events.

## Fix Plan

- Keep the implementation in the existing .NET/Blazor architecture rather than adding TypeScript `src/data` paths.
- Add UTC clock/window helpers and explicit near-window inclusion diagnostics.
- Make Gamma keyset events the canonical universe with `closed=false`, client-side active filtering, pagination, and visible API error diagnostics.
- Parse robust market identity, token, liquidity, volume, archived, accepting-orders, and timing fields.
- Hydrate CLOB books for near-event tokens with explicit `ok`, `missing_token`, `fetch_failed`, and `empty_book` states.
- Separate Gamma liquidity, Gamma 24h volume, CLOB top depth, and CLOB 2c/5c depth with source and quality.
- Update the Sports Scalp UI to show Polymarket tradability, token IDs, book tops, depth, freshness, near-window metadata, and diagnostics.
- Add tests before production-code changes for UTC windowing, parser aliases, classifier aliases, orderbook states/depth, and a mocked World Cup near-event integration.
