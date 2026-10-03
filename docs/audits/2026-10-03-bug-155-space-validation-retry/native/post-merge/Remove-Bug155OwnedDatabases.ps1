#requires -Version 7.2
[CmdletBinding()]
param([string]$OutputPath = 'D:\CP6\tmp\bug155-cleanup-owned-two.json')

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskName = 'DB-COMPAT-01-WP5'
$taskTmpRoot = 'D:\CP6\tmp'
$reportPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetDirectoryName($reportPath) -cne $taskTmpRoot -or
    [IO.Path]::GetFileName($reportPath) -cnotmatch '^bug155-cleanup-[a-z0-9-]+\.json$' -or
    (Test-Path -LiteralPath $reportPath)) { throw 'A new exact BUG155 cleanup report path is required.' }

$receiptSpecs = @(
    @{File='bug155-wp5-owned.private.json';Owner='d631932a239444c9aff5aaf6823ceebf';Database='CP6Compat_WP5_20261003_d631932a'}
)
$receiptChecks = [Collections.Generic.List[object]]::new()
$targets = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    Task = $taskName
    ScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    Scope = 'Only two exact BUG155 (WP5 fixture protocol) receipt-owned databases; all-target read-only preflight, per-target ownership/identity/activity recheck, ordinary DROP, no session termination.'
    StartedUtc = [DateTime]::UtcNow.ToString('O'); FinishedUtc = $null
    Status = 'Preparing'; Phase = 'ReceiptValidation'; FailureCode = $null
    ExpectedDatabaseCount = 2; AllTwoPreflightPassed = $false; Dropped = 0; AllTwoAbsent = $false
    DroppedCountScope = 'Confirmed native DROP successes; attempted-but-unconfirmed outcomes remain explicit on each target.'
    ReceiptBytesUnchanged = $null; PostgreSqlRolesRetained = $false; PostgreSqlEnvironmentRestored = $true
    Receipts = @(); Targets = @()
}
$failureCode = 'UnexpectedFailure'
$failed = $false

function Require-Wp5([bool]$Condition, [string]$Code) {
    if (!$Condition) { $script:failureCode = $Code; throw ('WP5_CLEANUP_REFUSED_' + $Code) }
}

function Save-Wp5Progress {
    # Only this new, task-specific sanitized report is written. Original receipt
    # bytes remain private and untouched; native output and errors are excluded.
    $report.Receipts = @($receiptChecks.ToArray())
    $report.Targets = @($targets | ForEach-Object { $_.Public })
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
}

function Convert-Wp5NativeJson([object[]]$Lines) {
    try { return (($Lines | ForEach-Object { [string]$_ }) -join "`n").Trim() | ConvertFrom-Json }
    catch { $script:failureCode = 'NativeJsonInvalid'; throw 'WP5 native JSON was invalid; raw output was not logged.' }
}

function Invoke-Wp5Sql([object]$Target, [string]$Query) {
    # Nonzero -y is compatible with -h -1; never combine -W with -y.
    $lines = & $sqlcmd -S $Target.Server -E -C -d master -l 10 -t 30 -b -h -1 -y 4000 -w 65535 -f 65001 -Q $Query 2>&1
    Require-Wp5 ($LASTEXITCODE -eq 0) 'SqlNativeCommandFailed'
    return Convert-Wp5NativeJson @($lines)
}

