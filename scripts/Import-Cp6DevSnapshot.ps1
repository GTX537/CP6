[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [Parameter(Mandatory = $true)]
    [string]$SnapshotPath,

    [string]$TargetDatabase = ('CP6DEV_IMPORT_{0}' -f (Get-Date -Format 'yyyyMMdd_HHmmss')),

    [ValidateSet('cp6-db')]
    [string]$Container = 'cp6-db',

    [string]$LocalSettingsPath = (Join-Path `
        (Split-Path -Parent $PSScriptRoot) `
        'CP6.WebApi\appsettings.Local.json')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($TargetDatabase -notmatch '^CP6DEV_IMPORT_\d{8}_\d{6}$') {
    throw 'TargetDatabase must use CP6DEV_IMPORT_yyyyMMdd_HHmmss.'
}
if ($TargetDatabase -eq 'CP6DB' -or $TargetDatabase -eq 'CP6_DEV') {
    throw 'Snapshots may only be restored side-by-side; CP6DB and CP6_DEV are forbidden targets.'
}

$resolvedSnapshotPath = [IO.Path]::GetFullPath($SnapshotPath)
if (-not (Test-Path -LiteralPath $resolvedSnapshotPath -PathType Leaf)) {
    throw "Snapshot was not found at '$resolvedSnapshotPath'."
}
if ([IO.Path]::GetExtension($resolvedSnapshotPath) -ne '.bak') {
    throw 'SnapshotPath must point to a .bak file.'
}
if (-not (Test-Path -LiteralPath $LocalSettingsPath -PathType Leaf)) {
    throw "Local settings were not found at '$LocalSettingsPath'."
}

$settings = Get-Content -LiteralPath $LocalSettingsPath -Raw | ConvertFrom-Json
$connectionString = [string]$settings.ConnectionStrings.DefaultConnection
$builder = [Data.SqlClient.SqlConnectionStringBuilder]::new($connectionString)
$password = [string]$builder.Password
if ([string]::IsNullOrWhiteSpace($password)) {
    throw 'The local SQL credential could not be read from appsettings.Local.json.'
}

$containerJson = @(& docker inspect $Container 2>$null) -join [Environment]::NewLine
$containerInspectExitCode = $LASTEXITCODE
if ($containerInspectExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($containerJson)) {
    throw "Container '$Container' was not found."
}
$containerInfo = $containerJson | ConvertFrom-Json
$containerRecord = @($containerInfo) | Select-Object -First 1
if (-not $containerRecord.State.Running) {
    throw "Container '$Container' is not running."
}
$dataMount = @($containerRecord.Mounts | Where-Object {
    $_.Destination -eq '/var/opt/mssql' -and $_.Name -eq 'cp6_cp6-db-data'
})
if ($dataMount.Count -ne 1) {
    throw "Container '$Container' is not the protected root CP6 database container."
}

$sqlcmd = '/opt/mssql-tools18/bin/sqlcmd'
$containerBackupPath = "/var/opt/mssql/backup/cp6-dev-import-$([Guid]::NewGuid().ToString('N')).bak"
$previousSqlCmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
$cleanupRequired = $false

function Invoke-ContainerSql {
    param(
        [Parameter(Mandatory = $true)][string]$Query,
        [switch]$Raw
    )

    $arguments = @(
        'exec',
        '--env', 'SQLCMDPASSWORD',
        $Container,
        $sqlcmd,
        '-S', 'localhost',
        '-U', 'sa',
        '-C',
        '-b',
        '-V', '16'
    )
    if ($Raw) {
        $arguments += @('-h', '-1', '-W', '-s', '|')
    }
    $arguments += @('-Q', $Query)
    $output = @(& docker @arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "SQL operation failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }
    return $output
}

try {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $password, 'Process')
    $exists = Invoke-ContainerSql `
        -Query "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name = N'$TargetDatabase';" `
        -Raw
    $databaseCount = @($exists | Where-Object { $_ -match '^\s*\d+\s*$' }) |
        Select-Object -First 1
    if ($null -eq $databaseCount) {
        throw "Unable to determine whether target database '$TargetDatabase' exists."
    }
    if ($databaseCount.Trim() -ne '0') {
        throw "Target database '$TargetDatabase' already exists; overwrite is forbidden."
    }

    if (-not $PSCmdlet.ShouldProcess(
        $TargetDatabase,
        "RESTORE VERIFYONLY and side-by-side restore from $resolvedSnapshotPath")) {
        return
    }

    & docker exec $Container mkdir -p /var/opt/mssql/backup | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the container backup directory.' }
    $cleanupRequired = $true
    & docker cp $resolvedSnapshotPath "${Container}:$containerBackupPath"
    if ($LASTEXITCODE -ne 0) { throw 'Unable to copy the snapshot into the SQL container.' }

    Invoke-ContainerSql `
        -Query "RESTORE VERIFYONLY FROM DISK = N'$containerBackupPath' WITH CHECKSUM;" |
        Out-Null
    $fileList = Invoke-ContainerSql `
        -Query "RESTORE FILELISTONLY FROM DISK = N'$containerBackupPath';" `
        -Raw
    $rows = @($fileList | Where-Object { $_ -match '\|' } | ForEach-Object {
        $fields = $_ -split '\|'
        if ($fields.Count -ge 3 -and $fields[2].Trim() -match '^[DL]$') {
            [pscustomobject]@{
                LogicalName = $fields[0].Trim()
                Type = $fields[2].Trim()
            }
        }
    })
    $dataFiles = @($rows | Where-Object Type -eq 'D')
    $logFiles = @($rows | Where-Object Type -eq 'L')
    if ($dataFiles.Count -ne 1 -or $logFiles.Count -ne 1) {
        throw 'The verified snapshot must contain exactly one data file and one log file.'
    }

    $dataLogical = $dataFiles[0].LogicalName.Replace("'", "''")
    $logLogical = $logFiles[0].LogicalName.Replace("'", "''")
    $restoreQuery = @"
RESTORE DATABASE [$TargetDatabase]
FROM DISK = N'$containerBackupPath'
WITH CHECKSUM,
     MOVE N'$dataLogical' TO N'/var/opt/mssql/data/$TargetDatabase.mdf',
     MOVE N'$logLogical' TO N'/var/opt/mssql/data/${TargetDatabase}_log.ldf',
     RECOVERY,
     STATS = 10;
"@
    Invoke-ContainerSql -Query $restoreQuery | Out-Null
    Write-Host "Restored CP6_DEV snapshot side-by-side as '$TargetDatabase'. CP6DB was not overwritten or merged."
}
finally {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $previousSqlCmdPassword, 'Process')
    if ($cleanupRequired) {
        & docker exec $Container rm -f $containerBackupPath 2>$null | Out-Null
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Could not remove temporary container snapshot '$containerBackupPath'."
        }
    }
    # Cleanup is best effort and must not leave a successful restore with a false native exit code.
    $global:LASTEXITCODE = 0
}
