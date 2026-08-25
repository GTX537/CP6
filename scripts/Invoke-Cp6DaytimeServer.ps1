[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('Start', 'Status', 'ClosePublic', 'StopAll')]
    [string]$Action,

    [switch]$Build,

    [switch]$SkipPublicCheck,

    [ValidateRange(30, 600)]
    [int]$TimeoutSeconds = 180,

    [string]$ProjectRoot = (Split-Path -Parent $PSScriptRoot)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:ProjectRootFull = [System.IO.Path]::GetFullPath($ProjectRoot)
$script:ComposeFile = Join-Path $script:ProjectRootFull 'docker-compose.yml'
$script:EnvironmentFile = Join-Path $script:ProjectRootFull '.env'
$script:TunnelConfigFile = Join-Path $script:ProjectRootFull 'cloudflared-docker\config.yml'
$script:ExpectedServices = @(
    'cp6-db',
    'cp6-redis',
    'cp6-mq',
    'cp6-kafka',
    'cp6-api',
    'cp6-web',
    'cp6-cloudflared'
)
$script:LocalEndpoints = @(
    [pscustomobject]@{ Name = 'Web (local)'; Url = 'http://127.0.0.1:8080' },
    [pscustomobject]@{ Name = 'API readiness (local)'; Url = 'http://127.0.0.1:9991/health/ready' }
)
$script:PublicEndpoints = @(
    [pscustomobject]@{ Name = 'Web (public)'; Url = 'https://cp6.uk' },
    [pscustomobject]@{ Name = 'API readiness (public)'; Url = 'https://api.cp6.uk/health/ready' }
)

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$LiteralPath,

        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if (-not (Test-Path -LiteralPath $LiteralPath -PathType Leaf)) {
        throw "$Description does not exist: $LiteralPath"
    }
}

function Assert-DockerAvailable {
    if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
        throw 'The docker command was not found. Install and start Docker Desktop first.'
    }

    $versionOutput = @(& docker version --format '{{.Server.Version}}' 2>&1)
    $versionExitCode = $LASTEXITCODE
    if ($versionExitCode -ne 0) {
        throw "Docker Engine is not ready. Start Docker Desktop and try again.`n$($versionOutput -join [Environment]::NewLine)"
    }
}

function Assert-BaseConfiguration {
    Assert-FileExists -LiteralPath $script:ComposeFile -Description 'Compose configuration'
    Assert-FileExists -LiteralPath $script:EnvironmentFile -Description 'Docker environment file .env'
}

function Assert-StartConfiguration {
    Assert-BaseConfiguration
    Assert-FileExists -LiteralPath $script:TunnelConfigFile -Description 'Cloudflare Tunnel configuration'

    $credentialLine = Select-String -LiteralPath $script:TunnelConfigFile -Pattern '^\s*credentials-file\s*:\s*(?<value>.+?)\s*$' | Select-Object -First 1
    if ($null -eq $credentialLine) {
        throw 'Cloudflare Tunnel configuration is missing credentials-file.'
    }

    $containerCredentialPath = $credentialLine.Matches[0].Groups['value'].Value.Trim().Trim('"').Trim("'")
    $credentialFileName = [System.IO.Path]::GetFileName($containerCredentialPath)
    if ([string]::IsNullOrWhiteSpace($credentialFileName)) {
        throw 'Cloudflare Tunnel credentials-file path is invalid.'
    }

    $hostCredentialFile = Join-Path (Join-Path $script:ProjectRootFull 'cloudflared-docker') $credentialFileName
    if (-not (Test-Path -LiteralPath $hostCredentialFile -PathType Leaf)) {
        throw 'Cloudflare Tunnel credentials are missing. Restore the local Tunnel JSON under cloudflared-docker and try again.'
    }
}

function Invoke-Cp6Compose {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$ComposeArguments
    )

    $baseArguments = @(
        'compose',
        '--project-name', 'cp6',
        '--project-directory', $script:ProjectRootFull,
        '-f', $script:ComposeFile
    )
    $output = @(& docker @baseArguments @ComposeArguments 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "docker compose failed with exit code ${exitCode}: docker compose $($ComposeArguments -join ' ')`n$($output -join [Environment]::NewLine)"
    }

    return $output
}

