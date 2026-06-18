# Polymarket Sports Scalp Cockpit Source Of Truth

This is the canonical operating spec for the Oloraculo Polymarket-style sports
scalp cockpit. It supersedes any one-fixture-only interpretation of Combo Lab.
Production ownership and the Polytrade donor-only boundary are defined in
`docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`.

The cockpit is **read-only / analysis-only**. It does not place, cancel, approve,
or simulate live orders. It ranks two-leg inventory opportunities and shows the
hedge math needed to lock profit if the market later offers executable opposite
side fills.

## Core Objective

The default trade is not a full-time directional bet. It is a two-leg inventory
trade:

```text
entry side + hedge side < 1.00
```

If we buy `N` shares of side A at entry price `a` and later buy `N` shares of
side B at hedge price `b`:

```text
locked_profit_per_share = 1 - a - b
locked_profit_dollars   = N * (1 - a - b)
deployed_capital        = N * (a + b)
roi                     = (1 - a - b) / (a + b)
```

For target ROI `r`, the required maximum hedge price is:

```text
required_hedge_price = 1 / (1 + r) - entry_price
```

Example:

```text
Buy Under 2.5 at 0.48 and target 8.7% ROI:
required_hedge = 1 / 1.087 - 0.48 = 0.43996 ≈ 0.44
Need to buy Over 2.5 at 0.44 or lower.
```

## Non-Negotiable Rules

1. Treat every trade as a two-leg inventory trade unless explicitly told to make
   a final directional bet.
2. Always identify the opposite side that must be bought later.
3. Always calculate hedge prices for breakeven, 2%, 5%, 8%, 10%, and 12% ROI.
4. Default share unit is **400 shares** unless the operator gives another size.
5. Do not recommend an entry unless the opposite side has a realistic decay or
   event-driven repricing path.
6. If an early goal, red card, VAR event, penalty, or other bad event happens,
   do not blindly hedge into a locked loss. Explain whether to wait, cut, average
   down, partially hedge, or abandon.
7. Separate final-score prediction from scalp plan. The cockpit optimizes for
   hedge paths, not for heroic directional calls.
8. If the operator provides a position screenshot, treat it as fresher than web
   data and calculate exact outcome PnL.
9. Use executable bid/ask and visible depth. Last price and midpoint are
   presentation-only unless no better data exists, in which case the row is a
   blocker, not a trade recommendation.
10. The edge depends on fills. Any candidate without fillable depth at the
    required hedge price is `WATCH` or `BLOCKED`, not `TRADE`.

## Required Market Board

For every active/relevant sports event, with World Cup games prioritized, the
cockpit should map:

- moneyline / draw
- spreads / handicaps, especially +1.5/-1.5
- match totals: O/U 0.5, 1.5, 2.5, 3.5, 4.5
- team totals
- BTTS Yes/No
- first goal if useful and liquid
- correct score if useful for context
- volume, liquidity, bid/ask, spread, depth, freshness, blockers

Normalize paired binary markets:

```text
normalized_over = over_price / (over_price + under_price)
```

Example:

```text
O2.5 = 0.53, U2.5 = 0.48
normalized O2.5 = 0.53 / (0.53 + 0.48) = 52.5%
```

## Data Sources And Precedence

Screenshots from the operator override web data if they are fresher.

Preferred source order for live execution thinking:

1. Polymarket/Gamma/CLOB: event identity, condition id, token id, bid/ask, depth,
   volume, liquidity, accepting-orders/live/closed state.
2. Operator screenshots: positions, open orders, current board if fresher.
3. ESPN: fixture status, match time, lineups, injuries, play-by-play.
4. Flashscore: live score, timeline, H2H, recent matches, cards, lineups.
5. AiScore: H2H, recent form, goals for/against, O/U and BTTS trends.
6. FIFA rankings / Elo-style strength: rough team-quality layer, not a raw-form
   substitute.
7. Reuters / AP / official news: lineups, injuries, tactical intent, referee,
   venue/weather/roof.
