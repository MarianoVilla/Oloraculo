# AI Tooling / MCP / Plugin Adaptation Plan

Date: 2026-06-18

Source inputs:

- `C:/Users/gianz/OneDrive/Escritorio/deep-research-report.md`
- `C:/Users/gianz/.codex/attachments/e19523f8-d922-4a60-a47a-8488d099504f/pasted-text.txt`
- Current Oloraculo repo audit at `C:/Users/gianz/prediction/Oloraculo`

## 1. Executive Summary

Short answer:

- Should this project use MCP now? Yes, but only as a narrow read-only control and context layer. The current `oloraculo-context` MCP is the right foundation.
- Should it use a native plugin? No, not for the core project. Oloraculo is not a Unity, Unreal, Godot, Figma, Photoshop, or editor-hosted project. A native plugin would add cost without matching the runtime.
- Should it use editor scripting? No for application behavior. Use .NET, Rust, PowerShell, Docker, and AWS/SSM-native tooling instead.
- Should it defer custom tooling? Defer broad custom tooling. The next useful step is a small read-only extension to the existing MCP, not a new omnipotent bridge.

Recommended direction:

Use MCP for project context, source-of-truth lookup, safe routing, do-not-touch validation, release gate summaries, and verification planning. Keep mutation in normal repo edits, shell commands, CI, and explicit human-approved tasks. Do not add an arbitrary shell MCP, broad file-write MCP, live-operation MCP, or secret-reading MCP.

## 2. Project Audit

Detected project type:

- Production-oriented sports market analysis and read-only trading cockpit.
- Current product name in docs: World Cup Edge Lab / Oloraculo.
- Forward production target: `C:/Users/gianz/prediction/Oloraculo`.
- Polytrade, C123, and vendor corpora are donor/reference material only unless explicitly ported into Oloraculo-owned code with tests.

Detected engine/toolchain:

- .NET 9 Blazor Server application.
- EF Core 9 with SQLite for local app state.
- Rust workspace with `rust/oloraculo_hotpath` for deterministic hotpath/scalp primitives.
- PowerShell release/security tooling.
- Python for MCP smoke tests, docs fetchers, and calibration helper.
- Docker container target for `oloraculo-cockpit-api`.
- GitHub Actions CI scaffold.
- AWS deployment direction through container, SSM, and R2/S3-compatible storage.

Main runtime:

- Local: `dotnet run --project Oloraculo.Web`.
- Production skeleton: Docker image exposing `8080`, `/healthz`, and `/snapshot.json`.
- Future production split: Blazor cockpit/API, Rust CLOB hotpath, feed status, sports scalp scanner, R2 archiver.

Primary editor/tool:

- Codex is the primary AI work surface.
- `.codex/config.toml`, `.codex/agents`, `.agents/skills`, and `tools/mcp` are canonical.
- OpenCode/Claude mirrors are not active release-scope tooling.

Target user:

- Operator/developer running a read-only sports scalp cockpit and research workflow.
- Future live execution remains explicitly out of scope until Phase 7 gates are approved and implemented.

Current maturity:

- Strong local app/test/docs foundation.
- Read-only MCP and agent layer already exists.
- Feed-status schema and fake/source adapters are substantially implemented.
- AWS/R2 runtime, live CLOB resident collector, production snapshot source, evidence layer, and live execution gates remain incomplete.

Current bottleneck:

- The bottleneck is not lack of AI tooling. It is production evidence: CI/remote validation, real configured source smoke, AWS/R2 runbooks, monitor parity, and evidence before execution.

Current automation surfaces:

