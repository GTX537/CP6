#requires -Version 7.2
[CmdletBinding()]
param([string]$OutputPath = 'D:\CP6\tmp\wp5-cleanup-final-owned-thirteen.json')

# Prepared for execution only after WP5 remote delivery and post-merge smoke.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$taskName = 'DB-COMPAT-01-WP5'
$taskTmpRoot = 'D:\CP6\tmp'
$reportPath = [IO.Path]::GetFullPath($OutputPath)
if ([IO.Path]::GetDirectoryName($reportPath) -cne $taskTmpRoot -or
    [IO.Path]::GetFileName($reportPath) -cnotmatch '^wp5-cleanup-[a-z0-9-]+\.json$' -or
    (Test-Path -LiteralPath $reportPath)) { throw 'A new exact WP5 cleanup report path is required.' }

# No prefix enumeration: these receipt hashes and all thirteen identities are frozen.
$receiptSpecs = @(
    @{File='db-compat.wp5-owned.json';Sha256='2637B5B4428C5C9A201C356EFA9052535C8ABDEA28EFDDF4FFD42541E5AF98FF';Shape='Pair';Targets=@(
        @{Provider='SqlServer';Owner='cb5b2306685a4cad84d7db04be47fd73';Database='CP6Compat_WP5_20261003_cb5b2306';Purpose='space'},
        @{Provider='PostgreSql';Owner='cb5b2306685a4cad84d7db04be47fd73';Database='CP6Compat_WP5_20261003_cb5b2306';Purpose='space'}
    )},
    @{File='db-compat.wp5-reports-owned.json';Sha256='AF0DC82DAA107731563041E36A2EF4A4DCC94D8DCD07F7F07B2D3F2055B3E15D';Shape='Pair';Targets=@(
        @{Provider='SqlServer';Owner='87e10506f1b842b6883b7eadeaf1bfb2';Database='CP6Compat_WP5_20261003_87e10506';Purpose='reports'},
        @{Provider='PostgreSql';Owner='87e10506f1b842b6883b7eadeaf1bfb2';Database='CP6Compat_WP5_20261003_87e10506';Purpose='reports'}
    )},
    @{File='db-compat.wp5-migration-owned.json';Sha256='5E4E9B08DD87145623028B38526167C71094E3378D42CDF26D2FFB4613DF693A';Shape='Array';Targets=@(
        @{Provider='SqlServer';Owner='5eb979dda6504aeabac6e7f8a534bdee';Database='CP6Compat_WP5_20261003_5eb979dd';Purpose='space-only'},
        @{Provider='SqlServer';Owner='77338f4aaf4343188a8aabf67c81ce30';Database='CP6Compat_WP5_20261003_77338f4a';Purpose='asset-legacy-failure'},
        @{Provider='SqlServer';Owner='09c6f3237a2745d59314eed6e5f0eae5';Database='CP6Compat_WP5_20261003_09c6f323';Purpose='ai-retention-script'},
        @{Provider='SqlServer';Owner='e9817587ba1b42a392805cfb11f029f7';Database='CP6Compat_WP5_20261003_e9817587';Purpose='publish-recovery-failure'},
        @{Provider='SqlServer';Owner='0ae62399d38246eab037850feccfbfe6';Database='CP6Compat_WP5_20261003_0ae62399';Purpose='publish-recovery-script'},
        @{Provider='PostgreSql';Owner='4f8d0aa0a7864d36829a0873dafb9f70';Database='CP6Compat_WP5_20261003_4f8d0aa0';Purpose='space-only'}
    )},
    @{File='db-compat.wp5-prerequisite-owned.json';Sha256='2A956AEE9FEA5759FD0D294B87FCDF5AE507EB913E0068D164AB78311A4F11EF';Shape='Array';Targets=@(
        @{Provider='PostgreSql';Owner='374b0729be164b9aa30f731da4e1f507';Database='CP6Compat_WP5_20261003_374b0729';Purpose='space-then-core'},
        @{Provider='PostgreSql';Owner='dca7d13274ce40d19f47380e570d3517';Database='CP6Compat_WP5_20261003_dca7d132';Purpose='core-then-space'},
        @{Provider='PostgreSql';Owner='db401972c3a9433584401b87b7a34114';Database='CP6Compat_WP5_20261003_db401972';Purpose='space-script'}
    )}
)
$receiptChecks = [Collections.Generic.List[object]]::new()
$targets = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    Task=$taskName; ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    Scope='Only thirteen exact WP5 receipt-owned databases: seven SQL Server and six PostgreSQL. All-target read-only preflight, per-target recheck, ordinary DROP, final absence checks; no session termination.'
    StartedUtc=[DateTime]::UtcNow.ToString('O'); FinishedUtc=$null
    Status='Preparing'; Phase='ReceiptValidation'; FailureCode=$null
    ExpectedDatabaseCount=13; AllThirteenPreflightPassed=$false; Dropped=0; AllThirteenAbsent=$false
    DroppedCountScope='Confirmed native DROP successes; attempted-but-unconfirmed outcomes remain explicit on each target.'
    ReceiptBytesUnchanged=$null; PostgreSqlRolesRetained=$false; PostgreSqlEnvironmentRestored=$true
    Receipts=@(); Targets=@()
}
$failureCode = 'UnexpectedFailure'
$failed = $false
$currentTarget = $null

