[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PilotManifestPath,
    [string]$K6Path,
    [ValidatePattern("^\d+(s|m|h)$")]
    [string]$Duration = "2m",
    [ValidateRange(1, 5000)]
    [int]$OnlineDevices = 500,
    [ValidateRange(1, 10000)]
    [int]$Rate = 100,
    [ValidateRange(1, 300)]
    [int]$WarmupSeconds = 10,
    [ValidateRange(1, 60000)]
    [int]$MaxReadP95Ms = 300,
    [ValidateRange(1, 60000)]
    [int]$MaxScanP95Ms = 300,
    [ValidateRange(1, 60000)]
    [int]$MaxCompleteP95Ms = 2000,
    [ValidateRange(1, 60000)]
    [int]$MaxRealtimeMs = 2000,
    [string]$OutputDirectory
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$accessToken = $env:CP6_PILOT_ACCESS_TOKEN
if ([string]::IsNullOrWhiteSpace($accessToken)) {
    throw "Set CP6_PILOT_ACCESS_TOKEN to a short-lived test token."
}

$resolvedManifest = (Resolve-Path -LiteralPath $PilotManifestPath -ErrorAction Stop).Path
$manifest = [IO.File]::ReadAllText($resolvedManifest, [Text.Encoding]::UTF8) |
    ConvertFrom-Json
if ($manifest.Status -ne "Ready" -or @($manifest.Tasks).Count -eq 0) {
    throw "Pilot manifest must have status Ready and contain at least one task."
}

if ([string]::IsNullOrWhiteSpace($K6Path)) {
    $candidate = Get-ChildItem -LiteralPath (Join-Path $repoRoot ".tools\k6") `
        -Recurse -File -Filter "k6.exe" -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) {
        throw "k6.exe was not found. Install a workspace portable binary or pass -K6Path."
    }
    $K6Path = $candidate.FullName
}
else {
    $K6Path = (Resolve-Path -LiteralPath $K6Path -ErrorAction Stop).Path
}

function Convert-DurationToMilliseconds {
    param([Parameter(Mandatory = $true)][string]$Value)

    if ($Value -notmatch "^(\d+)(s|m|h)$") {
        throw "Unsupported duration '$Value'."
    }
    $amount = [int64]$Matches[1]
    $multiplier = switch ($Matches[2]) {
        "s" { 1000 }
        "m" { 60 * 1000 }
        "h" { 60 * 60 * 1000 }
    }
    return $amount * $multiplier
}

$durationMs = Convert-DurationToMilliseconds $Duration
$socketHoldMs = $durationMs - 10000
if ($socketHoldMs -le ($WarmupSeconds * 1000)) {
    throw "Duration must leave at least 10 seconds after warm-up for workflow events."
}

$runId = "$($manifest.RunId)-execution-$([DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ'))"
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "artifacts\pilot\$runId"
}
elseif (-not [IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot $OutputDirectory
}
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null

$pilotSummary = Join-Path $OutputDirectory "pilot-summary.json"
$workflowSummary = Join-Path $OutputDirectory "workflow-summary.json"
$pilotStdout = Join-Path $OutputDirectory "pilot.stdout.log"
$pilotStderr = Join-Path $OutputDirectory "pilot.stderr.log"
$workflowStdout = Join-Path $OutputDirectory "workflow.stdout.log"
$workflowStderr = Join-Path $OutputDirectory "workflow.stderr.log"
$evidencePath = Join-Path $OutputDirectory "pilot-evidence.json"
$pilotScript = Join-Path $repoRoot "scripts\load\wms-v2-pilot.k6.js"
$workflowScript = Join-Path $repoRoot "scripts\load\wms-v2-workflow.k6.js"

$environmentNames = @(
    "API_URL",
    "ACCESS_TOKEN",
    "ONLINE_DEVICES",
    "RATE",
    "DURATION",
    "SOCKET_HOLD_MS",
    "REQUIRE_REALTIME_EVENT",
    "MAX_READ_P95_MS",
    "MAX_REALTIME_MS",
    "TASK_NOS",
    "DEVICE_IDS",
    "MAX_SCAN_P95_MS",
    "MAX_COMPLETE_P95_MS"
)
$previousEnvironment = @{}
foreach ($name in $environmentNames) {
    $previousEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
}

$pilotProcess = $null
$pilotExitCode = $null
$workflowExitCode = $null
try {
    $env:API_URL = [string]$manifest.BaseUrl
    $env:ACCESS_TOKEN = $accessToken
    $env:ONLINE_DEVICES = [string]$OnlineDevices
    $env:RATE = [string]$Rate
    $env:DURATION = $Duration
    $env:SOCKET_HOLD_MS = [string]$socketHoldMs
    $env:REQUIRE_REALTIME_EVENT = "true"
    $env:MAX_READ_P95_MS = [string]$MaxReadP95Ms
    $env:MAX_REALTIME_MS = [string]$MaxRealtimeMs
    $env:TASK_NOS = (@($manifest.Tasks | ForEach-Object TaskNo) -join ",")
    $env:DEVICE_IDS = (@($manifest.DeviceIds) -join ",")
    $env:MAX_SCAN_P95_MS = [string]$MaxScanP95Ms
    $env:MAX_COMPLETE_P95_MS = [string]$MaxCompleteP95Ms

    $pilotProcess = Start-Process -FilePath $K6Path `
        -ArgumentList @(
            "run",
            "--no-color",
            "--summary-export=`"$pilotSummary`"",
            "`"$pilotScript`""
        ) `
        -RedirectStandardOutput $pilotStdout `
        -RedirectStandardError $pilotStderr `
        -WindowStyle Hidden `
        -PassThru

    Start-Sleep -Seconds $WarmupSeconds

    $workflowProcess = Start-Process -FilePath $K6Path `
        -ArgumentList @(
            "run",
            "--no-color",
            "--summary-export=`"$workflowSummary`"",
            "`"$workflowScript`""
        ) `
        -RedirectStandardOutput $workflowStdout `
        -RedirectStandardError $workflowStderr `
        -WindowStyle Hidden `
        -Wait `
        -PassThru
    $workflowExitCode = $workflowProcess.ExitCode

    $pilotProcess.WaitForExit()
    $pilotExitCode = $pilotProcess.ExitCode
}
finally {
    if ($null -ne $pilotProcess -and -not $pilotProcess.HasExited) {
        Stop-Process -Id $pilotProcess.Id -ErrorAction SilentlyContinue
        $pilotProcess.WaitForExit()
        $pilotExitCode = $pilotProcess.ExitCode
    }
    foreach ($name in $environmentNames) {
        $previous = $previousEnvironment[$name]
        if ($null -eq $previous) {
            [Environment]::SetEnvironmentVariable($name, $null, "Process")
        }
        else {
            [Environment]::SetEnvironmentVariable($name, $previous, "Process")
        }
    }
}

$files = @(
    $pilotSummary,
    $workflowSummary,
    $pilotStdout,
    $pilotStderr,
    $workflowStdout,
    $workflowStderr
)
$evidence = [ordered]@{
    SchemaVersion = 1
    RunId = $runId
    CompletedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    BaseUrl = [string]$manifest.BaseUrl
    WarehouseCd = [string]$manifest.WarehouseCd
    TaskCount = @($manifest.Tasks).Count
    DeviceCount = @($manifest.DeviceIds).Count
    Load = [ordered]@{
        OnlineDevices = $OnlineDevices
        RatePerSecond = $Rate
        Duration = $Duration
        MaxReadP95Ms = $MaxReadP95Ms
        MaxScanP95Ms = $MaxScanP95Ms
        MaxCompleteP95Ms = $MaxCompleteP95Ms
        MaxRealtimeMs = $MaxRealtimeMs
    }
    K6 = (& $K6Path version | Select-Object -First 1)
    PilotExitCode = $pilotExitCode
    WorkflowExitCode = $workflowExitCode
    Files = @($files | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object {
        $item = Get-Item -LiteralPath $_
        [ordered]@{
            Name = $item.Name
            Bytes = $item.Length
            Sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash
        }
    })
}
[IO.File]::WriteAllText(
    $evidencePath,
    ($evidence | ConvertTo-Json -Depth 10),
    [Text.UTF8Encoding]::new($false)
)

Write-Host "Pilot evidence: $evidencePath"
if ($pilotExitCode -ne 0 -or $workflowExitCode -ne 0) {
    throw "R2 pilot failed. Pilot exit=$pilotExitCode; workflow exit=$workflowExitCode. Review archived summaries and logs."
}
Write-Host "R2 pilot performance gate passed."
