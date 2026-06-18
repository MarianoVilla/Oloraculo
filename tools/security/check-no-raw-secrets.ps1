$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ScriptDir ".." "..")).Path

function Get-RepoFileList {
  $results = New-Object System.Collections.Generic.List[string]
  $git = Get-Command git -ErrorAction SilentlyContinue
  if ($git) {
    Push-Location $Root
    try {
      $files = & git ls-files --cached --others --exclude-standard 2>$null
      if ($LASTEXITCODE -eq 0 -and $files) {
        foreach ($file in $files) {
          $results.Add($file)
        }
      }
    } finally {
      Pop-Location
    }
  }

  if ($results.Count -eq 0) {
    Get-ChildItem -LiteralPath $Root -Recurse -File | ForEach-Object {
      $results.Add([System.IO.Path]::GetRelativePath($Root, $_.FullName))
    }
  }

  $nestedRepos = Get-ChildItem -LiteralPath $Root -Recurse -Force -Directory -Filter ".git" |
    Where-Object { $_.FullName -ne (Join-Path $Root ".git") }
  foreach ($nestedGit in $nestedRepos) {
    $nestedRoot = Split-Path -Parent $nestedGit.FullName
    Get-ChildItem -LiteralPath $nestedRoot -Recurse -File -Force | Where-Object {
      $_.FullName -notlike (Join-Path $nestedRoot ".git*") -and
      $_.FullName -notmatch '[\\/](target|node_modules|dist|build|coverage|TestResults)[\\/]'
    } | ForEach-Object {
      $results.Add([System.IO.Path]::GetRelativePath($Root, $_.FullName))
    }
  }

  return $results | Sort-Object -Unique
}

function Test-SkippedPath {
  param([string]$RelativePath)

  $path = ($RelativePath -replace "\\", "/")
  if ($path -match '(^|/)(\.git|\.vs|\.idea|bin|obj|target|node_modules|dist|build|artifacts|TestResults|coverage|\.terraform|\.codex-tmp)(/|$)') {
    return $true
  }

  if ($path -match '(^|/)(\.env(\..*)?|pmkey\.txt|secrets\.json|dbhub\.local\.toml|\.mcp\.json)$' -and $path -notmatch '(^|/)\.env\.example$') {
    return $true
  }

  if ($path -match '\.(pem|key|pfx|crt|cert|kubeconfig|png|jpg|jpeg|gif|webp|ico|mp4|mov|zip|gz|zst|parquet|duckdb|db|dll|exe|pdb)$') {
    return $true
  }

  if ($path -match '^docs/vendor/polymarket-docs/') {
    return $true
  }

  return $false
}

function Test-SafeExampleLine {
  param(
    [string]$RelativePath,
    [string]$Line
  )

  $path = ($RelativePath -replace "\\", "/")
  if ($path -match '(^|/)\.env\.example$' -and $Line -match '(?i)=\s*(your-|<|REPLACE|REDACTED|PLACEHOLDER|EXAMPLE|env:|\$\{|\$env:|__)') {
    return $true
  }

  if ($path -eq "tools/polyfill-rs/src/auth.rs" -and $Line -match 'let private_key\s*=\s*"0x[a-fA-F0-9]{64}"') {
    return $true
  }

  return $false
}

$secretSignatures = @(
  @{ Name = "AWS access key"; Pattern = '(?<![A-Z0-9])(AKIA|ASIA)[A-Z0-9]{16}(?![A-Z0-9])' },
  @{ Name = "OpenAI API key"; Pattern = '(?<![A-Za-z0-9])sk-(proj-)?[A-Za-z0-9_-]{20,}' },
  @{ Name = "GitHub token"; Pattern = '(?<![A-Za-z0-9])(ghp|gho|ghu|ghs|ghr)_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}' },
  @{ Name = "Google API key"; Pattern = 'AIza[0-9A-Za-z_-]{35}' },
  @{ Name = "Slack token"; Pattern = 'xox[baprs]-[0-9A-Za-z-]{20,}' },
  @{ Name = "Firecrawl API key"; Pattern = '(?<![A-Za-z0-9])fc-[A-Za-z0-9]{20,}' },
  @{ Name = "Polymarket/private key assignment"; Pattern = '(?i)\b(private[_-]?key|wallet[_-]?key|polymarket[_-]?(private[_-]?key|pk)|pk)\b\s*[:=]\s*["'']?0x[a-f0-9]{64}\b' },
  @{ Name = "Polymarket credential assignment"; Pattern = '(?i)\bPOLYMARKET_(API_KEY|API_SECRET|PASSPHRASE|PRIVATE_KEY|PK)\b\s*[:=]\s*["'']?(?!(<|YOUR_|REPLACE|REDACTED|PLACEHOLDER|EXAMPLE|env:|\$\{|\$env:|__|null|todo))[^"''\s]{12,}' },
  @{ Name = "Generic secret assignment"; Pattern = '(?i)\b(API_SECRET|SECRET_KEY|ACCESS_TOKEN|AUTH_TOKEN|BEARER_TOKEN)\b\s*[:=]\s*["'']?(?!(<|YOUR_|REPLACE|REDACTED|PLACEHOLDER|EXAMPLE|env:|\$\{|\$env:|__|null|todo))[^"''\s]{24,}' }
)

$findings = New-Object System.Collections.Generic.List[string]

foreach ($relativePath in Get-RepoFileList) {
  if ([string]::IsNullOrWhiteSpace($relativePath) -or (Test-SkippedPath $relativePath)) {
    continue
  }

  $fullPath = Join-Path $Root $relativePath
  if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
    continue
  }

  try {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($fullPath)) {
      $lineNumber += 1
      if (Test-SafeExampleLine $relativePath $line) {
        continue
      }

      foreach ($signature in $secretSignatures) {
        if ([regex]::IsMatch($line, $signature.Pattern)) {
          $findings.Add(("{0}:{1} [{2}]" -f ($relativePath -replace "\\", "/"), $lineNumber, $signature.Name))
        }
      }
    }
  } catch {
    Write-Output ("[WARN] Skipped unreadable file: {0}" -f ($relativePath -replace "\\", "/"))
  }
}

if ($findings.Count -gt 0) {
  Write-Output ("[FAIL] Raw secret scan found {0} candidate(s). Values are intentionally not printed." -f $findings.Count)
  $findings | Select-Object -First 80 | ForEach-Object { Write-Output $_ }
  if ($findings.Count -gt 80) {
    Write-Output ("[FAIL] ... {0} additional finding(s) omitted." -f ($findings.Count - 80))
  }
  exit 1
}

Write-Output "[OK] Raw secret scan found no candidates in unignored repo files."
exit 0
