[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Text -notmatch $Pattern) {
        throw "$Description is missing required contract '$Pattern'."
    }
}

function Assert-ValidPowerShell {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $repoRoot $RelativePath),
        [ref]$tokens,
        [ref]$parseErrors
    )
    if ($parseErrors.Count -gt 0) {
        $messages = ($parseErrors | ForEach-Object Message) -join "; "
        throw "$RelativePath contains PowerShell parse errors: $messages"
    }
    return $ast
}

$loadScripts = @(
    "scripts\load\wms-v2-read.k6.js",
    "scripts\load\wms-v2-pilot.k6.js",
    "scripts\load\wms-v2-workflow.k6.js"
)
foreach ($relativePath in $loadScripts) {
    $absolutePath = Join-Path $repoRoot $relativePath
    & node --check $absolutePath
    if ($LASTEXITCODE -ne 0) {
        throw "$relativePath contains invalid JavaScript."
    }
}

$localK6 = Get-ChildItem -LiteralPath (Join-Path $repoRoot ".tools\k6") `
    -Recurse -File -Filter "k6.exe" -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if ($null -ne $localK6) {
    foreach ($relativePath in $loadScripts) {
        & $localK6.FullName inspect (Join-Path $repoRoot $relativePath) | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "$relativePath could not be inspected by the workspace k6 binary."
        }
    }
}

$read = Read-RepoText "scripts\load\wms-v2-read.k6.js"
foreach ($required in @(
    "constant-arrival-rate",
    "RATE\s*\|\|\s*100",
    "MAX_READ_P95_MS\s*\|\|\s*300",
    "dropped_iterations"
)) {
    Assert-Contains $read $required "WMS read performance test"
}

$pilot = Read-RepoText "scripts\load\wms-v2-pilot.k6.js"
foreach ($required in @(
    "ONLINE_DEVICES\s*\|\|\s*500",
    "RATE\s*\|\|\s*100",
    "MAX_REALTIME_MS\s*\|\|\s*2000",
    "per-vu-iterations",
    "constant-arrival-rate",
    "/hubs/wms/negotiate\?negotiateVersion=1",
    "protocol:\s*'json'",
    "wms_hub_connection_success",
    "wms_realtime_delivery_ms",
    "count>0",
    "p\(95\)<"
)) {
    Assert-Contains $pilot $required "WMS pilot load test"
}

$workflow = Read-RepoText "scripts\load\wms-v2-workflow.k6.js"
foreach ($required in @(
    "TASK_NOS",
    "DEVICE_IDS",
    "MAX_SCAN_P95_MS\s*\|\|\s*300",
    "MAX_COMPLETE_P95_MS\s*\|\|\s*2000",
    "'scan'",
    "'complete'",
    "clientScanNo",
    "completionOperationId"
)) {
    Assert-Contains $workflow $required "WMS workflow performance test"
}

$taskService = Read-RepoText "CP6.Client.Core\WmsTaskService.cs"
foreach ($required in @(
    "OperationCanceledException",
    "!callerToken\.IsCancellationRequested",
    "RELOAD_TASK_STATE_THEN_RETRY_SAME_OPERATION",
    "RESCAN_WITH_SAME_CLIENT_SCAN_NO",
    "ClientScanNo"
)) {
    Assert-Contains $taskService $required "Native unknown-outcome recovery"
}

$mobile = Read-RepoText "CP6.Mobile\ViewModels.cs"
foreach ($required in @(
    "_pendingClientScanNo",
    "_pendingScanStep",
    "_pendingScanValue",
    "ClearPendingScan\(\)"
)) {
    Assert-Contains $mobile $required "Android stable scan retry"
}

$installerPath = "scripts\install-k6-portable.ps1"
[void](Assert-ValidPowerShell $installerPath)
$installer = Read-RepoText $installerPath
foreach ($required in @(
    'Version = "2\.1\.0"',
    '185ca503ead8f0348daa79c002469e5eb324473c39452f29b5f70b1c1b4c8503',
    'github\.com/grafana/k6/releases/download',
    'Get-FileHash.+SHA256',
    '\.tools\\k6',
    'StartsWith\(\$resolvedSystemTemp'
)) {
    Assert-Contains $installer $required "Portable k6 installer"
}

$preparePath = "scripts\prepare-r2-pilot.ps1"
$prepareAst = Assert-ValidPowerShell $preparePath
$prepareParameterNames = @(
    $prepareAst.ParamBlock.Parameters |
        ForEach-Object { $_.Name.VariablePath.UserPath }
)
if ($prepareParameterNames -contains "AccessToken") {
    throw "Pilot preparation must only accept the access token through process environment."
}
$prepare = Read-RepoText $preparePath
foreach ($required in @(
    '\$env:CP6_PILOT_ACCESS_TOKEN',
    'ConfirmIsolatedWarehouse',
    'health/live',
    'health/ready',
    'api/client/bootstrap',
    'api/v2/admin/wms-features',
    'api/v2/admin/client-devices',
    'api/v2/wms/tasks',
    'R2_PILOT',
    'pilot-input\.json',
    'preparation rollback'
)) {
    Assert-Contains $prepare $required "R2 pilot preparation"
}

$runnerPath = "scripts\invoke-r2-pilot.ps1"
$runnerAst = Assert-ValidPowerShell $runnerPath
$runnerParameterNames = @(
    $runnerAst.ParamBlock.Parameters |
        ForEach-Object { $_.Name.VariablePath.UserPath }
)
if ($runnerParameterNames -contains "AccessToken") {
    throw "Pilot execution must only accept the access token through process environment."
}
$runner = Read-RepoText $runnerPath
foreach ($required in @(
    '\$env:CP6_PILOT_ACCESS_TOKEN',
    'wms-v2-pilot\.k6\.js',
    'wms-v2-workflow\.k6\.js',
    '--summary-export',
    'WindowStyle Hidden',
    'REQUIRE_REALTIME_EVENT',
    'MAX_SCAN_P95_MS',
    'MAX_COMPLETE_P95_MS',
    'pilot-evidence\.json',
    'Get-FileHash.+SHA256'
)) {
    Assert-Contains $runner $required "R2 pilot orchestration"
}

Write-Host "R2 pilot performance and recovery contract passed."
