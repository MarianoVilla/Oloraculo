# Oloraculo Asset Decision Production Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Convert the current Oloraculo workspace into one clean, production-ready source of truth by deciding what to use, what to keep as reference, what to delete or ignore, and what to build next.

**Architecture:** Oloraculo owns the product, runtime, archive, feeds, monitor, and future live path. Donor material from C123, NativePM, Polytrade, OpenCode, or Claude can inform implementation only after it is ported into Oloraculo-owned code, tests, docs, and release gates.

**Tech Stack:** .NET 9 Blazor Server, EF Core SQLite, MudBlazor, xUnit, Rust hotpath crate, Cloudflare R2/S3-compatible archive, AWS EC2/container/systemd target, Codex-native agents/skills/MCP tooling, Playwright for monitor verification.

---

## Non-Negotiable Decisions

- Use `C:/Users/gianz/prediction/Oloraculo` as the only active workspace.
- Do not work from external `C:/123`; use only copied reference assets under `docs/reference/c123`.
- Use Codex-native project files as canonical: `AGENTS.md`, `.codex/config.toml`, `.codex/agents`, `.agents/skills`, and `tools/mcp`.
- Treat `.opencode` and `.claude` as compatibility mirrors only. They are not the source of truth.
- Keep all order execution disabled until Oloraculo has a separately approved live crate, approval gate, WAL, reconciliation, heartbeat, kill switch, caps, and release evidence.
- Remove or explicitly deprecate SofaScore references because the chosen path is OddsPapi, Databet, GRID, Polymarket, and Oloraculo-owned snapshots.
- Treat local disk as hot cache only. Durable market history belongs in R2/S3-compatible object storage.
- Treat `pmkey.txt`, `.env`, `appsettings.Development.json`, `.mcp.json`, and local secret files as sensitive local files that must not be printed or committed.

## What To Use As Canonical

- `docs/source-of-truth/ACTIVE.md`: short authority index and current-state checkpoint.
- `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`: production architecture and Oloraculo-owned runtime direction.
- `docs/source-of-truth/OLORACULO_PRODUCTION_READINESS_PLAN.md`: current production-readiness strategy.
- `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md`: current executable production board and acceptance evidence.
- `docs/source-of-truth/OLORACULO_PRODUCTION_BACKLOG.md`: backlog after the active board.
- `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md`: canonical sports scalp cockpit requirements.
- `docs/source-of-truth/POLYMARKET_COMBO_LAB.md`: canonical analysis-only Combo Lab requirements.
- `docs/source-of-truth/DATA_AND_SECRETS.md`: data, archive, local config, and secret-handling rules.
- `docs/source-of-truth/COMMANDS.md`: safe local command map.
- `docs/source-of-truth/OLORACULO_LIVE_PATH_DONOR_AUDIT.md`: lessons from donor live paths, not permission to inherit them.
- `Oloraculo.Web`: canonical Blazor app and .NET runtime.
- `Oloraculo.Web.Tests`: canonical .NET test suite.
- `rust/oloraculo_hotpath`: canonical Rust hotpath candidate, once wired into app snapshots.
- `deploy/aws`: canonical Oloraculo AWS deployment folder.
- `Dockerfile`: canonical container build target.
- `tools/mcp`: canonical custom context MCP.
- `tools/codex`: canonical Codex health checks.
- `.codex/agents` and `.agents/skills`: canonical agent and skill layer.

## What To Keep As Reference Only

