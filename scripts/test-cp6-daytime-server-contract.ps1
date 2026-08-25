[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$serverScript = Join-Path $PSScriptRoot 'Invoke-Cp6DaytimeServer.ps1'
$batchScript = Join-Path $repositoryRoot 'cp6-daytime-server.bat'
$failures = @()

function Add-ContractFailure {
    param([string]$Message)
    $script:failures += $Message
}

foreach ($file in @($serverScript, $batchScript)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        Add-ContractFailure "Missing file: $file"
    }
}

if ($failures.Count -eq 0) {
    $tokens = $null
    $parseErrors = $null
    $null = [System.Management.Automation.Language.Parser]::ParseFile($serverScript, [ref]$tokens, [ref]$parseErrors)
    foreach ($parseError in $parseErrors) {
        Add-ContractFailure "PowerShell parse error: $($parseError.Message)"
    }

    $serverText = Get-Content -LiteralPath $serverScript -Raw
    $batchText = Get-Content -LiteralPath $batchScript -Raw

    foreach ($action in @('Start', 'Status', 'ClosePublic', 'StopAll')) {
        if ($serverText -notmatch [regex]::Escape("'$action'")) {
            Add-ContractFailure "Missing Action: $action"
        }
    }

    foreach ($endpoint in @(
        'http://127.0.0.1:8080',
        'http://127.0.0.1:9991/health/ready',
        'https://cp6.uk',
        'https://api.cp6.uk/health/ready'
    )) {
        if ($serverText -notmatch [regex]::Escape($endpoint)) {
            Add-ContractFailure "Missing health endpoint: $endpoint"
        }
    }

    if ($serverText -notmatch "@\('stop',\s*'cp6-cloudflared'\)") {
        Add-ContractFailure 'ClosePublic must stop only cp6-cloudflared.'
    }
    if ($serverText -notmatch "@\('stop'\)") {
        Add-ContractFailure 'StopAll must use docker compose stop.'
    }
    if ($serverText -notmatch 'credentials-file' -or $serverText -notmatch 'hostCredentialFile') {
        Add-ContractFailure 'Start must preflight the Cloudflare Tunnel credential file.'
    }

    $forbiddenPatterns = @(
        '(?i)compose\s+down',
        '(?i)down\s+-v',
        '(?i)powercfg',
        '(?i)schtasks',
        '(?i)SetSuspendState',
        '(?i)Stop-Process\s+[^\r\n]*cloudflared'
    )
    foreach ($pattern in $forbiddenPatterns) {
        if ($serverText -match $pattern -or $batchText -match $pattern) {
            Add-ContractFailure "Forbidden destructive or power-setting pattern found: $pattern"
        }
    }

    foreach ($verb in @('start', 'start-build', 'status', 'close', 'stop')) {
        if ($batchText -notmatch [regex]::Escape('"' + $verb + '"')) {
            Add-ContractFailure "Batch command is not mapped: $verb"
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'CP6 daytime server contract test failed:'
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host 'CP6 daytime server contract test passed.'
Write-Host 'Verified syntax, four actions, five entry points, health checks, Tunnel-only close, data-preserving stop, credential preflight, and no power changes.'
exit 0
