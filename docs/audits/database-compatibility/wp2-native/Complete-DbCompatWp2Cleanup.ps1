param(
    [Parameter(Mandatory)]
    [ValidatePattern('^wp2-cleanup-[a-z0-9][a-z0-9-]{0,63}$')]
    [string]$Label
)
# The caller runs this only after required WP2 gates and their evidence are complete.
# No connection strings, passwords, row values, or native diagnostic output are logged.
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskRoot = 'D:\CP6\tmp'
$taskName = 'DB-COMPAT-01-WP2'
$sqlInstance = 'localhost\KOUSQLSERVER'
$pgRole = 'cp6compat_wp1_0b54dc81'
$psqlPath = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
$npgsqlPath = 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
if ($Label -cnotmatch '^wp2-cleanup-[a-z0-9][a-z0-9-]{0,63}$') { throw 'A lowercase wp2-cleanup label is required.' }
$resultPath = Join-Path $taskRoot "$Label.json"
if (Test-Path -LiteralPath $resultPath) { throw 'Existing cleanup result must not be overwritten; choose a new wp2-cleanup label.' }
# Reserve this exact result path before any credential read or native operation.
$reservation = [IO.File]::Open($resultPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
$reservation.Dispose()
$receiptNames = @(
    'db-compat.wp2-initial-owned.json',
    'db-compat.wp2-pre-review-owned.json',
    'db-compat.wp2-before-raw-final-owned.json',
    'db-compat.wp2-before-sql-final-upgrade-owned.json',
    'db-compat.wp2-owned.json'
)
$ownerA = '3ac63d7a823843d492620af048349491'
$ownerB = '844f5957ba5047a0a3cf4c53a981e64f'
$ownerC = '36ef9cae14704ac88eb994416f628e24'
$expected = @(
    @{ Provider = 'SqlServer'; Database = 'CP6Compat_WP2_20261002_3ac63d7a'; Owner = $ownerA },
    @{ Provider = 'SqlServer'; Database = 'CP6Compat_WP2_20261002_844f5957'; Owner = $ownerB },
    @{ Provider = 'SqlServer'; Database = 'CP6Compat_WP2_20261002_36ef9cae'; Owner = $ownerC },
    @{ Provider = 'SqlServer'; Database = 'CP6Compat_WP2_20261002_63d019b6'; Owner = $ownerC },
    @{ Provider = 'PostgreSql'; Database = 'CP6Compat_WP2_20261002_3ac63d7a'; Owner = $ownerA },
    @{ Provider = 'PostgreSql'; Database = 'CP6Compat_WP2_20261002_844f5957'; Owner = $ownerB },
    @{ Provider = 'PostgreSql'; Database = 'CP6Compat_WP2_20261002_36ef9cae'; Owner = $ownerC },
    @{ Provider = 'PostgreSql'; Database = 'CP6Compat_WP2_20261002_31d68591'; Owner = $ownerC }
)
$expectedMap = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
foreach ($item in $expected) { $expectedMap.Add($item.Provider + '|' + $item.Database, $item) }
# Credentials live only in this private map; the public report never serializes it.
$privateTargets = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$progressTargets = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    Scope = 'Exact eight explicitly receipt-listed WP2 task databases only; all-target preflight followed by per-target recheck, native non-FORCE DROP and absence proof.'
    Task = $taskName; Label = $Label; StartedUtc = [DateTime]::UtcNow.ToString('O'); UpdatedUtc = $null; FinishedUtc = $null
    ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    Status = 'Preparing'; Phase = 'ReceiptValidation'; CurrentTarget = $null
    ExpectedTargetCount = 8; AllEightPreflightPassed = $false; DroppedAndAbsenceVerified = 0; AllEightAbsentVerified = $false; AnyDropOutcomeUnknown = $false
    ReceiptHashes = @(); ReceiptsModified = $false; ReceiptBytesUnchanged = $null
    Targets = @(); PostgreSqlRole = @{ Name = $pgRole; Retained = $null }
    Failure = $null
    Limits = 'Caller-owned final-gate evidence is a prerequisite. No prefix discovery, force option, session termination, role/authentication/service changes, business-row cleanup, or root-workspace cleanup.'
}
$currentPhase = 'ReceiptValidation'
$failureKind = 'UnclassifiedSetupFailure'
function Save-Progress {
    $report.UpdatedUtc = [DateTime]::UtcNow.ToString('O')
    $report.Phase = $currentPhase
    $report.Targets = @($progressTargets)
    [IO.File]::WriteAllText($resultPath, ($report | ConvertTo-Json -Depth 18), [Text.UTF8Encoding]::new($false))
}
function Require-Cleanup([bool]$Condition, [string]$Code) {
    if (-not $Condition) { $script:failureKind = $Code; throw 'Cleanup prerequisite failed; original native output and credentials are suppressed.' }
}
function Check-ReceiptBytes {
    foreach ($entry in $report.ReceiptHashes) {
        Require-Cleanup ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ceq $entry.Sha256) 'ReceiptBytesChanged'
    }
}
function Parse-NativeJson([object[]]$Lines) {
    try { return (($Lines | ForEach-Object { [string]$_ }) -join '').Trim() | ConvertFrom-Json -Depth 12 }
    catch { $script:failureKind = 'NativeJsonUnavailable'; throw 'Native operation did not return the expected sanitized JSON.' }
}
function Invoke-SqlJson([string]$Query) {
    $nativeLines = & $sqlcmdPath -S $sqlInstance -E -C -d master -l 15 -t 30 -b -h -1 -y 4000 -w 65535 -f 65001 -Q $Query 2>&1
    $nativeExit = $LASTEXITCODE
    Require-Cleanup ($nativeExit -eq 0) 'SqlNativeOperationRejected'
    return Parse-NativeJson @($nativeLines)
}
function Invoke-PgJson([object]$Target, [string]$Database, [string[]]$Queries) {
    $savedPgEnvironment = @{}
    foreach ($name in @('PGPASSWORD', 'PGCONNECT_TIMEOUT', 'PGHOSTADDR', 'PGSERVICE', 'PGSERVICEFILE', 'PGOPTIONS', 'PGAPPNAME', 'PGCLIENTENCODING')) {
        $savedPgEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
    }
    try {
        [Environment]::SetEnvironmentVariable('PGPASSWORD', $Target.PgBuilder.Password, 'Process')
        [Environment]::SetEnvironmentVariable('PGCONNECT_TIMEOUT', '5', 'Process')
        foreach ($name in @('PGHOSTADDR', 'PGSERVICE', 'PGSERVICEFILE')) { [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process') }
        [Environment]::SetEnvironmentVariable('PGOPTIONS', '-c statement_timeout=30000 -c lock_timeout=5000 -c search_path=pg_catalog', 'Process')
        [Environment]::SetEnvironmentVariable('PGAPPNAME', 'CP6Compat.WP2.Cleanup', 'Process')
        [Environment]::SetEnvironmentVariable('PGCLIENTENCODING', 'UTF8', 'Process')
        $nativeArgs = @('-X', '-w', '-q', '-h', $Target.PgBuilder.Host, '-p', '5432', '-U', $pgRole, '-d', $Database, '-v', 'ON_ERROR_STOP=1', '-At')
        # Each separate -c is one autocommit command. DROP DATABASE is never wrapped
        # in a transaction or combined with the guard in one multi-statement -c.
        foreach ($query in $Queries) { $nativeArgs += @('-c', $query) }
        $nativeLines = & $psqlPath @nativeArgs 2>&1
        $nativeExit = $LASTEXITCODE
        Require-Cleanup ($nativeExit -eq 0) 'PostgreSqlNativeOperationRejected'
        return Parse-NativeJson @($nativeLines)
    }
    finally {
        foreach ($name in $savedPgEnvironment.Keys) { if ($null -eq $savedPgEnvironment[$name]) { [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process') } else { [Environment]::SetEnvironmentVariable($name, $savedPgEnvironment[$name], 'Process') } }
    }
}
function Sql-State([object]$Target) {
    $db = $Target.Database
    $owner = $Target.Owner
    # SQL16 requires global activity visibility; a filtered zero is not accepted.
    # https://learn.microsoft.com/en-us/sql/relational-databases/system-dynamic-management-objects/sys-dm-exec-sessions-transact-sql
    $query = @'
SET NOCOUNT ON;
SELECT CONVERT(varchar(128),SERVERPROPERTY('ProductVersion')) AS Version,
 CONVERT(int,SERVERPROPERTY('ProductMajorVersion')) AS MajorVersion,
 DB_ID(N'%DB%') AS DatabaseId,
 CAST(CASE WHEN DB_ID(N'%DB%') IS NOT NULL THEN 1 ELSE 0 END AS bit) AS [Exists],
 CAST(CASE WHEN IS_SRVROLEMEMBER(N'sysadmin')=1 OR HAS_PERMS_BY_NAME(NULL,NULL,N'VIEW SERVER PERFORMANCE STATE')=1 THEN 1 ELSE 0 END AS bit) AS GlobalActivityVisible,
 (SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'%DB%')) AS ActiveSessions,
 (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'%DB%')) AS ActiveRequests
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
'@
    $state = Invoke-SqlJson ($query.Replace('%DB%', $db))
    Require-Cleanup ($state.Exists -eq $true -and $state.DatabaseId -gt 0) 'SqlDatabaseMissing'
    Require-Cleanup ($state.MajorVersion -eq 16 -and $state.GlobalActivityVisible -eq $true) 'SqlServerVersionOrActivityVisibilityUnconfirmed'
    $metadataQuery = @'
SET NOCOUNT ON;
SELECT CAST(CASE WHEN EXISTS(SELECT 1 FROM [%DB%].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'%OWNER%')
 AND EXISTS(SELECT 1 FROM [%DB%].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2') THEN 1 ELSE 0 END AS bit) AS OwnerAndTaskVerified,
 (SELECT COUNT_BIG(*) FROM [%DB%].sys.tables WHERE is_ms_shipped=0) AS TableCount
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
'@
    $metadata = Invoke-SqlJson ($metadataQuery.Replace('%DB%', $db).Replace('%OWNER%', $owner))
    Require-Cleanup ($metadata.OwnerAndTaskVerified -eq $true -and $null -ne $metadata.TableCount -and $metadata.TableCount -ge 0) 'SqlOwnerTaskOrTableInventoryUnconfirmed'
    Require-Cleanup ($state.ActiveSessions -eq 0 -and $state.ActiveRequests -eq 0) 'SqlActiveConnectionOrRequestPresent'
    return [ordered]@{
        DatabaseId = $state.DatabaseId; Version = $state.Version; OwnerAndTaskVerified = $true
        TableCount = $metadata.TableCount; ActiveSessions = $state.ActiveSessions; ActiveRequests = $state.ActiveRequests; GlobalActivityVisible = $true
    }
}
function Pg-State([object]$Target) {
    $query = @'
SELECT json_build_object(
 'Exists',EXISTS(SELECT 1 FROM pg_database WHERE datname='%DB%'),
 'DatabaseOid',(SELECT oid::bigint FROM pg_database WHERE datname='%DB%'),
 'Version',current_setting('server_version'),
 'VersionNumber',current_setting('server_version_num')::integer,
 'CurrentRole',current_user,
 'OwnerAndTaskVerified',COALESCE((SELECT shobj_description(d.oid,'pg_database')='DB-COMPAT-01-WP2:%OWNER%'
     AND r.rolname='%ROLE%' FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba WHERE d.datname='%DB%'),false),
 'ActiveConnections',(SELECT count(*) FROM pg_stat_activity WHERE datname='%DB%')
);
'@
    $state = Invoke-PgJson $Target 'postgres' @($query.Replace('%DB%', $Target.Database).Replace('%OWNER%', $Target.Owner).Replace('%ROLE%', $pgRole))
    Require-Cleanup ($state.Exists -eq $true -and $state.DatabaseOid -gt 0) 'PostgreSqlDatabaseMissing'
    Require-Cleanup ($state.VersionNumber -ge 180000 -and $state.VersionNumber -lt 190000 -and $state.CurrentRole -ceq $pgRole) 'PostgreSqlVersionOrLoginRoleMismatch'
    Require-Cleanup ($state.OwnerAndTaskVerified -eq $true) 'PostgreSqlNativeOwnerOrTaskCommentMismatch'
    Require-Cleanup ($state.ActiveConnections -eq 0) 'PostgreSqlActiveConnectionPresent'
    $tableQuery = @'
SELECT json_build_object(
 'Database',current_database(),'CurrentRole',current_user,
 'OwnerAndTaskVerified',COALESCE((SELECT shobj_description(d.oid,'pg_database')='DB-COMPAT-01-WP2:%OWNER%'
     AND r.rolname='%ROLE%' FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba WHERE d.datname=current_database()),false),
 'TableCount',(SELECT count(*) FROM pg_tables WHERE schemaname NOT LIKE 'pg_%' AND schemaname<>'information_schema')
);
'@
    $inventory = Invoke-PgJson $Target $Target.Database @($tableQuery.Replace('%OWNER%', $Target.Owner).Replace('%ROLE%', $pgRole))
    Require-Cleanup ($inventory.Database -ceq $Target.Database -and $inventory.CurrentRole -ceq $pgRole -and
        $inventory.OwnerAndTaskVerified -eq $true -and $null -ne $inventory.TableCount -and $inventory.TableCount -ge 0) 'PostgreSqlTableInventoryUnconfirmed'
    return [ordered]@{
        DatabaseOid = $state.DatabaseOid; Version = $state.Version; CurrentRole = $state.CurrentRole; OwnerAndTaskVerified = $true
        TableCount = $inventory.TableCount; ActiveConnections = $state.ActiveConnections
    }
}
function Drop-SqlExact([object]$Target, [object]$ExpectedState) {
    $query = @'
SET NOCOUNT ON;
IF CONVERT(int,SERVERPROPERTY('ProductMajorVersion'))<>16
 OR (COALESCE(IS_SRVROLEMEMBER(N'sysadmin'),0)<>1 AND COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,N'VIEW SERVER PERFORMANCE STATE'),0)<>1)
 THROW 51030,'SQL version or global activity visibility not confirmed.',1;
IF DB_ID(N'%DB%') IS NULL OR DB_ID(N'%DB%')<>%ID%
 THROW 51030,'Exact SQL database identity changed.',1;
IF NOT EXISTS(SELECT 1 FROM [%DB%].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'%OWNER%')
 OR NOT EXISTS(SELECT 1 FROM [%DB%].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51030,'Exact SQL ownership metadata changed.',1;
IF EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'%DB%'))
 OR EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'%DB%'))
 THROW 51030,'SQL database has active sessions or requests.',1;
DROP DATABASE [%DB%];
SELECT CAST(CASE WHEN DB_ID(N'%DB%') IS NULL THEN 1 ELSE 0 END AS bit) AS Absent,
 CONVERT(varchar(128),SERVERPROPERTY('ProductVersion')) AS Version
FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
'@
    return Invoke-SqlJson ($query.Replace('%DB%', $Target.Database).Replace('%OWNER%', $Target.Owner).Replace('%ID%', [string]$ExpectedState.DatabaseId))
}
function Drop-PgExact([object]$Target, [object]$ExpectedState) {
    # General session/database properties are visible to ordinary users:
    # https://www.postgresql.org/docs/18/monitoring-stats.html
    $guard = @'
DO $cp6cleanup$
BEGIN
 IF current_user<>'%ROLE%' OR current_setting('server_version_num')::integer NOT BETWEEN 180000 AND 189999
  THEN RAISE EXCEPTION 'Exact PostgreSQL login/version required'; END IF;
 IF NOT EXISTS(SELECT 1 FROM pg_database d JOIN pg_roles r ON r.oid=d.datdba
   WHERE d.datname='%DB%' AND d.oid::bigint=%OID% AND r.rolname='%ROLE%'
     AND shobj_description(d.oid,'pg_database')='DB-COMPAT-01-WP2:%OWNER%')
  THEN RAISE EXCEPTION 'Exact PostgreSQL database owner/task identity changed'; END IF;
 IF EXISTS(SELECT 1 FROM pg_stat_activity WHERE datname='%DB%')
  THEN RAISE EXCEPTION 'PostgreSQL database has active connections'; END IF;
END
$cp6cleanup$;
'@
    $guard = $guard.Replace('%ROLE%', $pgRole).Replace('%DB%', $Target.Database).Replace('%OID%', [string]$ExpectedState.DatabaseOid).Replace('%OWNER%', $Target.Owner)
    $drop = 'DROP DATABASE "' + $Target.Database + '";'
    $absence = "SELECT json_build_object('Absent',NOT EXISTS(SELECT 1 FROM pg_database WHERE datname='$($Target.Database)'),'Version',current_setting('server_version'));"
    return Invoke-PgJson $Target 'postgres' @($guard, $drop, $absence)
}
Save-Progress
try {
    Require-Cleanup ((Test-Path -LiteralPath $npgsqlPath -PathType Leaf) -and (Test-Path -LiteralPath $psqlPath -PathType Leaf)) 'RequiredPinnedLocalToolsUnavailable'
    $sqlcmdCommand = Get-Command sqlcmd -ErrorAction Stop
    Require-Cleanup ($sqlcmdCommand.CommandType -eq 'Application' -and
        [IO.Path]::GetFileName($sqlcmdCommand.Path) -ieq 'sqlcmd.exe' -and
        (Test-Path -LiteralPath $sqlcmdCommand.Path -PathType Leaf)) 'NativeSqlcmdExecutableUnconfirmed'
    $sqlcmdPath = $sqlcmdCommand.Path
    Add-Type -Path $npgsqlPath
    foreach ($receiptName in $receiptNames) {
        $receiptPath = Join-Path $taskRoot $receiptName
        Require-Cleanup (Test-Path -LiteralPath $receiptPath -PathType Leaf) 'ExplicitReceiptUnavailable'
        $receiptHash = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
        try { $receiptDoc = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json -Depth 10 }
        catch { $failureKind = 'ExplicitReceiptInvalid'; throw 'An explicit receipt could not be parsed.' }
        Require-Cleanup ($receiptDoc.Task -ceq $taskName -and $receiptDoc.Owner -cmatch '^[a-f0-9]{32}$') 'ReceiptTaskOrOwnerMismatch'
        $report.ReceiptHashes += [ordered]@{ Path = $receiptPath; Sha256 = $receiptHash }
        foreach ($provider in @('SqlServer', 'PostgreSql')) {
            $db = if ($provider -ceq 'SqlServer') { [string]$receiptDoc.SqlServerDatabase } else { [string]$receiptDoc.PostgreSqlDatabase }
            $key = $provider + '|' + $db
            Require-Cleanup ($expectedMap.ContainsKey($key) -and $receiptDoc.Owner -ceq $expectedMap[$key].Owner) 'ReceiptContainsAnUnapprovedTargetOrConflictingOwner'
            $pgBuilder = $null
            if ($provider -ceq 'SqlServer') {
                try {
                    $sqlBuilder = [System.Data.Common.DbConnectionStringBuilder]::new()
                    $sqlBuilder.set_ConnectionString($receiptDoc.SqlServerConnection)
                    $validSql = $sqlBuilder.ContainsKey('Server') -and $sqlBuilder.ContainsKey('Database') -and $sqlBuilder.ContainsKey('Integrated Security') -and
                        $sqlBuilder['Server'] -ceq $sqlInstance -and $sqlBuilder['Database'] -ceq $db -and
                        [string]$sqlBuilder['Integrated Security'] -ieq 'True'
                    foreach ($credentialKey in @('Password', 'Pwd', 'User ID', 'UserID', 'UID', 'AttachDbFilename', 'Data Source', 'Initial Catalog')) {
                        if ($sqlBuilder.ContainsKey($credentialKey)) { $validSql = $false }
                    }
                    $allowedSqlKeys = @('Server', 'Database', 'Integrated Security', 'TrustServerCertificate', 'MultipleActiveResultSets', 'Connect Timeout')
                    foreach ($sqlKey in $sqlBuilder.Keys) { if ($allowedSqlKeys -notcontains [string]$sqlKey) { $validSql = $false } }
                }
                catch { $failureKind = 'SqlReceiptConnectionInvalid'; throw 'SQL receipt connection could not be validated.' }
                Require-Cleanup $validSql 'SqlReceiptRequiresExactLoopbackWindowsIntegratedConnection'
            }
            else {
                try { $pgBuilder = [Npgsql.NpgsqlConnectionStringBuilder]::new($receiptDoc.PostgreSqlConnection) }
                catch { $failureKind = 'PostgreSqlReceiptConnectionInvalid'; throw 'PostgreSQL receipt connection could not be validated.' }
                Require-Cleanup ($pgBuilder.Host -cin @('localhost', '127.0.0.1', '::1') -and $pgBuilder.Port -eq 5432 -and
                    $pgBuilder.Database -ceq $db -and $pgBuilder.Username -ceq $pgRole -and -not [string]::IsNullOrEmpty($pgBuilder.Password)) 'PostgreSqlReceiptRequiresExactLoopbackRolePortDatabase'
            }
            if ($privateTargets.ContainsKey($key)) {
                $priorTarget = $privateTargets[$key]
                Require-Cleanup ($priorTarget.Owner -ceq $receiptDoc.Owner) 'DuplicateTargetReceiptOwnerConflict'
                if ($provider -ceq 'PostgreSql') {
                    Require-Cleanup ($priorTarget.PgBuilder.Username -ceq $pgBuilder.Username -and $priorTarget.PgBuilder.Password -ceq $pgBuilder.Password) 'DuplicatePostgreSqlReceiptLoginConflict'
                }
                $priorTarget.Progress.ReceiptSources += $receiptName
            }
            else {
                $progress = [ordered]@{
                    Provider = $provider; Database = $db; ExpectedOwnerMarker = $receiptDoc.Owner; ReceiptSources = @($receiptName)
                    Status = 'ValidatedReceipt'; Preflight = $null; PreflightUtc = $null; BeforeDropRecheck = $null; BeforeDropUtc = $null
                    DropAttempted = $false; DropOutcome = 'NotAttempted'
                    Dropped = $false; AbsenceVerified = $false; DropCompletedUtc = $null; FinalAbsent = $null
                }
                $progressTargets.Add($progress)
                $privateTargets.Add($key, [pscustomobject]@{ Provider = $provider; Database = $db; Owner = $receiptDoc.Owner; PgBuilder = $pgBuilder; Progress = $progress })
            }
        }
        Save-Progress
    }
    Require-Cleanup ($privateTargets.Count -eq 8 -and @($expectedMap.Keys | Where-Object { -not $privateTargets.ContainsKey($_) }).Count -eq 0) 'ExactlyEightExplicitTargetsRequired'
    Check-ReceiptBytes
    $report.Status = 'Preflight'
    $currentPhase = 'AllEightNativePreflight'
    foreach ($item in $expected) {
        $target = $privateTargets[$item.Provider + '|' + $item.Database]
        $report.CurrentTarget = $target.Provider + '|' + $target.Database
        $target.Progress.Status = 'PreflightChecking'
        Save-Progress
        $state = if ($target.Provider -ceq 'SqlServer') { Sql-State $target } else { Pg-State $target }
        $target.Progress.Preflight = $state
        $target.Progress.PreflightUtc = [DateTime]::UtcNow.ToString('O')
        $target.Progress.Status = 'PreflightPassed'
        Save-Progress
    }
    $report.AllEightPreflightPassed = $true
    Check-ReceiptBytes
    $report.Status = 'Dropping'
    $currentPhase = 'PerTargetRecheckAndDrop'
    Save-Progress
    foreach ($item in $expected) {
        $target = $privateTargets[$item.Provider + '|' + $item.Database]
        $report.CurrentTarget = $target.Provider + '|' + $target.Database
        $target.Progress.Status = 'BeforeDropRechecking'
        Save-Progress
        Check-ReceiptBytes
        $state = if ($target.Provider -ceq 'SqlServer') { Sql-State $target } else { Pg-State $target }
        if ($target.Provider -ceq 'SqlServer') {
            Require-Cleanup ($state.DatabaseId -eq $target.Progress.Preflight.DatabaseId) 'SqlIdentityChangedAfterAllTargetPreflight'
        }
        else { Require-Cleanup ($state.DatabaseOid -eq $target.Progress.Preflight.DatabaseOid) 'PostgreSqlIdentityChangedAfterAllTargetPreflight' }
        $target.Progress.BeforeDropRecheck = $state
        $target.Progress.BeforeDropUtc = [DateTime]::UtcNow.ToString('O')
        $target.Progress.Status = 'NativeDropStarted'
        $target.Progress.DropAttempted = $true
        $target.Progress.DropOutcome = 'UnknownUntilNativeAbsenceVerified'
        Save-Progress
        $absence = if ($target.Provider -ceq 'SqlServer') { Drop-SqlExact $target $state } else { Drop-PgExact $target $state }
        Require-Cleanup ($absence.Absent -eq $true) 'NativeDropAbsenceUnconfirmed'
        $target.Progress.Dropped = $true
        $target.Progress.AbsenceVerified = $true
        $target.Progress.DropOutcome = 'NativeAbsenceVerified'
        $target.Progress.DropCompletedUtc = [DateTime]::UtcNow.ToString('O')
        $target.Progress.Status = 'DroppedAndAbsenceVerified'
        $report.DroppedAndAbsenceVerified++
        Save-Progress
    }
    $currentPhase = 'FinalEightAbsenceAndRoleVerification'
    Save-Progress
    foreach ($item in $expected) {
        $target = $privateTargets[$item.Provider + '|' + $item.Database]
        $report.CurrentTarget = $target.Provider + '|' + $target.Database
        if ($target.Provider -ceq 'SqlServer') {
            $absence = Invoke-SqlJson ("SET NOCOUNT ON; SELECT CAST(CASE WHEN DB_ID(N'" + $target.Database + "') IS NULL THEN 1 ELSE 0 END AS bit) AS Absent FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;")
        }
        else {
            $absence = Invoke-PgJson $target 'postgres' @("SELECT json_build_object('Absent',NOT EXISTS(SELECT 1 FROM pg_database WHERE datname='$($target.Database)'),'RoleRetained',EXISTS(SELECT 1 FROM pg_roles WHERE rolname='$pgRole'));")
            Require-Cleanup ($absence.RoleRetained -eq $true) 'DedicatedPostgreSqlRoleRetentionUnconfirmed'
            $report.PostgreSqlRole.Retained = $true
        }
        Require-Cleanup ($absence.Absent -eq $true) 'FinalDatabaseAbsenceUnconfirmed'
        $target.Progress.FinalAbsent = $true
        Save-Progress
    }
    Check-ReceiptBytes
    $report.ReceiptBytesUnchanged = $true
    Require-Cleanup ($report.DroppedAndAbsenceVerified -eq 8 -and @($progressTargets | Where-Object { $_.FinalAbsent -ne $true }).Count -eq 0 -and $report.PostgreSqlRole.Retained -eq $true) 'FinalExactEightCleanupIncomplete'
    $report.AllEightAbsentVerified = $true
    $report.Status = 'Completed'
    $report.CurrentTarget = $null
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('O')
    Save-Progress
    Write-Output "WP2 cleanup completed: exactly eight approved databases absent; dedicated PostgreSQL role retained; receipt bytes unchanged. Result: $resultPath"
}
catch {
    $report.Status = 'Failed'
    $report.AnyDropOutcomeUnknown = @($progressTargets | Where-Object { $_.DropAttempted -eq $true -and $_.AbsenceVerified -ne $true }).Count -gt 0
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('O')
    $report.Failure = [ordered]@{ Code = $failureKind; Phase = $currentPhase; Target = $report.CurrentTarget; AtUtc = $report.FinishedUtc; OriginalExceptionOrNativeTextLogged = $false }
    try { Save-Progress } catch { Write-Output 'Cleanup stopped; could not persist the final progress update. Retain the existing result and inspect its last phase.' }
    Write-Output "WP2 cleanup stopped at $currentPhase ($failureKind); confirmed dropped/absent=$($report.DroppedAndAbsenceVerified)/8. No other database or session operation is attempted."
    exit 1
}
