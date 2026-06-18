---
name: oloraculo-mcp-tooling
description: Maintain Oloraculo Codex MCP servers, MCP profiles, project config, custom subagents, repo skills, and validation tooling. Use when adding or editing .codex/config.toml, .codex/agents, .agents/skills, custom MCP servers, skill metadata, or orchestration health checks.
---

# Oloraculo MCP Tooling

## Purpose

Use this skill to keep the agent/skill/MCP layer useful instead of theatrical.
Every custom MCP needs a smoke test. Every renamed agent or skill needs command
and documentation cleanup.

## Required Reads

- `.codex/config.toml`
- `.codex/agents/`
- `.agents/skills/`
- `.mcp.json.example`
- `tools/mcp/README.md`
- `tools/codex/check-oloraculo-codex.ps1`
- `docs/source-of-truth/DATA_AND_SECRETS.md`

## MCP Policy

- Always-on MCPs must not require secrets.
- Secret-backed MCPs must be disabled by default or rely on parent environment
  variables with placeholders only in committed files.
- Custom local MCPs must have a deterministic smoke test.
- Browser MCPs must redact network headers where supported.
- Read-only infrastructure MCPs are preferred unless a task explicitly needs
  writes.

## Procedure

1. Update `.codex/config.toml` first; update `.mcp.json.example` only when the placeholder example must stay aligned.
2. Add or update `tools/mcp/README.md` with enablement notes.
3. Validate TOML and JSON syntax.
4. Run custom MCP smoke tests.
5. Run skill validation for every new or changed skill.
6. Run `tools/codex/check-oloraculo-codex.ps1`.
7. Search for stale names with `rg`.

## Evidence

- List of enabled MCPs and why.
- List of disabled MCPs and required env/profile.
- Config parse result.
- MCP smoke-test result.
- Skill validation result.

## Vetoes

- Hardcoded tokens in examples.
- Optional MCPs blocking startup when env vars are absent.
- Stale command routes to deleted agents.
- Custom MCP server with no test.
