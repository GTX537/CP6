[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$modulePath = Join-Path $PSScriptRoot 'Cp6.Sqlcmd.psm1'
Import-Module $modulePath -Force

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMessage
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch [regex]::Escape($ExpectedMessage)) {
            throw "Expected '$ExpectedMessage', received '$($_.Exception.Message)'."
        }
        return
    }

    throw "Expected '$ExpectedMessage', but the action succeeded."
}

$originalPath = $env:PATH
$originalBackupPassword = [Environment]::GetEnvironmentVariable('CP6_DEV_DB_BACKUP_PASSWORD', 'Process')
$originalSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "cp6-sqlcmd-resolution-$([Guid]::NewGuid().ToString('N'))"
$resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
$resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $resolvedTempRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unsafe test directory '$resolvedTempRoot'."
}

try {
    [IO.Directory]::CreateDirectory($resolvedTempRoot) | Out-Null
    $pathCandidate = Join-Path $resolvedTempRoot 'sqlcmd.exe'
    [IO.File]::WriteAllBytes($pathCandidate, [byte[]]@(0))

    $env:PATH = $resolvedTempRoot
    $resolved = Resolve-Cp6SqlcmdPath -StandardPaths @()
    if (-not $resolved.Equals($pathCandidate, [StringComparison]::OrdinalIgnoreCase)) {
        throw "PATH candidate mismatch. Expected '$pathCandidate', received '$resolved'."
    }

    Assert-Throws `
        -Action { Resolve-Cp6SqlcmdPath -SqlcmdPath '.\sqlcmd.exe' } `
        -ExpectedMessage 'SqlcmdPath must be an absolute executable path when provided.'

    $missingAbsolute = Join-Path $resolvedTempRoot 'missing\sqlcmd.exe'
    Assert-Throws `
        -Action { Resolve-Cp6SqlcmdPath -SqlcmdPath $missingAbsolute } `
        -ExpectedMessage 'sqlcmd was not found on PATH or in a supported standard installation directory.'

    [IO.File]::Delete($pathCandidate)
    Assert-Throws `
        -Action { Resolve-Cp6SqlcmdPath -StandardPaths @() } `
        -ExpectedMessage 'sqlcmd was not found on PATH or in a supported standard installation directory.'

    $fallbackDirectory = Join-Path $resolvedTempRoot 'odbc17'
    [IO.Directory]::CreateDirectory($fallbackDirectory) | Out-Null
    $fallbackCandidate = Join-Path $fallbackDirectory 'SQLCMD.EXE'
    [IO.File]::WriteAllBytes($fallbackCandidate, [byte[]]@(0))
    $resolvedFallback = Resolve-Cp6SqlcmdPath -StandardPaths @($fallbackCandidate)
    if (-not $resolvedFallback.Equals($fallbackCandidate, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Standard fallback mismatch. Expected '$fallbackCandidate', received '$resolvedFallback'."
    }

    [Environment]::SetEnvironmentVariable('CP6_DEV_DB_BACKUP_PASSWORD', $null, 'Process')
    Assert-Throws `
        -Action {
            & (Join-Path $PSScriptRoot 'Backup-Cp6DevDatabase.ps1') `
                -SqlcmdPath $fallbackCandidate `
                -BackupRoot (Join-Path $resolvedTempRoot 'backup')
        } `
        -ExpectedMessage "Required backup Secret 'CP6_DEV_DB_BACKUP_PASSWORD' is missing."

    $temporaryBackupPassword = [Guid]::NewGuid().ToString('N')
    $sentinelSqlcmdPassword = [Guid]::NewGuid().ToString('N')
    [Environment]::SetEnvironmentVariable('CP6_DEV_DB_BACKUP_PASSWORD', $temporaryBackupPassword, 'Process')
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $sentinelSqlcmdPassword, 'Process')
    $invalidExecutableFailed = $false
    try {
        & (Join-Path $PSScriptRoot 'Backup-Cp6DevDatabase.ps1') `
            -SqlcmdPath $fallbackCandidate `
            -BackupRoot (Join-Path $resolvedTempRoot 'backup')
    }
    catch {
        $invalidExecutableFailed = $true
    }
    if (-not $invalidExecutableFailed) {
        throw 'Expected the invalid sqlcmd test executable to fail.'
    }
    $restoredSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    if ($restoredSqlcmdPassword -ne $sentinelSqlcmdPassword) {
        throw 'Backup failure did not restore the previous process SQLCMDPASSWORD value.'
    }

    Write-Host 'CP6 sqlcmd resolution behavior test passed (7 scenarios).'
}
finally {
    $env:PATH = $originalPath
    [Environment]::SetEnvironmentVariable('CP6_DEV_DB_BACKUP_PASSWORD', $originalBackupPassword, 'Process')
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $originalSqlcmdPassword, 'Process')
    Remove-Module Cp6.Sqlcmd -Force -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $resolvedTempRoot) {
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}
