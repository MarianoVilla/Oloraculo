# Polymarket Docs Reference Map

Status: current local vendor snapshot created from the official Polymarket docs `llms.txt` index.

## Snapshot

- Local corpus: `docs/vendor/polymarket-docs`
- Page inventory: `docs/vendor/polymarket-docs/manifest.json`
- Human index: `docs/vendor/polymarket-docs/quick-reference.md`
- Raw upstream index: `docs/vendor/polymarket-docs/raw/llms.txt`
- Raw upstream full corpus: `docs/vendor/polymarket-docs/raw/llms-full.txt`
- Refresh command: `python tools\docs\fetch_polymarket_docs.py`
- Coverage at creation: 199 of 199 indexed docs/spec pages downloaded, zero failures.

## First Pages To Open

- Platform/API overview: `docs/vendor/polymarket-docs/pages/api-reference/introduction.md`
- Auth model and API keys: `docs/vendor/polymarket-docs/pages/api-reference/authentication.md`
- Rate limits: `docs/vendor/polymarket-docs/pages/api-reference/rate-limits.md`
- SDKs: `docs/vendor/polymarket-docs/pages/api-reference/clients-sdks.md`
- Python SDK notes: `docs/vendor/polymarket-docs/pages/dev-tooling/python.md`
- TypeScript SDK notes: `docs/vendor/polymarket-docs/pages/dev-tooling/typescript.md`
- Trading quickstart: `docs/vendor/polymarket-docs/pages/trading/quickstart.md`
- Public client: `docs/vendor/polymarket-docs/pages/trading/clients/public.md`
- L1 client: `docs/vendor/polymarket-docs/pages/trading/clients/l1.md`
- L2 client: `docs/vendor/polymarket-docs/pages/trading/clients/l2.md`
- Builder client: `docs/vendor/polymarket-docs/pages/trading/clients/builder.md`

## Oloraculo Build Map

## Implementation Status In Oloraculo

- Public Gamma discovery is centralized in `Oloraculo.Web/ComboLab/Markets/PolymarketMarketDataService.cs` and now uses documented keyset endpoints (`/events/keyset`, `/markets/keyset`) instead of offset pagination.
- Public CLOB book reads use documented `/book` for single-token lookup and `/books` for batch token hydration.
- Public Data API current positions are available through `FetchCurrentPositionsAsync`, using `PolymarketDataBaseUrl` and `/positions`.
- Combo RFQ discovery remains public through `/v1/rfq/combo-markets`.
- The sports scalp scanner uses batched CLOB book hydration for lower latency and fewer REST calls.
- Authenticated CLOB order/user-order/heartbeat calls are not active in Blazor. They require L2 `POLY_*` HMAC headers and signed order payloads per official docs, and belong behind the future Rust/server-side live-order gate.

### Market Discovery

Use these for read-only candidate discovery, market identity, tags, and sports taxonomy:

- `docs/vendor/polymarket-docs/pages/api-reference/events/*`
- `docs/vendor/polymarket-docs/pages/api-reference/markets/*`
- `docs/vendor/polymarket-docs/pages/api-reference/search/search-markets-events-and-profiles.md`
- `docs/vendor/polymarket-docs/pages/api-reference/sports/*`
- `docs/vendor/polymarket-docs/pages/market-data/fetching-markets.md`
- `docs/vendor/polymarket-docs/pages/concepts/markets-events.md`

### Pricing, Books, And Freshness

Use these for the Rust hotpath snapshot contract, CLOB freshness, spread/depth checks, and monitor state:

- `docs/vendor/polymarket-docs/pages/api-reference/market-data/get-order-book.md`
- `docs/vendor/polymarket-docs/pages/api-reference/market-data/get-order-books-request-body.md`
- `docs/vendor/polymarket-docs/pages/api-reference/market-data/get-market-price.md`
- `docs/vendor/polymarket-docs/pages/api-reference/market-data/get-market-prices-request-body.md`
- `docs/vendor/polymarket-docs/pages/api-reference/market-data/get-midpoint-prices-request-body.md`
- `docs/vendor/polymarket-docs/pages/api-reference/market-data/get-spread.md`
- `docs/vendor/polymarket-docs/pages/api-reference/markets/get-prices-history.md`
- `docs/vendor/polymarket-docs/pages/trading/orderbook.md`
- `docs/vendor/polymarket-docs/pages/concepts/prices-orderbook.md`

### WebSockets

Use these for live market/user/sports stream contracts and reconnect state:

- `docs/vendor/polymarket-docs/pages/api-reference/wss/market.md`
- `docs/vendor/polymarket-docs/pages/api-reference/wss/user.md`
- `docs/vendor/polymarket-docs/pages/api-reference/wss/sports.md`
- `docs/vendor/polymarket-docs/pages/api-reference/wss/rfq.md`
- `docs/vendor/polymarket-docs/pages/market-data/websocket/overview.md`
- `docs/vendor/polymarket-docs/pages/market-data/websocket/market-channel.md`
- `docs/vendor/polymarket-docs/pages/market-data/websocket/user-channel.md`
- `docs/vendor/polymarket-docs/pages/market-data/websocket/sports.md`

