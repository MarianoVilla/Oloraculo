---
description: Read-only Polymarket market identity and CLOB/freshness mapping workflow for Oloraculo.
agent: feed-status-integrator
---

Map Polymarket markets for: $ARGUMENTS

Use `oloraculo-feed-hotpath`. Stay read-only. Do not call order endpoints.

Return:

- fixture/team mapping;
- event/market/condition/token/outcome fields;
- market type, line, period, settlement scope;
- bid/ask/size/freshness if available;
- stale/no-book/thin-depth blockers;
- reject reasons for unmapped rows.
