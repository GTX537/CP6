[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$PostgreSqlBin,
    [Parameter(Mandatory)][string]$PostgreSqlRoot,
    [Parameter(Mandatory)][string]$ArtifactsRoot,
    [Parameter(Mandatory)][ValidateRange(1024, 65535)][int]$Port,
    [string]$DotNetPath = 'dotnet',
    [string]$Filter = 'FullyQualifiedName~Stock_outbox|FullyQualifiedName~Actual_Program_rejects_old_access_after_password_change'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
foreach ($path in @($PostgreSqlBin, $PostgreSqlRoot, $ArtifactsRoot)) {
    if (-not [IO.Path]::IsPathFullyQualified($path)) { throw 'Use absolute local paths.' }
}
$pgRoot = [IO.Path]::GetFullPath($PostgreSqlRoot)
$artifacts = [IO.Path]::GetFullPath($ArtifactsRoot)
foreach ($output in @($pgRoot, $artifacts)) {
    if ($output.Equals($repository, [StringComparison]::OrdinalIgnoreCase) -or
        $output.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Use task-private data and artifacts outside the source worktree.'
    }
}
# The existing fixture owns cluster creation, port checks, role isolation and cleanup.
if ((Test-Path -LiteralPath $pgRoot) -or -not [IO.Path]::GetFileName($pgRoot).StartsWith('permission-consumption-', [StringComparison]::Ordinal)) {
    throw 'PostgreSqlRoot must be a new permission-consumption-* directory.'
}
[IO.Directory]::CreateDirectory($artifacts) | Out-Null
$settings = @{
    CP6_PERMISSION_PG_BIN = $PostgreSqlBin; CP6_PERMISSION_PG_ROOT = $pgRoot; CP6_PERMISSION_PG_PORT = [string]$Port
    DOTNET_PROCESSOR_COUNT = '2'; DOTNET_GCHeapHardLimit = '0x80000000'; DOTNET_gcServer = '0'; MSBUILDDISABLENODEREUSE = '1'
}
$previous = @{}
foreach ($name in $settings.Keys) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    [Environment]::SetEnvironmentVariable($name, $settings[$name], 'Process')
}
function Invoke-DotNet([string]$Label, [string[]]$Arguments) {
    & $DotNetPath @Arguments *> (Join-Path $artifacts ($Label + '.log'))
    if ($LASTEXITCODE -ne 0) {
        Get-Content (Join-Path $artifacts ($Label + '.log')) -Tail 30 | Out-Host
        throw "$Label failed with exit code $LASTEXITCODE."
    }
}
Push-Location $repository
try {
    $common = @('--artifacts-path', (Join-Path $artifacts 'build'), '-p:Cp6PostgreSqlDevelopment=true')
    $build = @('--disable-build-servers', '-m:1', '/p:UseSharedCompilation=false', '/p:BuildInParallel=false')
    foreach ($project in @('CP6.Tests/CP6.Tests.csproj', 'tools/CP6.DatabaseCompatibility.Probe/CP6.DatabaseCompatibility.Probe.csproj')) {
        $label = [IO.Path]::GetFileNameWithoutExtension($project)
        Invoke-DotNet "$label-restore" (@('restore', $project, '--locked-mode', '--configfile', 'eng/p10/NuGet.formal.config', '--disable-parallel') + $common)
        Invoke-DotNet "$label-build" (@('build', $project, '--no-restore') + $common + $build)
    }
    Invoke-DotNet 'postgresql-tests' (@('test', 'CP6.Tests/CP6.Tests.csproj', '--no-build', '--no-restore', '--filter', $Filter,
        '--logger', 'trx;LogFileName=postgresql-profile.trx', '--results-directory', (Join-Path $artifacts 'results')) + $common)
    Write-Output "PostgreSQL development checks passed. Logs and TRX: $artifacts"
}
finally {
    Pop-Location
    foreach ($name in $previous.Keys) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
}
