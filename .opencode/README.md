# Oloraculo OpenCode Layer

This directory makes the Oloraculo operating rules native to OpenCode.

## Files

- `opencode.json` loads project instructions, skills, MCP definitions, permissions, tool output limits, and default agent.
- `.opencode/agents/` defines the senior Oloraculo roster:
  architecture, AWS runtime, R2 archive, Rust hotpath, feed status, cockpit UI,
  quant evidence, security/risk, release verification, and MCP tooling.
- `.opencode/commands/` provides slash-command prompts for resume, planning,
  verification, health, data/feed refresh, snapshot export, combo lab, market
  mapping, and risk gates.
- `.opencode/plugins/` is intentionally unused. Oloraculo does not install a
  secret-blocking plugin; local files remain readable by project convention.

## Reload Rule

OpenCode loads config, agents, commands, plugins, and MCPs at startup. Restart
OpenCode after changing any file in this directory or `opencode.json`.

## MCPs

The default keyless MCPs are:

- `oloraculo-context`
- `context7`
- `chrome-devtools`
- `playwright`
- `serena`

Credentialed or infrastructure-backed MCPs stay disabled until explicitly
configured.

## Core Commands

- `/resume-oloraculo` rebuilds current context and routes work.
- `/plan-next` performs a read-only planning pass.
- `/verify-oloraculo` runs relevant verification gates.
- `/combo-bet-lab` routes Polymarket analysis, EV, and evidence work.
- `/polymarket-market-map` performs read-only market identity and freshness mapping.
- `/betting-risk-gate` returns `PASS_ANALYSIS`, `HOLD`, or `VETO`.