- `docs/reference/c123`: copied C123 source material, runbooks, vendor notes, infra, and monitor design references.
- `docs/reference/c123/monitor-design`: visual parity target for the monitor, including `.dc.html` panels, screenshots, design-system bundle, and support script.
- `docs/reference/c123/vendor`: vendor notes for Polymarket, Databet, OddsPapi, LoL Esports, and data-use maps.
- `docs/reference/c123/infra/aws-sports-scout`: donor Terraform/runtime example only, not Oloraculo deployment authority.
- `docs/reference/c123/scripts`: donor AWS helper examples only.
- `tools/c123-market-validation`: donor/reference validation probes for venues and latency.
- `tools/c123-market-archive`: donor/reference archive/query utilities.
- `tools/c123-sports-scout-reference`: donor/reference Rust scanner; useful for ideas, not canonical hotpath.
- `docs/polymarket-sports-scalp-cockpit.html`, `docs/oloraculo-production-architecture.html`, and `docs/mcp-market-tooling-audit.html`: readable companion exports, not editing sources.
- `.opencode/*` and `.claude/*`: mirrors if those clients remain useful, otherwise removable maintenance weight.

## What Not To Use As Production Runtime

- External `C:/123`.
- NativePM or Polytrade services as active runtime.
- Donor root/Python services, donor systemd units, donor Terraform, donor AWS paths, or donor execution defaults.
- `polytrade-agent` as doctrine or runtime; it is an experimental Codex audit harness only.
- `/monitor` as the live production monitor in its current form; `/sports-scalp` is the real scanner surface today.
- Config-presence feed rows as proof of live source health.
- Generated logs, generated audit output, local SQLite databases, raw market books, DuckDB lakes, Parquet lakes, local hot cache files, or secret files as commit candidates.

## Documentation And Asset Map

- Top-level project docs: `README.md`, `AGENTS.md`, `CLAUDE.md`, `design.md`.
- Source-of-truth docs: `ACTIVE.md`, `COMMANDS.md`, `DATA_AND_SECRETS.md`, `COMPLEMENTARY_STRATEGY_RESEARCH.md`, `OLORACULO_LIVE_PATH_DONOR_AUDIT.md`, `OLORACULO_PRODUCTION_ARCHITECTURE.md`, `OLORACULO_PRODUCTION_BACKLOG.md`, `OLORACULO_PRODUCTION_READINESS_PLAN.md`, `OLORACULO_PRODUCTION_TODO.md`, `POLYMARKET_COMBO_LAB.md`, `POLYMARKET_SPORTS_SCALP_COCKPIT.md`, `POLYTRADE_UNIVERSE_AUDIT.md`, `SANTISIGNALS_HEATMAP_RESEARCH.md`.
- Generated/readable docs: `docs/polymarket-sports-scalp-cockpit.html`, `docs/oloraculo-production-architecture.html`, `docs/mcp-market-tooling-audit.html`.
- AWS docs/assets: `deploy/aws/README.md`, `deploy/aws/oloraculo.aws.env.example`, `Dockerfile`, `.dockerignore`.
- C123 reference docs/assets: `docs/reference/c123/README.md`, `docs/reference/c123/docs`, `docs/reference/c123/vendor`, `docs/reference/c123/scripts`, `docs/reference/c123/infra`, `docs/reference/c123/monitor-design`.
- Monitor design assets: `BoxingMonitor.dc.html`, `AlertsPanel.dc.html`, `BlotterPanel.dc.html`, `ClobPanel.dc.html`, `DecayPanel.dc.html`, `EventPanel.dc.html`, `HedgePanel.dc.html`, `PlaybookPanel.dc.html`, `RoutePanel.dc.html`, `ScannerPanel.dc.html`, `TapePanel.dc.html`, `support.js`, `_ds`, `screenshots`, `uploads`.
- App static assets: `Oloraculo.Web/wwwroot/app.css`, `Oloraculo.Web/wwwroot/favicon.png`, `Oloraculo.Web/wwwroot/flags`, `Oloraculo.Web/wwwroot/lib/bootstrap`.
- Seed data assets: `Oloraculo.Web/Data/wc2026_groups.csv`, `historical_results.csv`, `goalscorers.csv`, `fifa_rankings.csv`, `elo_snapshot.csv`.
- AI/tooling assets: `.codex`, `.agents`, `.opencode`, `.claude`, `.mcp.json.example`, `opencode.json`, `tools/mcp`, `tools/codex`, `tools/opencode`.
- Experimental harness assets: `polytrade-agent`, `tooling/goal_strength_calibration.py`.
- Rust assets: root `Cargo.toml`, root `Cargo.lock`, `rust/oloraculo_hotpath`, `tools/c123-sports-scout-reference`.

