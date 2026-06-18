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

$Solution = RepoPath @("Oloraculo.sln")
$WebProject = RepoPath @("Oloraculo.Web", "Oloraculo.Web.csproj")
$TestProject = RepoPath @("Oloraculo.Web.Tests", "Oloraculo.Web.Tests.csproj")
$OpenCodeConfig = RepoPath @("opencode.json")
$AgentGuide = RepoPath @("AGENTS.md")
$ClaudeGuide = RepoPath @("CLAUDE.md")
$ClaudeSettings = RepoPath @(".claude", "settings.json")
$OpenCodeReadme = RepoPath @(".opencode", "README.md")
$ClaudeReadme = RepoPath @(".claude", "README.md")
$McpExample = RepoPath @(".mcp.json.example")
$McpServer = RepoPath @("tools", "mcp", "oloraculo_context_server.py")
$McpSmokeTest = RepoPath @("tools", "mcp", "test_oloraculo_context_server.py")
$DataDoc = RepoPath @("docs", "source-of-truth", "DATA_AND_SECRETS.md")
$ArchitectureDoc = RepoPath @("docs", "source-of-truth", "OLORACULO_PRODUCTION_ARCHITECTURE.md")
$BacklogDoc = RepoPath @("docs", "source-of-truth", "OLORACULO_PRODUCTION_BACKLOG.md")
$ReadinessPlan = RepoPath @("docs", "source-of-truth", "OLORACULO_PRODUCTION_READINESS_PLAN.md")
$ProductionTodo = RepoPath @("docs", "source-of-truth", "OLORACULO_PRODUCTION_TODO.md")
$ScalpDoc = RepoPath @("docs", "source-of-truth", "POLYMARKET_SPORTS_SCALP_COCKPIT.md")
$ComboLabDoc = RepoPath @("docs", "source-of-truth", "POLYMARKET_COMBO_LAB.md")
$CommandsDoc = RepoPath @("docs", "source-of-truth", "COMMANDS.md")
$ActiveDoc = RepoPath @("docs", "source-of-truth", "ACTIVE.md")
$SecretScan = RepoPath @("tools", "security", "check-no-raw-secrets.ps1")
$NoOrderScan = RepoPath @("tools", "security", "check-no-live-order-path.ps1")
$ReleaseScope = RepoPath @("tools", "release", "check-release-scope.ps1")
$HostPrereqs = RepoPath @("tools", "release", "check-host-prereqs.ps1")
$ContainerSmoke = RepoPath @("tools", "release", "test-container-smoke.ps1")
$PluginPath = RepoPath @(".opencode", "plugins", "protect-secrets.js")
$Workflow = RepoPath @(".github", "workflows", "dotnet.yml")
$SkillValidator = Join-Path $CodexHome "skills\.system\skill-creator\scripts\quick_validate.py"

function Status($Name, $Ok, $Detail) {
  $state = if ($Ok) { "OK" } else { "FAIL" }
  if (-not $Ok) {
    $script:FailureCount += 1
  }
  "[$state] $Name - $Detail"
}

