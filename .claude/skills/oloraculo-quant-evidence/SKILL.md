---
name: oloraculo-quant-evidence
description: Produce Oloraculo quantitative evidence for predictions, combo EV, hedge math, payoff masks, markouts, backtests, and PnL reports. Use when pricing Polymarket candidates, checking model calibration, proving edge, building paper/shadow tracking, or reviewing risk-adjusted sizing.
---

# Oloraculo Quant Evidence

## Purpose

Use this skill to keep probability, edge, and hedge claims falsifiable. It is not
enough for a candidate to look profitable; the evidence must show state space,
executable costs, liquidity limits, and realized follow-through.

## Required Reads

- `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`
- `docs/source-of-truth/POLYMARKET_COMBO_LAB.md`
- `Oloraculo.Web/Probability/`
- `Oloraculo.Web/Predictors/`
- `Oloraculo.Web/ComboLab/`
- Relevant tests under `Oloraculo.Web.Tests/`

## Core Rules

- Price buys at ask and sells at bid.
- Treat midpoint and last price as display-only.
- Compute payoffs over terminal states, not independent leg guesses.
- Preserve rejected and illiquid rows.
- Separate final-score prediction from hedge-path scalp recommendation.
- Never turn a model probability into a live trade instruction.

## Hedge Math

For entry price `a` and hedge price `b`:

```text
locked_profit_per_share = 1 - a - b
roi = (1 - a - b) / (a + b)
required_hedge_for_roi = 1 / (1 + target_roi) - a
```

Default target shares: 400. Always calculate breakeven, 2%, 5%, 8%, 10%, and
12%.

## Evidence Workflow

1. Define state space and settlement rules.
2. Map contracts to exact condition/token/line/period/team fields.
3. Compute payoff masks and bad-hole states.
4. Apply executable bid/ask/depth/freshness.
5. Track shadow candidates with markouts at 1s, 5s, 10s, 30s, and 60s.
6. Report queue/fillability denominator and realized hedge simulation.
7. Summarize PnL/EV/hour only after denominator and costs exist.

## Tests

- Payoff masks and state PnL.
- Scoreline/tournament distribution coverage.
- Hedge target formulas and partial equalization shares.
- Tail/truncation handling.
- Rejection reasons for stale, ambiguous, unmapped, or illiquid rows.
