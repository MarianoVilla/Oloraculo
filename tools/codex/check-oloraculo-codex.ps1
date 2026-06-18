$ErrorActionPreference = "Continue"

$script:FailureCount = 0
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path (Join-Path $ScriptDir "..") "..")).Path
$CodexHome = if ([string]::IsNullOrWhiteSpace($env:CODEX_HOME)) {
  Join-Path $HOME ".codex"
} else {
  $env:CODEX_HOME
}

function RepoPath {
  param([string[]]$Parts)
  $path = $Root
  foreach ($part in $Parts) {
    $path = Join-Path $path $part
  }
  return $path
}

$CodexConfig = RepoPath @(".codex", "config.toml")
$CodexAgents = RepoPath @(".codex", "agents")
$CodexSkills = RepoPath @(".agents", "skills")
$McpServer = RepoPath @("tools", "mcp", "oloraculo_context_server.py")
$McpSmokeTest = RepoPath @("tools", "mcp", "test_oloraculo_context_server.py")
$ReadinessPlan = RepoPath @("docs", "source-of-truth", "OLORACULO_PRODUCTION_READINESS_PLAN.md")
$ProductionTodo = RepoPath @("docs", "source-of-truth", "OLORACULO_PRODUCTION_TODO.md")
$SecretScan = RepoPath @("tools", "security", "check-no-raw-secrets.ps1")
$NoOrderScan = RepoPath @("tools", "security", "check-no-live-order-path.ps1")
$ReleaseScope = RepoPath @("tools", "release", "check-release-scope.ps1")
$HostPrereqs = RepoPath @("tools", "release", "check-host-prereqs.ps1")
$ContainerSmoke = RepoPath @("tools", "release", "test-container-smoke.ps1")
$SkillValidator = Join-Path $CodexHome "skills\.system\skill-creator\scripts\quick_validate.py"

function Status($Name, $Ok, $Detail) {
  $state = if ($Ok) { "OK" } else { "FAIL" }
  if (-not $Ok) {
    $script:FailureCount += 1
  }
  "[$state] $Name - $Detail"
}

Status "Root" (Test-Path -LiteralPath $Root) $Root
Status "AGENTS.md" (Test-Path -LiteralPath (Join-Path $Root "AGENTS.md")) "Codex repo instructions"
Status "Codex config" (Test-Path -LiteralPath $CodexConfig) ".codex\config.toml"
Status "Codex agents dir" (Test-Path -LiteralPath $CodexAgents) ".codex\agents"
Status "Codex repo skills dir" (Test-Path -LiteralPath $CodexSkills) ".agents\skills"
Status "Oloraculo context MCP" (Test-Path -LiteralPath $McpServer) "tools\mcp\oloraculo_context_server.py"
Status "Oloraculo context MCP smoke test" (Test-Path -LiteralPath $McpSmokeTest) "tools\mcp\test_oloraculo_context_server.py"
Status "Production readiness plan" (Test-Path -LiteralPath $ReadinessPlan) "docs\source-of-truth\OLORACULO_PRODUCTION_READINESS_PLAN.md"
Status "Production TODO" (Test-Path -LiteralPath $ProductionTodo) "docs\source-of-truth\OLORACULO_PRODUCTION_TODO.md"
Status "Secret scan" (Test-Path -LiteralPath $SecretScan) "tools\security\check-no-raw-secrets.ps1"
Status "No live-order scan" (Test-Path -LiteralPath $NoOrderScan) "tools\security\check-no-live-order-path.ps1"
Status "Release scope check" (Test-Path -LiteralPath $ReleaseScope) "tools\release\check-release-scope.ps1"
Status "Host prerequisites" (Test-Path -LiteralPath $HostPrereqs) "tools\release\check-host-prereqs.ps1"
Status "Container smoke" (Test-Path -LiteralPath $ContainerSmoke) "tools\release\test-container-smoke.ps1"
Status "Skill validator" (Test-Path -LiteralPath $SkillValidator) $SkillValidator
Status "python" ([bool](Get-Command python -ErrorAction SilentlyContinue)) "Required for MCP and validation"
Status "dotnet" ([bool](Get-Command dotnet -ErrorAction SilentlyContinue)) "Required for build/test"
Status "rg" ([bool](Get-Command rg -ErrorAction SilentlyContinue)) "Preferred search tool"

$expectedAgents = @(
  "chief-systems-architect",
  "aws-runtime-operator",
  "r2-archive-lake-engineer",
  "rust-hotpath-engineer",
  "feed-status-integrator",
  "cockpit-ux-engineer",
  "quant-evidence-scientist",
  "security-risk-sentinel",
  "release-verification-lead",
  "mcp-toolsmith"
)

foreach ($agent in $expectedAgents) {
  Status "Codex custom agent $agent" (Test-Path -LiteralPath (Join-Path $CodexAgents "$agent.toml")) ".codex\agents\$agent.toml"
}

