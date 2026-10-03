param(
    [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
    [Parameter(Mandatory)][ValidateSet('space-then-core','core-then-space','space-script')][string]$Purpose,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][ValidateRange(1,10)][int]$ExpectedCases
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\db-compat-wp5-20261003'
$receiptPath='D:\CP6\tmp\db-compat.wp5-prerequisite-owned.json'
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.Task -ne 'DB-COMPAT-01-WP5') { throw 'WP5 migration ownership receipt required.' }
$selectedEntries=@($receipt.Databases | Where-Object { $_.Provider -eq $Provider -and $_.Purpose -eq $Purpose })
if ($selectedEntries.Count -ne 1) { throw 'Select exactly one owned migration target.' }
$entry=$selectedEntries[0]
if ($entry.Owner -notmatch '^[a-f0-9]{32}$' -or $entry.State -ne 'CreatedAndOwnerMarked') { throw 'Owned migration target must be marked.' }
$databaseName='CP6Compat_WP5_20261003_'+$entry.Owner.Substring(0,8)
if ($entry.DatabaseName -ne $databaseName) { throw 'Owned migration target name does not match receipt.' }
$outputRoot=Join-Path 'D:\CP6\tmp\db-compat-wp5-tests' $Label
$reportPath='D:\CP6\tmp\'+$Label+'.json'
if ((Test-Path -LiteralPath $outputRoot) -or (Test-Path -LiteralPath $reportPath)) { throw 'Preserve earlier results; choose a fresh label.' }
$null=New-Item -ItemType Directory -Path $outputRoot
$logPath=Join-Path $outputRoot 'process.log'
$sourcePaths=@('CP6.Core/EFDbContext/CP6Context.cs','CP6.Core/Persistence/DatabaseMigrationProfile.cs','CP6.Core/Persistence/DatabaseContextOptions.cs','CP6.Core/Persistence/DatabaseFailureClassifier.cs','CP6.Core/Persistence/PostgreSqlMigrationPrerequisitesV1.cs','CP6.Core/Persistence/PostgreSqlPrerequisiteMigrationsSqlGenerator.cs')
foreach ($relativeRoot in @('CP6.Space.Infrastructure','CP6.Space.Application','CP6.Space.Domain','CP6.Space.IntegrationTests','CP6.Persistence.PostgreSql')) {
    $sourcePaths+=@(Get-ChildItem -LiteralPath (Join-Path $worktree $relativeRoot) -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and ($_.Extension -in @('.cs','.csproj','.sql') -or $_.Name -eq 'packages.lock.json') } | ForEach-Object { [IO.Path]::GetRelativePath($worktree,$_.FullName).Replace('\','/') })
}
$sourceHashes=@($sourcePaths | Sort-Object -Unique | ForEach-Object { $path=Join-Path $worktree $_; [ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} })
$binaryBase=Join-Path $worktree 'CP6.Space.IntegrationTests/bin/Debug/net8.0'
$binaryHashes=@(Get-ChildItem -LiteralPath $binaryBase -Filter 'CP6*.dll' -File | Sort-Object Name | ForEach-Object { [ordered]@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
if ($binaryHashes.Count -lt 5) { throw 'Expected compiled Space dependencies.' }
$names=@('CP6_SPACE_MIGRATION_TEST_PROVIDER','CP6_SPACE_MIGRATION_TEST_CONNECTION','CP6_SPACE_TEST_PROVIDER','CP6_SPACE_TEST_CONNECTION','CP6_WP5_CORE_TEST_PROVIDER','CP6_WP5_CORE_TEST_CONNECTION','CP6_CORE_TEST_PROVIDER','CP6_CORE_TEST_CONNECTION','CP6_TEST_DATABASE_OWNER','CP6_TEST_SQLSERVER')
$saved=@{}
foreach ($name in $names) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
$started=[DateTime]::UtcNow
$processExit=$null
Push-Location -LiteralPath $worktree
try {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') }
    $env:CP6_SPACE_MIGRATION_TEST_PROVIDER=$Provider
    $env:CP6_SPACE_MIGRATION_TEST_CONNECTION=$entry.ConnectionString
    $env:CP6_TEST_DATABASE_OWNER=$entry.Owner
    & dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
    $processExit=$LASTEXITCODE
} finally {
    Pop-Location
    foreach ($name in $saved.Keys) { if ($null -eq $saved[$name]) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') } else { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
}
$counts=$null
$trxPath=Join-Path $outputRoot 'results.trx'
if (Test-Path -LiteralPath $trxPath) {
    [xml]$trx=Get-Content -LiteralPath $trxPath -Raw
    $results=@($trx.TestRun.Results.UnitTestResult | Where-Object { $null -ne $_ })
    $counts=[ordered]@{Total=$results.Count;Passed=@($results | Where-Object outcome -eq 'Passed').Count;Failed=@($results | Where-Object outcome -eq 'Failed').Count;Other=@($results | Where-Object outcome -notin @('Passed','Failed')).Count;Expected=$ExpectedCases}
}
$valid=$null -ne $counts -and $counts.Total -eq $ExpectedCases -and $counts.Passed -eq $ExpectedCases -and $counts.Failed -eq 0 -and $counts.Other -eq 0
$exitCode=if ($processExit -eq 0 -and $valid) { 0 } else { 1 }
$report=[ordered]@{
    Task='DB-COMPAT-01-WP5'; Suite='SpaceMigration'; Provider=$Provider; Purpose=$Purpose; Label=$Label
    StartedUtc=$started.ToString('o'); FinishedUtc=[DateTime]::UtcNow.ToString('o'); ExitCode=$exitCode; ProcessExitCode=$processExit
    Selection=$Filter; Counts=$counts; RequiredCaseCountValidated=$valid; DatabaseName=$databaseName
    ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash; RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    SourcesBeforeRun=$sourceHashes; BinariesBeforeRun=$binaryHashes; ProcessLog=$logPath; ProcessLogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
    Scope='Actual selected migration/schema tests on exact owned database; no downgrade, database replacement, or full WP5 acceptance inferred.'
}
$report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
[pscustomobject]$report | Select-Object Provider,Purpose,Label,ExitCode,ProcessExitCode,Counts | ConvertTo-Json -Depth 5
exit $exitCode
