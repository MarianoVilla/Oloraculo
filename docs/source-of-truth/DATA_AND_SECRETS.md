# World Cup Edge Lab Data And Local Config

## Local Data

- CSV seed data lives under `Oloraculo.Web/Data`.
- SQLite database files are generated at runtime and ignored by git: `*.db`, `*.db-shm`, `*.db-wal`.
- README predictions are generated between the `oloraculo:snapshots` markers in `README.md`.
- Local disk is hot cache only for market data. Do not keep raw Polymarket books,
  raw venue payloads, Parquet lakes, DuckDB lakes, or long-lived snapshots under
  this repo or on the Windows disk as a permanent archive.
- Permanent market-data history belongs in Oloraculo-owned Cloudflare
  R2/S3-compatible object storage with compressed raw batches and Parquet/ZSTD
  research layers.

## External Data Sources

- FIFA rankings: `FifaRankingsRawUrl`.
- Elo ratings: `EloRankingsBaseUrl`.
- Goalscorers: `GoalscorersRawUrl`.
- Fixtures/context: API-Football v3.
- Availability news: configured `AvailabilitySourceUrls` plus OpenRouter classification.

## Local Config

Never read, print, commit, or paste local secret/config files unless the user explicitly asks for that specific file and the security impact is acknowledged.

Common sensitive values:

- `ApiFootballApiKey`
- `OpenRouterApiKey`
- `.mcp.json`
- `pmkey.txt`
- `.env` or `.env.*` except `.env.example`
- `appsettings.Development.json`
- `secrets.json`

Use `appsettings.Development.json`, user secrets, or environment variables for local key values.

Credential-like environment variables must be documented with placeholders only:

- `SPORTSBOOK_XAUTH=<provided separately>`
- `ODDSPAPI_KEY=<provided separately>`
- `GRID_KEY=<provided separately>`
- `OLORACULO_R2_BUCKET=<provided separately>`
- `OLORACULO_R2_ENDPOINT=<provided separately>`
- `OLORACULO_R2_ACCESS_KEY_ID=<provided separately>`
- `OLORACULO_R2_SECRET_ACCESS_KEY=<provided separately>`

SofaScore is not an active Oloraculo feed. Do not add SofaScore credentials
unless a future source decision explicitly reactivates it.

Safe dashboards may display only `*_present=true/false`, hostnames, age, status,
and redacted errors. They must never display raw tokens, key prefixes/suffixes,
signed URLs, private keys, or API-key query strings.

## Market Data Storage Rule

Oloraculo is an analysis/UI cockpit. It may keep current in-memory books and a
small last-good hot snapshot for the active page. It must not become the durable
raw market-data archive.

Canonical durable architecture:

```text
local disk = today / hot cache only
object storage = history
Parquet = research
raw compressed logs = replay/audit
```

Preferred archive stack:

- Cloudflare R2 bucket using the S3-compatible API.
- Raw: `.ndjson.zst` / `.jsonl.zst`, immutable, batched to about 100-500 MB.
- Bronze: normalized trades, book deltas, top-of-book/top-10 snapshots as
  Parquet with ZSTD compression.
- Silver: one-second/minute prices, spread curves, decay features, hedge
  simulations.
- Gold: strategy results, match cards, PnL reports.
- Query: DuckDB with `httpfs` against R2/S3-compatible paths.

Do not store every full book snapshot forever. Save raw WebSocket messages,
deltas, periodic full checkpoints, and derived top levels. Full depth is local
hot execution math unless a bounded archive policy explicitly stores it.

## Refresh Rules

- Before a refresh, state which data source is being refreshed and whether API keys are required.
- After a refresh, report changed files and any warnings/errors from the refresh report.
- If a key is missing, prefer deterministic fallback behavior over guessing or adding placeholder values.
- Do not treat network-fetched data as verified unless the command output and changed files are inspected.

## CI Rule

CI should avoid live network refresh unless a workflow is explicitly designed for it. Default build/test workflows should rely on checked-in seed data and test fakes.
