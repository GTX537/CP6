[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^\d{4}-\d{2}-\d{2}$")]
    [string]$BusinessDate,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$WarehouseCd,
    [ValidateRange(0, 2147483647)]
    [int]$CompletedMoveTasks,
    [ValidateRange(0, 2147483647)]
    [int]$DuplicateInventoryTransactions,
    [ValidateRange(0, 9999999999999)]
    [decimal]$LostInventoryQuantity,
    [ValidateRange(0, 2147483647)]
    [int]$UnexplainedDifferences,
    [ValidateRange(10, 10000)]
    [int]$ActivatedDeviceCount,
    [ValidateRange(0, 10000)]
    [int]$HealthyDeviceCount,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]]$EvidenceUris,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ReconciledBy,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$ApprovedBy,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
$parsedDate = [DateTime]::MinValue
if (-not [DateTime]::TryParseExact(
        $BusinessDate,
        "yyyy-MM-dd",
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::None,
        [ref]$parsedDate)) {
    throw "BusinessDate is not a valid calendar date."
}
if ($WarehouseCd.Trim().Length -gt 10) {
    throw "WarehouseCd exceeds the WMS warehouse-code limit."
}
if ($HealthyDeviceCount -gt $ActivatedDeviceCount) {
    throw "HealthyDeviceCount cannot exceed ActivatedDeviceCount."
}
if ($ReconciledBy.Trim().Equals(
        $ApprovedBy.Trim(),
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Daily reconciliation requires a different approver."
}
$normalizedUris = @($EvidenceUris |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ } |
    Select-Object -Unique)
if ($normalizedUris.Count -eq 0 -or
    @($normalizedUris | Where-Object { $_ -notmatch "^s3://[^/]+/.+" }).Count -gt 0) {
    throw "Every evidence URI must be a non-empty s3:// object URI."
}

$absoluteOutput = [IO.Path]::GetFullPath($OutputPath)
if (Test-Path -LiteralPath $absoluteOutput) {
    throw "Evidence output already exists and will not be overwritten."
}
$outputDirectory = Split-Path -Parent $absoluteOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
$evidence = [ordered]@{
    SchemaVersion = 1
    EvidenceType = "R2A_DAILY_RECONCILIATION"
    WarehouseCd = $WarehouseCd.Trim()
    BusinessDate = $parsedDate.ToString("yyyy-MM-dd")
    CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    Metrics = [ordered]@{
        CompletedMoveTasks = $CompletedMoveTasks
        DuplicateInventoryTransactions = $DuplicateInventoryTransactions
        LostInventoryQuantity = $LostInventoryQuantity
        UnexplainedDifferences = $UnexplainedDifferences
        ActivatedDeviceCount = $ActivatedDeviceCount
        HealthyDeviceCount = $HealthyDeviceCount
    }
    EvidenceUris = $normalizedUris
    ReconciledBy = $ReconciledBy.Trim()
    ApprovedBy = $ApprovedBy.Trim()
}
[IO.File]::WriteAllText(
    $absoluteOutput,
    ($evidence | ConvertTo-Json -Depth 8),
    [Text.UTF8Encoding]::new($false))
Write-Host "R2A daily reconciliation evidence: $absoluteOutput"
