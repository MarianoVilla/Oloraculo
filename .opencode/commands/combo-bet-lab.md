---
description: Plan or build the next Oloraculo Combo Lab or sports scalp evidence slice.
agent: quant-evidence-scientist
---

Work on the Oloraculo Polymarket analysis request: $ARGUMENTS

Procedure:

1. Use `oloraculo-quant-evidence`.
2. Read `docs/source-of-truth/POLYMARKET_COMBO_LAB.md` and
   `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`.
3. Route identity/feed/freshness work to `feed-status-integrator`.
4. Route Rust CLOB/VWAP/scanner work to `rust-hotpath-engineer`.
5. Route UI/monitor work to `cockpit-ux-engineer`.
6. Route live-order, key, or handoff risk to `security-risk-sentinel`.
7. Keep Oloraculo analysis-only: no live orders, wallet keys, approvals, or
   execution endpoints.
8. Implement the smallest slice with focused tests and `dotnet test
   Oloraculo.sln` as the usual final gate.
