$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path (Join-Path $ScriptDir "..") "..")).Path
$Failures = New-Object System.Collections.Generic.List[string]

function Normalize-GitPath {
  param([string]$Path)
  return ($Path -replace "\\", "/")
}

function Add-Failure {
  param([string]$Message)
  $Failures.Add($Message) | Out-Null
}

function Test-IgnoredByGit {
  param([string]$Path)
  & git -C $Root check-ignore --no-index -q -- $Path
  return ($LASTEXITCODE -eq 0)
}

$trackedPaths = & git -C $Root ls-files
if ($LASTEXITCODE -ne 0) {
  Add-Failure "git ls-files failed"
  $trackedPaths = @()
}

$normalizedTrackedPaths = $trackedPaths | ForEach-Object { Normalize-GitPath $_ }

$requiredReleaseScripts = @(
  "tools/release/check-release-scope.ps1",
  "tools/release/check-host-prereqs.ps1",
  "tools/release/test-container-smoke.ps1"
)

foreach ($path in $requiredReleaseScripts) {
  if (Test-IgnoredByGit $path) {
    Add-Failure "Git ignores required release script: $path"
  }

  if ($normalizedTrackedPaths -notcontains $path) {
    Add-Failure "Required release script is not tracked: $path"
  }
}

$requiredTrackedFiles = @(
  ".dockerignore",
  ".editorconfig",
  ".github/workflows/dotnet.yml",
  ".mcp.json.example",
  "AGENTS.md",
  "Cargo.lock",
  "Cargo.toml",
  "Dockerfile",
  "README.md",
  "design.md",
  "docs/llms.txt",
  "docs/source-of-truth/ACTIVE.md",
  "docs/source-of-truth/FEED_STATUS_CONTRACT.md",
  "docs/source-of-truth/OLORACULO_PRODUCTION_TODO.md",
  "docs/source-of-truth/RELEASE_SCOPE_AUDIT.md",
  "tools/mcp/oloraculo_context_server.py",
  "tools/mcp/test_oloraculo_context_server.py",
  "tools/security/check-no-raw-secrets.ps1",
  "tools/security/check-no-live-order-path.ps1"
)

foreach ($path in $requiredTrackedFiles) {
  if ($normalizedTrackedPaths -notcontains $path) {
    Add-Failure "Required release file is not tracked: $path"
  }
}

$requiredTrackedPrefixes = @(
  ".agents/skills/",
  ".codex/agents/",
  "deploy/aws/",
  "docs/source-of-truth/",
  "docs/superpowers/plans/",
  "Oloraculo.Web/Archive/",
  "Oloraculo.Web/ComboLab/",
  "Oloraculo.Web/Feeds/",
  "Oloraculo.Web/WorldCup/",
  "Oloraculo.Web.Tests/Archive/",
  "Oloraculo.Web.Tests/ComboLab/",
  "Oloraculo.Web.Tests/Feeds/",
  "Oloraculo.Web.Tests/WorldCup/",
  "rust/oloraculo_hotpath/",
  "tools/codex/",
  "tools/docs/",
  "tools/mcp/",
  "tools/release/",
  "tools/security/"
)

foreach ($prefix in $requiredTrackedPrefixes) {
  if (-not ($normalizedTrackedPaths | Where-Object { $_.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1)) {
    Add-Failure "Required release prefix has no tracked files: $prefix"
  }
}

$blockedSuffixes = @(".env", ".out.log", ".err.log", ".log")
$blockedPrefixes = @(
  ".serena/",
  ".claude/",
  ".opencode/",
  ".pytest_cache/",
  "docs/reference/c123/",
  "docs/vendor/polymarket-docs/",
  "polytrade-agent/",
  "tools/c123-market-archive/",
  "tools/c123-market-validation/",
  "tools/c123-sports-scout-reference/",
  "tools/opencode/",
  "tools/polyfill-rs/"
)
$blockedExactPaths = @(".env", "pmkey.txt", "CLAUDE.md", "opencode.json")

foreach ($path in $normalizedTrackedPaths) {
  if ($path -match '(^|/)\.env' -and $path -notmatch '(^|/)\.env\.example$') {
    Add-Failure "Tracked path is a blocked environment file: $path"
  }

  if ($path -match '(^|/)pmkey\.txt$') {
    Add-Failure "Tracked path is blocked wallet/key material: $path"
  }

  foreach ($suffix in $blockedSuffixes) {
    if ($path.EndsWith($suffix, [System.StringComparison]::OrdinalIgnoreCase)) {
      Add-Failure "Tracked path has blocked suffix '$suffix': $path"
      break
    }
  }

  foreach ($prefix in $blockedPrefixes) {
    if ($path.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
      Add-Failure "Tracked path has blocked prefix '$prefix': $path"
      break
    }
  }

  foreach ($exactPath in $blockedExactPaths) {
    if ([string]::Equals($path, $exactPath, [System.StringComparison]::OrdinalIgnoreCase)) {
      Add-Failure "Blocked path is tracked: $path"
      break
    }
  }
}

$untrackedPaths = & git -C $Root ls-files --others --exclude-standard
if ($LASTEXITCODE -ne 0) {
  Add-Failure "git ls-files --others failed"
  $untrackedPaths = @()
}

foreach ($path in ($untrackedPaths | ForEach-Object { Normalize-GitPath $_ })) {
  Add-Failure "Untracked path is not explicitly staged or ignored: $path"
}

if ($Failures.Count -gt 0) {
  Write-Output "[FAIL] Release scope check found $($Failures.Count) failure(s):"
  foreach ($failure in $Failures) {
    Write-Output "- $failure"
  }
  exit 1
}

Write-Output "release scope check passed"
exit 0
