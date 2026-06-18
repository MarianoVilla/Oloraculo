$ErrorActionPreference = "Stop"

$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$Root = (Resolve-Path (Join-Path $ScriptDir ".." "..")).Path

$scanRoots = @(
  "Oloraculo.Web",
  "rust/oloraculo_hotpath",
  "deploy",
  "Dockerfile"
)

function Get-ScanFiles {
  foreach ($scanRoot in $scanRoots) {
    $fullRoot = Join-Path $Root $scanRoot
    if (-not (Test-Path -LiteralPath $fullRoot)) {
      continue
    }

    if (Test-Path -LiteralPath $fullRoot -PathType Leaf) {
      Get-Item -LiteralPath $fullRoot
      continue
    }

    Get-ChildItem -LiteralPath $fullRoot -Recurse -File | Where-Object {
      $relative = ([System.IO.Path]::GetRelativePath($Root, $_.FullName) -replace "\\", "/")
      $relative -notmatch '(^|/)(bin|obj|target|node_modules|dist|build|wwwroot/lib)(/|$)' -and
      $relative -notmatch '\.(png|jpg|jpeg|gif|webp|ico|mp4|mov|zip|gz|zst|parquet|duckdb|db|dll|exe|pdb)$'
    }
  }
}

$denySignatures = @(
  @{ Name = "CLOB order endpoint string"; Pattern = '["''`]/orders?(\b|[/?#])' },
  @{ Name = "CLOB cancel endpoint string"; Pattern = '["''`]/cancel([/?#]|\b)|["''`]/cancel-orders?([/?#]|\b)' },
  @{ Name = "CLOB API-key derivation endpoint"; Pattern = '["''`]/auth/(api-key|derive-api-key)(\b|[/?#])' },
  @{ Name = "Live order method"; Pattern = '(?i)\b(createAndPostOrder|create_and_post_order|postOrder|post_order|placeOrder|place_order|submitOrder|submit_order|cancelOrder|cancel_order|redeemPositions?|mergePositions?|splitPositions?)\b' },
  @{ Name = "Allowance/approval method"; Pattern = '(?i)\b(approveAllowance|approveToken|setApprovalForAll|erc20Approve|ctfApprove|exchangeApprove)\b' }
)

$findings = New-Object System.Collections.Generic.List[string]

foreach ($file in Get-ScanFiles) {
  $relativePath = ([System.IO.Path]::GetRelativePath($Root, $file.FullName) -replace "\\", "/")
  try {
    $lineNumber = 0
    foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
      $lineNumber += 1
      foreach ($signature in $denySignatures) {
        if ([regex]::IsMatch($line, $signature.Pattern)) {
          $findings.Add(("{0}:{1} [{2}]" -f $relativePath, $lineNumber, $signature.Name))
        }
      }
    }
  } catch {
    Write-Output ("[WARN] Skipped unreadable file: {0}" -f $relativePath)
  }
}

$donorImportFindings = New-Object System.Collections.Generic.List[string]
$donorImportPattern = '(?i)(tools[/\\]polyfill-rs|polyfill_rs|polyfill-rs)'
foreach ($file in Get-ScanFiles) {
  $relativePath = ([System.IO.Path]::GetRelativePath($Root, $file.FullName) -replace "\\", "/")
  $lineNumber = 0
  foreach ($line in [System.IO.File]::ReadLines($file.FullName)) {
    $lineNumber += 1
    if ([regex]::IsMatch($line, $donorImportPattern)) {
      $donorImportFindings.Add(("{0}:{1} [donor live-order-capable tool reference]" -f $relativePath, $lineNumber))
    }
  }
}

if ($donorImportFindings.Count -gt 0) {
  Write-Output ("[FAIL] Production runtime references quarantined donor tooling {0} time(s)." -f $donorImportFindings.Count)
  $donorImportFindings | Select-Object -First 80 | ForEach-Object { Write-Output $_ }
  exit 1
}

$donorTestRoot = Join-Path $Root "tools/polyfill-rs/tests"
if (Test-Path -LiteralPath $donorTestRoot) {
  Get-ChildItem -LiteralPath $donorTestRoot -Recurse -File -Include *.rs | ForEach-Object {
    $text = Get-Content -LiteralPath $_.FullName -Raw
    if ($text -match '(?i)(create_and_post_order|createAndPostOrder|\.cancel\(|cancel_order|post_order)' -and
        ($text -notmatch 'OLORACULO_ALLOW_LIVE_ORDER_TESTS' -or $text -notmatch 'I_UNDERSTAND_THIS_CAN_PLACE_AND_CANCEL_REAL_ORDERS')) {
      Write-Output "[FAIL] Donor live order test is not protected by the Oloraculo high-friction live-order guard."
      Write-Output ([System.IO.Path]::GetRelativePath($Root, $_.FullName) -replace "\\", "/")
      exit 1
    }
  }
}

if ($findings.Count -gt 0) {
  Write-Output ("[FAIL] Live-order path scan found {0} candidate(s). This runtime must remain watch-only." -f $findings.Count)
  $findings | Select-Object -First 80 | ForEach-Object { Write-Output $_ }
  if ($findings.Count -gt 80) {
    Write-Output ("[FAIL] ... {0} additional finding(s) omitted." -f ($findings.Count - 80))
  }
  exit 1
}

Write-Output "[OK] Live-order path scan found no runtime order/cancel/approval paths."
exit 0
