[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ExpectedServer,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9_]{0,99}$')][string]$ExpectedDatabase,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ConnectionEnvironmentVariable = 'C04A_SOURCE_CONNECTION'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
. (Join-Path $PSScriptRoot 'LocalStorage.ps1')
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$foundationPath = Join-Path $root 'CP6.Core/Migrations/20260811030108_CrmFoundation.cs'
$foundation = [Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($foundationPath)).Replace("`r`n", "`n")
$foundationHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($foundation))).ToLowerInvariant()
if ($foundationHash -ne '76de7efa9baac910813ce1594157a74a635a4fb1c27c5ced11e2301d16259771') {
    throw 'Source Foundation differs from the CRM02 inspected baseline.'
}
$tableNames = @([regex]::Matches($foundation, 'CreateTable\(\s*name: "(Crm_[^"]+)"') |
    ForEach-Object { $_.Groups[1].Value } | Sort-Object -CaseSensitive)
if ($tableNames.Count -ne 20) { throw 'Expected exactly twenty Foundation tables.' }

# The connection is never rendered in output, error messages or the receipt.
try {
    $builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new(
        [Environment]::GetEnvironmentVariable($ConnectionEnvironmentVariable))
} catch { throw 'Missing or invalid source connection environment variable.' }
if ($builder.DataSource -cne $ExpectedServer -or $builder.InitialCatalog -cne $ExpectedDatabase -or
    $ExpectedDatabase -in @('master','model','msdb','tempdb') -or
    $ExpectedDatabase.StartsWith('CP6_C04A_Rehearsal_', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Source identity does not match the explicit original database input.'
}
$localServer = $ExpectedServer -split '\\', 2 | Select-Object -First 1
if ($localServer -notin @('.', '(local)', 'localhost', '127.0.0.1', $env:COMPUTERNAME)) {
    throw 'This helper only supports a local SQL Server source.'
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Output directory must not already exist.' }
$output = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)).FullName
$rehearsalId = [Guid]::NewGuid().ToString('N')
$restoreName = 'CP6_C04A_Rehearsal_' + $rehearsalId
$receipt = [ordered]@{
    schemaVersion = 1; startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); status = 'Started'
    rehearsalId = $rehearsalId; sourceDatabase = $ExpectedDatabase; restoredDatabase = $restoreName
    foundationSha256 = $foundationHash; backupCopyOnly = $false; backupChecksum = $false
    verifyOnlyPassed = $false; sourceBusinessWrites = $false; sourceSchemaWrites = $false
    sourceFenceActivated = $false; completeC04AAcceptance = $false
    scriptInputs = @('New-SourceRestore.ps1','LocalStorage.ps1','source-metadata.sql') | ForEach-Object {
        @{ path = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $PSScriptRoot $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
function Save-Receipt {
    [IO.File]::WriteAllText((Join-Path $output 'restore-receipt.json'), ($receipt | ConvertTo-Json -Depth 12) + "`n", $utf8)
}
function Read-Rows([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql, [hashtable]$Values = @{}) {
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 180
    $command.CommandText = $Sql
    foreach ($key in $Values.Keys) { [void]$command.Parameters.AddWithValue($key, $Values[$key]) }
    try {
        $reader = $command.ExecuteReader()
        try {
            while ($reader.Read()) {
                $row = [ordered]@{}
                for ($i = 0; $i -lt $reader.FieldCount; $i++) {
                    $row[$reader.GetName($i)] = $(if ($reader.IsDBNull($i)) { $null } else { $reader.GetValue($i) })
                }
                [pscustomobject]$row
            }
        } finally { $reader.Dispose() }
    } finally { $command.Dispose() }
}
function Invoke-Sql([System.Data.SqlClient.SqlConnection]$Connection, [string]$Sql, [hashtable]$Values = @{}) {
    $command = $Connection.CreateCommand()
    $command.CommandTimeout = 300
    $command.CommandText = $Sql
    foreach ($key in $Values.Keys) { [void]$command.Parameters.AddWithValue($key, $Values[$key]) }
    try { [void]$command.ExecuteNonQuery() } finally { $command.Dispose() }
}
function Observe-Source([System.Data.SqlClient.SqlConnection]$Connection, [string]$Prefix) {
    $actual = @(Read-Rows $Connection "SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID(N'dbo') AND name LIKE N'Crm[_]%' ORDER BY name;")
    if (($actual.name -join '|') -cne ($tableNames -join '|')) { throw 'Source table inventory differs from Foundation.' }
    $counts = @()
    foreach ($name in $tableNames) {
        $row = @(Read-Rows $Connection "SET LOCK_TIMEOUT 5000; SELECT COUNT_BIG(*) AS [Count] FROM [dbo].[$name];")[0]
        $counts += [ordered]@{ table = $name; rowsIncludingDeleted = $row.Count }
        if ($row.Count -ne 0) { throw 'Current approved empty-source profile rejects nonempty tables.' }
    }
    $metadataSql = [IO.File]::ReadAllText((Join-Path $PSScriptRoot 'source-metadata.sql'))
    $metadataParts = @(Read-Rows $Connection $metadataSql)
    $metadata = [Text.StringBuilder]::new()
    foreach ($part in $metadataParts) { [void]$metadata.Append(@($part.PSObject.Properties)[0].Value) }
    $metadataText = $metadata.ToString()
    $parsed = @($metadataText | ConvertFrom-Json)
    if ($parsed.Count -ne 20 -or @($parsed | ForEach-Object columns).Count -ne 305) {
        throw 'Incomplete source metadata result.'
    }
    $latestMigration = @(Read-Rows $Connection 'SELECT TOP (1) MigrationId FROM dbo.__EFMigrationsHistory ORDER BY MigrationId DESC;')[0].MigrationId
    if ($latestMigration -cne '20260811030108_CrmFoundation') { throw 'Source migration history differs from inspected CRM02 source.' }
    [IO.File]::WriteAllText((Join-Path $output "$Prefix-metadata.json"), $metadataText + "`n", $utf8)
    return [ordered]@{
        counts = $counts; latestMigration = $latestMigration
        metadataSha256 = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($utf8.GetBytes($metadataText))).ToLowerInvariant()
    }
}

$source = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
$admin = $null
$restored = $null
$phase = 'SourceObservation'
Save-Receipt
try {
    $source.Open()
    $identity = @(Read-Rows $source "SELECT DB_NAME() AS DatabaseName, CONVERT(nvarchar(128),SERVERPROPERTY('MachineName')) AS MachineName;")[0]
    if ($identity.DatabaseName -cne $ExpectedDatabase -or $identity.MachineName -ine $env:COMPUTERNAME) {
        throw 'Connected SQL identity is not the expected local source.'
    }
    $receipt.sourceBefore = Observe-Source $source 'source-before'
    $builder['Initial Catalog'] = 'master'
    $admin = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $admin.Open()
    $paths = @(Read-Rows $admin "SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultBackupPath')) AS BackupPath, CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')) AS DataPath, CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath')) AS LogPath;")[0]
    foreach ($value in @($paths.BackupPath, $paths.DataPath, $paths.LogPath)) {
        Assert-LocalStorageDirectory $value
    }
    $backupPath = Join-Path $paths.BackupPath ($restoreName + '.bak')
    if (Test-Path -LiteralPath $backupPath) { throw 'Unique backup path already exists.' }
    if (@(Read-Rows $admin 'SELECT name FROM sys.databases WHERE name=@name;' @{ '@name' = $restoreName }).Count -ne 0) {
        throw 'Unique restore database already exists; replacement is forbidden.'
    }
    [IO.File]::WriteAllText((Join-Path $output 'private-resource-locator.json'), (@{
        backupPath = $backupPath; restoredDatabase = $restoreName; restoredFiles = @()
    } | ConvertTo-Json) + "`n", $utf8)
    $phase = 'CopyOnlyBackup'
    Invoke-Sql $admin "BACKUP DATABASE [$ExpectedDatabase] TO DISK=@path WITH COPY_ONLY, CHECKSUM;" @{ '@path' = $backupPath }
    $header = @(Read-Rows $admin 'RESTORE HEADERONLY FROM DISK=@path;' @{ '@path' = $backupPath })
    if ($header.Count -ne 1 -or $header[0].DatabaseName -cne $ExpectedDatabase -or $header[0].BackupType -ne 1 -or
        !$header[0].IsCopyOnly -or !$header[0].HasBackupChecksums) { throw 'Backup identity or COPY_ONLY/CHECKSUM verification failed.' }
    $receipt.backupCopyOnly = $true
    $receipt.backupChecksum = $true
    $receipt.backupSetGuid = $header[0].BackupSetGUID.ToString()
    $receipt.backupSha256 = (Get-FileHash -LiteralPath $backupPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Invoke-Sql $admin 'RESTORE VERIFYONLY FROM DISK=@path WITH CHECKSUM;' @{ '@path' = $backupPath }
    $receipt.verifyOnlyPassed = $true
    Save-Receipt
    $phase = 'Restore'
    $files = @(Read-Rows $admin 'RESTORE FILELISTONLY FROM DISK=@path;' @{ '@path' = $backupPath })
    if ($files.Count -lt 2 -or @($files | Where-Object { $_.Type -notin @('D','L') }).Count -ne 0) {
        throw 'Only conventional data/log files are supported.'
    }
    $moves = @()
    $parameters = @{ '@path' = $backupPath }
    $privateFiles = @()
    for ($i = 0; $i -lt $files.Count; $i++) {
        $directory = $(if ($files[$i].Type -eq 'L') { $paths.LogPath } else { $paths.DataPath })
        $destination = Join-Path $directory ($restoreName + '_' + $i + $(if ($files[$i].Type -eq 'L') { '.ldf' } else { '.mdf' }))
        if (Test-Path -LiteralPath $destination) { throw 'Unique restore file path already exists.' }
        $parameters["@logical$i"] = $files[$i].LogicalName
        $parameters["@file$i"] = $destination
        $moves += "MOVE @logical$i TO @file$i"
        $privateFiles += $destination
    }
    [IO.File]::WriteAllText((Join-Path $output 'private-resource-locator.json'), (@{
        backupPath = $backupPath; restoredFiles = $privateFiles; restoredDatabase = $restoreName
    } | ConvertTo-Json) + "`n", $utf8)
    if (@(Read-Rows $admin 'SELECT name FROM sys.databases WHERE name=@name;' @{ '@name' = $restoreName }).Count -ne 0) {
        throw 'Restore database appeared during backup; replacement is forbidden.'
    }
    Invoke-Sql $admin ("RESTORE DATABASE [$restoreName] FROM DISK=@path WITH CHECKSUM, RECOVERY, " + ($moves -join ', ') + ';') $parameters
    $builder['Initial Catalog'] = $restoreName
    $restored = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    $restored.Open()
    $phase = 'RestoredObservation'
    $receipt.restored = Observe-Source $restored 'restored'
    if ($receipt.restored.metadataSha256 -cne $receipt.sourceBefore.metadataSha256) {
        throw 'Restored CRM metadata differs from the source observation.'
    }
    $receipt.restoredServiceBrokerGuid = @(Read-Rows $restored 'SELECT service_broker_guid AS Id FROM sys.databases WHERE name=DB_NAME();')[0].Id.ToString()
    $receipt.sourceAfter = Observe-Source $source 'source-after'
    if ($receipt.sourceAfter.metadataSha256 -cne $receipt.sourceBefore.metadataSha256) { throw 'Source CRM metadata changed during backup/restore.' }
    $receipt.status = 'RestoredEmptySourceVerified'
    $receipt.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Save-Receipt
    Write-Output "Restored verified empty source to $restoreName. Receipt: $(Join-Path $output 'restore-receipt.json')"
} catch {
    $receipt.status = 'Failed'
    $receipt.failedPhase = $phase
    # Do not serialize provider messages: they can contain paths, SQL or business values.
    $receipt.failureType = $_.Exception.GetType().Name
    $receipt.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Save-Receipt
    throw "Source restoration failed in $phase; sanitized receipt retained. Existing resources were not deleted."
} finally {
    if ($null -ne $restored) { $restored.Dispose() }
    if ($null -ne $admin) { $admin.Dispose() }
    $source.Dispose()
}