function Invoke-Wp5Pg([object]$Target, [string]$Query, [switch]$NonJson) {
    $savedEnvironment = @{}
    $environmentNames = @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE',
        'PGOPTIONS','PGAPPNAME','PGCLIENTENCODING','PGPASSFILE','PGSSLMODE')
    foreach ($name in $environmentNames) { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
    try {
        foreach ($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGOPTIONS','PGPASSFILE','PGSSLMODE')) {
            [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process')
        }
        [Environment]::SetEnvironmentVariable('PGPASSWORD', $Target.Password, 'Process')
        [Environment]::SetEnvironmentVariable('PGCONNECT_TIMEOUT', '5', 'Process')
        [Environment]::SetEnvironmentVariable('PGAPPNAME', 'CP6Compat.WP5.Cleanup', 'Process')
        [Environment]::SetEnvironmentVariable('PGCLIENTENCODING', 'UTF8', 'Process')
        # Password is inherited by the child process; it is never an argument.
        $lines = & $psql -X -w -h $Target.Host -p ([string]$Target.Port) -U $Target.Username -d postgres -v ON_ERROR_STOP=1 -At -c $Query 2>&1
        Require-Wp5 ($LASTEXITCODE -eq 0) 'PostgreSqlNativeCommandFailed'
        if (!$NonJson) { return Convert-Wp5NativeJson @($lines) }
    }
    finally {
        foreach ($name in $environmentNames) {
            if ($null -eq $savedEnvironment[$name]) { [Environment]::SetEnvironmentVariable($name, [NullString]::Value, 'Process') }
            else { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
        }
        foreach ($name in $environmentNames) {
            $restored = [Environment]::GetEnvironmentVariable($name, 'Process')
            if (![string]::Equals($restored, $savedEnvironment[$name], [StringComparison]::Ordinal)) {
                $report.PostgreSqlEnvironmentRestored = $false
                $script:failureCode = 'PostgreSqlEnvironmentNotRestored'
                throw 'WP5 PostgreSQL environment restoration failed; values were not logged.'
            }
        }
    }
}

function Read-Wp5State([object]$Target) {
    $database = $Target.Database
    if ($Target.Provider -ceq 'SqlServer') {
        return Invoke-Wp5Sql $Target @"
SET NOCOUNT ON;
SELECT DB_ID(N'$database') AS DatabaseId,
       IS_SRVROLEMEMBER(N'sysadmin') AS FullActivityVisible,
       (SELECT CONVERT(nvarchar(200),value) FROM [$database].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner') AS Owner,
       (SELECT CONVERT(nvarchar(200),value) FROM [$database].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask') AS Task,
       (SELECT COUNT_BIG(*) FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'$database')) AS ActiveSessions,
       (SELECT COUNT_BIG(*) FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'$database')) AS ActiveRequests
FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
"@
    }
    return Invoke-Wp5Pg $Target @"
SELECT json_build_object('DatabaseId',d.oid::text,'Owner',shobj_description(d.oid,'pg_database'),
    'DatabaseRole',pg_get_userbyid(d.datdba),'CurrentRole',current_user,
    'ActiveSessions',(SELECT count(*) FROM pg_stat_activity WHERE datname='$database'),
    'PreparedTransactions',(SELECT count(*) FROM pg_prepared_xacts WHERE database='$database'))
FROM pg_database d WHERE d.datname='$database';
"@
}

function Confirm-Wp5State([object]$Target, [object]$State, [object]$Expected = $null) {
    Require-Wp5 ($null -ne $State) 'OwnedDatabaseMissing'
    if ($Target.Provider -ceq 'SqlServer') {
        Require-Wp5 ($State.DatabaseId -gt 0 -and $State.FullActivityVisible -eq 1) 'SqlExistenceOrActivityVisibility'
        Require-Wp5 ($State.Owner -ceq $Target.Owner -and $State.Task -ceq $taskName) 'SqlOwnerMarkerMismatch'
        Require-Wp5 ($State.ActiveSessions -eq 0 -and $State.ActiveRequests -eq 0) 'SqlActiveSessionOrRequest'
    }
    else {
        Require-Wp5 ([string]$State.DatabaseId -cmatch '^[0-9]+$') 'PostgreSqlDatabaseMissing'
        Require-Wp5 ($State.Owner -ceq ($taskName + ':' + $Target.Owner) -and
            $State.DatabaseRole -ceq $Target.Username -and $State.CurrentRole -ceq $Target.Username) 'PostgreSqlOwnerMarkerMismatch'
        Require-Wp5 ($State.ActiveSessions -eq 0 -and $State.PreparedTransactions -eq 0) 'PostgreSqlActiveSessionOrPreparedTransaction'
    }
    if ($null -ne $Expected) { Require-Wp5 ([string]$State.DatabaseId -ceq [string]$Expected.DatabaseId) 'PhysicalDatabaseIdentityChanged' }
}

function Confirm-Wp5ReceiptBytes {
    foreach ($entry in $receiptChecks) {
        Require-Wp5 ((Test-Path -LiteralPath $entry.Path -PathType Leaf) -and
            (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ceq $entry.Sha256) 'ReceiptBytesChanged'
    }
}

try {
    $sqlCommand = Get-Command sqlcmd -ErrorAction Stop
    Require-Wp5 ($sqlCommand.CommandType -eq 'Application' -and [IO.Path]::GetFileName($sqlCommand.Source) -ieq 'sqlcmd.exe') 'NativeSqlcmdRequired'
    $sqlcmd = $sqlCommand.Source
    $psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe'
    Require-Wp5 (Test-Path -LiteralPath $psql -PathType Leaf) 'NativePsqlRequired'

    foreach ($spec in $receiptSpecs) {
        $receiptPath = Join-Path $taskTmpRoot $spec.File
        Require-Wp5 (Test-Path -LiteralPath $receiptPath -PathType Leaf) 'ReceiptUnavailable'
        $receiptHash = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
        $receiptChecks.Add([ordered]@{File=$spec.File;Path=$receiptPath;Sha256=$receiptHash;BytesUnchanged=$null})
        $receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
        Require-Wp5 ($receipt.Task -ceq $taskName -and $receipt.Owner -ceq $spec.Owner -and
            $receipt.Owner -cmatch '^[a-f0-9]{32}$' -and $receipt.SqlServerState -ceq 'CreatedAndOwnerMarked' -and
            $receipt.PostgreSqlState -ceq 'CreatedAndOwnerMarked') 'ReceiptTaskOwnerOrState'
        Require-Wp5 ($spec.Database -cmatch '^CP6Compat_WP5_20261003_[a-f0-9]{8}$' -and
            $spec.Database.EndsWith('_' + $receipt.Owner.Substring(0,8), [StringComparison]::Ordinal) -and
            $receipt.SqlServerDatabase -ceq $spec.Database -and $receipt.PostgreSqlDatabase -ceq $spec.Database) 'ReceiptExactTargetNames'

        # Use explicit connection-string setters: PowerShell's dynamic dictionary
        # adapter otherwise adds a literal ConnectionString key to this builder.
        $sql = [System.Data.Common.DbConnectionStringBuilder]::new()
        $sql.set_ConnectionString($receipt.SqlServerConnection)
        $allowedSql = @('Server','Database','Integrated Security','TrustServerCertificate','MultipleActiveResultSets','Connect Timeout')
        Require-Wp5 (@($sql.Keys | Where-Object { $_ -notin $allowedSql }).Count -eq 0 -and
            $sql['Server'] -ceq 'localhost\KOUSQLSERVER' -and $sql['Database'] -ceq $spec.Database -and
            [string]$sql['Integrated Security'] -ieq 'True' -and [string]$sql['MultipleActiveResultSets'] -ieq 'False') 'SqlExactLoopbackIntegratedConnection'
        $pg = [System.Data.Common.DbConnectionStringBuilder]::new()
        $pg.set_ConnectionString($receipt.PostgreSqlConnection)
        $allowedPg = @('Host','Port','Database','Username','Password','Timeout','Search Path','SSL Mode')
        Require-Wp5 (@($pg.Keys | Where-Object { $_ -notin $allowedPg }).Count -eq 0 -and
            $pg['Host'] -cin @('localhost','127.0.0.1','::1') -and [string]$pg['Port'] -ceq '5432' -and
            $pg['Database'] -ceq $spec.Database -and [string]$pg['Username'] -cmatch '^cp6compat_wp1_[a-f0-9]{8}$' -and
            ![string]::IsNullOrEmpty([string]$pg['Password']) -and [string]$pg['SSL Mode'] -ceq 'Disable') 'PostgreSqlLoopbackRoleOrTarget'

        foreach ($provider in @('SqlServer','PostgreSql')) {
            $public = [ordered]@{Provider=$provider;Database=$spec.Database;OwnerSuffix=$receipt.Owner.Substring(0,8);
                Receipt=$spec.File;PreflightPassed=$false;DatabaseId=$null;ActiveSessions=$null;ActiveRequests=$null;
                DropAttempted=$false;DropConfirmed=$false;Absent=$false;Outcome='NotAttempted';FailureCode=$null}
            # Connection details remain in memory only; never serialize this object.
            $target = [pscustomobject]@{Provider=$provider;Database=$spec.Database;Owner=$spec.Owner;Public=$public;
                Server=[string]$sql['Server'];Host=[string]$pg['Host'];Port=5432;Username=[string]$pg['Username'];Password=[string]$pg['Password'];Baseline=$null}
            $targets.Add($target)
        }
    }
    Require-Wp5 ($targets.Count -eq 2 -and @($targets | Group-Object Provider,Database | Where-Object Count -ne 1).Count -eq 0) 'TwoDistinctReceiptTargetsRequired'
    Confirm-Wp5ReceiptBytes
    $report.Phase = 'AllTwoReadOnlyPreflight'
    foreach ($target in $targets) {
        $state = Read-Wp5State $target
        Confirm-Wp5State $target $state
        $target.Baseline = $state
        $target.Public.DatabaseId = $state.DatabaseId
        $target.Public.ActiveSessions = $state.ActiveSessions
        if ($target.Provider -ceq 'SqlServer') { $target.Public.ActiveRequests = $state.ActiveRequests }
        $target.Public.PreflightPassed = $true
        Save-Wp5Progress
    }
    $report.AllTwoPreflightPassed = $true
    $report.Status = 'Dropping'
    Save-Wp5Progress

    foreach ($target in $targets) {
        $report.Phase = 'DropExact' + $target.Provider
        Confirm-Wp5ReceiptBytes
        Confirm-Wp5State $target (Read-Wp5State $target) $target.Baseline
        $database = $target.Database
        $owner = $target.Owner
        $target.Public.DropAttempted = $true
        $target.Public.Outcome = 'AttemptedUnconfirmed'
        Save-Wp5Progress
        if ($target.Provider -ceq 'SqlServer') {
            # Same-batch recheck retains the original protocol; no forced access
            # change, termination, or rollback of another session is permitted.
            $after = Invoke-Wp5Sql $target @"
SET NOCOUNT ON;
IF ISNULL(DB_ID(N'$database'),-1) <> $($target.Baseline.DatabaseId) OR ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0) <> 1
 OR NOT EXISTS(SELECT 1 FROM [$database].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'$owner')
 OR NOT EXISTS(SELECT 1 FROM [$database].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'$taskName')
 OR EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'$database'))
 OR EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'$database'))
 THROW 51060, 'WP5_CLEANUP_SQL_RECHECK_REFUSED', 1;
DROP DATABASE [$database];
SELECT COUNT(*) AS Present FROM sys.databases WHERE name=N'$database' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
"@
            $target.Public.DropConfirmed = $true; $target.Public.Outcome = 'DropConfirmed'; $report.Dropped++
            Require-Wp5 ($after.Present -eq 0) 'SqlAbsenceNotConfirmed'
        }
        else {
            # DROP DATABASE cannot be inside a transaction. This is one ordinary
            # autocommit command; a new connection makes it fail, never terminate.
            Invoke-Wp5Pg $target ('DROP DATABASE "' + $database + '";') -NonJson
            $target.Public.DropConfirmed = $true; $target.Public.Outcome = 'DropConfirmed'; $report.Dropped++
            $after = Invoke-Wp5Pg $target ("SELECT json_build_object('Present',(SELECT count(*) FROM pg_database WHERE datname='$database'),'RoleExists',(SELECT count(*) FROM pg_roles WHERE rolname='" + $target.Username + "'));" )
            Require-Wp5 ($after.Present -eq 0 -and $after.RoleExists -eq 1) 'PostgreSqlAbsenceOrRetainedRoleNotConfirmed'
        }
        $target.Public.Absent = $true
        $target.Public.Outcome = 'ConfirmedAbsent'
        Save-Wp5Progress
    }
    $report.PostgreSqlRolesRetained = $true
    $report.AllTwoAbsent = ($report.Dropped -eq 2 -and @($targets | Where-Object { !$_.Public.Absent }).Count -eq 0)
    Require-Wp5 $report.AllTwoAbsent 'TwoAbsencesRequired'
    $report.Status = 'Completed'; $report.Phase = 'Completed'
}
catch {
    $failed = $true; $report.Status = 'Failed'; $report.FailureCode = $failureCode
    if ($report.Phase -cne 'ReceiptValidation' -and $null -ne (Get-Variable target -ErrorAction SilentlyContinue)) {
        $target.Public.FailureCode = $failureCode
    }
    # Never serialize receipts, connections, native stderr, or exception messages.
}
finally {
    $receiptsUnchanged = ($receiptChecks.Count -eq 1)
    $receiptBytesChanged = $false
    foreach ($entry in $receiptChecks) {
        $entry.BytesUnchanged = (Test-Path -LiteralPath $entry.Path -PathType Leaf) -and
            (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ceq $entry.Sha256
        if (!$entry.BytesUnchanged) { $receiptsUnchanged = $false; $receiptBytesChanged = $true }
    }
    $report.ReceiptBytesUnchanged = $receiptsUnchanged
    if ($receiptBytesChanged) { $failed = $true; $report.Status = 'Failed'; $report.FailureCode = 'ReceiptBytesChanged' }
    $report.FinishedUtc = [DateTime]::UtcNow.ToString('O')
    Save-Wp5Progress
}
if ($failed) { throw ('WP5 cleanup failed in ' + $report.Phase + ': ' + $report.FailureCode + '. See sanitized report.') }
Write-Output ('Completed ordinary DROP of two exact BUG155 (WP5 fixture protocol) databases; receipts and PostgreSQL roles retained. Report: ' + $reportPath)