### Account State

Use these for read-only account monitors, position reconciliation, activity, and current order status:

- `docs/vendor/polymarket-docs/pages/api-reference/core/get-current-positions-for-a-user.md`
- `docs/vendor/polymarket-docs/pages/api-reference/core/get-closed-positions-for-a-user.md`
- `docs/vendor/polymarket-docs/pages/api-reference/core/get-total-value-of-a-users-positions.md`
- `docs/vendor/polymarket-docs/pages/api-reference/core/get-user-activity.md`
- `docs/vendor/polymarket-docs/pages/api-reference/core/get-trades-for-a-user-or-markets.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/get-user-orders.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/get-single-order-by-id.md`

### Trading Lifecycle Boundary

Use these for future paper/shadow/live boundary design only. Oloraculo remains analysis-only until the explicit live-order approval, WAL, reconciliation, arming, cap, and kill-switch gates are implemented and verified.

- `docs/vendor/polymarket-docs/pages/trading/orders/overview.md`
- `docs/vendor/polymarket-docs/pages/trading/orders/create.md`
- `docs/vendor/polymarket-docs/pages/trading/orders/cancel.md`
- `docs/vendor/polymarket-docs/pages/trading/orderbook.md`
- `docs/vendor/polymarket-docs/pages/concepts/order-lifecycle.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/post-a-new-order.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/post-multiple-orders.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/cancel-single-order.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/cancel-multiple-orders.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/cancel-all-orders.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/send-heartbeat.md`
- `docs/vendor/polymarket-docs/pages/api-reference/trade/get-order-scoring-status.md`

### Relayer, Wallets, And Funds

Use these for deposit wallet, gasless, relayer, and wallet deployment design:

- `docs/vendor/polymarket-docs/pages/trading/deposit-wallets.md`
- `docs/vendor/polymarket-docs/pages/trading/gasless.md`
- `docs/vendor/polymarket-docs/pages/api-reference/relayer/*`
- `docs/vendor/polymarket-docs/pages/api-reference/relayer-api-keys/get-all-relayer-api-keys.md`
- `docs/vendor/polymarket-docs/pages/trading/bridge/*`
- `docs/vendor/polymarket-docs/pages/api-reference/bridge/*`
- `docs/vendor/polymarket-docs/pages/concepts/pusd.md`

### Combos, CTF, And Negative Risk

Use these for combo EV, payoff masks, compression/redeem/reconcile design, and negative-risk handling:

- `docs/vendor/polymarket-docs/pages/api-reference/combo-markets/get-combo-markets.md`
- `docs/vendor/polymarket-docs/pages/api-reference/core/get-user-combo-positions.md`
- `docs/vendor/polymarket-docs/pages/api-reference/core/get-user-combo-activity.md`
- `docs/vendor/polymarket-docs/pages/api-reference/maker/*`
- `docs/vendor/polymarket-docs/pages/market-makers/combos.md`
- `docs/vendor/polymarket-docs/pages/trading/ctf/*`
- `docs/vendor/polymarket-docs/pages/advanced/neg-risk.md`
- `docs/vendor/polymarket-docs/pages/concepts/positions-tokens.md`

### Builders, Fees, Rewards, And Rebates

Use these for builder attribution, fee model, rewards, rebates, and market maker evidence:

- `docs/vendor/polymarket-docs/pages/builders/*`
- `docs/vendor/polymarket-docs/pages/api-reference/builders/*`
- `docs/vendor/polymarket-docs/pages/api-reference/rewards/*`
- `docs/vendor/polymarket-docs/pages/api-reference/rebates/get-current-rebated-fees-for-a-maker.md`
- `docs/vendor/polymarket-docs/pages/trading/fees.md`
- `docs/vendor/polymarket-docs/pages/trading/taker-rebates.md`
- `docs/vendor/polymarket-docs/pages/market-makers/*`

### Specs

Use these when generated clients, schemas, or MCP validators need precise machine-readable contracts:

- `docs/vendor/polymarket-docs/pages/api-spec/*.yaml.md`
- `docs/vendor/polymarket-docs/pages/api-reference/*openapi*.md`
- `docs/vendor/polymarket-docs/pages/developers/open-api/*.json.md`
- `docs/vendor/polymarket-docs/pages/asyncapi*.md`

## Search Commands

```powershell
rg -n "heartbeat|order scoring|cancel|positions|User Channel|Market Channel" docs\vendor\polymarket-docs
rg -n "api key|signature|L1|L2|funder|proxy|relayer" docs\vendor\polymarket-docs
rg -n "negative risk|merge|split|redeem|combo|RFQ" docs\vendor\polymarket-docs
rg -n "rate limit|429|cursor|pagination|next_cursor" docs\vendor\polymarket-docs
```

## Refresh And Verification

After refreshing, verify:

```powershell
python tools\docs\fetch_polymarket_docs.py
python -c "import json,pathlib; d=json.loads(pathlib.Path('docs/vendor/polymarket-docs/manifest.json').read_text(encoding='utf-8')); print(d['counts'])"
```

The expected success shape is `pages_indexed == pages_downloaded` and `failures == 0`.
