$ErrorActionPreference = "Continue"

$script:FailureCount = 0

function Status($Name, $Ok, $Detail) {
  $state = if ($Ok) { "OK" } else { "FAIL" }
  if (-not $Ok) {
    $script:FailureCount += 1
  }
  "[$state] $Name - $Detail"
}

function CommandStatus($Name, $VersionArgs = @("--version")) {
  $cmd = Get-Command $Name -ErrorAction SilentlyContinue
  if (-not $cmd) {
    Status $Name $false "not found on PATH"
    return
  }

  $detail = $cmd.Source
  try {
    $version = & $cmd.Source @VersionArgs 2>&1 | Select-Object -First 1
    if ($version) {
      $detail = "$detail - $version"
    }
  } catch {
    $detail = "$detail - version probe failed: $($_.Exception.Message)"
  }
  Status $Name $true $detail
}

function Find-Docker {
  $cmd = Get-Command docker -ErrorAction SilentlyContinue
  if ($cmd) {
    return $cmd.Source
  }

  $common = @(
    "C:\Program Files\Docker\Docker\resources\bin\docker.exe",
    "/usr/bin/docker",
    "/usr/local/bin/docker"
  )
  foreach ($path in $common) {
    if (Test-Path -LiteralPath $path) {
      return $path
    }
  }
  return $null
}

CommandStatus "git"
CommandStatus "dotnet"
CommandStatus "cargo"
CommandStatus "rustc"
CommandStatus "python"
CommandStatus "pwsh"
CommandStatus "rg"
CommandStatus "gh"

$docker = Find-Docker
Status "docker" ([bool]$docker) ($(if ($docker) { $docker } else { "not found on PATH or common install paths" }))
if ($docker) {
  $dockerVersion = & $docker --version 2>&1
  Status "docker version" ($LASTEXITCODE -eq 0) ($dockerVersion -join " ")
  $dockerInfo = & $docker info 2>&1
  Status "docker daemon" ($LASTEXITCODE -eq 0) ($(if ($LASTEXITCODE -eq 0) { "ready" } else { ($dockerInfo -join " ") }))
}

if ($script:FailureCount -gt 0) {
  Write-Output "[FAIL] Host prerequisite check completed with $script:FailureCount failure(s)."
  exit 1
}

Write-Output "[OK] Host prerequisite check completed with no failures."
exit 0
