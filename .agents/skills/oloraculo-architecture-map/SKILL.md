---
name: oloraculo-architecture-map
description: Audit and route Oloraculo work from the project source of truth. Use when Codex needs to understand current plans, backlog phases, ownership boundaries, Polytrade donor rules, or choose the right senior agent, skill, files, and verification gate before implementation.
---

# Oloraculo Architecture Map

## Purpose

Use this skill before broad work, ambiguous requests, or cross-cutting changes.
It turns the repo's plans into a concrete implementation route.

## Required Reads

- `AGENTS.md`
- `docs/source-of-truth/ACTIVE.md`
- `docs/source-of-truth/COMMANDS.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_BACKLOG.md`
- `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`
- `docs/source-of-truth/POLYMARKET_COMBO_LAB.md`

## Procedure

1. Run `git status --short` and note unrelated dirty files without reverting them.
2. Identify the active backlog phase and whether the request touches app, Rust,
   AWS, R2, feeds, cockpit UI, quant evidence, MCP/tooling, or security.
3. State the current inventory in three buckets: already built, partially built,
   missing.
4. Choose one primary agent and any reviewer agents.
5. Name the exact files likely to change and the expected verification gate.
6. If the request could cross a live-order, secret, data-refresh, archive-prune,
   or deployment boundary, invoke `oloraculo-security-boundary` before acting.

## Routing

- Architecture and phase sequencing: `chief-systems-architect`.
- AWS deployment/runtime: `aws-runtime-operator`.
- R2 archive/lake: `r2-archive-lake-engineer`.
- Rust CLOB/scanner hotpath: `rust-hotpath-engineer`.
- Feed status/adapters: `feed-status-integrator`.
- Cockpit visual/UI work: `cockpit-ux-engineer`.
- Prediction, EV, markouts, PnL: `quant-evidence-scientist`.
- Release evidence: `release-verification-lead`.
- MCP/agent/skill tooling: `mcp-toolsmith`.
- Secrets, side effects, live-order boundary: `security-risk-sentinel`.

## Output Shape

Return current state, gaps, next slice, owner agent/skill, files, verification
command, and unresolved risks.
