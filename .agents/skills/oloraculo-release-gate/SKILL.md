---
name: oloraculo-release-gate
description: Verify Oloraculo changes before done or ready claims. Use for build/test gates, Rust tests, UI screenshot evidence, Codex config and skill validation, MCP validation, compatibility mirror checks, CI checks, coverage, and final release-readiness summaries.
---

# Oloraculo Release Gate

## Purpose

Use this skill before saying a change is done, ready, passing, deployed,
visually correct, or production-shaped.

## Required Reads

- `docs/source-of-truth/COMMANDS.md`
- `tools/codex/check-oloraculo-codex.ps1`
- `tools/opencode/check-oloraculo-opencode.ps1`
- Changed files and matching tests

## Default Gates

```powershell
git status --short
dotnet build Oloraculo.sln
dotnet test Oloraculo.sln
```

Use `cargo test` when Rust changed. Use browser screenshots for UI claims. Use
`python tools\mcp\test_oloraculo_context_server.py` and
`pwsh -NoProfile -ExecutionPolicy Bypass -File tools/codex/check-oloraculo-codex.ps1`
when MCP/Codex tooling changed.

## Procedure

1. Inspect dirty worktree and diff.
2. Choose focused tests based on changed surface.
3. Run config validation for JSON, TOML, agents, skills, and MCP files.
4. Broaden to full solution tests when shared code, startup, Razor, config, or
   release claims are involved.
5. Report commands, pass/fail summaries, and unverified surfaces.

## UI Evidence

For cockpit/monitor UI:

- start the app or use the existing dev server;
- capture desktop and mobile screenshots;
- verify text does not overlap;
- verify refresh/loading/error states if relevant.

## Vetoes

- Done claims without exact evidence.
- Ignoring failures without inspection.
- Treating generated files as harmless without checking them.
- Leaving optional credential-backed MCP failures as startup blockers.
