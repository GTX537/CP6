[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$RestoreEvidencePath,
    [Parameter(Mandatory = $true)][string]$OutputEvidencePath
)

$ErrorActionPreference = "Stop"
$dailyRoot = (Resolve-Path -LiteralPath $EvidenceDirectory -ErrorAction Stop).Path
$restorePath = (Resolve-Path -LiteralPath $RestoreEvidencePath -ErrorAction Stop).Path
$dailyFiles = @(Get-ChildItem -LiteralPath $dailyRoot -File -Filter "*.json" |
    Where-Object { $_.FullName -ne $restorePath })
$daily = @()
$reasons = [System.Collections.Generic.List[string]]::new()

foreach ($file in $dailyFiles) {
    try {
        $value = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8) |
            ConvertFrom-Json
        if ([string]$value.EvidenceType -eq "R2A_DAILY_RECONCILIATION") {
            $daily += [pscustomobject]@{
                File = $file
                Value = $value
                Date = [DateTime]::ParseExact(
                    [string]$value.BusinessDate,
                    "yyyy-MM-dd",
                    [Globalization.CultureInfo]::InvariantCulture)
            }
        }
    }
    catch {
        $reasons.Add("Invalid daily evidence '$($file.Name)': $($_.Exception.Message)")
    }
}
$daily = @($daily | Sort-Object Date)
if ($daily.Count -lt 14) {
    $reasons.Add("At least 14 daily reconciliation records are required.")
}
if (@($daily | Group-Object { $_.Date.ToString("yyyy-MM-dd") } |
        Where-Object Count -gt 1).Count -gt 0) {
    $reasons.Add("Daily reconciliation dates must be unique.")
}
for ($index = 1; $index -lt $daily.Count; $index++) {
    if (($daily[$index].Date - $daily[$index - 1].Date).Days -ne 1) {
        $reasons.Add("Daily reconciliation records must form one continuous window.")
        break
    }
}
$warehouseCodes = @($daily |
    ForEach-Object { [string]$_.Value.WarehouseCd } |
    Select-Object -Unique)
if ($warehouseCodes.Count -ne 1 -or
    [string]::IsNullOrWhiteSpace($warehouseCodes[0])) {
    $reasons.Add("All daily evidence must identify the same warehouse.")
}

$completedMoves = 0L
foreach ($record in $daily) {
    $metrics = $record.Value.Metrics
    $completedMoves += [int64]$metrics.CompletedMoveTasks
    if ([int64]$metrics.DuplicateInventoryTransactions -ne 0) {
        $reasons.Add("$($record.Value.BusinessDate): duplicate inventory transactions are non-zero.")
    }
    if ([decimal]$metrics.LostInventoryQuantity -ne 0) {
        $reasons.Add("$($record.Value.BusinessDate): lost inventory quantity is non-zero.")
    }
    if ([int64]$metrics.UnexplainedDifferences -ne 0) {
        $reasons.Add("$($record.Value.BusinessDate): unexplained differences are non-zero.")
    }
    if ([int]$metrics.ActivatedDeviceCount -lt 10 -or
        [int]$metrics.HealthyDeviceCount -lt 10) {
        $reasons.Add("$($record.Value.BusinessDate): fewer than ten pilot devices are healthy.")
    }
    if ([string]::IsNullOrWhiteSpace([string]$record.Value.ReconciledBy) -or
        [string]::IsNullOrWhiteSpace([string]$record.Value.ApprovedBy) -or
        ([string]$record.Value.ReconciledBy).Equals(
            [string]$record.Value.ApprovedBy,
            [StringComparison]::OrdinalIgnoreCase)) {
        $reasons.Add("$($record.Value.BusinessDate): two-person reconciliation approval is invalid.")
    }
}
if ($completedMoves -lt 1000) {
    $reasons.Add("The continuous pilot window contains fewer than 1000 completed MOVE tasks.")
}

try {
    $restore = [IO.File]::ReadAllText($restorePath, [Text.Encoding]::UTF8) |
        ConvertFrom-Json
    if ([string]$restore.EvidenceType -ne "DATABASE_RESTORE_REHEARSAL" -or
        -not [bool]$restore.Succeeded) {
        $reasons.Add("The database restore rehearsal did not succeed.")
    }
    if ([decimal]$restore.RpoMinutes -gt 5) {
        $reasons.Add("Database restore RPO exceeds five minutes.")
    }
    if ([decimal]$restore.RtoMinutes -gt 60) {
        $reasons.Add("Database restore RTO exceeds sixty minutes.")
    }
    if ([string]$restore.EvidenceUri -notmatch "^s3://[^/]+/.+") {
        $reasons.Add("Database restore evidence has no immutable object URI.")
    }
}
catch {
    $reasons.Add("Invalid database restore evidence: $($_.Exception.Message)")
}

$inputs = @($daily | ForEach-Object {
    [ordered]@{
        File = $_.File.Name
        Sha256 = (Get-FileHash -LiteralPath $_.File.FullName -Algorithm SHA256).Hash
    }
})
$inputs += [ordered]@{
    File = (Split-Path -Leaf $restorePath)
    Sha256 = (Get-FileHash -LiteralPath $restorePath -Algorithm SHA256).Hash
}
$decision = if ($reasons.Count -eq 0) { "GO" } else { "NO-GO" }
$result = [ordered]@{
    SchemaVersion = 1
    EvidenceType = "R2A_PILOT_EXIT"
    Decision = $decision
    WarehouseCd = if ($warehouseCodes.Count -eq 1) { $warehouseCodes[0] } else { $null }
    WindowStart = if ($daily.Count) { $daily[0].Date.ToString("yyyy-MM-dd") } else { $null }
    WindowEnd = if ($daily.Count) { $daily[-1].Date.ToString("yyyy-MM-dd") } else { $null }
    ContinuousDays = $daily.Count
    CompletedMoveTasks = $completedMoves
    EvaluatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    Reasons = @($reasons)
    Inputs = $inputs
}
$absoluteOutput = [IO.Path]::GetFullPath($OutputEvidencePath)
$outputDirectory = Split-Path -Parent $absoluteOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
[IO.File]::WriteAllText(
    $absoluteOutput,
    ($result | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
if ($decision -ne "GO") {
    throw "R2A pilot exit is NO-GO. See '$absoluteOutput'."
}
Write-Host "R2A pilot exit gate passed: $absoluteOutput"