| Surface | Current asset | Authority | Current fit |
| --- | --- | --- | --- |
| Project MCP | `tools/mcp/oloraculo_context_server.py` | Canonical for read-only docs/routing | Keep and extend narrowly |
| MCP profile | `.codex/config.toml` | Canonical Codex MCP config | Keep Codex-first |
| MCP example | `.mcp.json.example` | Placeholder reference for Codex-compatible MCP setup | Keep placeholder-only |
| Agent skills | `.agents/skills/*` | Canonical Codex repo skills | Keep validated |
| Custom agents | `.codex/agents/*.toml` | Canonical Codex subagents | Keep validated |
| Build/test | `dotnet`, `cargo test`, xUnit | Authoritative verification | Prefer over MCP mutation |
| Release scripts | `tools/release/*.ps1` | Authoritative local release checks | Use as gate commands |
| Security scripts | `tools/security/*.ps1` | Authoritative safety checks | Use before readiness claims |
| Docs fetch | `tools/docs/fetch_polymarket_docs.py` | Official-doc snapshot helper | Read-only/reference refresh with care |
| Browser checks | Playwright/chrome-devtools MCPs | UI evidence only | Use for local screenshots, not live ops |
| CI | `.github/workflows/dotnet.yml` | Reproducibility target | Needs committed/pushed proof |
| Docker | `Dockerfile`, `tools/release/test-container-smoke.ps1` | Runtime smoke target | Keep as production gate |

Current source of truth:

| Source | Classification | Notes |
| --- | --- | --- |
| `docs/source-of-truth/ACTIVE.md` | AUTHORITATIVE | Current operating checkpoint |
| `docs/source-of-truth/COMMANDS.md` | AUTHORITATIVE | Command gates and side effects |
| `docs/source-of-truth/DATA_AND_SECRETS.md` | AUTHORITATIVE | Secret and data-refresh policy |
| `docs/source-of-truth/FEED_STATUS_CONTRACT.md` | AUTHORITATIVE | `/snapshot.json` and feed status schema |
| `docs/source-of-truth/OLORACULO_PRODUCTION_ARCHITECTURE.md` | AUTHORITATIVE | Production architecture and boundaries |
| `docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md` | AUTHORITATIVE | Current executable queue |
| `AGENTS.md` | AUTHORITATIVE | Agent operating rules |
| `Oloraculo.Web/Program.cs` | AUTHORITATIVE runtime wiring | Startup, health, snapshot routes, DI |
| `Oloraculo.Web/OloraculoConfig.cs` | AUTHORITATIVE config schema | Supported config and env var names |
| `Oloraculo.Web/Feeds/*` | AUTHORITATIVE app feed status implementation | Sanitized status and probes |
| `rust/oloraculo_hotpath` | AUTHORITATIVE Rust hotpath skeleton | Deterministic network-free primitives |
| `docs/vendor/polymarket-docs` | REFERENCE | Local official docs snapshot, excluded from first release |
| `docs/reference/c123`, `polytrade-agent`, `tools/c123-*`, `tools/polyfill-rs` | REFERENCE / UNSAFE TO EDIT | Donor/reference only until ownership review |
| Local `.env`, `.mcp.json`, `appsettings.Development.json`, `secrets.json`, `*.db` | UNSAFE TO EDIT / SECRET OR GENERATED | Do not read/print/commit unless explicitly requested with risk acknowledged |

Current do-not-touch areas:

- Secret-bearing local files: `.env`, `.env.*` except `.env.example`, `.mcp.json`, `pmkey.txt`, `secrets.json`, `appsettings.Development.json`, `tools/mcp/dbhub.local.toml`.
- Runtime/generated state: `*.db`, `*.db-shm`, `*.db-wal`, `*.log`, `bin/`, `obj/`, `target/`, `TestResults/`, coverage output, Docker/build artifacts.
- Deferred references: `docs/vendor/polymarket-docs/`, `docs/reference/c123/`, `polytrade-agent/`, `tools/c123-market-archive/`, `tools/c123-market-validation/`, `tools/c123-sports-scout-reference/`, `tools/polyfill-rs/`.
- Live-control surfaces: order placement, cancel, approval, signing, wallet/private user operations, relayer submit, live production mutation.
- Infrastructure mutation: AWS deploy/delete/restart, R2 prune, package/plugin install, schema migration, live source credential changes, unless separately approved.

