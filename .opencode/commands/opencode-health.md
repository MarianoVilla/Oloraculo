---
description: Check OpenCode, MCP, docs, and Oloraculo workflow prerequisites.
agent: mcp-toolsmith
---

Check whether this OpenCode session has the required Oloraculo capabilities.

Use `oloraculo-mcp-tooling`.

Run:

```powershell
pwsh -NoProfile -ExecutionPolicy Bypass -File tools/opencode/check-oloraculo-opencode.ps1
```

Then inspect `opencode.json` if needed. Explain any red/yellow items and the exact fix.
