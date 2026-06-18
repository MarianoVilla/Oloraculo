---
description: Run relevant Oloraculo verification gates and cite output.
agent: release-verification-lead
---

Run the relevant Oloraculo verification gates for: $ARGUMENTS

Use `oloraculo-release-gate`.

Always start with `git status --short`.

Use these gates when relevant:

- Config/tooling docs: JSON parse plus `pwsh -NoProfile -ExecutionPolicy Bypass -File tools/opencode/check-oloraculo-opencode.ps1`.
- MCP tooling: `python tools\mcp\test_oloraculo_context_server.py`.
- Normal code: `dotnet test Oloraculo.sln`.
- Build-sensitive Razor/config work: `dotnet build Oloraculo.sln` then `dotnet test Oloraculo.sln`.
- Rust hotpath: `cargo test`.
- Model calibration: focused tests plus `python tooling\goal_strength_calibration.py` when goal strengths/windows changed.
- Coverage request: `dotnet test Oloraculo.Web.Tests\Oloraculo.Web.Tests.csproj --collect:"XPlat Code Coverage"`.
- README snapshot export: use `/snapshot-export-gate`.

Report only gates actually run, with output. Say clearly what was not verified.