function Require-Wp5([bool]$Condition, [string]$Code) {
    if (!$Condition) { $script:failureCode=$Code; throw ('WP5_CLEANUP_REFUSED_' + $Code) }
}

function Save-Wp5Progress {
    # The private target objects, receipt contents and native errors are never serialized.
    $report.Receipts = @($receiptChecks.ToArray())
    $report.Targets = @($targets | ForEach-Object { $_.Public })
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
}

function Convert-Wp5NativeJson([object[]]$Lines) {
    try { return (($Lines | ForEach-Object { [string]$_ }) -join "`n").Trim() | ConvertFrom-Json }
    catch { $script:failureCode='NativeJsonInvalid'; throw 'WP5 native JSON was invalid; raw output was not logged.' }
}

function Invoke-Wp5Sql([object]$Target, [string]$Query) {
    $lines = & $sqlcmd -S $Target.Server -E -C -d master -l 10 -t 30 -b -h -1 -y 4000 -w 65535 -f 65001 -Q $Query 2>&1
    Require-Wp5 ($LASTEXITCODE -eq 0) 'SqlNativeCommandFailed'
    return Convert-Wp5NativeJson @($lines)
}

function Invoke-Wp5Pg([object]$Target, [string]$Query, [switch]$NonJson) {
    $savedEnvironment = @{}
    $environmentNames = @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE',
        'PGOPTIONS','PGAPPNAME','PGCLIENTENCODING','PGPASSFILE','PGSSLMODE')
    foreach ($name in $environmentNames) { $savedEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
    try {
        foreach ($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGOPTIONS','PGPASSFILE','PGSSLMODE')) {
            [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')
        }
        [Environment]::SetEnvironmentVariable('PGPASSWORD',$Target.Password,'Process')
        [Environment]::SetEnvironmentVariable('PGCONNECT_TIMEOUT','5','Process')
        [Environment]::SetEnvironmentVariable('PGAPPNAME','CP6Compat.WP5.Cleanup','Process')
        [Environment]::SetEnvironmentVariable('PGCLIENTENCODING','UTF8','Process')
        # The password is inherited by psql, never included in command arguments or output.
        $lines = & $psql -X -w -h $Target.Host -p ([string]$Target.Port) -U $Target.Username -d postgres -v ON_ERROR_STOP=1 -At -c $Query 2>&1
        Require-Wp5 ($LASTEXITCODE -eq 0) 'PostgreSqlNativeCommandFailed'
        if (!$NonJson) { return Convert-Wp5NativeJson @($lines) }
    }
    finally {
        foreach ($name in $environmentNames) {
            if ($null -eq $savedEnvironment[$name]) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') }
            else { [Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name],'Process') }
        }
        foreach ($name in $environmentNames) {
            if (![string]::Equals([Environment]::GetEnvironmentVariable($name,'Process'),$savedEnvironment[$name],[StringComparison]::Ordinal)) {
                $report.PostgreSqlEnvironmentRestored=$false
                $script:failureCode='PostgreSqlEnvironmentNotRestored'
                throw 'WP5 PostgreSQL environment restoration failed; values were not logged.'
            }
        }
    }
}

