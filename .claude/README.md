# Oloraculo Orchestration Layer

This directory defines project-local guidance for AI-assisted Oloraculo work.

## Primitives

| Layer | Lives In | Use For |
| --- | --- | --- |
| Durable guide | `CLAUDE.md`, `AGENTS.md` | Fresh sessions, compaction recovery, safety rules |
| Source of truth | `docs/source-of-truth/` | Current context, architecture, backlog, commands, data/config rules |
| Skills | `.claude/skills/<name>/SKILL.md` | Repeatable playbooks for risky or specialized surfaces |
| Agents | `.opencode/agents/*.md`, `.claude/agents/*.md` | OpenCode and Claude role routing |
| Commands | `.opencode/commands/*.md` | Slash-command workflows |
| MCPs | `tools/mcp/`, `opencode.json`, `.mcp.json.example` | Context resources and tool integrations |
| Plugins | `.opencode/plugins/*.js` | Intentionally unused |

## Skills

- `oloraculo-architecture-map`: audit source-of-truth docs and route work.
- `oloraculo-aws-r2-ops`: AWS services, SSM runbooks, R2/S3 archive, retention, and materializers.
- `oloraculo-feed-hotpath`: feed status adapters, CLOB freshness, Rust hotpath contracts.
- `oloraculo-cockpit-parity`: Blazor cockpit UI and screenshot-backed visual parity.
- `oloraculo-quant-evidence`: prediction, combo EV, hedge math, markouts, and PnL evidence.
- `oloraculo-release-gate`: build/test/config/MCP/UI verification before done claims.
- `oloraculo-mcp-tooling`: MCP servers, profiles, agents, skills, commands, and health checks.
- `oloraculo-security-boundary`: secrets, side effects, archive prune, deploy, and live-order boundaries.

## Fresh Session Protocol

1. Read `CLAUDE.md` and `AGENTS.md`.
2. Read `docs/source-of-truth/ACTIVE.md`.
3. Run `git status --short` before edits.
4. Load the matching skill for the touched surface.
5. State the smallest intended change and verification gate.
6. Preserve unrelated dirty worktree changes.

## Boundary

Oloraculo may research and display World Cup and sports scalp candidates. It
must not place, approve, cancel, or route live orders. Read
`docs/source-of-truth/POLYMARKET_COMBO_LAB.md` and
`docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md` before market mapping,
payoff masks, EV, sizing, or handoff work.

## Verification Rule

Do not say work is done, fixed, passing, refreshed, published, calibrated, or
visually correct unless the response names the evidence. Usually that means
command output from `dotnet test Oloraculo.sln`; UI work also needs
browser/screenshot evidence when available.