## Build Target

The build path is:

1. Clean and fence the workspace.
2. Make docs internally consistent.
3. Make feed status honest and testable.
4. Wire Rust hotpath snapshots into the app.
5. Build AWS/R2 runtime from Oloraculo-owned deployment assets.
6. Replace the mock monitor with a real monitor that matches or beats the C123 visual reference.
7. Add paper/shadow evidence and markout loops.
8. Design live execution as a separate, disabled Oloraculo Rust boundary.

## File Structure To Create Or Modify

- Create: `docs/source-of-truth/ASSET_DECISIONS.md` with the durable use/reference/delete policy.
- Modify: `docs/source-of-truth/ACTIVE.md` to point to `ASSET_DECISIONS.md`.
- Modify: `README.md` to align live-boundary language and add current routes.
- Modify: `design.md` to add an authority/status banner.
- Modify: `docs/source-of-truth/DATA_AND_SECRETS.md` to remove or deprecate SofaScore.
- Modify: `Oloraculo.Web/Feeds/FeedStatusService.cs` to remove or deprecate SofaScore rows.
- Modify: `Oloraculo.Web.Tests/Archive/ObjectArchiveServiceTests.cs` or add feed-status tests for the source-policy change.
- Modify: `.gitignore` to ignore generated audit/log/cache files that are still leaking.
- Modify: `polytrade-agent/README.md` or relocate `polytrade-agent` under `tools/experimental/polytrade-supervisor`.
- Delete or ignore: `polytrade-agent/audit-output.md`, `polytrade-agent/README1.MD`, local runtime logs.
- Create: `tools/security/check-no-raw-secrets.ps1` if absent.
- Create: `tools/security/check-no-live-order-path.ps1` if absent.
- Modify: `tools/codex/check-oloraculo-codex.ps1` and `tools/opencode/check-oloraculo-opencode.ps1` only if their expectations no longer match the intended toolset.
- Modify: `deploy/aws` after the release baseline is clean, not before.

---

### Task 1: Freeze The Asset Decision Source Of Truth

**Files:**
- Create: `docs/source-of-truth/ASSET_DECISIONS.md`
- Modify: `docs/source-of-truth/ACTIVE.md`

- [ ] **Step 1: Create `ASSET_DECISIONS.md`**

Add the use/reference/delete policy from this plan with these required sections:

```markdown
# Oloraculo Asset Decisions

Date: 2026-06-18

## Canonical

- `Oloraculo.Web`
- `Oloraculo.Web.Tests`
- `rust/oloraculo_hotpath`
- `deploy/aws`
- `docs/source-of-truth`
- `.codex/agents`
- `.agents/skills`
- `tools/mcp`
- `tools/codex`

## Reference Only

- `docs/reference/c123`
- `tools/c123-market-validation`
- `tools/c123-market-archive`
- `tools/c123-sports-scout-reference`
- `.opencode`
- `.claude`
- `polytrade-agent`

## Excluded From Production Runtime

- External `C:/123`
- NativePM runtime services
- Polytrade runtime services
- Donor AWS/systemd/Terraform paths
- Generated logs
- Raw local market-data archives
- Local secrets

## Current Production Rule

Oloraculo is watch-only and analysis-only until the live execution boundary is
designed, tested, approved, and released as a separate Oloraculo-owned runtime.
```

- [ ] **Step 2: Link it from `ACTIVE.md`**

Add one bullet under Current State:

