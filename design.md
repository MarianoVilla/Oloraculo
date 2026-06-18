You are building a quantitative research and live-monitoring system for Polymarket football markets, focused specifically on binary boxing / hedge-entry strategies in World Cup-style soccer markets.

The goal is NOT to predict final scores in a generic way. The goal is to model whether an entry side can be hedged with the opposite side at a target ROI before an adverse event happens.

Core strategy:

* A binary market has two complementary sides, e.g. U2.5 and O2.5, BTTS No and BTTS Yes, Team +1.5 and Team -1.5.

* A boxed trade is profitable when:

  `entry_price + hedge_price < 1.00`

* Locked profit per share:

  `locked_profit = 1 - entry_price - hedge_price`

* ROI on deployed capital:

  `roi = locked_profit / (entry_price + hedge_price)`

* Required hedge price for a target ROI:

  `required_hedge_price = 1 / (1 + target_roi) - entry_price`

Examples:

* Entry 0.59, 5% target:
  `required = 1 / 1.05 - 0.59 = 0.36238`
  So the opposite side must be bought at ~36c or lower.

* Entry 0.59, 8.7% target:
  `required = 1 / 1.087 - 0.59 = ~0.33`
  So the opposite side must be bought at ~33c or lower.

Build a system that answers:

1. At what match minute does the hedge side usually reach the required price?
2. How often does it reach the required price before a goal, red card, penalty, or other adverse event?
3. How much executable depth exists at the hedge target?
4. Which markets actually decay tradeably: O/U 2.5, BTTS, spreads, team totals, etc.?
5. When is the market sticky despite clock decay?
6. When does post-goal panic revert, and when does it not?
7. Which live match states should block new entries or block averaging down?

Important principle:

Use executable prices, not just chart prices. For boxing, the relevant curve is the best ask of the hedge side, with enough size available. Midpoint and last-traded price are secondary.

Data sources to use:

A. Polymarket market discovery

Use Polymarket Gamma / market discovery endpoints to find all relevant World Cup matches and all associated markets.

For each match, discover and store:

* match slug
* event slug
* kickoff timestamp
* teams
* market titles
* market slugs
* condition IDs
* token IDs / asset IDs for every outcome
* outcome labels
* market type classification:

  * moneyline
  * over/under total goals
  * team total
  * BTTS
  * spread/handicap
  * first goal
  * exact score
  * clean sheet if available
  * halftime markets if available

The system must normalize names. Examples:

* `England vs Croatia`
* `ENG-CRO`
* `England-Croatia`
* `World Cup Group L England Croatia`

All should map to the same internal `match_id`.

B. Polymarket historical prices

Use Polymarket CLOB historical prices endpoint for token-level historical price curves.

Important:

* The historical prices endpoint uses token/asset IDs.
* For every market, collect both sides if possible.
* Store timestamps in UTC.
* Align historical price points to match clock using kickoff time.

This is Stage 1 and is enough for rough research:

* approximate decay curves
* approximate post-goal jumps
* approximate hedge target hit times
* approximate stickiness

But historical price points alone are not enough for fill simulation. For true execution modeling, live orderbook data is required.

C. Polymarket live orderbook / WebSocket

Build a live recorder using Polymarket CLOB WebSocket market channel.

Subscribe to all token IDs for the matches being played that day.

Record:

* full book snapshots
* price changes
* best bid/ask updates
* last trade price events
* trade events
* market-level updates
* spread changes
* tick size changes if any

Store raw WebSocket events exactly as received in compressed append-only files.

From raw events, derive top-of-book snapshots every 1 second:

For each token:

* timestamp
* token_id
* match_id
* market_type
* line
* side label
* best_bid
* best_bid_size
* best_ask
* best_ask_size
* midpoint
* spread
* spread in cents
* depth available within 1c of best ask
* depth available within 2c
* depth available within 5c
* depth available within 10c
* cumulative ask depth at specific target prices
* cumulative bid depth at specific target prices
* book imbalance
* book_hash or checksum if available

For boxing, the key is always:

* If holding U2.5, track O2.5 best ask and depth.
* If holding O2.5, track U2.5 best ask and depth.
* If holding BTTS No, track BTTS Yes best ask and depth.
* If holding BTTS Yes, track BTTS No best ask and depth.
* If holding +1.5 spread, track -1.5 best ask and depth.
* If holding -1.5 spread, track +1.5 best ask and depth.