## 3. Research Principles Applied

Generalized lessons from `deep-research-report.md` and how they apply here:

| Research principle | Oloraculo application |
| --- | --- |
| Prefer native APIs over GUI automation | Use .NET tests, Rust tests, PowerShell scripts, Docker, AWS SSM, and HTTP health endpoints. Use browser automation only for localhost UI screenshots. |
| MCP is a control/context layer, not the product | Keep MCP focused on source docs, routing, status summaries, and verification planning. Runtime logic belongs in .NET/Rust/services. |
| Native plugin is best only when the host is an editor/app | No Unity/Unreal/Godot/Figma/Photoshop host exists here, so a native plugin is not the right first move. |
| CLI wrapper is enough for many workflows | Build, test, release, secret scan, no-order scan, docs fetch, and container smoke already have CLI gates. Expose summaries, not arbitrary execution. |
| GUI/browser automation is high-fragility | Use Playwright/chrome-devtools for visual verification of the cockpit only. Do not use GUI automation for feeds, AWS, secrets, or trading actions. |
| Start read-only before mutation | The existing `oloraculo-context` MCP follows this. Extend read-only before any write tool. |
| Mutation needs approval and rollback | Writes should remain normal code changes with git diff, tests, release scans, and user approval for high-risk surfaces. |
| Verification must be native | Use `dotnet test`, `cargo test`, MCP smoke tests, PowerShell scans, Docker smoke, browser screenshots, and future AWS SSM smoke. |
| Narrow tool surfaces beat broad agents | Add a few explicit read-only tools. Do not add arbitrary shell, file write, file delete, package install, secret reader, deploy, or live-operation tools. |
| Secret isolation is non-negotiable | Committed MCP configs use placeholders. Optional credential-backed MCPs stay disabled by default. MCP must not read local secrets. |

What MCP is useful for:

- Fast access to canonical source-of-truth docs.
- Routing work to the correct senior agent/skill.
- Returning guardrails and do-not-touch policy.
- Summarizing current release gates and production phases.
- Validating proposed file paths against allowlists/denylists.
- Producing task-specific verification plans.

When MCP is not necessary:

- Implementing .NET/Rust business logic.
- Running ordinary build/test commands.
- Editing normal source files under Codex supervision.
- Reading local docs with normal filesystem tools.
- UI visual QA where Playwright is already available.

When a native plugin is better:

- If a future workflow deeply integrates with an editor or design tool, such as Figma, Photoshop, Unity, Unreal, Godot, or Blender.
- Not applicable to current Oloraculo core runtime.

When editor scripting is enough:

- If the project later adopts a game/editor host.
- Not applicable to current Oloraculo core runtime.

When a CLI wrapper is enough:

- Release gates, security scans, test runs, Docker smoke, docs fetch, and calibration.
- Current scripts are adequate; the AI bridge should explain and select them, not replace them.

When GUI automation is too risky:

- Secret entry, AWS console mutation, live trading, package installs, production deploys, or any operation where a mis-click creates financial or infrastructure side effects.

Why read-only tools should come first:

- Oloraculo has live-market, credential, and future execution surfaces. A read-only bridge reduces blast radius while still improving agent performance.

Why mutation tools need approval:

- Even non-order mutation can change production claims, feed meaning, source-of-truth docs, CI policy, or deploy behavior.

Why verification must be project-native:

- Oloraculo correctness is encoded in xUnit tests, Rust tests, PowerShell safety scans, Docker smoke, and eventually AWS SSM evidence. Model assertions are not evidence.

## 4. Candidate Tooling Options

