# Oloraculo MCP Tooling

This folder contains project MCP tooling. Committed MCP config must use
placeholders only; never commit real tokens, signed URLs, private keys, or local
secret paths.

## Codex MCP Config

Codex reads the project MCP profile from:

```text
.codex/config.toml
```

Codex is the only active AI client profile for this repo.

## Always-On MCPs

`.codex/config.toml` enables these by default:

- `oloraculo-context`: local project source-of-truth MCP in this folder.
- `context7`: public docs lookup.
- `chrome-devtools`: headless browser evidence with network headers redacted.
- `playwright`: local browser automation for localhost UI checks.
- `serena`: semantic code navigation from the current project.

## Oloraculo Context MCP

Run the smoke test:

```powershell
python tools\mcp\test_oloraculo_context_server.py
```

The server exposes:

- source-of-truth resources under `oloraculo://source-of-truth/<name>`;
- `production-readiness-plan` and `production-todo` resources for current
  production planning and execution status;
- `read_source_doc`;
- `list_backlog_phase`;
- `guardrail_report`;
- `route_work`.

It reads only committed project docs and does not read `.env`, `.mcp.json`,
`appsettings.Development.json`, `pmkey.txt`, or other secret-bearing files.

## Optional MCPs

These are disabled in `.codex/config.toml` until explicitly configured:

- `firecrawl-research`: requires `FIRECRAWL_API_KEY` when the service requires it.
- `dbhub-oloraculo`: requires `tools/mcp/dbhub.local.toml`.
- `github-readonly`: requires `GITHUB_PERSONAL_ACCESS_TOKEN`.
- `grafana-readonly`: requires Grafana URL and read-only service token.
- `prometheus-readonly`: requires Prometheus URL/token when applicable.
- `sentry-readonly`: remote OAuth flow.
- `aws-docs`: AWS documentation lookup via `uvx`.

## DBHub

`.codex/config.toml` points disabled `dbhub-oloraculo` at:

```text
tools/mcp/dbhub.local.toml
```

That file is intentionally gitignored because it may contain local paths. To
enable DBHub:

1. Copy `dbhub.local.example.toml` to `dbhub.local.toml`.
2. Point the SQLite DSN at a copied/read-only database file when possible.
3. Keep `readonly = true` and a conservative `max_rows`.
4. Enable `dbhub-oloraculo` only after the TOML exists.
