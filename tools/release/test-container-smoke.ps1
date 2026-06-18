$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ScriptDir ".." "..")).Path
$Image = "oloraculo:local-smoke"
$Container = "oloraculo-smoke-$PID"
$SecretPattern = '(fc-[A-Za-z0-9]{20,}|ghp_[A-Za-z0-9_]{20,}|github_pat_[A-Za-z0-9_]{20,}|AKIA[0-9A-Z]{16}|AIza[0-9A-Za-z_-]{35}|xox[baprs]-[0-9A-Za-z-]{20,}|-----BEGIN (RSA |EC |OPENSSH |DSA |)?PRIVATE KEY-----)'

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

function Free-Port {
  $listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 0)
  $listener.Start()
  try {
    return $listener.LocalEndpoint.Port
  } finally {
    $listener.Stop()
  }
}

function Cleanup {
  if ($Docker) {
    & $Docker rm -f $Container 1>$null 2>$null
  }
}

$Docker = Find-Docker
if (-not $Docker) {
  Write-Output "[FAIL] docker was not found on PATH or common install paths"
  exit 1
}

$info = & $Docker info 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-Output "[FAIL] docker daemon is not ready: $($info -join ' ')"
  exit 1
}

try {
  Write-Output "[INFO] Building $Image"
  & $Docker build -t $Image $Root
  if ($LASTEXITCODE -ne 0) {
    Write-Output "[FAIL] docker build failed"
    exit 1
  }

  $port = Free-Port
  Write-Output "[INFO] Starting $Container on 127.0.0.1:$port"
  $containerId = & $Docker run -d --name $Container -p "127.0.0.1:$($port):8080" $Image
  if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
    Write-Output "[FAIL] docker run failed"
    exit 1
  }

  $health = $null
  for ($i = 1; $i -le 40; $i++) {
    try {
      $health = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/healthz" -TimeoutSec 3
      if ($health.StatusCode -eq 200) {
        break
      }
    } catch {
      Start-Sleep -Seconds 2
    }
  }

  if ($null -eq $health -or $health.StatusCode -ne 200) {
    $logs = & $Docker logs $Container 2>&1
    Write-Output "[FAIL] /healthz did not return 200"
    Write-Output ($logs -join "`n")
    exit 1
  }

  try {
    $snapshot = Invoke-WebRequest -UseBasicParsing -Uri "http://127.0.0.1:$port/snapshot.json" -TimeoutSec 10 -ErrorAction Stop
  } catch {
    $logs = & $Docker logs $Container 2>&1
    Write-Output "[FAIL] /snapshot.json request failed: $($_.Exception.Message)"
    Write-Output ($logs -join "`n")
    exit 1
  }
  if ($snapshot.StatusCode -ne 200) {
    Write-Output "[FAIL] /snapshot.json returned HTTP $($snapshot.StatusCode)"
    exit 1
  }

  $snapshotText = [string]$snapshot.Content
  if ($snapshotText -notmatch 'READ_ONLY_STATUS_ONLY') {
    Write-Output "[FAIL] /snapshot.json did not expose READ_ONLY_STATUS_ONLY mode"
    exit 1
  }
  try {
    $snapshotJson = $snapshotText | ConvertFrom-Json -ErrorAction Stop
  } catch {
    Write-Output "[FAIL] /snapshot.json was not valid JSON: $($_.Exception.Message)"
    exit 1
  }

  if ($snapshotJson.schema_version -ne 1) {
    Write-Output "[FAIL] /snapshot.json schema_version was not 1"
    exit 1
  }
  if ($snapshotJson.mode -ne "READ_ONLY_STATUS_ONLY") {
    Write-Output "[FAIL] /snapshot.json mode was not READ_ONLY_STATUS_ONLY"
    exit 1
  }

  $requiredSources = @(
    "databet_sportsbook",
    "databet_widgets",
    "oddspapi_pinnacle",
    "grid",
    "polymarket_clob",
    "object_archive"
  )
  $sourceIds = @($snapshotJson.feeds.rows | ForEach-Object { $_.source_id })
  foreach ($sourceId in $requiredSources) {
    if ($sourceIds -notcontains $sourceId) {
      Write-Output "[FAIL] /snapshot.json missing feed source_id $sourceId"
      exit 1
    }
  }
  if ($snapshotText -match '(?i)sofascore|sofa score') {
    Write-Output "[FAIL] /snapshot.json included deprecated SofaScore text"
    exit 1
  }
  if ($snapshotText -match '(?i)bearer\s+[A-Za-z0-9._-]{8,}') {
    Write-Output "[FAIL] /snapshot.json included a bearer token-like value"
    exit 1
  }
  if ($snapshotText -match '(?i)(place[_-]?order|cancel[_-]?order|submit[_-]?order|approve|approval|allowance|signing|signature|signed[_-]?order|redeem|redemption|relayer)') {
    Write-Output "[FAIL] /snapshot.json included live-order affordance text"
    exit 1
  }
  $archiveRow = @($snapshotJson.feeds.rows | Where-Object { $_.source_id -eq "object_archive" })[0]
  if ($archiveRow.present -eq $true) {
    Write-Output "[FAIL] object_archive reported present=true without measured archive health in container smoke"
    exit 1
  }
  if ([regex]::IsMatch($snapshotText, $SecretPattern)) {
    Write-Output "[FAIL] /snapshot.json matched a raw secret pattern"
    exit 1
  }

  Write-Output "[OK] Container smoke passed: /healthz and /snapshot.json are reachable and secret-safe"
  exit 0
} finally {
  Cleanup
}