| Option | Fit for Oloraculo | Setup cost | Maintenance cost | Security risk | Capability | Verification quality | Failure modes | Recommended |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| MCP | High for read-only context, routing, status, path policy, verification planning | Low, existing server exists | Low-medium | Low if read-only; high if write/shell/deploy tools are added | Good context and orchestration | Good if backed by tests | Stale docs, overbroad tools, secret leakage if policy weak | Yes, narrow only |
| Native plugin | Low for current core runtime | Medium-high | Medium-high | Medium | Poor fit unless targeting a host editor | Weak for current app | Adds unused layer and drift | No |
| Editor scripting | Low | Medium | Medium | Medium | Not relevant without editor host | Weak for current app | False sense of native automation | No |
| CLI wrapper | High | Low | Low | Low-medium depending command | Strong for build/test/release | Excellent when commands are deterministic | Broad shell wrappers can become dangerous | Yes, allowlisted only |
| File watcher | Medium later for snapshots/artifacts | Medium | Medium | Medium | Useful for hot snapshot ingestion or local dev status | Good if read-only | Hidden side effects and race conditions | Defer |
| GUI automation | Medium for localhost visual QA only | Low | Medium | Medium-high | Screenshots and layout checks | Good for UI evidence | Flaky selectors, wrong target, credential exposure | Limited fallback only |
| No custom tooling yet | Medium | None | None | Lowest | Preserves current state | Depends on manual work | Slower audits, repeated context gathering | Not enough; keep existing MCP |
| Existing MCP/tool bridge | High for current read-only layer | Already present | Low | Low | Source docs and routing | Good with smoke test | Limited project status insight | Use and extend |
| Build narrow custom MCP | High if extending current server | Low-medium | Low-medium | Low if read-only | Adds project status/path policy/verification plans | Strong with smoke tests | Scope creep | Recommended next |
| Server/admin tool | Medium later for AWS read-only status | Medium | Medium | Medium-high | SSM/health/log summaries | Strong if read-only | Can drift into production mutation | Future read-only only |
| Asset-pipeline bridge | Low now | Medium | Medium | Medium | Not current need | N/A | Unused complexity | No |

Build-vs-buy conclusion:

- Keep existing MCPs: `oloraculo-context`, `context7`, `chrome-devtools`, `playwright`, `serena`.
- Keep credentialed or infrastructure MCPs disabled by default: Firecrawl, DBHub, GitHub, Grafana, Prometheus, Sentry, AWS docs.
- Build only a narrow extension to `oloraculo-context` if additional bridge work is approved.
- Do not buy/install a broad trading, AWS, browser, or filesystem mutation bridge.

## 5. Recommended First Bridge

Smallest useful bridge:

Name:

- `oloraculo-context` v0.2, extending the existing `tools/mcp/oloraculo_context_server.py`.

Purpose:

- Make Codex faster and safer by exposing read-only project state, source-of-truth docs, route suggestions, do-not-touch validation, and verification plans.

Users:

- Codex as the active project MCP client.
- Human operator/developer reviewing returned guardrails and plans.

Host/client:

- Codex as the project MCP host/client.

Transport if MCP:

- Local stdio only.
- No remote Streamable HTTP server for the project context bridge in v0.

Native APIs used:

- Filesystem reads under explicit doc/source allowlist.
- Git read-only status commands, if added.
- No shell execution outside allowlisted read-only git commands.
- No AWS, R2, secrets, database mutation, order, or live source calls.

Read-only tools:

| Tool | Purpose |
| --- | --- |
| `read_source_doc` | Existing: read committed canonical docs by name |
| `list_backlog_phase` | Existing: summarize production backlog phases |
| `guardrail_report` | Existing: return core safety guardrails |
| `route_work` | Existing: route task to correct agent/skill |
| `get_project_status` | Proposed: branch, dirty summary, staged/untracked counts, top-level path |
| `get_tooling_inventory` | Proposed: enabled/disabled MCPs, agents, skills, validation scripts |
| `validate_do_not_touch_paths` | Proposed: classify requested paths as allowed, confirmation-required, or forbidden |
| `get_verification_plan` | Proposed: return commands required for a task category |
| `get_release_gate_summary` | Proposed: summarize current required gates without running them |

