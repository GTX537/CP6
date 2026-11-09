[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $ArtifactRoot,
    [Parameter(Mandatory)][string] $OutputRoot,
    [Parameter(Mandatory)][string] $DotnetHome,
    [Parameter(Mandatory)][string] $PostgreSqlBin,
    [Parameter(Mandatory)][ValidateSet(10, 100)][int] $TenantCount,
    [Parameter(Mandatory)][ValidateRange(1024, 65535)][int] $Port,
    [ValidateSet(1, 2)][int] $Processes = 1,
    [string] $SdkVersion = '8.0.424',
    [string] $RefVersion = '8.0.30'
)

$ErrorActionPreference = 'Stop'
$artifact = (Resolve-Path -LiteralPath $ArtifactRoot).Path
$output = (Resolve-Path -LiteralPath $OutputRoot).Path
$dotnet = Join-Path (Resolve-Path -LiteralPath $DotnetHome).Path 'dotnet.exe'
$pgBin = (Resolve-Path -LiteralPath $PostgreSqlBin).Path
$tests = Join-Path $artifact 'build/bin/CP6.Tests/release'
$cli = Join-Path $artifact 'build/bin/CP6.TenantStorage/release/CP6.TenantStorage.dll'
$cache = Join-Path $artifact 'n'
$csc = Join-Path $DotnetHome "sdk/$SdkVersion/Roslyn/bincore/csc.dll"
$references = @(
    Join-Path $DotnetHome "packs/Microsoft.NETCore.App.Ref/$RefVersion/ref/net8.0"
    Join-Path $DotnetHome "packs/Microsoft.AspNetCore.App.Ref/$RefVersion/ref/net8.0"
)
foreach ($file in @($dotnet, $csc, $cli, (Join-Path $tests 'CP6.Tests.dll'), (Join-Path $pgBin 'pg_ctl.exe'))) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing pinned input $file" }
}
if (@([System.Net.NetworkInformation.IPGlobalProperties]::GetIPGlobalProperties().GetActiveTcpListeners() |
        Where-Object Port -eq $Port).Count) { throw "Dedicated port $Port is already listening" }
Get-ChildItem Env: | Where-Object Name -like 'PG*' | ForEach-Object { Remove-Item -LiteralPath "Env:$($_.Name)" }

$run = Join-Path $output ("n$TenantCount-p$Processes-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
[IO.Directory]::CreateDirectory($run) | Out-Null
$dll = Join-Path $run 'StockCapacity.dll'
$flags = @('/nologo', '/target:exe', '/langversion:12', '/nullable:enable', '/deterministic', '/debug:portable', ('/out:"' + $dll + '"'))
foreach ($reference in (Get-ChildItem -LiteralPath $references -File -Filter '*.dll')) {
    $flags += '/reference:"' + $reference.FullName + '"'
}
foreach ($reference in (Get-ChildItem -LiteralPath $tests -File | Where-Object {
    $_.Name -like 'CP6*.dll' -or $_.Name -like 'Microsoft.EntityFrameworkCore*.dll' -or $_.Name -like 'Npgsql.dll'
})) {
    $flags += '/reference:"' + $reference.FullName + '"'
}
$flags += '"' + (Join-Path $PSScriptRoot 'StockCapacity.cs') + '"'
$rsp = Join-Path $run 'csc.rsp'
Set-Content -LiteralPath $rsp -Value $flags -Encoding utf8

$env:DOTNET_ROOT = $DotnetHome
$env:DOTNET_CLI_HOME = Join-Path $run 'dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:CP6_CAPACITY_RUN = $run
$env:CP6_CAPACITY_BINARIES = $tests
$env:CP6_CAPACITY_PACKAGES = $cache
$env:CP6_CAPACITY_DOTNET = $dotnet
$env:CP6_CAPACITY_CLI = $cli
$env:CP6_PERMISSION_PG_ROOT = Join-Path $run 'permission-consumption-stock'
$env:CP6_PERMISSION_PG_BIN = $pgBin
$env:CP6_PERMISSION_PG_PORT = [string]$Port

Write-Output $run
& $dotnet $csc "@$rsp" 2>&1 | Tee-Object -FilePath (Join-Path $run 'compile.log')
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$runtime = Join-Path $tests 'CP6.Tests.runtimeconfig.json'
$deps = Join-Path $tests 'CP6.Tests.deps.json'
& $dotnet exec --runtimeconfig $runtime --depsfile $deps --additionalprobingpath $cache $dll $TenantCount $Processes 2>&1 |
    Tee-Object -FilePath (Join-Path $run 'run.log')
exit $LASTEXITCODE
