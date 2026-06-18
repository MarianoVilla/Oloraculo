# Complementary Strategy Research

Status: **analysis-only / WATCH_ONLY**. This note describes the generalized
research path for World Cup Edge Lab. It does not authorize live orders and does
not replace NativePM.

## Mechanism

The mechanism is structural consistency plus in-play repricing.

We buy one leg, then try to buy a related opposite or covering leg after a live
state move makes the second leg cheaper. The goal is a completed package whose
terminal payoff is at least the combined cost.

For terminal state `x`:

```text
cost = sum(shares_i * executable_price_i) + buffers
payoff(x) = sum(shares_i * 1[leg_i pays in x])
profit(x) = payoff(x) - cost
```

Classification:

| Label | Test |
| --- | --- |
| True positive lock | `min_x profit(x) > 0` and no uncovered terminal states |
| Break-even lock | `min_x profit(x) = 0` and no uncovered terminal states |
| Middle hedge | every terminal state pays, but cost is above the guaranteed floor; upside is in a middle bucket |
| Gap hedge | at least one reachable terminal state pays zero, but some states have upside |
| Correlated only | payoff coverage is not structural |

## Totals

For total goals `G = home + away` and half-goal line `m.5`:

```text
Over m.5 = [G >= m + 1]
Under m.5 = [G <= m]
```

Nested totals:

```text
Over 3.5 subset Over 2.5 subset Over 1.5
Under 1.5 subset Under 2.5 subset Under 3.5
```

Safe covering patterns include:

```text
Over 1.5 + Under 1.5   exact complement
Over 1.5 + Under 2.5   pays 2 if exactly 2 goals, else 1
Over 1.5 + Under 3.5   pays 2 if exactly 2 or 3 goals, else 1
Over 2.5 + Under 3.5   pays 2 if exactly 3 goals, else 1
```

The reverse direction leaves gaps:

```text
Under 1.5 + Over 3.5   loses on total goals 2 or 3
```

## Poisson decay / jump model

The model converts a market-implied total into a remaining-goals process. It is
not actual xG. It is a compact representation of the market's total-goal belief.

If Polymarket shows:

```text
Over 2.5 = 49c
Under 2.5 = 52c
```

Normalize:

```text
P(Over 2.5) = 0.49 / (0.49 + 0.52) = 48.5%
```

Find `lambda` such that:

```text
P(Poisson(lambda) >= 3) = 48.5%
```

That gives `lambda ≈ 2.61` market-implied total expected goals.

If the game is still 0-0 at minute `t`:

```text
mu_remaining = lambda * (90 - t) / 90
P(Over m.5) = P(Poisson(mu_remaining) >= required_remaining_goals)
```

For a scoreless minute, the over loses approximately:

```text
decay_per_minute = (lambda / 90) * P(Poisson(mu_remaining) = required_remaining_goals - 1)
```

The one-goal jump is controlled by the same threshold mass:

```text
goal_jump = P(Poisson(mu_remaining) = required_remaining_goals - 1)
```

So the same term explains both:

- scoreless decay helping unders
- early-goal jumps hurting unders and helping overs

## BTTS

`BTTS Yes = [home > 0 and away > 0]`.

`BTTS No = [home = 0 or away = 0]`.

Important implication:

```text
BTTS Yes subset Over 1.5
```

Therefore:

```text
BTTS No + Over 1.5
```

pays at least 1 in every terminal scoreline. It pays 2 on one-sided scores with
2+ goals, such as 2-0, 3-0, 0-2, and 0-3.

The opposite package:

```text
BTTS Yes + Under 1.5
```

is not a lock because one-sided multi-goal scores are uncovered.

## Moneyline and complements

Three-way moneyline is a partition:

```text
Home win, Draw, Away win
```

Buying all three is a true lock only if combined executable cost is below 1.

Binary complements create middle patterns:

```text
Not Home + Not Away   pays 2 on draw, 1 otherwise
Not Home + Not Draw   pays 2 on away, 1 otherwise
Not Away + Not Draw   pays 2 on home, 1 otherwise
```

## Exact score baskets

Exact score can cover the under side of a total if every required score exists.

Example:

```text
Over 2.5 + {0-0, 1-0, 0-1, 1-1, 2-0, 0-2}
```

pays at least 1 across terminal total-goal states, but only if Polymarket has
unambiguous exact-score tokens for every listed score and no hidden “other”
bucket issue. Otherwise verdict remains WATCH_ONLY.

## Evidence gates before anything beyond WATCH_ONLY

Single blocker: candidates do not yet carry a complete receive-time state bundle.

Required before promotion:

- exact fixture-to-event identity
- exact market/condition/token/outcome identity
- book receive time and raw payload hash
- executable bid/ask/depth, not midpoint or outcome price
- parsed score/live/elapsed state
- fee, slippage, partial-fill, and latency buffers
- calibrated model version and no-lookahead input hash
- payoff table hash and gap states
- authenticated fills and markouts if ever tested live through NativePM

Until then, every row is **WATCH_ONLY**.
