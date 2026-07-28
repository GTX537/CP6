[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^https?://")]
    [string]$BaseUrl,
    [Parameter(Mandatory = $true)]
    [string]$WarehouseCd,
    [string]$AreaCd,
    [Parameter(Mandatory = $true)]
    [string]$FromLocationCd,
    [Parameter(Mandatory = $true)]
    [string]$ToLocationCd,
    [Parameter(Mandatory = $true)]
    [string]$ProductCd,
    [string]$LotNo,
    [ValidateRange(0.00000001, 9999999999999)]
    [decimal]$Quantity = 1,
    [ValidateRange(1, 1000)]
    [int]$TaskCount = 10,
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string[]]$DeviceIds,
    [string]$OutputDirectory,
    [switch]$ConfirmIsolatedWarehouse,
    [switch]$KeepPartialTasks
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$baseUri = [uri]$BaseUrl
$isLoopback = $baseUri.IsLoopback
if ($baseUri.Scheme -ne [uri]::UriSchemeHttps -and -not $isLoopback) {
    throw "Pilot preparation requires HTTPS except for a loopback development URL."
}
if (-not $isLoopback -and -not $ConfirmIsolatedWarehouse) {
    throw "Pass -ConfirmIsolatedWarehouse only after verifying this is a non-production warehouse."
}

$accessToken = $env:CP6_PILOT_ACCESS_TOKEN
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "Set CP6_PILOT_ACCESS_TOKEN to a short-lived test token. Tokens are never accepted as parameters or written to evidence."
}

$runId = "r2-pilot-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))-$([Guid]::NewGuid().ToString('N').Substring(0, 8))"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\pilot\$runId"
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$manifestPath = Join-Path $OutputDirectory "pilot-input.json"
$normalizedBaseUrl = $BaseUrl.TrimEnd("/")
$headers = @{
    Authorization = "Bearer $accessToken"
    Accept = "application/json"
}

function Invoke-Cp6 {
    param(
        [Parameter(Mandatory = $true)][string]$Method,
        [Parameter(Mandatory = $true)][string]$Path,
        $Body
    )

    $arguments = @{
        Method = $Method
        Uri = "$normalizedBaseUrl/$($Path.TrimStart('/'))"
        Headers = $headers
        TimeoutSec = 30
    }
    if ($null -ne $Body) {
        $arguments.ContentType = "application/json"
        $arguments.Body = $Body | ConvertTo-Json -Depth 10 -Compress
    }
    try {
        return Invoke-RestMethod @arguments
    }
    catch {
        $detail = $_.ErrorDetails.Message
        if ([string]::IsNullOrWhiteSpace($detail)) {
            $detail = $_.Exception.Message
        }
        throw "CP6 $Method $Path failed: $detail"
    }
}

function Write-Manifest {
    param(
        [Parameter(Mandatory = $true)][string]$Status,
        [Parameter(Mandatory = $true)][System.Collections.IEnumerable]$Tasks,
        [string]$Failure
    )

    $manifest = [ordered]@{
        SchemaVersion = 1
        RunId = $runId
        Status = $Status
        CreatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        BaseUrl = $normalizedBaseUrl
        WarehouseCd = $WarehouseCd.Trim()
        AreaCd = if ([string]::IsNullOrWhiteSpace($AreaCd)) { $null } else { $AreaCd.Trim() }
        FromLocationCd = $FromLocationCd.Trim()
        ToLocationCd = $ToLocationCd.Trim()
        ProductCd = $ProductCd.Trim()
        LotNo = if ([string]::IsNullOrWhiteSpace($LotNo)) { $null } else { $LotNo.Trim() }
        Quantity = $Quantity
        DeviceIds = @($DeviceIds | ForEach-Object { $_.Trim() } | Select-Object -Unique)
        Tasks = @($Tasks)
        Failure = $Failure
    }
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 10),
        [Text.UTF8Encoding]::new($false)
    )
}

Write-Host "Checking CP6 liveness, readiness, bootstrap, features, and devices..."
[void](Invoke-Cp6 "GET" "health/live" $null)
[void](Invoke-Cp6 "GET" "health/ready" $null)
[void](Invoke-Cp6 "GET" "api/client/bootstrap?platform=android&currentVersion=1.0.0" $null)