Status "Root" (Test-Path -LiteralPath $Root) $Root
Status "Solution" (Test-Path -LiteralPath $Solution) $Solution
Status "Web project" (Test-Path -LiteralPath $WebProject) $WebProject
Status "Test project" (Test-Path -LiteralPath $TestProject) $TestProject
Status "AGENTS.md" (Test-Path -LiteralPath $AgentGuide) $AgentGuide
Status "CLAUDE.md" (Test-Path -LiteralPath $ClaudeGuide) $ClaudeGuide
Status ".claude/settings.json" (Test-Path -LiteralPath $ClaudeSettings) $ClaudeSettings
Status "OpenCode config" (Test-Path -LiteralPath $OpenCodeConfig) $OpenCodeConfig
Status "OpenCode README" (Test-Path -LiteralPath $OpenCodeReadme) $OpenCodeReadme
Status "Claude README" (Test-Path -LiteralPath $ClaudeReadme) $ClaudeReadme
Status "MCP example" (Test-Path -LiteralPath $McpExample) $McpExample
Status "Oloraculo context MCP" (Test-Path -LiteralPath $McpServer) $McpServer
Status "Oloraculo context MCP smoke test" (Test-Path -LiteralPath $McpSmokeTest) $McpSmokeTest
Status "Active doc" (Test-Path -LiteralPath $ActiveDoc) $ActiveDoc
Status "Commands doc" (Test-Path -LiteralPath $CommandsDoc) $CommandsDoc
Status "Data/config doc" (Test-Path -LiteralPath $DataDoc) $DataDoc
Status "Architecture doc" (Test-Path -LiteralPath $ArchitectureDoc) $ArchitectureDoc
Status "Backlog doc" (Test-Path -LiteralPath $BacklogDoc) $BacklogDoc
Status "Readiness plan" (Test-Path -LiteralPath $ReadinessPlan) $ReadinessPlan
Status "Production TODO" (Test-Path -LiteralPath $ProductionTodo) $ProductionTodo
Status "Sports scalp cockpit doc" (Test-Path -LiteralPath $ScalpDoc) $ScalpDoc
Status "Polymarket Combo Lab doc" (Test-Path -LiteralPath $ComboLabDoc) $ComboLabDoc
Status "CI workflow" (Test-Path -LiteralPath $Workflow) $Workflow
Status "Secret scan" (Test-Path -LiteralPath $SecretScan) $SecretScan
Status "No live-order scan" (Test-Path -LiteralPath $NoOrderScan) $NoOrderScan
Status "Release scope check" (Test-Path -LiteralPath $ReleaseScope) $ReleaseScope
Status "Host prerequisites" (Test-Path -LiteralPath $HostPrereqs) $HostPrereqs
Status "Container smoke" (Test-Path -LiteralPath $ContainerSmoke) $ContainerSmoke
Status "No secret-blocking plugin" (-not (Test-Path -LiteralPath $PluginPath)) "Project policy keeps local config readable"
Status "Skill validator" (Test-Path -LiteralPath $SkillValidator) $SkillValidator
Status "dotnet" ([bool](Get-Command dotnet -ErrorAction SilentlyContinue)) "Required for build/test"
Status "python" ([bool](Get-Command python -ErrorAction SilentlyContinue)) "Required for Oloraculo context MCP"
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
  Status "OpenCode agent $agent" (Test-Path -LiteralPath (RepoPath @(".opencode", "agents", "$agent.md"))) ".opencode\agents\$agent.md"
  Status "Claude agent $agent" (Test-Path -LiteralPath (RepoPath @(".claude", "agents", "$agent.md"))) ".claude\agents\$agent.md"
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
  $skillDir = RepoPath @(".claude", "skills", $skill)
  Status "Skill $skill" (Test-Path -LiteralPath (Join-Path $skillDir "SKILL.md")) ".claude\skills\$skill\SKILL.md"
  Status "Skill UI metadata $skill" (Test-Path -LiteralPath (Join-Path $skillDir "agents\openai.yaml")) ".claude\skills\$skill\agents\openai.yaml"
  if (Test-Path -LiteralPath $SkillValidator) {
    $validation = & python $SkillValidator $skillDir 2>&1
    Status "Skill validation $skill" ($LASTEXITCODE -eq 0) ($validation -join " ")
  }
}

foreach ($command in @(
  "resume-oloraculo",
  "plan-next",
  "verify-oloraculo",
  "combo-bet-lab",
  "polymarket-market-map",
  "betting-risk-gate",
  "data-refresh-gate",
  "opencode-health",
  "quality-gate",
  "snapshot-export-gate"
)) {
  Status "OpenCode command $command" (Test-Path -LiteralPath (RepoPath @(".opencode", "commands", "$command.md"))) ".opencode\commands\$command.md"
}

