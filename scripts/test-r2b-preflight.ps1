[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SnapshotPath,
    [Parameter(Mandatory = $true)][string]$OutputEvidencePath
)

$ErrorActionPreference = "Stop"
$resolvedSnapshot = (Resolve-Path -LiteralPath $SnapshotPath -ErrorAction Stop).Path
$snapshot = [IO.File]::ReadAllText(
    $resolvedSnapshot,
    [Text.Encoding]::UTF8) | ConvertFrom-Json
$reasons = [System.Collections.Generic.List[string]]::new()
if ([int]$snapshot.SchemaVersion -ne 1 -or
    [string]$snapshot.EvidenceType -ne "R2B_READ_ONLY_PREFLIGHT") {
    $reasons.Add("Snapshot schema or evidence type is invalid.")
}
$pilotWarehouse = ([string]$snapshot.PilotWarehouseCd).Trim()
if ([string]::IsNullOrWhiteSpace($pilotWarehouse)) {
    $reasons.Add("PilotWarehouseCd is required.")
}
if ([int]$snapshot.ActiveTaskCount -ne 0) {
    $reasons.Add("The maintenance window has active WMS tasks.")
}
if (-not [bool]$snapshot.MaintenanceWindowApproved) {
    $reasons.Add("The maintenance window is not approved.")
}
foreach ($uriName in @(
    "SnapshotEvidenceUri",
    "R2AExitEvidenceUri",
    "FeatureApprovalEvidenceUri")) {
    if ([string]$snapshot.$uriName -notmatch "^s3://[^/]+/.+") {
        $reasons.Add("$uriName must reference immutable object evidence.")
    }
}

$products = @($snapshot.Products)
if ($products.Count -eq 0) {
    $reasons.Add("At least one product is required.")
}
$duplicateProducts = @($products |
    Group-Object { ([string]$_.ProductCd).Trim().ToUpperInvariant() } |
    Where-Object Count -gt 1)
if ($duplicateProducts.Count -gt 0) {
    $reasons.Add("Product codes in the conversion batch must be unique.")
}
foreach ($product in $products) {
    $productCd = ([string]$product.ProductCd).Trim()
    if ([string]::IsNullOrWhiteSpace($productCd)) {
        $reasons.Add("A product has no ProductCd.")
        continue
    }
    $physicalTotal = [decimal]0
    $scannedTotal = 0
    $buckets = @($product.Buckets)
    if ($buckets.Count -eq 0) {
        $reasons.Add("$productCd has no inventory buckets.")
        continue
    }
    foreach ($bucket in $buckets) {
        $physical = [decimal]$bucket.PhysicalQuantity
        $scanned = [int]$bucket.ScannedSerialCount
        if ($physical -lt 0 -or [decimal]::Truncate($physical) -ne $physical) {
            $reasons.Add("$productCd contains negative or fractional stock.")
        }
        if ($physical -ne 0 -and
            -not ([string]$bucket.WarehouseCd).Equals(
                $pilotWarehouse,
                [StringComparison]::OrdinalIgnoreCase)) {
            $reasons.Add("$productCd has non-zero stock outside the pilot warehouse.")
        }
        if ($scanned -ne [int]$physical) {
            $reasons.Add("$productCd has a bucket scan-to-stock mismatch.")
        }
        $physicalTotal += $physical
        $scannedTotal += $scanned
    }
    if ($physicalTotal -ne [decimal]$product.ExpectedPhysicalQuantity -or
        $scannedTotal -ne [int]$product.ExpectedSerialCount -or
        $physicalTotal -ne $scannedTotal) {
        $reasons.Add("$productCd aggregate quantity does not reconcile.")
    }
}

$decision = if ($reasons.Count -eq 0) { "GO" } else { "NO-GO" }
$result = [ordered]@{
    SchemaVersion = 1
    EvidenceType = "R2B_PREFLIGHT_DECISION"
    Decision = $decision
    PilotWarehouseCd = $pilotWarehouse
    ProductCount = $products.Count
    EvaluatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    SnapshotSha256 = (
        Get-FileHash -LiteralPath $resolvedSnapshot -Algorithm SHA256
    ).Hash
    Reasons = @($reasons)
}
$absoluteOutput = [IO.Path]::GetFullPath($OutputEvidencePath)
$outputDirectory = Split-Path -Parent $absoluteOutput
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
[IO.File]::WriteAllText(
    $absoluteOutput,
    ($result | ConvertTo-Json -Depth 6),
    [Text.UTF8Encoding]::new($false))
if ($decision -ne "GO") {
    throw "R2B preflight is NO-GO. See '$absoluteOutput'."
}
Write-Host "R2B read-only preflight passed: $absoluteOutput"
