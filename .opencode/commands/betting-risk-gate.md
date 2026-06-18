---
description: Risk-review Oloraculo analysis candidates before any handoff or execution discussion.
agent: security-risk-sentinel
---

Review the Oloraculo candidate: $ARGUMENTS

Use `oloraculo-security-boundary` and `oloraculo-quant-evidence`.

Return exactly one verdict:

- `PASS_ANALYSIS`
- `HOLD`
- `VETO`

Include identity, price/liquidity, model freshness, state-PnL, sizing, feed
blockers, side effects, and live-order blockers. Oloraculo must remain
analysis-only.
