[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][string]$PrivateDiagnosticDirectory,
    [ValidateSet('TransportProbe')][string]$Mode = 'TransportProbe'
)
$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repositoryRoot 'eng/crm/identity-events-fixture'
$binary = Join-Path $fixture 'bin/Debug/net8.0/CP6.IdentityEvents.Fixture.dll'
if (-not (Test-Path -LiteralPath $binary)) { throw 'Build only the identity-events fixture locally before running this probe.' }
$publicRoot = [IO.Path]::GetFullPath($OutputDirectory)
$privateRoot = [IO.Path]::GetFullPath($PrivateDiagnosticDirectory)
if (Test-Path -LiteralPath (Join-Path $publicRoot 'transport-probe.json')) { throw 'Use a fresh attempt directory; prior evidence must be preserved.' }
[IO.Directory]::CreateDirectory($publicRoot) | Out-Null
[IO.Directory]::CreateDirectory($privateRoot) | Out-Null
$compose = Join-Path $fixture 'transport/compose.yaml'
$project = 'cp6-c02-probe-' + [guid]::NewGuid().ToString('N').Substring(0, 12)
$environmentNames = @('CP6_C02_PROBE_APP_PORT', 'CP6_C02_PROBE_HTTP_PORT', 'CP6_C02_PROBE_GRPC_PORT', 'CP6_C02_PROBE_HTTP', 'CP6_C02_PROBE_GRPC', 'CP6_C02_PROBE_SOURCE', 'APP_API_TOKEN')
$previous = @{}
foreach ($name in $environmentNames) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$owned = $false
$cleanupFailed = $false
try {
    # Reserve distinct loopback ports while selecting all three. No existing listener is stopped.
    $reservations = @()
    try {
        foreach ($name in $environmentNames[0..2]) {
            $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
            $listener.Start()
            $reservations += $listener
            [Environment]::SetEnvironmentVariable($name, [string]$listener.LocalEndpoint.Port, 'Process')
        }
    } finally { foreach ($listener in $reservations) { $listener.Stop() } }
    $env:CP6_C02_PROBE_HTTP = 'http://127.0.0.1:' + $env:CP6_C02_PROBE_HTTP_PORT
    $env:CP6_C02_PROBE_GRPC = 'http://127.0.0.1:' + $env:CP6_C02_PROBE_GRPC_PORT
    $env:CP6_C02_PROBE_SOURCE = (& git -C $repositoryRoot rev-parse HEAD).Trim() + '+working-tree-transport-probe'
    $env:APP_API_TOKEN = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
    $images = @()
    foreach ($image in @('apache/kafka:4.3.1', 'daprio/daprd:1.18.2')) {
        $identity = & docker image inspect $image --format '{{.Id}}'
        if ($LASTEXITCODE -ne 0) { throw "Required cached image missing: $image. No implicit download or image build is performed." }
        $images += @{ image = $image; imageId = $identity }
    }
    @{ project = $project; images = $images; kind = 'isolated local byte probe, not production acceptance' } |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $publicRoot 'transport-environment.json') -Encoding utf8NoBOM
    $owned = $true
    & docker compose -p $project -f $compose up -d --pull never --no-build 2>&1 |
        Tee-Object -FilePath (Join-Path $privateRoot 'compose-up.log')
    if ($LASTEXITCODE -ne 0) { throw 'Owned transport startup failed.' }
    & dotnet $binary $publicRoot $privateRoot 'transport-probe' 2>&1 |
        Tee-Object -FilePath (Join-Path $privateRoot 'probe-console.log')
    $probeExit = $LASTEXITCODE
    if ($probeExit -ne 0) { throw "Transport byte probe failed with exit $probeExit; original evidence preserved." }
} finally {
    if ($owned) {
        & docker compose -p $project -f $compose logs --no-color 2>&1 |
            Set-Content -LiteralPath (Join-Path $privateRoot 'transport.log') -Encoding utf8NoBOM
        & docker compose -p $project -f $compose down --timeout 10 2>&1 |
            Tee-Object -FilePath (Join-Path $privateRoot 'cleanup.log')
        if ($LASTEXITCODE -ne 0) { $cleanupFailed = $true; Write-Error 'Owned transport cleanup failed.' -ErrorAction Continue }
    }
    foreach ($name in $environmentNames) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
if ($cleanupFailed) { throw 'The probe cannot pass while owned transport cleanup is unverified.' }
