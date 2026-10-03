param([string]$OutputPath = 'D:\CP6\tmp\wp3-cleanup-final-owned-two.json')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$receiptPath = 'D:\CP6\tmp\db-compat.wp3-owned.json'
$task = 'DB-COMPAT-01-WP3'
$output = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetDirectoryName($output) -cne 'D:\CP6\tmp' -or
    [IO.Path]::GetFileName($output) -cnotmatch '^wp3-cleanup-[a-z0-9-]+\.json$' -or
    (Test-Path -LiteralPath $output)) { throw 'A new exact WP3 cleanup report path is required.' }

$report = [ordered]@{
    Task = $task; Scope = 'Only the two exact WP3 receipt databases; all-target read-only preflight, per-target identity/activity recheck, ordinary non-FORCE DROP.'
    StartedHostUtc = [DateTime]::UtcNow.ToString('o'); FinishedHostUtc = $null
    Status = 'Preparing'; Phase = 'ReceiptValidation'; FailureCode = $null
    ReceiptSha256 = $null; ReceiptBytesUnchanged = $null
    AllTwoPreflightPassed = $false; Dropped = 0; AllTwoAbsent = $false
    PostgreSqlRoleRetained = $false; Targets = @()
}
$failureCode = 'UnexpectedFailure'
$failed = $false
function Require-Wp3([bool]$Condition, [string]$Code) {
    if (!$Condition) { $script:failureCode = $Code; throw ('WP3_CLEANUP_REFUSED_' + $Code) }
}
function Save-Wp3Progress { $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $output -Encoding utf8NoBOM }
function Read-Wp3NativeJson([object[]]$Lines) {
    try { return (($Lines | ForEach-Object { [string]$_ }) -join "`n").Trim() | ConvertFrom-Json }
    catch { $script:failureCode = 'NativeJsonInvalid'; throw 'Native JSON could not be parsed.' }
}
function Invoke-Wp3Sql([string]$Query) {
    # -h -1 is compatible with this nonzero -y value; never combine -W with -y.
    $lines = & $sqlcmd -S $sqlServer -E -C -d master -l 10 -t 30 -b -h -1 -y 4000 -w 65535 -f 65001 -Q $Query 2>&1
    Require-Wp3 ($LASTEXITCODE -eq 0) 'SqlNativeCommandFailed'
    return Read-Wp3NativeJson @($lines)
}
function Invoke-Wp3Pg([string]$Query, [switch]$NonJson) {
    $saved = @{}
    foreach ($name in @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGOPTIONS','PGAPPNAME','PGCLIENTENCODING')) {
        $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    try {
        foreach ($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGOPTIONS')) {
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process')
        }
        [Environment]::SetEnvironmentVariable('PGPASSWORD', $pg.Password, 'Process')
        [Environment]::SetEnvironmentVariable('PGCONNECT_TIMEOUT', '5', 'Process')
        [Environment]::SetEnvironmentVariable('PGAPPNAME', 'CP6Compat.WP3.Cleanup', 'Process')
        [Environment]::SetEnvironmentVariable('PGCLIENTENCODING', 'UTF8', 'Process')
        $lines = & $psql -X -w -h $pg.Host -p ([string]$pg.Port) -U $pg.Username -d postgres -v ON_ERROR_STOP=1 -At -c $Query 2>&1
        Require-Wp3 ($LASTEXITCODE -eq 0) 'PostgreSqlNativeCommandFailed'
        if (!$NonJson) { return Read-Wp3NativeJson @($lines) }
    }
    finally {
        foreach ($name in $saved.Keys) {
            if ($null -eq $saved[$name]) { [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process') }
            else { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
        }
    }
}
function Read-Wp3SqlState {
    return Invoke-Wp3Sql @"
SET NOCOUNT ON;
SELECT DB_ID(N'$databaseName') AS DatabaseId,
       IS_SRVROLEMEMBER(N'sysadmin') AS FullActivityVisible,
       (SELECT CONVERT(nvarchar(200),value) FROM [$databaseName].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner') AS Owner,
       (SELECT CONVERT(nvarchar(200),value) FROM [$databaseName].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask') AS Task,
       (SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'$databaseName')) AS ActiveSessions,
       (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'$databaseName')) AS ActiveRequests
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
"@
}
function Read-Wp3PgState {
    return Invoke-Wp3Pg @"
SELECT json_build_object('DatabaseId',d.oid::text,'Owner',shobj_description(d.oid,'pg_database'),
  'DatabaseRole',pg_get_userbyid(d.datdba),'CurrentRole',current_user,
  'ActiveSessions',(SELECT count(*) FROM pg_stat_activity WHERE datname='$databaseName'))
FROM pg_database d WHERE d.datname='$databaseName';
"@
}
function Confirm-Wp3SqlState([object]$State, [object]$Expected = $null) {
    Require-Wp3 ($State.DatabaseId -gt 0 -and $State.FullActivityVisible -eq 1) 'SqlExistenceOrActivityVisibility'
    Require-Wp3 ($State.Owner -ceq $owner -and $State.Task -ceq $task) 'SqlOwnerMarkerMismatch'
    Require-Wp3 ($State.ActiveSessions -eq 0 -and $State.ActiveRequests -eq 0) 'SqlActiveSessionOrRequest'
    if ($null -ne $Expected) { Require-Wp3 ($State.DatabaseId -eq $Expected.DatabaseId) 'SqlPhysicalIdentityChanged' }
}
function Confirm-Wp3PgState([object]$State, [object]$Expected = $null) {
    Require-Wp3 ($null -ne $State -and [string]$State.DatabaseId -cmatch '^[0-9]+$') 'PostgreSqlDatabaseMissing'
    Require-Wp3 ($State.Owner -ceq ($task + ':' + $owner) -and
        $State.DatabaseRole -ceq $pg.Username -and $State.CurrentRole -ceq $pg.Username) 'PostgreSqlOwnerMarkerMismatch'
    Require-Wp3 ($State.ActiveSessions -eq 0) 'PostgreSqlActiveSession'
    if ($null -ne $Expected) { Require-Wp3 ($State.DatabaseId -ceq $Expected.DatabaseId) 'PostgreSqlPhysicalIdentityChanged' }
}

try {
    Require-Wp3 (Test-Path -LiteralPath $receiptPath -PathType Leaf) 'ReceiptUnavailable'
    $report.ReceiptSha256 = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
    $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
    Require-Wp3 ($receipt.Task -ceq $task -and $receipt.Owner -cmatch '^[a-f0-9]{32}$' -and
        $receipt.SqlServerState -ceq 'CreatedAndOwnerMarked' -and
        $receipt.PostgreSqlState -ceq 'CreatedAndOwnerMarked') 'ReceiptTaskOwnerOrState'
    $owner = $receipt.Owner
    $databaseName = 'CP6Compat_WP3_20261003_' + $owner.Substring(0,8)
    Require-Wp3 ($receipt.SqlServerDatabase -ceq $databaseName -and $receipt.PostgreSqlDatabase -ceq $databaseName) 'ReceiptExactTargetNames'

    $sql = [System.Data.Common.DbConnectionStringBuilder]::new()
    $sql.set_ConnectionString($receipt.SqlServerConnection)
    $allowedSql = @('Server','Database','Integrated Security','TrustServerCertificate','MultipleActiveResultSets','Connect Timeout')
    Require-Wp3 (@($sql.Keys | Where-Object { $_ -notin $allowedSql }).Count -eq 0 -and
        $sql['Server'] -ceq 'localhost\KOUSQLSERVER' -and $sql['Database'] -ceq $databaseName -and
        [string]$sql['Integrated Security'] -ieq 'True') 'SqlExactLoopbackIntegratedConnection'
    $sqlServer = [string]$sql['Server']
    $sqlCommand = Get-Command sqlcmd -ErrorAction Stop
    Require-Wp3 ($sqlCommand.CommandType -eq 'Application' -and [IO.Path]::GetFileName($sqlCommand.Source) -ieq 'sqlcmd.exe') 'NativeSqlcmdRequired'
    $sqlcmd = $sqlCommand.Source

    Add-Type -Path 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
    $pg = [Npgsql.NpgsqlConnectionStringBuilder]::new($receipt.PostgreSqlConnection)
    Require-Wp3 ($pg.Host -cin @('localhost','127.0.0.1','::1') -and $pg.Port -eq 5432 -and
        $pg.Database -ceq $databaseName -and $pg.Username -cmatch '^cp6compat_wp1_[a-f0-9]{8}$' -and
        ![string]::IsNullOrEmpty($pg.Password)) 'PostgreSqlLoopbackRoleOrTarget'
    $psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
    Require-Wp3 (Test-Path -LiteralPath $psql -PathType Leaf) 'NativePsqlRequired'
    $sqlTarget = [ordered]@{Provider='SqlServer';Database=$databaseName;PreflightPassed=$false;DatabaseId=$null;ActiveSessions=$null;DropConfirmed=$false;Absent=$false}
    $pgTarget = [ordered]@{Provider='PostgreSql';Database=$databaseName;PreflightPassed=$false;DatabaseId=$null;ActiveSessions=$null;DropConfirmed=$false;Absent=$false}
    $report.Targets = @($sqlTarget,$pgTarget)

    $report.Phase = 'AllTargetReadOnlyPreflight'
    $sqlState = Read-Wp3SqlState
    Confirm-Wp3SqlState $sqlState
    $sqlTarget.PreflightPassed = $true; $sqlTarget.DatabaseId = $sqlState.DatabaseId; $sqlTarget.ActiveSessions = 0
    Save-Wp3Progress
    $pgState = Read-Wp3PgState
    Confirm-Wp3PgState $pgState
    $pgTarget.PreflightPassed = $true; $pgTarget.DatabaseId = $pgState.DatabaseId; $pgTarget.ActiveSessions = 0
    $report.AllTwoPreflightPassed = $true
    Save-Wp3Progress

    $report.Phase = 'DropExactSqlServer'
    Require-Wp3 ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -ceq $report.ReceiptSha256) 'ReceiptBytesChanged'
    Confirm-Wp3SqlState (Read-Wp3SqlState) $sqlState
    # Recheck ownership, physical database identity and visible activity in the same SQL batch.
    $sqlAbsent = Invoke-Wp3Sql @"
SET NOCOUNT ON;
IF DB_ID(N'$databaseName') <> $($sqlState.DatabaseId) OR IS_SRVROLEMEMBER(N'sysadmin') <> 1
 OR NOT EXISTS(SELECT 1 FROM [$databaseName].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'$owner')
 OR NOT EXISTS(SELECT 1 FROM [$databaseName].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'$task')
 OR EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'$databaseName'))
 OR EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'$databaseName'))
 THROW 51060, 'WP3_CLEANUP_SQL_RECHECK_REFUSED', 1;