$expectedSkills = @(
  "oloraculo-architecture-map",
  "oloraculo-aws-r2-ops",
  "oloraculo-feed-hotpath",
  "oloraculo-cockpit-parity",
  "oloraculo-quant-evidence",
  "oloraculo-release-gate",
  "oloraculo-mcp-tooling",
  "oloraculo-security-boundary"
)

foreach ($skill in $expectedSkills) {
  $skillDir = Join-Path $CodexSkills $skill
  Status "Codex skill $skill" (Test-Path -LiteralPath (Join-Path $skillDir "SKILL.md")) ".agents\skills\$skill\SKILL.md"
  Status "Codex skill UI metadata $skill" (Test-Path -LiteralPath (Join-Path $skillDir "agents\openai.yaml")) ".agents\skills\$skill\agents\openai.yaml"
  if (Test-Path -LiteralPath $SkillValidator) {
    $validation = & python $SkillValidator $skillDir 2>&1
    Status "Codex skill validation $skill" ($LASTEXITCODE -eq 0) ($validation -join " ")
  }
}

try {
  $tomlCheck = @"
import pathlib
import tomllib
root = pathlib.Path(r"$Root")
toml = tomllib.loads((root / ".codex" / "config.toml").read_text(encoding="utf-8"))
servers = toml.get("mcp_servers", {})
expected_enabled = {"oloraculo-context", "context7", "chrome-devtools", "playwright", "serena"}
expected_disabled = {"firecrawl-research", "dbhub-oloraculo", "github-readonly", "grafana-readonly", "prometheus-readonly", "sentry-readonly", "aws-docs"}
missing = (expected_enabled | expected_disabled) - set(servers)
assert not missing, f"missing MCP servers: {sorted(missing)}"
bad_enabled = [name for name in expected_enabled if servers[name].get("enabled") is not True]
bad_disabled = [name for name in expected_disabled if servers[name].get("enabled") is not False]
assert not bad_enabled, f"not enabled: {bad_enabled}"
assert not bad_disabled, f"not disabled: {bad_disabled}"
for path in (root / ".codex" / "agents").glob("*.toml"):
    agent = tomllib.loads(path.read_text(encoding="utf-8"))
    for key in ("name", "description", "developer_instructions"):
        assert key in agent and agent[key], f"{path.name} missing {key}"
print("Codex TOML OK")
"@
  $result = $tomlCheck | python - 2>&1
  Status "Codex TOML parse/schema" ($LASTEXITCODE -eq 0) ($result -join " ")
} catch {
  Status "Codex TOML parse/schema" $false $_.Exception.Message
}

try {
  $mcpSmoke = & python $McpSmokeTest 2>&1
  Status "Oloraculo context MCP smoke" ($LASTEXITCODE -eq 0) ($mcpSmoke -join " ")
} catch {
  Status "Oloraculo context MCP smoke" $false $_.Exception.Message
}

try {
  $secretScan = & $SecretScan 2>&1
  Status "Raw secret scan run" ($LASTEXITCODE -eq 0) ($secretScan -join " ")
} catch {
  Status "Raw secret scan run" $false $_.Exception.Message
}

try {
  $noOrderScan = & $NoOrderScan 2>&1
  Status "No live-order scan run" ($LASTEXITCODE -eq 0) ($noOrderScan -join " ")
} catch {
  Status "No live-order scan run" $false $_.Exception.Message
}

try {
  $releaseScope = & $ReleaseScope 2>&1
  Status "Release scope check run" ($LASTEXITCODE -eq 0) ($releaseScope -join " ")
} catch {
  Status "Release scope check run" $false $_.Exception.Message
}

try {
  $tracked = & git -C $Root ls-files 2>&1
  $blockedTracked = @()
  foreach ($path in $tracked) {
    $normalized = $path -replace "\\", "/"
    if (
      $normalized -eq "CLAUDE.md" -or
      $normalized -eq "opencode.json" -or
      $normalized.StartsWith(".claude/", [System.StringComparison]::OrdinalIgnoreCase) -or
      $normalized.StartsWith(".opencode/", [System.StringComparison]::OrdinalIgnoreCase) -or
      $normalized.StartsWith("tools/opencode/", [System.StringComparison]::OrdinalIgnoreCase)
    ) {
      $blockedTracked += $normalized
    }
  }
  Status "No tracked non-Codex tooling" ($blockedTracked.Count -eq 0) (($blockedTracked | Sort-Object) -join " | ")
} catch {
  Status "No tracked non-Codex tooling" $false $_.Exception.Message
}

if (Get-Command rg -ErrorAction SilentlyContinue) {
  $stale = & rg "\.claude/skills|\.opencode/agents|opencode default agent|Claude-only|compatibility mirrors" -n .agents .codex AGENTS.md tools --glob "!tools/codex/check-oloraculo-codex.ps1" 2>$null
  Status "Codex-native docs avoid compatibility-only routing" ([string]::IsNullOrWhiteSpace(($stale -join "`n"))) (($stale -join " | "))
}

if ($script:FailureCount -gt 0) {
  Write-Output "[FAIL] Codex health completed with $script:FailureCount failure(s)."
  exit 1
}

Write-Output "[OK] Codex health completed with no failures."
exit 0