Optional write tools:

- None in v0.
- Future, confirmation-required only: `create_debug_report` or `update_non_generated_markdown_docs`.
- Even future write tools must be path-limited, auditable, and covered by MCP smoke tests.

Denied tools:

- Arbitrary shell execution.
- Arbitrary file read or write.
- Delete, move, recursive cleanup.
- Package/plugin installs.
- AWS deploy/restart/delete.
- R2 prune or object deletion.
- Secret reads.
- Database writes.
- Polymarket order, cancel, approval, signing, relayer, private user channel, or wallet operations.
- Browser/GUI actions against production consoles or credential pages.

Verification:

- `python tools/mcp/test_oloraculo_context_server.py`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/codex/check-oloraculo-codex.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/security/check-no-raw-secrets.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/security/check-no-live-order-path.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/release/check-release-scope.ps1`

## 6. Tool Surface v0

| Tool name | Read/write/destructive | Inputs | Outputs | Risk level | Approval required | Verification method |
| --- | --- | --- | --- | --- | --- | --- |
| `read_source_doc` | Read | `name` enum | Markdown text | Low | No | MCP smoke test |
| `list_backlog_phase` | Read | `phase` or `all` | Backlog markdown | Low | No | MCP smoke test |
| `guardrail_report` | Read | none | Guardrail markdown | Low | No | MCP smoke test |
| `route_work` | Read/inspect | task text | Suggested agent/skill routing | Low | No | MCP smoke test |
| `get_project_status` | Read/inspect | optional `include_files=false` | Repo root, branch, staged/modified/untracked counts, no secret values | Low | No | Unit/smoke test with sample git output |
| `get_tooling_inventory` | Read/inspect | none | MCP/agent/skill/check inventory and enabled/disabled state | Low | No | TOML/JSON parse tests |
| `validate_do_not_touch_paths` | Read/inspect | path list | classification, rule matched, required approval | Low | No | Table-driven path policy tests |
| `get_verification_plan` | Read/inspect | task category or free text | required commands and evidence | Low | No | Route/gate tests |
| `get_release_gate_summary` | Read/inspect | none | release gates and last-known docs references, no command execution | Low | No | Fixture tests |
| `create_debug_report` | Write, future only | approved output path under docs or temp | Markdown report | Medium | Yes | git diff, secret scan, no-order scan |
| `update_non_generated_markdown_docs` | Write, future only | explicit doc path and patch | Markdown diff | Medium | Yes | git diff, markdown review, secret scan |
| `run_compile_or_validation` | Runtime execution, future only | allowlisted command id | command result | Medium-high | Yes | command allowlist tests |
| `run_tests` | Runtime execution, future only | allowlisted test id | test result | Medium | Yes | command allowlist tests |
| `capture_screenshot` | Runtime/UI, future only through Playwright MCP | localhost URL only | screenshot artifact | Medium | Yes if app state may mutate | Playwright logs and screenshot review |

Unacceptable v0 tools:

- `shell(command)`.
- `read_file(path)` without allowlist.
- `write_file(path, content)` without approval and path allowlist.
- `delete_path(path)`.
- `deploy_aws`.
- `read_secret`.
- `place_order`, `cancel_order`, `approve_order`, `sign_order`, `send_heartbeat`, `open_user_ws`.

## 7. Security Model

Local/remote assumptions:

- `oloraculo-context` is local stdio only.
- It should run from the repo root and read only allowlisted committed files.
- Remote MCP is not needed for project context in v0.

Auth:

- No auth for local stdio context bridge beyond local process boundary.
- Any future remote MCP must use proper auth, origin validation, resource scoping, and no token passthrough.

Always allowed:

- Read-only source-of-truth docs.
- Read-only project routing.
- Read-only guardrails.
- Read-only task verification plan.
- Read-only path classification.

Allowed with confirmation:

- Small scoped writes to non-generated Markdown docs.
- Creating temporary reports under an approved path.
- Running existing release/test commands.
- Capturing localhost screenshots.

Requires explicit approval:

- `.codex/config.toml`, `.codex/agents`, `.agents/skills`, and `.mcp.json.example`.
- CI workflow changes.
- Dockerfile/deploy changes.
- AWS/SSM/R2 configuration or scripts.
- Package/plugin installs.
- Database/schema migrations.
- Live-source network probes with credentials.
- Any task that could alter production behavior or readiness claims.

Forbidden by default:

- Secret access.
- Destructive deletion.
- Live production mutation.
- Polymarket order/cancel/approval/signing/wallet/relayer operations.
- Anti-cheat bypass or unauthorized client/server manipulation.
- Credential scraping.
- Broad arbitrary execution.
- Reading or printing `.env`, `.mcp.json`, `pmkey.txt`, `appsettings.Development.json`, `secrets.json`, `dbhub.local.toml`, or live credential values.

Path allowlist:

- `AGENTS.md`
- `README.md`
- `docs/source-of-truth/*.md`
- `docs/superpowers/plans/*.md`
- `tools/mcp/README.md`
- `.codex/config.toml` for explicit MCP tasks only
- `.codex/agents/*.toml` for explicit agent tasks only
- `.agents/skills/*/SKILL.md` for explicit skill tasks only
- `tools/mcp/oloraculo_context_server.py` and `tools/mcp/test_oloraculo_context_server.py` for explicit MCP implementation only

Path denylist:

- `.env`, `.env.*` except `.env.example`
- `.mcp.json`
- `pmkey.txt`
- `secrets.json`
- `appsettings.Development.json`
- `tools/mcp/dbhub.local.toml`
- `*.db`, `*.db-shm`, `*.db-wal`
- `*.log`, `*.out.log`, `*.err.log`
- `bin/`, `obj/`, `target/`, `node_modules/`, `dist/`, `build/`, `coverage/`, `TestResults/`
- `docs/vendor/polymarket-docs/`
- `docs/reference/c123/`
- `polytrade-agent/`
- `tools/c123-market-archive/`
- `tools/c123-market-validation/`
- `tools/c123-sports-scout-reference/`
- `tools/polyfill-rs/`

Command allowlist:

- `git status --short`
- `git rev-parse --show-toplevel`
- `git branch --show-current`
- `python tools/mcp/test_oloraculo_context_server.py`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/codex/check-oloraculo-codex.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/security/check-no-raw-secrets.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/security/check-no-live-order-path.ps1`
- `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/release/check-release-scope.ps1`
- `dotnet test Oloraculo.sln`
- `cargo test`

