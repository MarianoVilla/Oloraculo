# Release Hygiene Execution Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the current Oloraculo production layer intentionally reviewable by staging production files and excluding donor/reference/local-only material from the first release.

**Architecture:** Keep Oloraculo-owned runtime code, tests, docs, skills, MCPs, Rust hotpath, Docker, CI, release/security scripts, and AWS deploy scaffolding in release scope. Keep C123 reference corpora, vendored Polymarket docs, polytrade-agent, c123 validation/archive/scout tools, and nested `tools/polyfill-rs` out of first release until a separate review decides ownership.

**Tech Stack:** Git, PowerShell release checks, .NET, Rust, Docker, Oloraculo source-of-truth docs.

---

### Task 1: Freeze First-Release Scope

**Files:**
- Modify: `.gitignore`
- Modify: `docs/source-of-truth/RELEASE_SCOPE_AUDIT.md`
- Modify: `tools/release/check-release-scope.ps1`

- [ ] Add explicit ignore rules for deferred folders and files:
  - `/docs/reference/c123/`
  - `/docs/vendor/polymarket-docs/`
  - `/polytrade-agent/`
  - `/tools/c123-market-archive/`
  - `/tools/c123-market-validation/`
  - `/tools/c123-sports-scout-reference/`
  - `/tools/polyfill-rs/`
- [ ] Add release-scope guard failures for those blocked prefixes if they are ever tracked accidentally.
- [ ] Update `RELEASE_SCOPE_AUDIT.md` to say these folders are preserved locally but intentionally excluded from the first production release.
- [ ] Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1`.

### Task 2: Stage Oloraculo-Owned Release Files

**Files:**
- Stage: Oloraculo app/runtime/test/docs/skills/MCP/Rust/Docker/CI/deploy files.
- Remove from index: `oloraculo-dev.out.log`, `oloraculo-dev.err.log`.

- [ ] Stage `.agents`, `.codex`, `.github`, `.dockerignore`, `.editorconfig`, `.mcp.json.example`, `.opencode`, `AGENTS.md`, `CLAUDE.md`, `Cargo.toml`, `Cargo.lock`, `Dockerfile`, `deploy/aws`, `design.md`, `docs/source-of-truth`, `docs/llms.txt`, `docs/*.html`, `docs/superpowers/plans`, `Oloraculo.Web`, `Oloraculo.Web.Tests`, `README.md`, `rust/oloraculo_hotpath`, `tools/codex`, `tools/docs`, `tools/mcp`, `tools/opencode`, `tools/release`, and `tools/security`.
- [ ] Remove tracked runtime logs from the index while preserving local files through ignore coverage.
- [ ] Confirm `git status --short --untracked-files=all` shows no untracked production-scope files.

### Task 3: Verify Release Boundary

**Files:**
- Read-only verification.

- [ ] Run `git diff --check`.
- [ ] Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1`.
- [ ] Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1`.
- [ ] Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1`.
- [ ] Run `python tools\mcp\test_oloraculo_context_server.py`.
- [ ] Run `cargo test`.
- [ ] Run `dotnet test Oloraculo.sln`.
- [ ] Run `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\test-container-smoke.ps1`.

### Task 4: Record Evidence

**Files:**
- Modify: `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md`
- Modify: `docs/source-of-truth/RELEASE_SCOPE_AUDIT.md`

- [ ] Add fresh validation counts and the final release boundary decision.
- [ ] Leave AWS deploy and live configured-source smoke as pending until credentials are loaded into the isolated runtime and smoke evidence is captured.
