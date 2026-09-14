[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$RestoreReceipt,
    [Parameter(Mandatory)][string]$ToolAssembly,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$ConnectionEnvironmentVariable = 'C04A_RESTORED_CONNECTION'
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$utf8 = [Text.UTF8Encoding]::new($false)
$receipt = Get-Content -LiteralPath $RestoreReceipt -Raw | ConvertFrom-Json
if ($receipt.status -cne 'RestoredEmptySourceVerified' -or !$receipt.backupCopyOnly -or
    !$receipt.backupChecksum -or !$receipt.verifyOnlyPassed -or
    $receipt.restoredDatabase -cnotmatch '^CP6_C04A_Rehearsal_[0-9a-f]{32}$' -or
    $receipt.restored.metadataSha256 -cne $receipt.sourceBefore.metadataSha256) {
    throw 'A verified real-source restore receipt is required.'
}
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new(
    [Environment]::GetEnvironmentVariable($ConnectionEnvironmentVariable))
if ($builder.InitialCatalog -cne $receipt.restoredDatabase) { throw 'Connection database differs from the restored receipt.' }
$builder['Pooling'] = $false
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Evidence directory must be new.' }
$output = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)).FullName
$assembly = (Resolve-Path -LiteralPath $ToolAssembly).Path
$checks = [Collections.Generic.List[object]]::new()
$report = [ordered]@{
    schemaVersion = 1; startedAtUtc = [DateTimeOffset]::UtcNow.ToString('O'); status = 'Started'
    restoreReceiptSha256 = (Get-FileHash -LiteralPath $RestoreReceipt -Algorithm SHA256).Hash.ToLowerInvariant()
    assemblySha256 = (Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash.ToLowerInvariant()
    runnerSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash.ToLowerInvariant()
    restoredDatabase = $receipt.restoredDatabase; realSourceRestore = $true; sourceProfile = 'empty'
    completeC04AAcceptance = $false; targetFirstWriteObserved = $false; sourceFenceActivated = $false
}
function Save-Report {
    $report.checks = @($checks.ToArray())
    [IO.File]::WriteAllText((Join-Path $output 'acceptance.json'), ($report | ConvertTo-Json -Depth 16) + "`n", $utf8)
}
function Query([string]$Sql, [switch]$Scalar) {
    $connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
    try {
        $connection.Open()
        $command = $connection.CreateCommand()
        $command.CommandTimeout = 30
        $command.CommandText = $Sql
        try {
            if ($Scalar) { return $command.ExecuteScalar() }
            [void]$command.ExecuteNonQuery()
        } finally { $command.Dispose() }
    } finally { $connection.Dispose() }
}
function Check([string]$Name, [bool]$Condition) {
    if (!$Condition) { throw "Acceptance assertion failed: $Name" }
    $checks.Add(@{ name = $Name; passed = $true })
    Save-Report
}
function Denied([string]$Sql, [int[]]$Numbers) {
    try { Query $Sql } catch {
        $exception = $_.Exception
        while ($null -ne $exception.InnerException) { $exception = $exception.InnerException }
        if ($exception -is [System.Data.SqlClient.SqlException] -and $exception.Number -in $Numbers) { return $exception.Number }
        throw 'Unexpected SQL failure in a rejection probe; no query or provider message was recorded.'
    }
    throw 'A protected write unexpectedly succeeded.'
}
function Fence([string]$Command, [Guid]$Run, [long]$Generation, [string]$State, [long]$ResultGeneration) {
    $env:C04A_RUN_ID = $Run.ToString()
    $env:C04A_EXPECTED_GENERATION = $Generation.ToString([Globalization.CultureInfo]::InvariantCulture)
    $path = Join-Path $output (('{0:D2}' -f $checks.Count) + '-' + $Command + '.json')
    & dotnet $assembly $Command > $path 2> ($path + '.stderr')
    if ($LASTEXITCODE -ne 0) { throw "Source fence command failed: $Command" }
    $result = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    Check "$Command-$Generation-$State" ($result.state -ceq $State -and $result.generation -eq $ResultGeneration -and !$result.completeWriteFenceVerified)
    return $result
}

$savedEnvironment = @{}
foreach ($key in @('C04A_SQL_CONNECTION','C04A_EXPECTED_DATABASE','C04A_EXPECTED_DATABASE_GUID','C04A_RUN_ID','C04A_EXPECTED_GENERATION')) {
    $savedEnvironment[$key] = [Environment]::GetEnvironmentVariable($key)
}
Save-Report
try {
    # All SQL probes are confined to this new local restored database.
    Check 'local-restored-identity' ((Query "SELECT CONVERT(nvarchar(128),SERVERPROPERTY('MachineName'));" -Scalar) -ieq $env:COMPUTERNAME -and
        (Query 'SELECT CONVERT(nvarchar(36),service_broker_guid) FROM sys.databases WHERE name=DB_NAME();' -Scalar) -ieq $receipt.restoredServiceBrokerGuid)
    $env:C04A_SQL_CONNECTION = $builder.ConnectionString
    $env:C04A_EXPECTED_DATABASE = $receipt.restoredDatabase
    $env:C04A_EXPECTED_DATABASE_GUID = $receipt.restoredServiceBrokerGuid
    $run = [Guid]::NewGuid()
    $before = Fence preflight $run 0 Uninitialized 0
    Check 'twenty-empty-tables' (@($before.sourceRows.PSObject.Properties).Count -eq 20 -and
        @($before.sourceRows.PSObject.Properties | Where-Object Value -ne 0).Count -eq 0)
    Check 'preflight-created-no-control-schema' ((Query "SELECT COUNT(*) FROM sys.schemas WHERE name=N'crm_source_control';" -Scalar) -eq 0)

    Query @'
CREATE SCHEMA crm_source_rehearsal AUTHORIZATION dbo;
'@
    Query @'
CREATE TABLE crm_source_rehearsal.ErpSentinel (Id int NOT NULL PRIMARY KEY, Value int NOT NULL);
INSERT crm_source_rehearsal.ErpSentinel VALUES (1,10);
CREATE USER C04A_Rehearsal_Writer WITHOUT LOGIN;
GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::dbo TO C04A_Rehearsal_Writer;
GRANT SELECT,INSERT,UPDATE,DELETE ON SCHEMA::crm_source_rehearsal TO C04A_Rehearsal_Writer;
'@
    $frozen = Fence freeze $run 0 Frozen 1
    Check 'all-guard-inventory-verified' $frozen.guardInventoryVerified
    [void](Fence freeze $run 0 Frozen 1)
    Check 'freeze-replay-audit-once' ((Query 'SELECT COUNT(*) FROM crm_source_control.Audit;' -Scalar) -eq 1)
    $tables = @($receipt.restored.counts.table)
    foreach ($table in $tables) {
        if ($table -cnotmatch '^Crm_[A-Za-z]+$') { throw 'Unsafe table identifier in restore receipt.' }
        [void](Denied "UPDATE dbo.[$table] SET Id=Id WHERE 1=0;" @(51041))
        [void](Denied "DELETE dbo.[$table] WHERE 1=0;" @(51041))
        [void](Denied "INSERT dbo.[$table] (Id) SELECT Id FROM dbo.[$table] WHERE 1=0;" @(51041))
        foreach ($statement in @("UPDATE dbo.[$table] SET Id=Id WHERE 1=0;", "DELETE dbo.[$table] WHERE 1=0;",
            "INSERT dbo.[$table] (Id) SELECT Id FROM dbo.[$table] WHERE 1=0;")) {
            [void](Denied ("EXECUTE AS USER=N'C04A_Rehearsal_Writer'; " + $statement) @(229))
        }
        Check "protected-and-readable-$table" ((Query "SELECT COUNT_BIG(*) FROM dbo.[$table];" -Scalar) -eq 0)
    }
    Query "EXECUTE AS USER=N'C04A_Rehearsal_Writer'; UPDATE crm_source_rehearsal.ErpSentinel SET Value=11 WHERE Id=1;"
    Check 'unrelated-ERP-write-allowed' ((Query 'SELECT Value FROM crm_source_rehearsal.ErpSentinel WHERE Id=1;' -Scalar) -eq 11)
    [void](Denied 'SET XACT_ABORT ON; BEGIN TRAN; UPDATE crm_source_rehearsal.ErpSentinel SET Value=99 WHERE Id=1; DELETE dbo.Crm_Account WHERE 1=0; COMMIT;' @(51041))
    Check 'mixed-ERP-CRM-transaction-rolled-back' ((Query 'SELECT Value FROM crm_source_rehearsal.ErpSentinel WHERE Id=1;' -Scalar) -eq 11)
    [void](Denied 'UPDATE dbo.Crm_Account SET Name=Name OUTPUT inserted.Id WHERE 1=0;' @(334))
    Check 'legacy-OUTPUT-blocked-while-frozen' $true

    [void](Fence reopen $run 1 Reopened 2)
    Query 'UPDATE dbo.Crm_Account SET Name=Name OUTPUT inserted.Id WHERE 1=0;'
    Query @'
EXECUTE AS USER=N'C04A_Rehearsal_Writer';
BEGIN TRAN;
INSERT dbo.Crm_Account (Id,Name,NormalizedName,CreateDate,TenantId,IsDeleted)
VALUES (NEWID(),N'rehearsal-only',N'rehearsal-only',SYSUTCDATETIME(),NEWID(),0);
ROLLBACK;
'@
    Check 'reopen-restored-writes-and-OUTPUT-without-retaining-fixture-data' ((Query 'SELECT COUNT_BIG(*) FROM dbo.Crm_Account;' -Scalar) -eq 0)
    $secondRun = [Guid]::NewGuid()
    [void](Fence freeze $secondRun 2 Frozen 3)
    [void](Fence seal-forward-only $secondRun 3 ForwardOnly 4)
    $env:C04A_RUN_ID = $secondRun.ToString()
    $env:C04A_EXPECTED_GENERATION = '4'
    & dotnet $assembly reopen 2> (Join-Path $output 'reopen-after-seal.json')
    if ($LASTEXITCODE -eq 0) { throw 'ForwardOnly was reopened.' }
    $refusal = Get-Content (Join-Path $output 'reopen-after-seal.json') -Raw | ConvertFrom-Json
    Check 'forward-only-refuses-reopen' ($refusal.error -eq 'C04A_FORWARD_ONLY')
    [void](Fence status $secondRun 4 ForwardOnly 4)
    Check 'four-transitions-audited' ((Query 'SELECT COUNT(*) FROM crm_source_control.Audit;' -Scalar) -eq 4)
    [void](Denied 'DELETE dbo.Crm_Account WHERE 1=0;' @(51041))
    Check 'source-stays-frozen-after-rejected-reopen' $true
    $report.status = 'Passed'
    $report.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Save-Report
    Write-Output "Restored-source acceptance passed: $($checks.Count) checks. The isolated copy remains ForwardOnly."
} catch {
    $report.status = 'Failed'
    $report.failureType = $_.Exception.GetType().Name
    $report.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    Save-Report
    throw 'Restored-source acceptance failed; completed checks and original tool results were retained.'
} finally {
    foreach ($key in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($key, $savedEnvironment[$key]) }
}