Command denylist:

- `git reset --hard`
- `git clean`
- force push
- recursive delete/move
- package/plugin install unless explicitly approved
- AWS deploy/delete/restart commands unless explicitly approved
- R2 delete/prune unless explicitly approved and verification exists
- shell commands that print environment variables or secrets
- live order/cancel/approval/signing commands

Audit log policy:

- Every MCP tool call should return the rule or source used for its decision.
- Future write/run tools must log tool name, inputs with secrets redacted, allowlist rule, timestamp, and output path or command id.
- Logs must not include secret values, token prefixes/suffixes, signed URLs, wallet/private keys, or raw credential-bearing request headers.

Rollback policy:

- Normal code/docs changes rollback through git diff and revert.
- Generated docs/reports must be isolated in approved paths.
- Runtime operations require documented rollback before approval.
- R2 prune must remain blocked until upload hash/size verification and manifest evidence exist.

Human review points:

- Before any write tool is added.
- Before enabling disabled credential-backed MCPs.
- Before CI/deploy/security policy changes.
- Before using live credentials for source probes.
- Before any AWS/R2 mutation.
- Before any future live-order design or implementation.

## 8. Implementation Options

Fastest prototype:

- Extend existing `tools/mcp/oloraculo_context_server.py`.
- Add only read-only tools: `get_project_status`, `get_tooling_inventory`, `validate_do_not_touch_paths`, `get_verification_plan`.
- Add table-driven tests in `tools/mcp/test_oloraculo_context_server.py`.
- Update `tools/mcp/README.md`.
- Run MCP smoke, Codex health, secret scan, no-order scan, release-scope check.

