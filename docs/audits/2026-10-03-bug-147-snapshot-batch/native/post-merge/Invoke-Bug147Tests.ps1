param(
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
 [string]$Filter='FullyQualifiedName~IdentitySnapshotBatchSqlServerTests'
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\bug-147-snapshot-batch-collation'
$outputRoot=Join-Path 'D:\CP6\tmp\bug147-tests' $Label
$reportPath='D:\CP6\tmp\'+$Label+'.json'
if((Test-Path -LiteralPath $outputRoot) -or (Test-Path -LiteralPath $reportPath)){throw 'Use a fresh evidence label.'}
$null=New-Item -ItemType Directory -Path $outputRoot
$logPath=Join-Path $outputRoot 'process.log'
$original=[Environment]::GetEnvironmentVariable('CP6_TEST_SQLSERVER','Process')
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.wp4-owned.json' -Raw|ConvertFrom-Json
if($receipt.Task -ne 'DB-COMPAT-01-WP4'){throw 'Local test connection source required.'}
$sources=@('CP6.Core/EFDbContext/CP6Context.cs','CP6.Core/Persistence/SqlServerIdentitySnapshotUpdateSqlGenerator.cs','CP6.Tests/Persistence/IdentitySnapshotBatchSqlServerTests.cs')|ForEach-Object {$path=Join-Path $worktree $_;if(Test-Path -LiteralPath $path){[ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}}}
$binaries=@('CP6.Core.dll','CP6.Tests.dll','CP6.WebApi.dll')|ForEach-Object {$path=Join-Path (Join-Path $worktree 'CP6.Tests/bin/Debug/net8.0') $_;[ordered]@{Name=$_;Sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}}
$started=[DateTime]::UtcNow
Push-Location -LiteralPath $worktree
try{
 $env:CP6_TEST_SQLSERVER=$receipt.SqlServerConnection
 & dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
 $code=$LASTEXITCODE
}finally{
 Pop-Location
 if($null -eq $original){[Environment]::SetEnvironmentVariable('CP6_TEST_SQLSERVER',[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable('CP6_TEST_SQLSERVER',$original,'Process')}
}
$counts=$null
$trx=Join-Path $outputRoot 'results.trx'
if(Test-Path -LiteralPath $trx){[xml]$xml=Get-Content -LiteralPath $trx -Raw;$counts=$xml.TestRun.ResultSummary.Counters|Select-Object total,executed,passed,failed,notExecuted}
$report=[ordered]@{Task='BUG147';Label=$Label;Provider='SqlServer';StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');ExitCode=$code;Filter=$Filter;Counts=$counts;SourcesBeforeRun=@($sources);BinariesBeforeRun=@($binaries);RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;ProcessLogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;Scope='Actual SQL Server regression in fixture-created isolated databases; no production or remote Actions.'}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
[pscustomobject]$report|Select-Object Label,Provider,ExitCode,Counts|ConvertTo-Json -Depth 5
exit $code