try {
  $cfg = Get-Content -LiteralPath $OpenCodeConfig -Raw | ConvertFrom-Json
  Status "opencode.json syntax" $true "JSON parse succeeded"
  Status "OpenCode default agent" ($cfg.default_agent -eq "chief-systems-architect") "default_agent=$($cfg.default_agent)"
  Status "OpenCode read allow" ($cfg.permission.read -eq "allow") "read permission should remain fully allowed"
  Status "OpenCode edit allow" ($cfg.permission.edit -eq "allow") "edit permission should remain fully allowed"

  $expectedMcp = @(
    "oloraculo-context",
    "context7",
    "chrome-devtools",
    "playwright",
    "serena",
    "firecrawl-research",
    "dbhub-oloraculo",
    "github-readonly",
    "grafana-readonly",
    "prometheus-readonly",
    "sentry-readonly",
    "aws-docs"
  )
  foreach ($name in $expectedMcp) {
    Status "MCP $name present" ($null -ne $cfg.mcp.$name) "opencode.json mcp.$name"
  }
  foreach ($name in @("oloraculo-context", "context7", "chrome-devtools", "playwright", "serena")) {
    Status "MCP $name enabled" ($cfg.mcp.$name.enabled -eq $true) "keyless/default MCP"
  }
  foreach ($name in @("firecrawl-research", "dbhub-oloraculo", "github-readonly", "grafana-readonly", "prometheus-readonly", "sentry-readonly", "aws-docs")) {
    Status "MCP $name disabled by default" ($cfg.mcp.$name.enabled -eq $false) "credentialed or infrastructure-gated MCP"
  }

  $serialized = Get-Content -LiteralPath $OpenCodeConfig -Raw
  $rawSecretPattern = '(fc-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|xox[baprs]-[0-9A-Za-z-]{20,})'
  Status "No obvious raw token in opencode.json" (-not [regex]::IsMatch($serialized, $rawSecretPattern)) "env-var-only credential policy"
} catch {
  Status "opencode.json syntax" $false $_.Exception.Message
}

try {
  Get-Content -LiteralPath $ClaudeSettings -Raw | ConvertFrom-Json | Out-Null
  Status ".claude/settings.json syntax" $true "JSON parse succeeded"
} catch {
  Status ".claude/settings.json syntax" $false $_.Exception.Message
}

try {
  Get-Content -LiteralPath $McpExample -Raw | ConvertFrom-Json | Out-Null
  Status ".mcp.json.example syntax" $true "JSON parse succeeded"
} catch {
  Status ".mcp.json.example syntax" $false $_.Exception.Message
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
  $version = & dotnet --version
  Status "dotnet version" $true $version
} catch {
  Status "dotnet version" $false $_.Exception.Message
}

if (Get-Command rg -ErrorAction SilentlyContinue) {
  $stalePattern = "oloraculo-build|oloraculo-planner|prediction-model-quant|data-refresh-engineer|blazor-ui-lead|quality-release-ops|polymarket-market-analyst|combo-pricing-quant|betting-risk-officer|production-gate|prediction-calibration|data-refresh-safety|blazor-ui-evidence|combo-bet-pricing|clob-liquidity-check|polymarket-market-mapping|readme-snapshot-publish|local-config-hygiene"
  $stale = & rg $stalePattern -n AGENTS.md CLAUDE.md .opencode .claude tools opencode.json .mcp.json.example --glob "!tools/opencode/check-oloraculo-opencode.ps1" 2>$null
  Status "No stale old agent/skill names" ([string]::IsNullOrWhiteSpace(($stale -join "`n"))) (($stale -join " | "))
}

if ($script:FailureCount -gt 0) {
  Write-Output "[FAIL] OpenCode compatibility health completed with $script:FailureCount failure(s)."
  exit 1
}

Write-Output "[OK] OpenCode compatibility health completed with no failures."
exit 0