```markdown
- Asset and reference decisions are tracked in `docs/source-of-truth/ASSET_DECISIONS.md`; external donor material is reference-only unless ported into Oloraculo-owned code with tests.
```

- [ ] **Step 3: Verify**

Run:

```powershell
python tools\mcp\test_oloraculo_context_server.py
```

Expected: exit code `0`.

### Task 2: Fix Documentation Drift

**Files:**
- Modify: `README.md`
- Modify: `design.md`
- Modify: `docs/source-of-truth/DATA_AND_SECRETS.md`
- Modify: `docs/source-of-truth/POLYMARKET_SPORTS_SCALP_COCKPIT.md` if it references external donor paths

- [ ] **Step 1: Update README live-boundary wording**

Replace language that sends real-money execution to older external systems with:

```markdown
Oloraculo is currently watch-only and analysis-only. Future live execution must
be Oloraculo-owned, separately approved, disabled by default, and protected by
approval, WAL, reconciliation, heartbeat, kill switch, capital caps, and release
evidence.
```

- [ ] **Step 2: Add current routes to README**

Add:

```markdown
Current operator routes:

- `/sports-scalp`: active watch-only Polymarket sports scanner.
- `/combo-lab`: analysis-only Combo Lab.
- `/monitor`: monitor visual work in progress.
- `/healthz`: container/runtime health.
- `/snapshot.json`: sanitized read-only snapshot endpoint.
```

- [ ] **Step 3: Add a banner to `design.md`**

Add this at the top:

```markdown
> Status: product/design brief. Implementation authority lives in
> `docs/source-of-truth/ACTIVE.md` and the linked source-of-truth documents.
```

- [ ] **Step 4: Remove or deprecate SofaScore from secret docs**

In `DATA_AND_SECRETS.md`, replace SofaScore token guidance with:

```markdown
- SofaScore is not an active Oloraculo feed. Do not add SofaScore credentials
  unless a future source decision explicitly reactivates it.
```

- [ ] **Step 5: Verify doc references**

Run:

```powershell
rg -n "C:\\\\123|SofaScore|SOFASCORE|NativePM|Polytrade" README.md design.md docs/source-of-truth
```

Expected: any remaining matches explicitly say reference-only, donor-only, disabled, or deprecated.

### Task 3: Clean Generated And Experimental Files

**Files:**
- Modify: `.gitignore`
- Modify: `polytrade-agent/README.md`
- Delete or ignore: `polytrade-agent/audit-output.md`
- Delete or ignore: `polytrade-agent/README1.MD`
- Delete or ignore: `oloraculo-dev.out.log`
- Delete or ignore: `oloraculo-dev.err.log`

- [ ] **Step 1: Extend `.gitignore`**

Add:

```gitignore
## Local logs and generated audit output
*.out.log
*.err.log
polytrade-agent/audit-output.md
polytrade-agent/README1.MD

## Local tool state
.serena/
.pytest_cache/
**/__pycache__/
```

- [ ] **Step 2: Fix `polytrade-agent/README.md` path language**

Replace old sibling-path setup with:

```markdown
Run from `C:\Users\gianz\prediction\Oloraculo\polytrade-agent`.
Set `POLYTRADE_REPO=C:\Users\gianz\prediction\Oloraculo` in `.env`.
This harness is experimental and must not define Oloraculo production doctrine.
```

- [ ] **Step 3: Verify ignored candidates**

Run:

```powershell
git status --short --ignored
```

Expected: generated logs, generated audit output, caches, local secrets, and local DBs are ignored or intentionally untracked for review.

### Task 4: Restore Release Guard Scripts

**Files:**
- Create: `tools/security/check-no-raw-secrets.ps1`
- Create: `tools/security/check-no-live-order-path.ps1`
- Modify: `tools/codex/check-oloraculo-codex.ps1` only if paths differ
- Modify: `tools/opencode/check-oloraculo-opencode.ps1` only if paths differ