8. Odds aggregators and betting previews: Covers, Action Network, Squawka,
   Fox/FanDuel, VSiN, SI Betting. Use them only to cross-check consensus.

## Team-Strength Layer

The analyst view must adjust raw recent form by opponent quality.

- Beating a weak team 3-0 is not equivalent to drawing a top team 0-0.
- Losing 2-0 to Argentina can be less negative than drawing 1-1 with a weak side.
- Use FIFA ranking/points or Elo-like ratings as an anchor.
- Incorporate injuries, lineups, tactical setup, referee, weather/venue/roof, and
  motivation/group state.

This layer informs lambdas and event-path realism. It is not enough by itself to
create a trade recommendation.

## Goal Model

For football, use approximate Poisson lambdas:

```text
home_goals ~ Pois(lambda_home)
away_goals ~ Pois(lambda_away)
total_lambda = lambda_home + lambda_away
```

For total goals O2.5:

```text
P(O2.5) = P(total goals >= 3)
        = 1 - exp(-lambda) * (1 + lambda + lambda^2 / 2)
```

Use a score-matrix for exact score, moneyline, spreads, BTTS, and team totals.
Calibrate lambdas to the market board and strength layer; do not overfit recent
goals without opponent adjustment.

## Decay Model

For a scoreless game:

```text
remaining_lambda = total_lambda * remaining_minutes / 90
```

At 0-0 in O/U 2.5, Over 2.5 needs 3 goals from now:

```text
P(O2.5 live) = P(Pois(remaining_lambda) >= 3)
```

At 1-0, Over 2.5 needs 2 more goals:

```text
P(O2.5 live) = P(Pois(remaining_lambda) >= 2)
```

At 2-0, Over 2.5 needs 1 more goal:

```text
P(O2.5 live) = P(Pois(remaining_lambda) >= 1)
```

Theoretical decay must be adjusted for live danger:

- big chances
- VAR goals / offside goals
- penalty appeals
- high xG / repeated box entries
- constant pressure
- red card risk
- goalkeeper injury or tactical mismatch

A dangerous 0-0 decays slower than a dead 0-0.

## Primary Scalp Types

### 1. Under 2.5 Decay Scalp

Entry: buy U2.5.
Hedge: buy O2.5 later.

Works when first 10-15 minutes are scoreless, tempo is normal/dead, and the
Over is not being held up by huge chances.

Fails with early goal, VAR chaos, constant pressure, penalty/red-card risk.

### 2. BTTS No Decay Scalp

Entry: buy BTTS No.
Hedge: buy BTTS Yes later.

Often safer than U2.5 because a 1-0 favorite lead does not instantly kill No.
Works best with one strong defensive team or a favorite likely to score first.

### 3. Underdog +1.5 Spread Scalp

Entry: buy underdog +1.5.
Hedge: buy favorite -1.5 later.

This is a bet against an early favorite blowout. Works when 0-0 after 10-20
minutes and the underdog is compact. Fails when the favorite scores early or
dominates chance quality.

### 4. Favorite -1.5 Event Scalp

Entry: buy favorite -1.5.
Hedge: buy underdog +1.5 later.

This is not time decay; it needs an event such as early favorite goal, red card
to underdog, or massive pressure. Use smaller size because it is event-dependent.

### 5. Under 3.5 Safer Scalp

Entry: buy U3.5.
Hedge: buy O3.5 later.

Survives one goal better than U2.5. Lower ROI, safer path.

### 6. Under 1.5 Fragile Scalp

Usually avoid. One early goal crushes it.

## Hedge Math Surface

For every candidate, the cockpit must show this target table:

| Target ROI | Required hedge price formula |
| --- | --- |
| Breakeven | `1.0000 - entry_price` |
| 2% | `1 / 1.02 - entry_price` |
| 5% | `1 / 1.05 - entry_price` |
| 8% | `1 / 1.08 - entry_price` |
| 10% | `1 / 1.10 - entry_price` |
| 12% | `1 / 1.12 - entry_price` |

The 400-share ladder defaults to targeting 8-10% ROI.

