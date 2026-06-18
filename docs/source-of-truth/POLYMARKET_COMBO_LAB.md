# Polymarket Combo Lab

World Cup Edge Lab may become the read-only model and combo-EV lab for 2026 FIFA World
Cup Polymarket markets. It must not become a live trading executor.

For the active all-sports / World-Cup-priority two-leg scalp workflow, use
`docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md` as the canonical
source of truth. Combo Lab remains a component of that read-only cockpit, not a
live execution surface.

## Boundary

World Cup Edge Lab owns:

- football fair values and scoreline/tournament state distributions
- Polymarket event/market identity mapping as read-only metadata
- contract payoff masks over model states
- combo EV, state PnL, sizing suggestions, and evidence reports
- Blazor cockpit surfaces for model-vs-market review

World Cup Edge Lab does not own:

- live order placement
- wallet keys, signatures, approvals, WAL, or cancellations
- production NativePM runtime controls

Existing Rust NativePM/polytrade services are donor evidence only. Any future
live order path must be rebuilt under Oloraculo, Rust-first, disabled by default,
and pass Oloraculo Phase 7 gates.

## Required Combo Candidate Packet

Every candidate row must identify:

- fixture, teams, kickoff/live minute, and source timestamp
- Polymarket event slug/id, market id, condition id, token id, outcome side
- market type: moneyline, team total, match total, BTTS, handicap, correct score,
  qualify/group/tournament, or rejected/unknown
- exact line/period/team/outcome and resolution-rule text when available
- executable bid/ask and available size; midpoint is presentation only
- model version, scoreline/tournament distribution id, and input source hash
- contract payoff mask and combo state-PnL table
- EV, ROI, max loss, probability of loss, break-even probability, and suggested
  capped size
- reject reasons for unmapped, stale, illiquid, or ambiguous contracts

## Market Mapping Rules

- Do not guess team identity from title text alone when token/condition mapping is
  available.
- Prefer documented Polymarket fields (`sportsMarketType`, `line`, condition id,
  CLOB token id, event/team context) over title regexes. Regex is a fallback only.
- Reject ambiguous markets instead of forcing a mapping.
- Exact score, totals, handicaps, and team totals must carry line, period, and
  settlement scope.
- Tournament markets must use tournament-state simulation, not a single-match
  scoreline grid.
- Store reject reasons; discarded rows are evidence.

## Payoff And EV Rules

- Compute payoffs over terminal states, not over independent leg assumptions.
- Combo legs are correlated through the shared scoreline/tournament state.
- Use ask price for buys and bid price for sells; do not use midpoint as an
  executable cost.
- Show the bad-hole states explicitly, e.g. `team scores but does not win`.
- Report tail handling when the scoreline grid truncates high-goal states.
- Sizing suggestions are analysis only until a separate Oloraculo live risk gate
  exists.

## 2026 FIFA World Cup Polymarket Discovery Surface

Current docs/API discovery path:

- `GET /sports` identifies FIFA World Cup as sport `fifwc`, tag `102232`, series
  `11433`.
- `GET /sports/market-types` provides documented soccer market families.
- `GET /markets/keyset?tag_id=102232&related_tags=true&sports_market_types=...` is the
  primary family-level market scan.
- `GET /events/keyset?series_id=11433&include_children=true&include_best_lines=true`
  is the event/fixture scan.
- `GET https://combos-rfq-api.polymarket.com/v1/rfq/combo-markets` is the public
  combo-leg catalog.
- Market websocket is for live updates/new markets; it is not the primary static
  discovery source.

Scoreline-grid coverage now maps: moneyline, spread, match total, team total,
BTTS, and exact score. Player props, corners, half markets, first-to-score, and
tournament futures are classified but rejected/priced only after the required
submodel exists.

The read-only universe report joins Gamma markets to Combo RFQ eligibility by
condition id first, then slug as a fallback, and reports counts by market family,
model-coverage class, combo eligibility, and source-market reject state. This is
a map for pricing and review; it is not an edge claim.

The in-app Combo Lab monitor lives at `/combo-lab`. It refreshes the current
Polymarket World Cup universe, shows source blockers and model-coverage gaps,
joins Combo RFQ eligibility, and can run an unsaved tournament Monte Carlo board
for analysis context. It remains analysis-only and contains no live-order path.

## Fixture Burden And Pre-Registration

The monitor includes an analysis-only World Cup burden gate (`wc26-burden-v1`).
The score is not usable unless each fixture has:

- kickoff UTC
- venue/city
- roof/open-air/climate-control state
- WBGT estimate
- prior travel distance
- eastward timezone shift
- rest days
- altitude meters

The burden score combines heat, travel, rest, and altitude into a 0-100 feature.
Missing inputs produce visible blockers and low confidence; they must not be
converted into an EV claim.

After a Combo Lab tournament simulation, the page creates an unsaved
pre-registration checkpoint hash. The checkpoint binds the model name,
simulation count, projection input hash, universe counts, burden coverage, source
manifest, and required validation gates. Its verdict is deliberately
`HOLD_ANALYSIS_ONLY` until OOS calibration, sharp comparison, feature ablation,
and execution-cost gates exist.

## Default Build Order

1. Contract ontology and fake Polymarket fixtures.
2. Generic scoreline payoff masks and unit tests.
3. Read-only Polymarket Gamma/CLOB price snapshots with fake HTTP tests.
4. Combo EV/sizing calculator.
5. Persistent candidate ledger and Combo Lab UI.
6. Optional analysis handoff artifact for candidates; no execution from World Cup Edge Lab.

## Verification

Normal code changes still end with:

```powershell
dotnet test Oloraculo.sln
```

Browser/UI work should additionally use Playwright or Chrome DevTools evidence
after the app is running.
