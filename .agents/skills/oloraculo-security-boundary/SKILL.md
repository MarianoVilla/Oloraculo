---
name: oloraculo-security-boundary
description: Protect Oloraculo secrets, side effects, local config, archive prune behavior, deployment operations, and live-order boundaries. Use when reading env or secret files, changing config, running refresh/export/deploy/archive commands, reviewing trading risk, or touching anything that could place, approve, cancel, or route orders.
---

# Oloraculo Security Boundary

## Purpose

Use this skill to separate allowed local implementation work from unsafe
disclosure, unsafe side effects, or accidental trading.

## Required Reads

- `AGENTS.md`
- `docs/source-of-truth/DATA_AND_SECRETS.md`
- `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`
- `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`
- `.gitignore`
- `.codex/config.toml`
- `.codex/agents/`
- `.agents/skills/`
- `.mcp.json.example`

## Secret Handling

- Read local config only when needed for the task.
- Never paste raw secret values, private keys, signed URLs, token fragments, or
  wallet material into committed files or final answers.
- Prefer `*_present=true/false`, hostnames, timestamps, and redacted errors.
- Use placeholder-only docs for env vars.

## Side-Effect Handling

Before running a side-effecting command, state expected side effects:

- data refresh;
- README snapshot export;
- archive upload/prune;
- deployment;
- service start/stop;
- MCP server using external credentials.

Afterward, report changed files or external limitations.

## Live-Order Boundary

Oloraculo is analysis-only until Phase 7 is explicitly approved and implemented.
Before any live order path exists, require:

- resident Rust supervisor;
- fresh quote intent;
- exact approval validation;
- WAL before side effect;
- private/reconciliation gate;
- kill switch and cancel-all proof;
- tiny caps;
- explicit operator arming.

## Output

Return `PASS`, `HOLD`, or `VETO` with specific reasons and mitigations.
