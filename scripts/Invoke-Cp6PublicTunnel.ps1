[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Validate', 'Start', 'Status', 'Stop')]
    [string]$Action,

    [string]$TunnelConfigDirectory = (Join-Path `
        (Split-Path -Parent $PSScriptRoot) `
        'cloudflared-docker'),

    [string]$CloudflaredImage = '',

    [string]$ExpectedGitSha = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$composePath = Join-Path $repoRoot 'deploy\home\tunnel\compose.yaml'
$configDirectory = [IO.Path]::GetFullPath($TunnelConfigDirectory)
$configPath = Join-Path $configDirectory 'config.yml'
$projectName = 'cp6-public-tunnel'

function Invoke-Docker {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = @(& docker @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

function Assert-TunnelConfiguration {
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        throw "Cloudflare Tunnel config was not found at '$configPath'."
    }

    $configText = Get-Content -LiteralPath $configPath -Raw -Encoding utf8
    foreach ($requiredRoute in @(
        'hostname:\s*cp6\.uk',
        'service:\s*http://cp6-web:80',
        'hostname:\s*api\.cp6\.uk',
        'service:\s*http://cp6-api:5000',
        'service:\s*http_status:404'
    )) {
        if ($configText -notmatch $requiredRoute) {
            throw "Tunnel config is missing required cp6-dev route '$requiredRoute'."
        }
    }

    $credentialMatch = [regex]::Match(
        $configText,
        '(?m)^\s*credentials-file\s*:\s*(?<path>.+?)\s*$')
    if (-not $credentialMatch.Success) {
        throw 'Tunnel config does not declare credentials-file.'
    }
    $credentialName = [IO.Path]::GetFileName(
        $credentialMatch.Groups['path'].Value.Trim().Trim('"').Trim("'"))
    if ([string]::IsNullOrWhiteSpace($credentialName) -or
        -not (Test-Path -LiteralPath (Join-Path $configDirectory $credentialName) -PathType Leaf)) {
        throw 'Tunnel credential JSON is missing from the configured directory.'
    }
}

function Assert-DockerReady {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'docker was not found.'
    }
    Invoke-Docker -Arguments @('info', '--format', '{{.ServerVersion}}') | Out-Null
}

function Assert-DevApplicationReady {
    Invoke-Docker -Arguments @('network', 'inspect', 'cp6-dev_default') | Out-Null
    foreach ($url in @(
        'http://127.0.0.1:19991/health/ready',
        'http://127.0.0.1:18080/release.json'
    )) {
        Invoke-RestMethod -Uri $url -TimeoutSec 15 | Out-Null
    }
}

function Test-RootTunnelRunning {
    $state = @(& docker inspect --format '{{.State.Running}}' cp6-cloudflared 2>$null)
    if ($LASTEXITCODE -ne 0) {
        return $false
    }
    return ($state | Select-Object -First 1).Trim() -eq 'true'
}

function Resolve-CloudflaredImage {
    if (-not [string]::IsNullOrWhiteSpace($CloudflaredImage)) {
        if ($CloudflaredImage -notmatch '^sha256:[0-9a-f]{64}$') {
            throw 'CloudflaredImage must be a pinned local sha256 image ID.'
        }
        return $CloudflaredImage
    }

    $imageId = $null
    foreach ($containerName in @('cp6-public-tunnel-cloudflared-1', 'cp6-cloudflared')) {
        $candidate = @(& docker inspect --format '{{.Image}}' $containerName 2>$null) |
            Select-Object -First 1
        if ($LASTEXITCODE -eq 0 -and $candidate -match '^sha256:[0-9a-f]{64}$') {
            $imageId = $candidate.Trim()
            break
        }
    }
    if ($null -eq $imageId) {
        throw 'Supply -CloudflaredImage with a pinned sha256 image ID.'
    }
    return $imageId
}

function Invoke-PublicProbe {
    param([Parameter(Mandatory = $true)][string]$Uri)

    $lastError = $null
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            return Invoke-RestMethod -Uri $Uri -TimeoutSec 15
        }
        catch {
            $lastError = $_
            if ($attempt -lt 6) {
                Start-Sleep -Seconds 5
            }
        }
    }
    throw "Public probe '$Uri' did not succeed after six attempts: $($lastError.Exception.Message)"
}

function Invoke-TunnelCompose {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $composeArguments = @(
        'compose',
        '--project-name', $projectName,
        '-f', $composePath
    ) + $Arguments
    Invoke-Docker -Arguments $composeArguments
}

if ($Action -eq 'Validate') {
    Assert-TunnelConfiguration
    Assert-DockerReady
    Resolve-CloudflaredImage | Out-Null
    Assert-DevApplicationReady
    if (Test-RootTunnelRunning) {
        Write-Warning 'The old cp6-cloudflared connector is still running; stop it before Start.'
    }
    Write-Host 'cp6-dev and the dedicated public Tunnel configuration are ready for a controlled cutover.'
    exit 0
}

Assert-DockerReady
$env:CP6_TUNNEL_CONFIG_DIR = $configDirectory
$env:CP6_CLOUDFLARED_IMAGE = Resolve-CloudflaredImage

switch ($Action) {
    'Start' {
        Assert-TunnelConfiguration
        Assert-DevApplicationReady
        if ($ExpectedGitSha -notmatch '^[0-9a-fA-F]{40}$') {
            throw 'Start requires -ExpectedGitSha with the exact 40-character DEV commit.'
        }
        if (Test-RootTunnelRunning) {
            throw 'cp6-cloudflared is still running. Stop the old connector explicitly before starting cp6-public-tunnel.'
        }

        Invoke-TunnelCompose -Arguments @('config', '--quiet') | Out-Null
        Invoke-TunnelCompose -Arguments @('up', '-d', 'cloudflared') | Out-Null
        $running = Invoke-TunnelCompose -Arguments @(
            'ps', 'cloudflared', '--status', 'running', '--quiet')
        if (@($running).Count -eq 0) {
            throw 'The dedicated cp6-public-tunnel connector did not remain running.'
        }

        try {
            $publicReady = Invoke-PublicProbe -Uri 'https://api.cp6.uk/health/ready'
            $publicRelease = Invoke-PublicProbe -Uri 'https://api.cp6.uk/health/release'
            $publicWeb = Invoke-PublicProbe -Uri 'https://cp6.uk/release.json'
            if ($publicReady.status -ne 'Healthy') {
                throw 'The public API did not become ready after Tunnel cutover.'
            }
            if ($publicRelease.gitSha -ne $ExpectedGitSha -or
                $publicWeb.gitSha -ne $ExpectedGitSha) {
                throw 'The public API/Web identity does not match ExpectedGitSha.'
            }
        }
        catch {
            $cutoverFailure = $_
            try {
                Invoke-TunnelCompose -Arguments @('stop', 'cloudflared') | Out-Null
            }
            catch {
                Write-Warning 'Public verification failed and the new connector could not be stopped automatically.'
            }
            throw $cutoverFailure
        }
        Write-Host "cp6-public-tunnel is serving cp6-dev at Git SHA $($publicRelease.gitSha)."
    }
    'Status' {
        Invoke-TunnelCompose -Arguments @('ps', '--all')
    }
    'Stop' {
        Invoke-TunnelCompose -Arguments @('stop', 'cloudflared') | Out-Null
        Write-Host 'Stopped cp6-public-tunnel without removing containers, networks, or volumes.'
    }
}
