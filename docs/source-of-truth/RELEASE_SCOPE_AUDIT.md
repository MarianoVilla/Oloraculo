# Oloraculo Release Scope Audit
Date: 2026-06-18

## Current State
- Branch evidence from `git status --short --branch --untracked-files=all`: `main...origin/main` with many modified tracked files and a large set of untracked release, tooling, docs, Rust, agent, and reference-material paths.
- Existing unrelated dirty changes were observed and left untouched.
- `git diff --stat` reports 44 tracked files changed, with 10,982 insertions and 1,494 deletions. The largest tracked diff is `oloraculo-dev.out.log`.
- `git diff --stat` emitted LF-to-CRLF warnings for existing tracked files.
- `git check-ignore -v tools\release\check-host-prereqs.ps1 tools\release\test-container-smoke.ps1 oloraculo-dev.out.log` showed both `tools\release` scripts are ignored by `.gitignore:4:[Rr]elease/`.
- The same `git check-ignore` command produced no ignore match for `oloraculo-dev.out.log`; it appeared as a modified tracked file in current status and should not be part of release scope.
- The release-scope guard later exposed `oloraculo-dev.err.log` as another tracked runtime log; it belongs in the same remove-from-index class as `oloraculo-dev.out.log`.
- Release hygiene update: the first-release production scope is now staged, `git ls-files --others --exclude-standard` is empty, and deferred donor/reference folders are intentionally ignored rather than deleted.
- Staged release scope includes Oloraculo-owned app/runtime/test/docs/skills/MCP/Rust/Docker/CI/AWS scaffold files and removes tracked runtime logs from the index.

## Track For Production
- `AGENTS.md`
- `.agents/skills`
- `.codex/agents`
- `.codex/config.toml`
- `.github/workflows/dotnet.yml`
- `.dockerignore`
- `.editorconfig`
- `.mcp.json.example`
- `Dockerfile`
- `deploy/aws` production-safe docs/scripts, except `deploy/aws/oloraculo.aws.env.example`, which remains gated under secret-safe review
- `docs/source-of-truth`
- `docs/llms.txt`
- `design.md`
- `Oloraculo.Web/Archive`
- `Oloraculo.Web/ComboLab`
- `Oloraculo.Web/Feeds`
- `Oloraculo.Web/WorldCup`
- `Oloraculo.Web.Tests/Archive`
- `Oloraculo.Web.Tests/ComboLab`
- `Oloraculo.Web.Tests/Feeds`
- `Oloraculo.Web.Tests/WorldCup`
- `Cargo.toml`
- `Cargo.lock`
- `rust/oloraculo_hotpath`
- `tools/codex`
- `tools/docs`
- `tools/mcp`
- `tools/release`
- `tools/security`
- Release-scope note: `tools/release` is currently blocked by the broad `[Rr]elease/` ignore rule and needs an explicit allow-list or ignore-rule adjustment before it can be tracked.
- Release-scope note resolved: `.gitignore` now explicitly allows `/tools/release/*.ps1`, and `tools/release/check-release-scope.ps1` requires those scripts to be tracked.

## Track After Secret-Safe Review
- `Oloraculo.Web/appsettings.Production.json`
- `deploy/aws/oloraculo.aws.env.example`
- Every `*.env.example` category, including production/deploy examples, archive/validation tool examples, and local agent examples.
- Current `*.env.example` paths visible in status include `polytrade-agent/.env.example`, `tools/c123-market-archive/.env.example`, `tools/c123-market-validation/.env.example`, and `deploy/aws/oloraculo.aws.env.example`.
- These files should be reviewed to confirm they contain placeholders only, no live credentials, account identifiers that should remain private, wallet material, API keys, or operational secrets.
- First-release result: `Oloraculo.Web/appsettings.Production.json` and `deploy/aws/oloraculo.aws.env.example` are staged after the raw-secret scan passed; deferred donor/tool `.env.example` files remain ignored with their parent folders.

## Ignore Or Remove From Release Scope
- `oloraculo-dev.out.log`
- `oloraculo-dev.err.log`
- `.serena/`
- `.opencode/`
- `.claude/`
- `.pytest_cache/`
- `**/__pycache__/`
- `polytrade-agent/`
- `tools/c123-market-archive/`
- `tools/c123-market-validation/`
- `tools/c123-sports-scout-reference/`
- `tools/opencode/`
- `CLAUDE.md`
- `opencode.json`
- Local runtime databases and hot-cache files already covered by `.gitignore`.
- Current evidence: `oloraculo-dev.out.log` and `oloraculo-dev.err.log` were tracked runtime logs and need explicit removal from release scope plus ignore-rule coverage.
- Current decision: deferred donor/reference folders are preserved on disk but excluded from the first production release by `.gitignore`; `tools/release/check-release-scope.ps1` fails if they are tracked.

## Deferred Reference Material
- `polytrade-agent/`
- `docs/reference/c123/`
- `docs/vendor/polymarket-docs/`
- `tools/c123-market-archive/`
- `tools/c123-market-validation/`
- `tools/c123-sports-scout-reference/`
- `tools/polyfill-rs/`
- Deferred reference material remains in the workspace but is split from the first production release unless deliberately reviewed and assigned an ownership model.

## Nested Repo Decision
- `tools/polyfill-rs` must be explicitly decided as vendored source, submodule, or ignored before commit.
- First-release decision: `tools/polyfill-rs/` is ignored and blocked by the release-scope guard. Choose vendored source, submodule, or permanent ignore in a separate review before tracking it.

## Release Scope Guard
- `tools/release/check-release-scope.ps1` now requires key Oloraculo-owned release files and directories to be tracked.
- The same guard blocks raw env/log/key material, deferred donor/reference prefixes, and any untracked path that is not explicitly ignored.
- A passing guard means the working tree may still have modified tracked files, but it should not have ambiguous untracked release material.

## Verification Commands
- `git diff --check`
- `git diff --cached --check`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\check-release-scope.ps1`
- `cargo test`
- `dotnet test Oloraculo.sln`
- `python tools\mcp\test_oloraculo_context_server.py`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\test-container-smoke.ps1`