function Read-Wp5State([object]$Target) {
    $database=$Target.Database
    if ($Target.Provider -ceq 'SqlServer') {
        return Invoke-Wp5Sql $Target @"
SET NOCOUNT ON;
SELECT DB_ID(N'$database') AS DatabaseId,
       IS_SRVROLEMEMBER(N'sysadmin') AS FullActivityVisible,
       (SELECT CONVERT(varchar(8),create_date,112)+'|'+CONVERT(varchar(12),create_date,114) FROM sys.databases WHERE name=N'$database') AS CreatedIdentity,
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

function Confirm-Wp5State([object]$Target, [object]$State, [object]$Expected=$null) {
    Require-Wp5 ($null -ne $State) 'OwnedDatabaseMissing'
    if ($Target.Provider -ceq 'SqlServer') {
        Require-Wp5 ($State.DatabaseId -gt 0 -and $State.FullActivityVisible -eq 1 -and
            [string]$State.CreatedIdentity -cmatch '^\d{8}\|\d{2}:\d{2}:\d{2}:\d{3}$') 'SqlExistenceOrActivityVisibility'
        Require-Wp5 ($State.Owner -ceq $Target.Owner -and $State.Task -ceq $taskName) 'SqlOwnerMarkerMismatch'
        Require-Wp5 ($State.ActiveSessions -eq 0 -and $State.ActiveRequests -eq 0) 'SqlActiveSessionOrRequest'
    }
    else {
        Require-Wp5 ([string]$State.DatabaseId -cmatch '^[0-9]+$') 'PostgreSqlDatabaseMissing'
        Require-Wp5 ($State.Owner -ceq ($taskName + ':' + $Target.Owner) -and
            $State.DatabaseRole -ceq $Target.Username -and $State.CurrentRole -ceq $Target.Username) 'PostgreSqlOwnerMarkerMismatch'
        Require-Wp5 ($State.ActiveSessions -eq 0 -and $State.PreparedTransactions -eq 0) 'PostgreSqlActiveSessionOrPreparedTransaction'
    }
    if ($null -ne $Expected) {
        Require-Wp5 ([string]$State.DatabaseId -ceq [string]$Expected.DatabaseId) 'PhysicalDatabaseIdentityChanged'
        if ($Target.Provider -ceq 'SqlServer') { Require-Wp5 ($State.CreatedIdentity -ceq $Expected.CreatedIdentity) 'SqlDatabaseCreationIdentityChanged' }
    }
}

function Confirm-Wp5ReceiptBytes {
    foreach ($entry in $receiptChecks) {
        Require-Wp5 ((Test-Path -LiteralPath $entry.Path -PathType Leaf) -and
            (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ceq $entry.ExpectedSha256) 'ReceiptBytesChanged'
    }
}

function Confirm-Wp5Absent([object]$Target) {
    $database=$Target.Database
    if ($Target.Provider -ceq 'SqlServer') {
        $state=Invoke-Wp5Sql $Target "SET NOCOUNT ON; SELECT COUNT(*) AS Present FROM sys.databases WHERE name=N'$database' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;"
        Require-Wp5 ($state.Present -eq 0) 'SqlAbsenceNotConfirmed'
    }
    else {
        $state=Invoke-Wp5Pg $Target ("SELECT json_build_object('Present',(SELECT count(*) FROM pg_database WHERE datname='$database'),'RoleExists',(SELECT count(*) FROM pg_roles WHERE rolname='" + $Target.Username + "'));")
        Require-Wp5 ($state.Present -eq 0 -and $state.RoleExists -eq 1) 'PostgreSqlAbsenceOrRetainedRoleNotConfirmed'
    }
}

try {
    Save-Wp5Progress
    foreach ($spec in $receiptSpecs) {
        $receiptPath=Join-Path $taskTmpRoot $spec.File
        Require-Wp5 (Test-Path -LiteralPath $receiptPath -PathType Leaf) 'ReceiptUnavailable'
        $failureCode='ReceiptReadOrParseFailed'
        $receiptBytes=[IO.File]::ReadAllBytes($receiptPath)
        $receiptHash=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($receiptBytes))
        $receiptChecks.Add([ordered]@{File=$spec.File;Path=$receiptPath;ExpectedSha256=$spec.Sha256;Sha256=$receiptHash;BytesUnchanged=$null})
        Require-Wp5 ($receiptHash -ceq $spec.Sha256) 'ReceiptFrozenHashMismatch'
        $receipt=[Text.Encoding]::UTF8.GetString($receiptBytes).TrimStart([char]0xFEFF) | ConvertFrom-Json
        Require-Wp5 ($receipt.Task -ceq $taskName) 'ReceiptTaskMismatch'
        if ($spec.Shape -ceq 'Array') { Require-Wp5 (@($receipt.Databases).Count -eq $spec.Targets.Count) 'ReceiptExactEntryCount' }
        foreach ($expected in $spec.Targets) {
            Require-Wp5 ($expected.Owner -cmatch '^[a-f0-9]{32}$' -and
                $expected.Database -ceq ('CP6Compat_WP5_20261003_' + $expected.Owner.Substring(0,8))) 'FrozenTargetNameInvalid'
            if ($spec.Shape -ceq 'Pair') {
                $provider=$expected.Provider
                Require-Wp5 ($receipt.Owner -ceq $expected.Owner -and
                    $receipt.($provider + 'Database') -ceq $expected.Database -and
                    $receipt.($provider + 'State') -ceq 'CreatedAndOwnerMarked') 'ReceiptExactPairIdentity'
                $connection=[string]$receipt.($provider + 'Connection')
            }
            else {
                $matchedEntries=@($receipt.Databases | Where-Object { $_.Provider -ceq $expected.Provider -and $_.DatabaseName -ceq $expected.Database })
                Require-Wp5 ($matchedEntries.Count -eq 1) 'ReceiptExactArrayIdentity'
                $entry=$matchedEntries[0]
                Require-Wp5 ($entry.Owner -ceq $expected.Owner -and $entry.Purpose -ceq $expected.Purpose -and
                    $entry.State -ceq 'CreatedAndOwnerMarked') 'ReceiptArrayOwnerPurposeOrState'
                $connection=[string]$entry.ConnectionString
            }
            $failureCode='ReceiptConnectionParseFailed'
            $builder=[System.Data.Common.DbConnectionStringBuilder]::new()
            $builder.set_ConnectionString($connection)
            $public=[ordered]@{Provider=$expected.Provider;Database=$expected.Database;OwnerSuffix=$expected.Owner.Substring(0,8);
                Purpose=$expected.Purpose;Receipt=$spec.File;PreflightPassed=$false;DatabaseId=$null;ActiveSessions=$null;ActiveRequests=$null;
                DropAttempted=$false;DropConfirmed=$false;Absent=$false;FinalAbsenceCheckedUtc=$null;Outcome='NotAttempted';FailureCode=$null}
            $target=[pscustomobject]@{Provider=$expected.Provider;Database=$expected.Database;Owner=$expected.Owner;Public=$public;
                Server=$null;Host=$null;Port=5432;Username=$null;Password=$null;Baseline=$null}
            if ($expected.Provider -ceq 'SqlServer') {
                $allowed=@('Server','Database','Integrated Security','TrustServerCertificate','MultipleActiveResultSets','Connect Timeout')
                Require-Wp5 (@($builder.Keys | Where-Object { $_ -notin $allowed }).Count -eq 0 -and
                    $builder['Server'] -ceq 'localhost\KOUSQLSERVER' -and $builder['Database'] -ceq $expected.Database -and
                    [string]$builder['Integrated Security'] -ieq 'True' -and
                    [string]$builder['MultipleActiveResultSets'] -ieq 'False') 'SqlExactLoopbackIntegratedConnection'
                $target.Server=[string]$builder['Server']
            }
            else {
                $allowed=@('Host','Port','Database','Username','Password','Timeout','Search Path')
                Require-Wp5 (@($builder.Keys | Where-Object { $_ -notin $allowed }).Count -eq 0 -and
                    $builder['Host'] -cin @('localhost','127.0.0.1','::1') -and [string]$builder['Port'] -ceq '5432' -and
                    $builder['Database'] -ceq $expected.Database -and [string]$builder['Username'] -cmatch '^cp6compat_wp1_[a-f0-9]{8}$' -and
                    [string]$builder['Search Path'] -ceq 'public' -and ![string]::IsNullOrEmpty([string]$builder['Password'])) 'PostgreSqlLoopbackRoleOrTarget'
                $target.Host=[string]$builder['Host']; $target.Username=[string]$builder['Username']; $target.Password=[string]$builder['Password']
            }
            $targets.Add($target)
        }
        Save-Wp5Progress
    }
    Require-Wp5 ($targets.Count -eq 13 -and
        @($targets | Where-Object Provider -CEQ 'SqlServer').Count -eq 7 -and
        @($targets | Where-Object Provider -CEQ 'PostgreSql').Count -eq 6 -and
        @($targets | Group-Object Provider,Database | Where-Object Count -ne 1).Count -eq 0) 'ThirteenDistinctReceiptTargetsRequired'
    Require-Wp5 (@($targets | Where-Object Provider -CEQ 'PostgreSql' | Select-Object -ExpandProperty Username -Unique).Count -eq 1) 'OneExistingPostgreSqlRoleRequired'
    Confirm-Wp5ReceiptBytes
    $failureCode='NativeClientUnavailable'
    $sqlCommand=Get-Command sqlcmd -ErrorAction Stop
    Require-Wp5 ($sqlCommand.CommandType -eq 'Application' -and [IO.Path]::GetFileName($sqlCommand.Source) -ieq 'sqlcmd.exe') 'NativeSqlcmdRequired'
    $sqlcmd=$sqlCommand.Source
    $psql='C:\Program Files\PostgreSQL\18\bin\psql.exe'
    Require-Wp5 (Test-Path -LiteralPath $psql -PathType Leaf) 'NativePsqlRequired'
    $report.Phase='AllThirteenReadOnlyPreflight'
    foreach ($target in $targets) {
        $currentTarget=$target
        $failureCode='PreflightUnexpectedFailure'
        $state=Read-Wp5State $target
        Confirm-Wp5State $target $state
        $target.Baseline=$state
        $target.Public.DatabaseId=$state.DatabaseId
        $target.Public.ActiveSessions=$state.ActiveSessions
        if ($target.Provider -ceq 'SqlServer') { $target.Public.ActiveRequests=$state.ActiveRequests }
        $target.Public.PreflightPassed=$true
        Save-Wp5Progress
    }
    Confirm-Wp5ReceiptBytes
    $report.AllThirteenPreflightPassed=$true
    $report.Status='Dropping'
    Save-Wp5Progress

    foreach ($target in $targets) {
        $currentTarget=$target
        $report.Phase='DropExact' + $target.Provider
        $failureCode='DropUnexpectedFailure'
        Confirm-Wp5ReceiptBytes
        Confirm-Wp5State $target (Read-Wp5State $target) $target.Baseline
        $database=$target.Database; $owner=$target.Owner
        $target.Public.DropAttempted=$true; $target.Public.Outcome='AttemptedUnconfirmed'
        Save-Wp5Progress
        if ($target.Provider -ceq 'SqlServer') {
            $after=Invoke-Wp5Sql $target @"
SET NOCOUNT ON;
IF ISNULL(DB_ID(N'$database'),-1) <> $($target.Baseline.DatabaseId) OR ISNULL(IS_SRVROLEMEMBER(N'sysadmin'),0) <> 1
 OR NOT EXISTS(SELECT 1 FROM sys.databases WHERE name=N'$database' AND CONVERT(varchar(8),create_date,112)+'|'+CONVERT(varchar(12),create_date,114)='$($target.Baseline.CreatedIdentity)')
 OR NOT EXISTS(SELECT 1 FROM [$database].sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'$owner')
 OR NOT EXISTS(SELECT 1 FROM [$database].sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'$taskName')
 OR EXISTS(SELECT 1 FROM sys.dm_exec_sessions WHERE database_id=DB_ID(N'$database'))
 OR EXISTS(SELECT 1 FROM sys.dm_exec_requests WHERE database_id=DB_ID(N'$database'))
 THROW 51060, 'WP5_CLEANUP_SQL_RECHECK_REFUSED', 1;
DROP DATABASE [$database];
SELECT COUNT(*) AS Present FROM sys.databases WHERE name=N'$database' FOR JSON PATH, WITHOUT_ARRAY_WRAPPER;
"@
            $target.Public.DropConfirmed=$true; $target.Public.Outcome='DropConfirmed'; $report.Dropped++
            Require-Wp5 ($after.Present -eq 0) 'SqlAbsenceNotConfirmed'
        }
        else {
            # PostgreSQL DROP DATABASE is an ordinary standalone autocommit command.
            # A newly connected session causes refusal; it is never disconnected.
            Invoke-Wp5Pg $target ('DROP DATABASE "' + $database + '";') -NonJson
            $target.Public.DropConfirmed=$true; $target.Public.Outcome='DropConfirmed'; $report.Dropped++
            Confirm-Wp5Absent $target
        }
        $target.Public.Absent=$true; $target.Public.Outcome='ConfirmedAbsent'
        Save-Wp5Progress
    }
    $report.Phase='AllThirteenFinalAbsenceCheck'
    foreach ($target in $targets) {
        $currentTarget=$target
        Confirm-Wp5Absent $target
        $target.Public.FinalAbsenceCheckedUtc=[DateTime]::UtcNow.ToString('O')
        Save-Wp5Progress
    }
    Confirm-Wp5ReceiptBytes
    $report.PostgreSqlRolesRetained=$true
    $report.AllThirteenAbsent=($report.Dropped -eq 13 -and @($targets | Where-Object { !$_.Public.Absent -or $null -eq $_.Public.FinalAbsenceCheckedUtc }).Count -eq 0)
    Require-Wp5 $report.AllThirteenAbsent 'ThirteenAbsencesRequired'
    $report.Status='Completed'; $report.Phase='Completed'
}
catch {
    $failed=$true; $report.Status='Failed'; $report.FailureCode=$failureCode
    if ($null -ne $currentTarget) { $currentTarget.Public.FailureCode=$failureCode }
    # Native output, connection strings, passwords and exception messages stay private.
}
finally {
    $receiptsUnchanged=($receiptChecks.Count -eq 4)
    $receiptBytesChanged=$false
    foreach ($entry in $receiptChecks) {
        try {
            $entry.BytesUnchanged=(Test-Path -LiteralPath $entry.Path -PathType Leaf) -and
                (Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ceq $entry.ExpectedSha256
        }
        catch { $entry.BytesUnchanged=$false }
        if (!$entry.BytesUnchanged) { $receiptsUnchanged=$false; $receiptBytesChanged=$true }
    }
    $report.ReceiptBytesUnchanged=$receiptsUnchanged
    if ($receiptBytesChanged) { $failed=$true; $report.Status='Failed'; $report.FailureCode='ReceiptBytesChanged' }
    $report.FinishedUtc=[DateTime]::UtcNow.ToString('O')
    Save-Wp5Progress
}
if ($failed) { throw ('WP5 cleanup failed in ' + $report.Phase + ': ' + $report.FailureCode + '. See sanitized report.') }
Write-Output ('Completed ordinary DROP of thirteen exact WP5 databases; receipts and PostgreSQL role retained. Report: ' + $reportPath)