Example:

```text
Entry: buy 400 U2.5 @ 0.46

Hedge ladder: buy O2.5
75 @ 0.48
125 @ 0.46
150 @ 0.44
50 @ 0.42

avg_hedge = (75*0.48 + 125*0.46 + 150*0.44 + 50*0.42) / 400
          = 0.45125

pair_cost = 0.46 + 0.45125 = 0.91125
profit    = 0.08875/share = $35.50 on 400 shares
roi       = 0.08875 / 0.91125 = 9.74%
```

## Partial Hedge Math

If original shares are `N` at entry `a`, and hedge shares are `x` at hedge `b`:

```text
total_cost = N*a + x*b

if original side wins:
  profit = N - total_cost

if hedge side wins:
  profit = x - total_cost
```

To guarantee no loss on the hedge side:

```text
x >= (N * a) / (1 - b)
```

To guarantee target profit `P` on the hedge side:

```text
x >= (N*a + P) / (1 - b)
```

The cockpit must show exact equalization shares when the operator sends a
position screenshot.

## Position Screenshot Handling

When the operator sends a position screenshot, extract:

- shares on each side
- average price
- total cost
- current market value
- open orders if visible

Then calculate:

```text
total_cost = cost_side_A + cost_side_B

if side A wins:
  payout = shares_A
  profit = shares_A - total_cost

if side B wins:
  payout = shares_B
  profit = shares_B - total_cost

if same shares both sides:
  guaranteed_profit = shares - total_cost

if unequal shares:
  minimum_guaranteed_profit = min(shares_A, shares_B) - total_cost
```

Always state whether the position is fully locked, how many shares are needed to
equalize, and whether to leave extra upside or flatten.

## Required Output For Each Match/Event

1. One-line verdict: best scalp market and why.
2. Market board: current prices, bid/ask, depth, liquidity, normalized pairs.
3. Team strength: ranking/Elo, opponent-adjusted form, goals for/against,
   tactical and news layer.
4. Model: `lambda_home`, `lambda_away`, `total_lambda`.
5. Fair probabilities: ML, spread, totals, BTTS, team totals, first goal where
   relevant.
6. Ranked scalp candidates: entry, opposite hedge, trigger, risk.
7. Exact trade plan: entry price, 400-share hedge ladder, average hedge, locked
   profit, ROI.
8. Failure plan: early goal, red card, dangerous pressure, partial hedge/cut
   rules.
9. Live execution rules: quiet start, dangerous 0-0, bad event, lock at 8%+,
   partial hedge if danger rises.
10. Exact-score prediction: separate, gun-to-head, never confused with scalp.

## Dashboard Requirements

The all-sports scalp cockpit must not require manual refresh.

Minimum dashboard surfaces:

- auto-refresh status: interval, running/not running, last refresh, next refresh,
  last error, scan duration
- all-sports event universe, with World Cup priority sorting
- event status: kickoff/live/closed, timing freshness, data blockers
- market board: ML, draw, spreads, totals, BTTS, team totals, first goal, correct
  score where available
- hedge-price grid: BE/2/5/8/10/12 for each candidate
- 400-share ladder and executable depth/VWAP
- candidate rank: `TRADE`, `WATCH`, `BLOCKED`, `REJECT`
- blocker rollups: missing tokens, no book, stale book, thin depth, no timing,
  unclassified rules, no realistic hedge path
- clear analysis-only / no-order-path banner

## Storage Architecture

The local PC is a **hot cache only**. Do not keep raw books/snapshots on the main
disk as a permanent archive.

Oloraculo local policy:

- keep current live collector state and current scan books in memory;
- optionally keep short-lived hot cache only for today / last 1-7 days;
- never append raw full book snapshots to the evidence ledger;
- never store uncompressed raw JSON forever;
- never create one file per event/update;
- never use SQLite/Postgres as the raw book lake.

Permanent market-data history belongs in Oloraculo-owned object storage:

```text
r2://market-data/
  polymarket/
    raw/clob_ws/date=YYYY-MM-DD/hour=HH/market_slug=.../*.ndjson.zst
    bronze/trades/date=YYYY-MM-DD/market_slug=.../*.parquet
    bronze/book_deltas/date=YYYY-MM-DD/market_slug=.../*.parquet
    bronze/book_top10/date=YYYY-MM-DD/market_slug=.../*.parquet
    silver/price_1s/date=YYYY-MM-DD/*.parquet
    silver/price_1m/date=YYYY-MM-DD/*.parquet
    silver/decay_curves/date=YYYY-MM-DD/*.parquet
    silver/hedge_simulations/date=YYYY-MM-DD/*.parquet
    gold/strategy_results/
    gold/match_cards/
    gold/pnl_reports/
```

Recommended stack:

- Cloudflare R2, S3-compatible API;
- raw replay/audit files: `.ndjson.zst` / `.jsonl.zst`, batched around 100-500 MB;
- queryable layers: Parquet with ZSTD compression;
- analysis: DuckDB `httpfs` reading R2/S3-compatible paths without downloading
  the whole archive locally;
- optional later: ClickHouse for interactive large-scale analytics.

Order-book retention principle:

- save raw WebSocket messages compressed;
- save deltas;
- save full book checkpoints every 30-60 seconds and on subscribe/reconnect;
- save derived top 5 every 1s, top 10 every 5s, and full depth only when needed;
- keep full book snapshots locally for 1-7 days max if enabled;
- keep top-of-book/top-10/trades/derived bars much longer in Parquet.

The sports scalp dashboard uses full depth for **current executable VWAP math**
but does not persist raw full-depth books locally.

## Cross-Venue Feed Plan

Polytrade contains reusable read-only donor notes/wrappers/specs for Databet,
OddsPapi/Pinnacle, GRID, and Polymarket. The Oloraculo cockpit may extract
protocol lessons only into Oloraculo-owned adapters and sanitized
status/silver-gold outputs. It must not hold or print venue credentials.

Safe feed status fields:

- `databet_xauth_present`
- `databet_widget_auth_mode`
- `oddspapi_key_present`
- `grid_key_present`
- `r2_config_present`
- `r2_auth_present`
- `latest_recv_ts`
- `rows_last_minute`
- `join_coverage`
- `last_error_redacted`

SofaScore is not an active Oloraculo feed. Never display raw
`SPORTSBOOK_XAUTH`, widget JWTs, OddsPapi keys, GRID keys, R2 access keys,
signed URLs, or key prefixes/suffixes.

## Candidate Verdicts

- `TRADE`: executable entry exists, opposite side can realistically decay/reprice,
  depth supports the target unit, hedge math is positive at target prices, and no
  red blocker is active. In this app, `TRADE` still means analysis-only; it does
  not submit orders.
- `WATCH`: plausible setup, but needs timing, better price, more depth, or live
  state confirmation.
- `BLOCKED`: missing token/book/timing/liquidity or no realistic hedge path.
- `REJECT`: closed, unclassified, ambiguous, props without hedge mechanics, or
  market family not supported.

## Implementation Plan

1. Write this source-of-truth and companion HTML.
2. Add a read-only all-sports scalp scanner service using existing Polymarket
   service boundaries where possible.
3. Start with football/World Cup families already supported by Combo Lab:
   totals, BTTS, spread, moneyline, team total.
4. Add auto-refreshing Blazor page with a busy guard and last-good snapshot.
5. Show blockers for unsupported/non-football sports instead of pretending a
   soccer decay model applies everywhere.
6. Add tests for hedge math, 400-share ladder, freshness, and auto-refresh-safe
   snapshot construction.
7. Only after this read-only scanner is stable, add richer live feeds and
   sport-specific decay models.

## Safety Boundary

This cockpit never reads private keys, wallet secrets, API keys, `.env` files, or
AWS secret material. It never places live orders. Existing Rust
NativePM/polytrade services are donor evidence only; any future live order path
must be Oloraculo-owned, Rust-first, disabled by default, and gated separately.