DROP DATABASE [$databaseName];
SELECT COUNT(*) AS Present FROM sys.databases WHERE name=N'$databaseName' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
"@
    $sqlTarget.DropConfirmed = $true; $report.Dropped++
    Require-Wp3 ($sqlAbsent.Present -eq 0) 'SqlAbsenceNotConfirmed'
    $sqlTarget.Absent = $true
    Save-Wp3Progress

    $report.Phase = 'DropExactPostgreSql'
    Require-Wp3 ((Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -ceq $report.ReceiptSha256) 'ReceiptBytesChanged'
    Confirm-Wp3PgState (Read-Wp3PgState) $pgState
    # DROP is one separate autocommit command. No FORCE or session termination.
    Invoke-Wp3Pg ('DROP DATABASE "' + $databaseName + '";') -NonJson
    $pgTarget.DropConfirmed = $true; $report.Dropped++
    $pgAfter = Invoke-Wp3Pg ("SELECT json_build_object('Present',(SELECT count(*) FROM pg_database WHERE datname='$databaseName'),'RoleExists',(SELECT count(*) FROM pg_roles WHERE rolname='" + $pg.Username + "'));" )
    Require-Wp3 ($pgAfter.Present -eq 0 -and $pgAfter.RoleExists -eq 1) 'PostgreSqlAbsenceOrRetainedRoleNotConfirmed'
    $pgTarget.Absent = $true; $report.PostgreSqlRoleRetained = $true
    $report.AllTwoAbsent = $true; $report.Status = 'Completed'; $report.Phase = 'Completed'
}
catch {
    $failed = $true; $report.Status = 'Failed'; $report.FailureCode = $failureCode
    # The report deliberately excludes native stderr, exception messages, connection strings and credentials.
}
finally {
    if ($null -ne $report.ReceiptSha256) {
        $report.ReceiptBytesUnchanged = (Test-Path -LiteralPath $receiptPath -PathType Leaf) -and
            (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash -ceq $report.ReceiptSha256
        if (!$report.ReceiptBytesUnchanged) { $failed = $true; $report.Status = 'Failed'; $report.FailureCode = 'ReceiptBytesChanged' }
    }
    $report.FinishedHostUtc = [DateTime]::UtcNow.ToString('o')
    Save-Wp3Progress
}
if ($failed) { throw ('WP3 cleanup failed during ' + $report.Phase + ': ' + $report.FailureCode + '. See sanitized report.') }
Write-Output ('Completed exact two-database WP3 cleanup; original receipt and PostgreSQL test role retained. Report: ' + $output)