function ConvertFrom-ComposePsOutput {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [object[]]$Output
    )

    $text = ($Output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ([string]::IsNullOrWhiteSpace($text)) {
        return @()
    }

    $trimmed = $text.Trim()
    if ($trimmed.StartsWith('[')) {
        return @($trimmed | ConvertFrom-Json)
    }

    $items = @()
    foreach ($line in ($trimmed -split '\r?\n')) {
        if (-not [string]::IsNullOrWhiteSpace($line)) {
            $items += ($line | ConvertFrom-Json)
        }
    }
    return $items
}

function Get-Cp6ServiceStatus {
    $psOutput = @(Invoke-Cp6Compose -ComposeArguments @('ps', '--all', '--format', 'json'))
    $items = @(ConvertFrom-ComposePsOutput -Output $psOutput)
    $result = @()

    foreach ($expectedService in $script:ExpectedServices) {
        $item = $items | Where-Object {
            ($_.PSObject.Properties.Name -contains 'Service' -and $_.Service -eq $expectedService) -or
            ($_.PSObject.Properties.Name -contains 'Name' -and $_.Name -eq $expectedService)
        } | Select-Object -First 1

        if ($null -eq $item) {
            $result += [pscustomobject]@{
                Service = $expectedService
                State = 'missing'
                Health = '-'
                Ready = $false
            }
            continue
        }

        $state = if ($item.PSObject.Properties.Name -contains 'State') { [string]$item.State } else { 'unknown' }
        $health = if ($item.PSObject.Properties.Name -contains 'Health' -and -not [string]::IsNullOrWhiteSpace([string]$item.Health)) {
            [string]$item.Health
        }
        else {
            '-'
        }
        $ready = $state -eq 'running' -and ($health -eq '-' -or $health -eq 'healthy')
        $result += [pscustomobject]@{
            Service = $expectedService
            State = $state
            Health = $health
            Ready = $ready
        }
    }

    return $result
}

function Test-HttpEndpoint {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Endpoint
    )

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $statusCode = $null
    $errorMessage = $null
    try {
        $response = Invoke-WebRequest -Uri $Endpoint.Url -Method Get -UseBasicParsing -TimeoutSec 15
        $statusCode = [int]$response.StatusCode
    }
    catch {
        if ($null -ne $_.Exception.Response -and $null -ne $_.Exception.Response.StatusCode) {
            $statusCode = [int]$_.Exception.Response.StatusCode
        }
        $errorMessage = $_.Exception.Message
    }
    finally {
        $stopwatch.Stop()
    }

    $ok = $null -ne $statusCode -and $statusCode -ge 200 -and $statusCode -lt 400
    [pscustomobject]@{
        Name = $Endpoint.Name
        Url = $Endpoint.Url
        Status = if ($null -ne $statusCode) { $statusCode } else { 'unreachable' }
        Milliseconds = $stopwatch.ElapsedMilliseconds
        Ready = $ok
        Detail = if ($ok) { 'OK' } elseif ($errorMessage) { $errorMessage } else { 'HTTP request failed' }
    }
}

function Wait-Cp6Services {
    param(
        [Parameter(Mandatory = $true)]
        [datetime]$Deadline
    )

    do {
        $services = @(Get-Cp6ServiceStatus)
        if (@($services | Where-Object { -not $_.Ready }).Count -eq 0) {
            return $services
        }
        Start-Sleep -Seconds 3
    } while ([datetime]::UtcNow -lt $Deadline)

    $notReady = $services | Where-Object { -not $_.Ready } | ForEach-Object { "$($_.Service)=$($_.State)/$($_.Health)" }
    throw "Timed out waiting for containers: $($notReady -join ', ')"
}