$features = @(Invoke-Cp6 "GET" "api/v2/admin/wms-features" $null)
$warehouseFeature = $features |
    Where-Object { $_.warehouseCd -eq $WarehouseCd.Trim() } |
    Select-Object -First 1
if ($null -eq $warehouseFeature -or -not $warehouseFeature.productionMoveEnabled) {
    throw "Production MOVE is not enabled for warehouse '$WarehouseCd'."
}

$warehouseQuery = [uri]::EscapeDataString($WarehouseCd.Trim())
$devicesPage = Invoke-Cp6 "GET" "api/v2/admin/client-devices?warehouseCd=$warehouseQuery&page=1&pageSize=500" $null
$activeDevices = @($devicesPage.items | Where-Object { $_.status -eq "Active" })
$normalizedDeviceIds = @($DeviceIds |
    ForEach-Object { $_.Trim() } |
    Where-Object { $_ } |
    Select-Object -Unique)
if ($normalizedDeviceIds.Count -eq 0) {
    throw "At least one non-empty device ID is required."
}
foreach ($deviceId in $normalizedDeviceIds) {
    $device = $activeDevices |
        Where-Object { $_.deviceId -eq $deviceId } |
        Select-Object -First 1
    if ($null -eq $device) {
        throw "Device '$deviceId' is not active in warehouse '$WarehouseCd'."
    }
    if (
        -not [string]::IsNullOrWhiteSpace($AreaCd) -and
        -not [string]::IsNullOrWhiteSpace($device.areaCd) -and
        $device.areaCd -ne $AreaCd.Trim()
    ) {
        throw "Device '$deviceId' is restricted to area '$($device.areaCd)', not '$AreaCd'."
    }
}

$created = [System.Collections.Generic.List[object]]::new()
try {
    for ($index = 1; $index -le $TaskCount; $index++) {
        $operationId = [Guid]::NewGuid()
        $request = [ordered]@{
            operationId = $operationId
            priority = 2
            warehouseCd = $WarehouseCd.Trim()
            areaCd = if ([string]::IsNullOrWhiteSpace($AreaCd)) { $null } else { $AreaCd.Trim() }
            fromLocationCd = $FromLocationCd.Trim()
            toLocationCd = $ToLocationCd.Trim()
            productCd = $ProductCd.Trim()
            lotNo = if ([string]::IsNullOrWhiteSpace($LotNo)) { $null } else { $LotNo.Trim() }
            qty = $Quantity
            instruction = "R2 isolated pilot $runId ($index/$TaskCount)"
            remarks = "Generated by prepare-r2-pilot.ps1"
            sourceType = "R2_PILOT"
            sourceNo = $runId
        }
        $task = Invoke-Cp6 "POST" "api/v2/wms/tasks" $request
        $created.Add([ordered]@{
            TaskNo = $task.taskNo
            OperationId = $operationId
            Status = $task.status
            RowVersion = $task.rowVersion
        })
        Write-Progress -Activity "Preparing R2 MOVE tasks" `
            -Status "$index / $TaskCount" `
            -PercentComplete (($index / $TaskCount) * 100)
    }
    Write-Progress -Activity "Preparing R2 MOVE tasks" -Completed
    Write-Manifest "Ready" $created $null
}
catch {
    $failure = $_.Exception.Message
    Write-Manifest "Failed" $created $failure
    if (-not $KeepPartialTasks) {
        foreach ($task in $created) {
            try {
                [void](Invoke-Cp6 "POST" "api/v2/wms/tasks/$([uri]::EscapeDataString($task.TaskNo))/cancel" @{
                    operationId = [Guid]::NewGuid()
                    rowVersion = $task.RowVersion
                    reason = "R2 pilot preparation rollback"
                })
            }
            catch {
                Write-Warning "Could not cancel partial pilot task '$($task.TaskNo)': $($_.Exception.Message)"
            }
        }
    }
    throw
}

Write-Host "Prepared $($created.Count) isolated MOVE tasks."
Write-Host "Pilot manifest: $manifestPath"