D. Polymarket trades

Collect all trade events.

Store:

* timestamp
* token_id
* market_id
* match_id
* price
* size
* side if available
* last_trade_price
* matched size
* trade direction if authoritative
* inferred direction only as low-trust field
* score_state at trade time
* match minute at trade time

Do not rely heavily on inferred trade direction from public CLOB events. If authoritative on-chain OrderFilled event data is available, join trades to on-chain fills for true fill-side direction. Otherwise, use trades mostly for volume/liquidity intensity, not for maker/taker classification.

E. Football match state data

Use a football data provider such as API-Football, Sportmonks, Sportradar, StatsBomb, Opta/Stats Perform, or another live event provider.

Minimum required event fields:

* fixture ID
* kickoff timestamp
* home team / away team
* current minute
* current second if available
* period: pregame, first half, halftime, second half, full time
* score home
* score away
* goal events:

  * time
  * team
  * scorer
  * assist if available
  * penalty / own goal flag
* red cards:

  * time
  * team
  * player
* yellow cards:

  * time
  * team
  * player
* penalties:

  * awarded time
  * scored/missed
  * team
* VAR events if available
* substitutions
* lineups
* formations
* match venue
* referee
* injury time / stoppage time if available

Useful but not mandatory:

* shots
* shots on target
* xG
* big chances
* dangerous attacks
* possession
* corners
* fouls
* field tilt
* attacks by minute

Do not overfit to noisy live stats, but use them to classify whether the match is calm or chaotic.

F. External odds / sportsbook comparison

Collect sportsbook odds only as context:

* pre-match O/U 2.5
* live O/U 2.5 if available
* moneyline
* BTTS
* spreads
* line movement before kickoff

Sources can include:

* The Odds API
* Pinnacle if accessible
* Betfair Exchange if accessible
* bookmaker APIs
* odds screen scrape only if legally and technically allowed

Use external odds to estimate whether Polymarket is stale, overreacting, or underreacting. Do not make sportsbook odds the primary signal unless coverage is reliable and timestamps are precise.

G. Pre-match context data

Collect only context that can plausibly affect market decay or goal hazard:

* confirmed lineup
* missing key attackers
* missing defensive midfielders
* missing center backs
* goalkeeper changes
* formation
* team Elo / SPI / FIFA ranking
* rest days
* travel distance if relevant
* weather only if the stadium is open-air or heat materially affects tempo
* referee profile:

  * cards per game
  * penalties per game
  * fouls per game
* venue:

  * indoor / outdoor
  * turf / grass if available
  * altitude if extreme

Do not collect low-signal noise:

* generic team “motivation”
* vague pundit opinions
* Twitter sentiment
* fan narratives
* raw last-five results without opponent adjustment
* unverified injury rumors
* broad weather if stadium is closed/controlled
* historical H2H older than current squad cycle except as very low weight

Storage architecture:

Use a three-layer data lake.

Raw layer:

* immutable, append-only
* compressed NDJSON or JSONL ZSTD
* every WebSocket message exactly as received
* every API response exactly as received
* no transformations
* path format:

  `raw/polymarket_ws/date=YYYY-MM-DD/hour=HH/match_id=.../*.ndjson.zst`

  `raw/football_events/date=YYYY-MM-DD/match_id=.../*.json.zst`

Bronze layer:

* parsed structured data
* Parquet with ZSTD compression
* one table per object type:

  `bronze/book_events`
  `bronze/price_change_events`
  `bronze/trade_events`
  `bronze/match_events`
  `bronze/market_metadata`
  `bronze/football_lineups`

Silver layer:

* cleaned, aligned, timestamp-normalized
* one-second top-of-book snapshots
* match clock attached
* score state attached
* derived liquidity metrics

Tables:

1. `silver.markets`

Fields:

* market_id
* match_id
* event_slug
* market_slug
* market_type
* line
* side
* token_id
* condition_id
* team_a
* team_b
* kickoff_ts
* close_ts
* resolved_outcome

2. `silver.book_top_1s`

Fields:

* ts
* match_id
* token_id
* market_type
* line
* side
* best_bid
* best_bid_size
* best_ask
* best_ask_size
* mid
* spread
* depth_1c
* depth_2c
* depth_5c
* depth_10c
* score_home
* score_away
* score_state
* minute
* second
* period
* seconds_since_kickoff
* seconds_since_last_goal
* red_card_state

3. `silver.trades`

Fields:

* ts
* match_id
* token_id
* market_type
* line
* side
* price
* size
* notional
* match_clock
* score_state
* direction_authoritative
* direction_inferred
* direction_quality_flag

4. `silver.match_state_1s`

Fields:

* ts
* match_id
* minute
* second
* period
* home_score
* away_score
* total_goals
* goal_diff
* red_home
* red_away
* last_event_type
* seconds_since_last_goal
* seconds_since_last_red
* home_yellows
* away_yellows
* shots_home
* shots_away
* shots_on_target_home
* shots_on_target_away
* xg_home
* xg_away
* corners_home
* corners_away
* dangerous_attacks_home
* dangerous_attacks_away

Gold layer:

* strategy research outputs
* tradeable decay curves
* backtest results
* signals
* dashboards

Tables:

1. `gold.decay_curves`

Fields:

* match_id
* market_type
* line
* entry_side
* hedge_side
* entry_price
* minute
* score_state
* hedge_best_ask
* hedge_best_ask_size
* hedge_depth_at_required_5pct
* hedge_depth_at_required_87pct
* observed_mid
* theoretical_mid
* chaos_premium
* clock_decay_expected
* clock_decay_observed
* spread
* liquidity_score

2. `gold.strategy_backtests`

Fields:

* strategy_id
* match_id
* market_type
* line
* entry_time_rule
* entry_side
* entry_price
* entry_size
* hedge_rule
* target_roi
* required_hedge_price
* hit_target
* target_hit_minute
* target_hit_ts
* adverse_event_before_hit
* first_goal_minute
* first_goal_team
* max_drawdown
* realized_pnl_if_boxed
* realized_pnl_if_stopped
* fill_quality
* slippage_assumed
* fees_assumed
* notes

3. `gold.live_signals`

Fields:

* ts
* match_id
* market_type
* line
* signal_type
* entry_side
* entry_price
* hedge_side
* required_hedge_5pct
* required_hedge_87pct
* current_hedge_ask
* liquidity_at_target
* chaos_premium
* sticky_flag
* no_trade_flag
* recommendation
* confidence
* reason_codes

Core models to implement:

1. Clock-only Poisson decay model

For O/U markets, fit pre-match total-goal lambda from the market-implied O2.5 price.

For O2.5:

`P(Over 2.5) = 1 - P(0) - P(1) - P(2)`

Assume Poisson goals with total lambda.

At score 0-0 and minute t:

`lambda_remaining = lambda_pre * (remaining_minutes / 90)`

Then:

`P(Over 2.5 | 0-0, t) = P(remaining_goals >= 3)`

At score 1-0 or 0-1:

`P(Over 2.5 | one goal, t) = P(remaining_goals >= 2)`

At score 2-0, 1-1, or 0-2:

`P(Over 2.5 | two goals, t) = P(remaining_goals >= 1)`

Use this as a baseline only. The market may correctly deviate from it due to live tempo, injuries, cards, or tactical state.

2. Market chaos premium

Define:

`chaos_premium = observed_over_mid - theoretical_over_probability`

For U2.5 trades, if chaos premium is high, the market thinks the game is more live than clock-only math says.

Flags:

* `chaos_premium > 0.04`: caution
* `chaos_premium > 0.07`: block new under entries
* `chaos_premium > 0.10`: no averaging down

3. Sticky no-decay detector

Define expected decay from clock-only model.

A market is sticky if:

* score is 0-0
* at least 8 minutes have passed
* theoretical O2.5 has declined by at least 4c from kickoff
* observed O2.5 hedge ask has declined by 2c or less
* spread is not abnormally wide

This indicates that the market is not giving cheap decay despite time passing.

Signal:

`NO_TRADE_STICKY_MARKET`

Interpretation:

Do not add Under. Wait.

4. Decay start detector

Track rolling slope of hedge best ask:

* 1-minute slope
* 3-minute slope
* 5-minute slope

For a U2.5 entry, decay starts when O2.5 best ask declines persistently:

`rolling_3m_slope <= -0.004 per minute for 2 consecutive minutes`

