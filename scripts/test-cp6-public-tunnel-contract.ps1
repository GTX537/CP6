[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$composePath = Join-Path $repoRoot 'deploy\home\tunnel\compose.yaml'
$scriptPath = Join-Path $PSScriptRoot 'Invoke-Cp6PublicTunnel.ps1'
$labComposePath = Join-Path $repoRoot 'deploy\lab\compose\compose.yaml'
$pipelinePath = Join-Path $repoRoot 'azure-pipelines-dev.yml'

foreach ($path in @($composePath, $scriptPath, $labComposePath, $pipelinePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required public Tunnel contract file is missing: $path"
    }
}

$compose = Get-Content -LiteralPath $composePath -Raw -Encoding utf8
$script = Get-Content -LiteralPath $scriptPath -Raw -Encoding utf8
$labCompose = Get-Content -LiteralPath $labComposePath -Raw -Encoding utf8
$pipeline = Get-Content -LiteralPath $pipelinePath -Raw -Encoding utf8

foreach ($pattern in @(
    'CP6_CLOUDFLARED_IMAGE:\?Set CP6_CLOUDFLARED_IMAGE to a pinned image ID',
    'CP6_TUNNEL_CONFIG_DIR:\?Set CP6_TUNNEL_CONFIG_DIR',
    ':/etc/cloudflared:ro',
    'external:\s*true',
    'name:\s*cp6-dev_default'
)) {
    if ($compose -notmatch $pattern) {
        throw "Dedicated Tunnel Compose is missing '$pattern'."
    }
}
if ($compose -match '(?m)^\s*ports:' -or $compose -match '(?i):latest') {
    throw 'Dedicated Tunnel must not publish ports or use a mutable latest image.'
}

foreach ($pattern in @(
    "Test-RootTunnelRunning",
    "throw 'cp6-cloudflared is still running",
    "Assert-DevApplicationReady",
    "Start requires -ExpectedGitSha",
    "'https://api.cp6.uk/health/ready'",
    "'https://cp6.uk/release.json'",
    "'stop', 'cloudflared'",
    "Public verification failed and the new connector could not be stopped",
    'sha256:\[0-9a-f\]\{64\}'
)) {
    if ($script -notmatch $pattern) {
        throw "Dedicated Tunnel controller is missing '$pattern'."
    }
}
foreach ($forbidden in @(
    'Invoke-Cp6DaytimeServer',
    "'stop'.*'cp6-cloudflared'",
    '(?i)\bdown\b',
    '(?i)docker\s+volume',
    '(?i)Remove-Item'
)) {
    if ($script -match $forbidden) {
        throw "Dedicated Tunnel controller contains forbidden operation '$forbidden'."
    }
}

if ($labCompose -notmatch '(?s)web:.*?aliases:\s*\r?\n\s*- cp6-web') {
    throw 'cp6-dev Web does not expose the cp6-web network alias required by Tunnel config.'
}
if ($pipeline -match 'Invoke-Cp6PublicTunnel\.ps1') {
    throw 'DEV deployment must not silently perform the one-time public Tunnel cutover.'
}

Write-Host 'CP6 dedicated public Tunnel contract passed.'
