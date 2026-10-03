param(
    [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][ValidateRange(1,1000)][int]$ExpectedCases
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\db-compat-wp5-20261003'
$receiptPath='D:\CP6\tmp\db-compat.wp5-reports-owned.json'
$receipt=Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.Task -ne 'DB-COMPAT-01-WP5' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$') { throw 'WP5 owner receipt required.' }
$databaseName='CP6Compat_WP5_20261003_'+$receipt.Owner.Substring(0,8)
if ($receipt.SqlServerDatabase -ne $databaseName -or $receipt.PostgreSqlDatabase -ne $databaseName -or $receipt.SqlServerState -ne 'CreatedAndOwnerMarked' -or $receipt.PostgreSqlState -ne 'CreatedAndOwnerMarked') { throw 'Both owned WP5 targets must match receipt.' }
$outputRoot=Join-Path 'D:\CP6\tmp\db-compat-wp5-tests' $Label
$reportPath='D:\CP6\tmp\'+$Label+'.json'
if ((Test-Path -LiteralPath $outputRoot) -or (Test-Path -LiteralPath $reportPath)) { throw 'Preserve earlier results; choose a fresh label.' }
$null=New-Item -ItemType Directory -Path $outputRoot
$logPath=Join-Path $outputRoot 'process.log'
$sourceRoots=@('CP6.Tests/DatabaseCompatibility','CP6.Core/Services/Mes','CP6.Core/Services/Platform')
$sourcePaths=@('CP6.Core/EFDbContext/CP6Context.cs','CP6.Core/Persistence/DatabaseMigrationProfile.cs','CP6.Core/Persistence/DatabaseContextOptions.cs','CP6.WebApi/Controllers/Sys/DashboardController.cs','CP6.Core/Utilities/CacheService.cs','CP6.Tests/CP6.Tests.csproj','CP6.Tests/packages.lock.json')
foreach ($relativeRoot in $sourceRoots) {
    $folder=Join-Path $worktree $relativeRoot
    $sourcePaths+=@(Get-ChildItem -LiteralPath $folder -Recurse -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and ($_.Extension -in @('.cs','.csproj') -or $_.Name -eq 'packages.lock.json') } | ForEach-Object { [IO.Path]::GetRelativePath($worktree,$_.FullName).Replace('\','/') })
}
$sourceHashes=@($sourcePaths | Sort-Object -Unique | ForEach-Object { $path=Join-Path $worktree $_; if (Test-Path -LiteralPath $path) { [ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash} } })
$binaryBase=Join-Path $worktree 'CP6.Tests/bin/Debug/net8.0'
$binaryHashes=@(Get-ChildItem -LiteralPath $binaryBase -Filter 'CP6*.dll' -File | Sort-Object Name | ForEach-Object { [ordered]@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} })
if ($binaryHashes.Count -lt 5) { throw 'Expected compiled report dependencies.' }
$saved=@{}
$names=@('CP6_WP5_CORE_TEST_PROVIDER','CP6_WP5_CORE_TEST_CONNECTION','CP6_TEST_DATABASE_OWNER','CP6_TEST_SQLSERVER')
foreach ($name in $names) { $saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process') }
$started=[DateTime]::UtcNow
$processExit=$null
Push-Location -LiteralPath $worktree
try {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') }
    $env:CP6_WP5_CORE_TEST_PROVIDER=$Provider
    $env:CP6_WP5_CORE_TEST_CONNECTION=if ($Provider -eq 'PostgreSql') { $receipt.PostgreSqlConnection } else { $receipt.SqlServerConnection }
    $env:CP6_TEST_DATABASE_OWNER=$receipt.Owner
    & dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
    $processExit=$LASTEXITCODE
} finally {
    Pop-Location
    foreach ($name in $saved.Keys) { if ($null -eq $saved[$name]) { [Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process') } else { [Environment]::SetEnvironmentVariable($name,$saved[$name],'Process') } }
}
$counts=$null
$results=@()
$trxPath=Join-Path $outputRoot 'results.trx'
if (Test-Path -LiteralPath $trxPath) {
    [xml]$trx=Get-Content -LiteralPath $trxPath -Raw
    $results=@($trx.TestRun.Results.UnitTestResult | Where-Object { $null -ne $_ })
    $counts=[ordered]@{Total=$results.Count;Passed=@($results | Where-Object outcome -eq 'Passed').Count;Failed=@($results | Where-Object outcome -eq 'Failed').Count;Other=@($results | Where-Object outcome -notin @('Passed','Failed')).Count;Expected=$ExpectedCases}
}
$valid=$null -ne $counts -and $counts.Total -eq $ExpectedCases -and $counts.Passed -eq $ExpectedCases -and $counts.Failed -eq 0 -and $counts.Other -eq 0
$exitCode=if ($processExit -eq 0 -and $valid) { 0 } else { 1 }
$report=[ordered]@{
    Task='DB-COMPAT-01-WP5'; Suite='ReportsAndGdpr'; Provider=$Provider; Label=$Label
    StartedUtc=$started.ToString('o'); FinishedUtc=[DateTime]::UtcNow.ToString('o'); ExitCode=$exitCode; ProcessExitCode=$processExit
    Selection=$Filter; Counts=$counts; RequiredCaseCountValidated=$valid; DatabaseName=$databaseName
    ReceiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash; RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    SourcesBeforeRun=$sourceHashes; BinariesBeforeRun=$binaryHashes; ProcessLog=$logPath; ProcessLogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
    Scope='Actual selected report/GDPR tests on owned native database; fixture validates owner and real migration profiles. No full WP5 acceptance inferred.'
}
$report | ConvertTo-Json -Depth 9 | Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
[pscustomobject]$report | Select-Object Provider,Label,ExitCode,ProcessExitCode,Counts | ConvertTo-Json -Depth 5
exit $exitCode