Also require:

* spread <= 4c
* depth at best ask >= minimum threshold
* no major event in last 90 seconds

Output:

* decay_start_minute
* decay_start_price
* decay_speed_cents_per_minute

5. Target-hit model

For each entry type, compute whether the hedge side reached required hedge price before an adverse event.

For every candidate entry:

* entry side
* entry price
* entry time
* target ROI: 5%, 8.7%, 10%
* required hedge price
* hedge side best ask over time
* depth available at or below required hedge price
* first time target is hit
* whether enough size was available
* first adverse event before target

Adverse events:

For U2.5:

* any goal
* red card that increases tempo
* penalty
* high chaos premium sustained

For BTTS No:

* underdog goal is more adverse than favorite goal
* both teams generating shots after first goal
* red card to defending team
* penalty
* high BTTS Yes premium

For +1.5 spread:

* favorite goal
* red card to underdog
* sustained favorite pressure

6. Post-goal reversion model

For every goal event:

Record:

* price 60 seconds before goal
* price immediately after goal
* price 1 minute after
* price 3 minutes after
* price 5 minutes after
* price 10 minutes after
* score state
* goal team
* favorite or underdog
* minute
* pre-goal market
* post-goal market

Metrics:

* jump_size
* reversion_3m
* reversion_5m
* reversion_10m
* half_life_to_decay
* did_price_revert_at_least_25pct
* did_price_revert_at_least_50pct
* did_next_goal_happen_before_reversion

For a post-goal dip strategy, do not buy immediately. Test rules:

* wait 3 minutes after goal
* buy only if O2.5 has stopped rising
* buy only if spread normalizes
* buy only if chaos premium is falling
* buy only if no second major chance/event
* buy smaller if underdog scored first

7. Correlation guardrail

The system must detect when the user holds multiple positions that lose in the same branch.

Example:

* BTTS No + U2.5 are strongly correlated.
* U2.5 + Under 3.5 are correlated.
* BTTS No + favorite clean sheet are correlated.
* Underdog +1.5 + Under 2.5 may be moderately correlated depending on favorite strength.

Create a correlation map:

* `same_low_goal_cluster`
* `favorite_control_cluster`
* `underdog_resistance_cluster`
* `chaos_over_cluster`
* `favorite_blowout_cluster`

Before recommending or allowing another entry, compute exposure by cluster.

Rule:

If unboxed exposure already exists in a cluster, do not add another large unboxed exposure in the same cluster.

8. Fillability model

A theoretical hedge hit is not enough. The model must ask whether the required price had enough size.

For each target hit:

* best ask <= required price
* cumulative ask depth at required price >= desired hedge size
* quote persisted at least N seconds
* spread <= max spread
* no immediate one-tick flicker

Fields:

* target_price
* depth_at_target
* depth_1c_better
* seconds_available
* likely_fill_size
* fillability_score

Example:

A hedge price touched 33c for 1 second with 20 shares available. That is not a real fill for a 500-share position.

9. Live opportunity score

Create a score from 0 to 100 for each possible entry.

Inputs:

* entry price attractiveness
* required hedge distance
* historical probability target hit before adverse event
* current liquidity
* spread
* orderbook depth
* current chaos premium
* current sticky flag
* time to expected decay window
* live tempo
* correlation with existing positions
* event risk

Example score formula:

`opportunity_score = 0.25 * target_hit_prob + 0.20 * liquidity_score + 0.15 * price_value_score + 0.15 * calm_state_score + 0.10 * spread_score + 0.10 * historical_decay_score - 0.20 * correlation_penalty - 0.20 * chaos_penalty`

This formula can be adjusted, but every component must be interpretable.

Backtests to run:

A. Pre-match U2.5 scalp

Entry:

* buy U2.5 at kickoff if price between 55c and 63c

Exit:

* buy O2.5 at required 5% hedge
* buy O2.5 at required 8.7% hedge
* stop if adverse goal before hedge
* track whether waiting until 5, 8, 10, 12, or 15 minutes improves edge

Variants:

* enter at kickoff
* enter at minute 5 if still 0-0
* enter at minute 8 if still 0-0 and not sticky
* enter at minute 10 if still 0-0 and chaos premium low
* skip if sticky flag true

B. BTTS No scalp

Entry:

* buy BTTS No pre-match or minute 5 if no goal