Why this path:

- Reuses the existing local stdio MCP.
- Low setup cost.
- No new runtime dependency.
- No secret-backed service.
- Fastest improvement to agent safety and speed.

Safest production-ish path:

- Keep `oloraculo-context` read-only.
- Define explicit JSON schemas for every tool response.
- Add a policy module for path classification and command gate selection.
- Add tests for every allowlist/denylist rule.
- Add CI gates that run MCP smoke, Codex health, secret scan, no-order scan, and release-scope check.
- Keep all optional external MCPs disabled by default.

Why this path:

- Makes MCP behavior reproducible.
- Prevents accidental scope creep.
- Turns tooling into release evidence.

Long-term scalable path:

- Split tooling into separate narrow bridges:
  - `oloraculo-context`: source-of-truth, routing, guardrails.
  - `oloraculo-release-readonly`: CI/release status summaries only.
  - `oloraculo-ops-readonly`: AWS/SSM/Grafana/Prometheus/Sentry status only, disabled until production exists.
  - `oloraculo-data-readonly`: DBHub or typed read-only snapshots only, never direct mutation.
- Add an MCP App or local report UI only if a human approval/review surface becomes necessary.
- Keep mutation in normal code changes and CI until a specific high-value write tool justifies itself.

Why this path:

- Keeps each bridge auditable.
- Limits blast radius per trust boundary.
- Matches MCP's host/client/server model without creating a monolith.

## 9. Stop Conditions

The agent must stop and ask the user before:

- Reading or printing secret-bearing local files.
- Enabling any disabled credential-backed MCP.
- Adding arbitrary shell or arbitrary file write tools.
- Adding delete, move, prune, deploy, restart, or package install tools.
- Editing `.codex/config.toml`, `.agents/skills`, `.codex/agents`, CI, Docker, or deploy files outside a clearly scoped tooling task.
- Touching `docs/vendor/polymarket-docs`, `docs/reference/c123`, `polytrade-agent`, or `tools/c123-*` as production-owned source.
- Changing source-of-truth logic or feed readiness semantics.
- Running live external probes with credentials.
- Making AWS/R2 changes.
- Modifying production config or appsettings that could affect live runtime.
- Introducing or modifying any Polymarket order, cancel, approval, signing, wallet, relayer, or private user operation.
- Claiming production readiness without current command output, CI evidence, screenshot evidence, or AWS/SSM smoke evidence as applicable.

## 10. Recommended Next Task

One narrow next task:

Extend the existing read-only `oloraculo-context` MCP with project-status and safety-policy inspection tools only.

Scope:

- Modify `tools/mcp/oloraculo_context_server.py`.
- Modify `tools/mcp/test_oloraculo_context_server.py`.
- Modify `tools/mcp/README.md`.
- Optionally expose this document as a source-of-truth resource.

Tools to add:

- `get_project_status`
- `get_tooling_inventory`
- `validate_do_not_touch_paths`
- `get_verification_plan`

Acceptance criteria:

- No write tools are added.
- No arbitrary shell tool is added.
- No local secret file is read.
- Proposed paths are classified with explicit allow/deny/approval reason.
- Verification plans return existing commands from `docs/source-of-truth/COMMANDS.md`.
- MCP smoke test passes.
- Codex health passes.
- Secret scan passes.
- No live-order scan passes.
- Release-scope check passes.

Do not implement this next task until explicitly requested.