- [ ] **Step 1: Add raw-secret scanner**

Implement a PowerShell scanner that checks committed and unignored files for raw private keys, API-key assignments, wallet/private-key markers, and `.env` leakage. It must skip vendor bundles, Bootstrap assets, flag SVGs, binary images, and generated directories.

- [ ] **Step 2: Add live-order scanner**

Implement a PowerShell scanner that allows docs discussing disabled future live execution but fails on active order-submit code paths outside approved reference folders.

- [ ] **Step 3: Verify**

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\codex\check-oloraculo-codex.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\opencode\check-oloraculo-opencode.ps1
```

Expected: exit code `0` for each command.

### Task 5: Make Feed Status Honest

**Files:**
- Modify: `Oloraculo.Web/Feeds/FeedStatusService.cs`
- Modify: `Oloraculo.Web/Feeds/FeedStatusModels.cs` only if schema fields are missing
- Add or modify: `Oloraculo.Web.Tests/*/FeedStatusServiceTests.cs`
- Modify: `docs/source-of-truth/DATA_AND_SECRETS.md`

- [ ] **Step 1: Remove active SofaScore rows**

Replace SofaScore rows with either no row or a deprecated row that cannot be mistaken for a configured feed:

```csharp
new FeedStatusRow(
    Source: "SofaScore",
    Component: "Deprecated",
    State: FeedReadinessState.NotConfigured,
    Present: false,
    FreshnessSeconds: null,
    Message: "Not an active Oloraculo feed. Use OddsPapi, Databet, GRID, Polymarket, and Oloraculo snapshots.",
    Blockers: ["SOFASCORE_DEPRECATED"]);
```

- [ ] **Step 2: Add deterministic tests**

Add tests that assert:

```text
SofaScore is not reported as ready.
OddsPapi/Databet/GRID/Polymarket/R2 status rows never expose secret values.
Missing credentials produce blockers, not fake readiness.
```

- [ ] **Step 3: Verify**

Run:

```powershell
dotnet test Oloraculo.sln --filter FeedStatus
```

Expected: feed-status tests pass.

### Task 6: Promote `/sports-scalp` Into The Real Monitor Path

**Files:**
- Modify: `Oloraculo.Web/Components/Pages/SportsScalp.razor`
- Modify: `Oloraculo.Web/Components/Pages/BoxingMonitor.razor`
- Modify: `Oloraculo.Web/Components/Layout/NavMenu.razor`
- Modify: `Oloraculo.Web/wwwroot/app.css`
- Reference: `docs/reference/c123/monitor-design`

- [ ] **Step 1: Decide route names**

Use:

```text
/sports-scalp = real watch-only scanner
/monitor = real operator monitor after parity work
/boxing-monitor = reference/demo only or removed from nav
```

- [ ] **Step 2: Build monitor parity from copied reference assets**

Use these assets:

```text
docs/reference/c123/monitor-design/BoxingMonitor.dc.html
docs/reference/c123/monitor-design/ScannerPanel.dc.html
docs/reference/c123/monitor-design/ClobPanel.dc.html
docs/reference/c123/monitor-design/HedgePanel.dc.html
docs/reference/c123/monitor-design/RoutePanel.dc.html
docs/reference/c123/monitor-design/TapePanel.dc.html
docs/reference/c123/monitor-design/AlertsPanel.dc.html
docs/reference/c123/monitor-design/screenshots
```

- [ ] **Step 3: Verify visually**

Run the app and capture desktop and mobile screenshots with Playwright.

Expected:

```text
Monitor is data-dense, visually aligned with the reference, has no overlapping text, and clearly labels watch-only state.
```

### Task 7: Wire Rust Hotpath Snapshots

**Files:**
- Modify: `rust/oloraculo_hotpath`
- Modify or create: .NET integration boundary under `Oloraculo.Web`
- Modify: `/snapshot.json` producer in `Program.cs` or a dedicated service
- Add tests in `Oloraculo.Web.Tests`

- [ ] **Step 1: Freeze snapshot schema**

Define one JSON contract shared by:

```text
Rust hotpath
.NET app
/snapshot.json
monitor UI
AWS runtime
R2 archive
```

- [ ] **Step 2: Add stale/ok tests**

Add tests that assert:

```text
fresh snapshot => OK
old snapshot => STALE_SNAPSHOT
missing snapshot => STALE_SNAPSHOT
schema mismatch => BLOCKED
```

- [ ] **Step 3: Verify**

Run:

```powershell
cargo test
dotnet test Oloraculo.sln --filter Snapshot
```

Expected: Rust and .NET snapshot tests pass.

### Task 8: Build Oloraculo-Owned AWS Runtime

**Files:**
- Modify: `deploy/aws`
- Modify: `Dockerfile` only if runtime packaging needs changes
- Create scripts under `tools/release` or `deploy/aws/scripts`
- Update: `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md`

- [ ] **Step 1: Keep donor AWS separate**

Do not copy active runtime decisions from:

```text
docs/reference/c123/infra/aws-sports-scout
```

- [ ] **Step 2: Define Oloraculo AWS target**

Use:

```text
EC2 or container host
systemd-managed app service
AWS Secrets Manager for env
R2/S3-compatible archive variables
/healthz smoke check
/snapshot.json smoke check
rollback command
logs command
```

- [ ] **Step 3: Verify**

Run local build and smoke first:

```powershell
dotnet test Oloraculo.sln
cargo test
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\release\test-container-smoke.ps1
```

Expected: all commands exit `0` before AWS deployment.

### Task 9: Create Evidence Loop Before Any Live Path

**Files:**
- Modify or create under `Oloraculo.Web/ComboLab/Monitor`
- Modify or create under `Oloraculo.Web.Tests/ComboLab`
- Update: `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md`

- [ ] **Step 1: Add paper/shadow lifecycle**

Model candidate lifecycle:

```text
detected -> qualified -> shadow_submitted -> shadow_filled_or_missed -> markout_recorded -> replayed
```

- [ ] **Step 2: Add markouts**

Record:

```text
1s, 5s, 10s, 30s, 60s markout
visible-depth fillability
queue/partial-fill approximation
post-hedge PnL
```

- [ ] **Step 3: Verify**

Run:

```powershell
dotnet test Oloraculo.sln --filter ComboLab
```

Expected: evidence lifecycle tests pass.

### Task 10: Final Release Baseline

**Files:**
- Review entire repo scope
- Update docs only with evidence from fresh commands

- [ ] **Step 1: Run full local gate**

Run:

```powershell
dotnet test Oloraculo.sln
cargo test
python tools\mcp\test_oloraculo_context_server.py
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-raw-secrets.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\security\check-no-live-order-path.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\codex\check-oloraculo-codex.ps1
pwsh -NoProfile -ExecutionPolicy Bypass -File tools\opencode\check-oloraculo-opencode.ps1
```

Expected: every command exits `0`.

- [ ] **Step 2: Review release scope**

Run:

```powershell
git status --short
git diff --stat
```

Expected:

```text
Every modified/untracked file is intentionally kept, ignored, moved, or deleted.
```

- [ ] **Step 3: Commit in logical slices**

Use slices:

```text
docs/source-of-truth
security/release gates
feed status
monitor parity
rust snapshot
aws runtime
evidence loop
```

## Self-Review

- Spec coverage: the plan covers documentation, assets, app runtime, monitor, feeds, Rust, AWS, agents, skills, MCPs, donor/reference material, generated files, secrets, and release gates.
- Placeholder scan: every implementation task names files, exact commands, and expected outcomes.
- Type consistency: Oloraculo is consistently the canonical runtime; donor material remains reference-only unless ported into Oloraculo-owned code with tests.