Exit:

* buy BTTS Yes at target hedge price
* stop if underdog scores first and market does not revert
* compare favorite-score-first vs underdog-score-first paths

C. Post-goal U2.5 dip buy

Entry:

* first goal occurs before minute 20
* wait 3 minutes
* buy U2.5 only if:

  * price dipped at least X cents
  * O2.5 stopped rising
  * chaos premium not increasing
  * spread normal
  * no second huge chance or card

Exit:

* buy O2.5 when target hit
* stop if second goal before reversion

Run separately for:

* favorite scores first
* underdog scores first
* match was high tempo before goal
* match was low tempo before goal

D. Spread +1.5 scalp

Entry:

* buy underdog +1.5 at kickoff if price reasonable

Exit:

* buy favorite -1.5 at target hedge
* evaluate scoreless decay
* evaluate favorite early goal branch
* block if favorite pressure too high

E. No-trade detector

Train/derive rules for when not to trade:

* sticky no-decay
* high chaos premium
* wide spread
* poor depth
* correlated exposure already open
* post-goal market not reverting
* early red/yellow risk
* live tempo too high

Useful outputs:

1. Match decay report

For each game:

* first goal minute
* U2.5 entry price
* O2.5 hedge target for 5%
* O2.5 hedge target for 8.7%
* minute hedge target hit
* did target hit before goal
* max drawdown before target
* liquidity at target
* final result
* notes

2. Market decay curve chart

Plot:

* observed O2.5 best ask
* theoretical O2.5 clock-only curve
* required hedge price for 5%
* required hedge price for 8.7%
* goal events
* red card events
* chaos premium

3. Post-goal reversion chart

Plot:

* O2.5 before goal
* spike after goal
* decay/reversion path
* second goal marker if any

4. Live dashboard

Panels:

* current open positions
* hedge side current ask
* required hedge price
* distance to hedge
* projected time to hedge
* liquidity at target
* chaos premium
* sticky flag
* correlation warning
* suggested action:

  * wait
  * hedge now
  * cancel same-side bids
  * no trade
  * reduce exposure
  * entry allowed

5. Research summary table

Columns:

* strategy
* entry rule
* average entry price
* hit rate 5%
* hit rate 8.7%
* median time to box
* probability goal before box
* average max drawdown
* average locked ROI when boxed
* expected value with execution
* worst 5% outcome
* recommended/not recommended

What to avoid:

* Do not use only final scores.
* Do not use only last traded prices.
* Do not use only midpoint.
* Do not infer fills at prices where there was no depth.
* Do not backtest with future information.
* Do not stack correlated exposures.
* Do not average down just because price moved against entry.
* Do not model every football statistic; only use stats that help classify current goal hazard, chaos, or market decay.

Priority build order:

Phase 1: Historical price research

* Discover all World Cup markets.
* Pull price history for U2.5/O2.5, BTTS, spreads.
* Align prices to kickoff and goal times.
* Build decay curves.
* Identify when hedge targets were hit.

Phase 2: Live recorder

* Build Polymarket WebSocket recorder.
* Save raw events.
* Derive top-of-book 1-second snapshots.
* Join to live match state.

Phase 3: Execution backtester

* Simulate strategy entries and hedges using best ask and depth.
* Include spread and slippage.
* Include fillability thresholds.
* Report realistic hit rates.

Phase 4: Live decision engine

* Show current opportunities.
* Warn about sticky markets.
* Warn about chaos premium.
* Warn about correlated exposure.
* Tell user exact hedge prices and share sizes.

Phase 5: Pattern discovery

Answer these questions empirically:

* Does the first 5–8 minutes actually fail to decay?
* Does decay usually start around 8, 10, 12, or 15 minutes?
* Which markets decay fastest: O/U 2.5, BTTS, spread, team totals?
* Which markets have enough depth to hedge?
* Does favorite-scoring-first post-goal dip buying work?
* Does underdog-scoring-first post-goal dip buying fail?
* What chaos premium threshold predicts under trades are bad?
* Does waiting 3–5 minutes after a goal improve dip-buy ROI?
* Which entry prices produce real boxability?
* Which markets are traps due to low liquidity?

The final product should be a research notebook plus a live dashboard. The research notebook proves the patterns. The dashboard executes the rules in real time.