function Wait-HttpReady {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Endpoint,

        [Parameter(Mandatory = $true)]
        [datetime]$Deadline
    )

    do {
        $result = Test-HttpEndpoint -Endpoint $Endpoint
        if ($result.Ready) {
            return $result
        }
        Start-Sleep -Seconds 3
    } while ([datetime]::UtcNow -lt $Deadline)

    throw "Timed out waiting for endpoint $($Endpoint.Url). Last result: $($result.Detail)"
}

function Show-Cp6Status {
    param(
        [switch]$LocalOnly
    )

    $services = @(Get-Cp6ServiceStatus)
    Write-Host ''
    Write-Host 'CP6 container status'
    Write-Host (($services | Format-Table Service, State, Health, Ready -AutoSize | Out-String).TrimEnd())

    $endpoints = @()
    foreach ($endpoint in $script:LocalEndpoints) {
        $endpoints += Test-HttpEndpoint -Endpoint $endpoint
    }
    if (-not $LocalOnly) {
        foreach ($endpoint in $script:PublicEndpoints) {
            $endpoints += Test-HttpEndpoint -Endpoint $endpoint
        }
    }

    Write-Host 'CP6 endpoint checks'
    Write-Host (($endpoints | Format-Table Name, Status, Milliseconds, Ready, Url -AutoSize | Out-String).TrimEnd())

    $allReady = (@($services | Where-Object { -not $_.Ready }).Count -eq 0) -and
        (@($endpoints | Where-Object { -not $_.Ready }).Count -eq 0)
    return $allReady
}

try {
    Assert-DockerAvailable
    Assert-BaseConfiguration

    switch ($Action) {
        'Start' {
            Assert-StartConfiguration
            $composeArguments = @('up', '-d')
            if ($Build) {
                $composeArguments += '--build'
                Write-Host 'Building and starting the CP6 daytime test environment...'
            }
            else {
                Write-Host 'Starting the CP6 daytime test environment with existing images...'
            }
            Invoke-Cp6Compose -ComposeArguments $composeArguments | ForEach-Object { Write-Host $_ }

            $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
            $null = Wait-Cp6Services -Deadline $deadline
            foreach ($endpoint in $script:LocalEndpoints) {
                $null = Wait-HttpReady -Endpoint $endpoint -Deadline $deadline
            }
            if (-not $SkipPublicCheck) {
                foreach ($endpoint in $script:PublicEndpoints) {
                    $null = Wait-HttpReady -Endpoint $endpoint -Deadline $deadline
                }
            }

            Write-Host ''
            Write-Host 'CP6 daytime test environment is ready.'
            Write-Host 'Colleague URL: https://cp6.uk'
            Write-Host 'Local URL: http://127.0.0.1:8080'
            Write-Host 'This PC must stay powered on and awake. The site is expected to be unavailable while this PC sleeps or is turned off.'
        }
        'Status' {
            $ready = Show-Cp6Status -LocalOnly:$SkipPublicCheck
            if (-not $ready) {
                throw 'One or more CP6 containers or endpoints are not ready.'
            }
            Write-Host 'CP6 is healthy.'
        }
        'ClosePublic' {
            Invoke-Cp6Compose -ComposeArguments @('stop', 'cp6-cloudflared') | ForEach-Object { Write-Host $_ }
            Write-Host 'The Docker Tunnel for cp6.uk is stopped. API, Web, and infrastructure services remain available locally.'

            $hostTunnel = @(Get-Process -Name 'cloudflared' -ErrorAction SilentlyContinue)
            if ($hostTunnel.Count -gt 0) {
                Write-Warning 'A host-level cloudflared process is also running. This script will not terminate it because it may belong to another Tunnel. Review it manually if cp6.uk remains reachable.'
            }
        }
        'StopAll' {
            Invoke-Cp6Compose -ComposeArguments @('stop') | ForEach-Object { Write-Host $_ }
            Write-Host 'All CP6 containers are stopped safely. Containers and named-volume data for SQL Server, Redis, RabbitMQ, Kafka, and i18n are preserved.'
        }
    }

    exit 0
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
