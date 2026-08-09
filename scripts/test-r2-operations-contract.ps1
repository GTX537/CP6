[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$tempRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "cp6-r2-operations-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($tempRoot) | Out-Null
try {
    $dailyRoot = Join-Path $tempRoot "daily"
    [IO.Directory]::CreateDirectory($dailyRoot) | Out-Null
    $start = [DateTime]::new(2026, 7, 1)
    for ($index = 0; $index -lt 14; $index++) {
        $date = $start.AddDays($index).ToString("yyyy-MM-dd")
        & (Join-Path $repoRoot "scripts\new-r2-reconciliation-evidence.ps1") `
            -BusinessDate $date `
            -WarehouseCd "PILOT-WH" `
            -CompletedMoveTasks 72 `
            -DuplicateInventoryTransactions 0 `
            -LostInventoryQuantity 0 `
            -UnexplainedDifferences 0 `
            -ActivatedDeviceCount 10 `
            -HealthyDeviceCount 10 `
            -EvidenceUris @(
                "s3://cp6-evidence/r2a/$date/tasks.json",
                "s3://cp6-evidence/r2a/$date/inventory.json",
                "s3://cp6-evidence/r2a/$date/devices.json"
            ) `
            -ReconciledBy "warehouse-auditor" `
            -ApprovedBy "warehouse-owner" `
            -OutputPath (Join-Path $dailyRoot "$date.json")
    }

    $restorePath = Join-Path $tempRoot "restore.json"
    $restore = [ordered]@{
        SchemaVersion = 1
        EvidenceType = "DATABASE_RESTORE_REHEARSAL"
        Succeeded = $true
        RpoMinutes = 5
        RtoMinutes = 60
        EvidenceUri = "s3://cp6-evidence/restore/rehearsal.json"
    }
    [IO.File]::WriteAllText(
        $restorePath,
        ($restore | ConvertTo-Json -Depth 4),
        [Text.UTF8Encoding]::new($false))
    $exitPath = Join-Path $tempRoot "pilot-exit.json"
    & (Join-Path $repoRoot "scripts\test-r2-pilot-exit.ps1") `
        -EvidenceDirectory $dailyRoot `
        -RestoreEvidencePath $restorePath `
        -OutputEvidencePath $exitPath
    $exit = [IO.File]::ReadAllText($exitPath, [Text.Encoding]::UTF8) |
        ConvertFrom-Json
    if ($exit.Decision -ne "GO" -or
        [int]$exit.ContinuousDays -ne 14 -or
        [int]$exit.CompletedMoveTasks -lt 1000) {
        throw "R2A exit gate did not preserve the acceptance contract."
    }

    $snapshotPath = Join-Path $tempRoot "r2b-snapshot.json"
    $snapshot = [ordered]@{
        SchemaVersion = 1
        EvidenceType = "R2B_READ_ONLY_PREFLIGHT"
        PilotWarehouseCd = "PILOT-WH"
        ActiveTaskCount = 0
        MaintenanceWindowApproved = $true
        SnapshotEvidenceUri = "s3://cp6-evidence/r2b/snapshot.json"
        R2AExitEvidenceUri = "s3://cp6-evidence/r2a/pilot-exit.json"
        FeatureApprovalEvidenceUri = "s3://cp6-evidence/r2b/approval.json"
        Products = @(
            [ordered]@{
                ProductCd = "SERIAL-100"
                ExpectedPhysicalQuantity = 2
                ExpectedSerialCount = 2
                Buckets = @(
                    [ordered]@{
                        WarehouseCd = "PILOT-WH"
                        LocationCd = "A-01"
                        LotNo = ""
                        PhysicalQuantity = 2
                        ScannedSerialCount = 2
                    }
                )
            }
        )
    }
    [IO.File]::WriteAllText(
        $snapshotPath,
        ($snapshot | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    $preflightPath = Join-Path $tempRoot "r2b-preflight.json"
    & (Join-Path $repoRoot "scripts\test-r2b-preflight.ps1") `
        -SnapshotPath $snapshotPath `
        -OutputEvidencePath $preflightPath
    $preflight = [IO.File]::ReadAllText(
        $preflightPath,
        [Text.Encoding]::UTF8) | ConvertFrom-Json
    if ($preflight.Decision -ne "GO" -or
        [int]$preflight.ProductCount -ne 1) {
        throw "R2B preflight did not preserve the acceptance contract."
    }

    $snapshot.ActiveTaskCount = 1
    $blockedSnapshotPath = Join-Path $tempRoot "r2b-blocked-snapshot.json"
    [IO.File]::WriteAllText(
        $blockedSnapshotPath,
        ($snapshot | ConvertTo-Json -Depth 8),
        [Text.UTF8Encoding]::new($false))
    $blocked = $false
    try {
        & (Join-Path $repoRoot "scripts\test-r2b-preflight.ps1") `
            -SnapshotPath $blockedSnapshotPath `
            -OutputEvidencePath (Join-Path $tempRoot "r2b-blocked.json")
    }
    catch {
        $blocked = $true
    }
    if (-not $blocked) {
        throw "R2B preflight must reject a warehouse with active tasks."
    }

    Write-Host "R2 operations evidence contract passed."
}
finally {
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $resolvedTarget = [IO.Path]::GetFullPath($tempRoot)
    if ($resolvedTarget.StartsWith(
            $resolvedTempRoot,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTarget)) {
        Remove-Item -LiteralPath $resolvedTarget -Recurse -Force
    }
}
