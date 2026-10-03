$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$worktree='D:\CP6\tmp\worktrees\db-compat-wp4-20261003'
$label='wp4-wms-unselected-core-owner-discovery'
$directory=Join-Path 'D:\CP6\tmp\db-compat-wp4-tests' $label
$reportPath=Join-Path 'D:\CP6\tmp' ($label+'.json')
if((Test-Path -LiteralPath $directory) -or (Test-Path -LiteralPath $reportPath)){throw 'Preserve prior discovery evidence.'}
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.wp4-business-owned.json' -Raw|ConvertFrom-Json
if($receipt.Task -ne 'DB-COMPAT-01-WP4' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$'){throw 'Known WP4 ownership required.'}
$names=@('CP6_WMS_TEST_PROVIDER','CP6_WMS_TEST_CONNECTION','CP6_TEST_SQLSERVER','CP6_TEST_DATABASE_OWNER','CP6_CORE_TEST_PROVIDER','CP6_CORE_TEST_CONNECTION')
$saved=@{}
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$null=New-Item -ItemType Directory -Path $directory
$start=[DateTime]::UtcNow
$source=@('CP6.Tests/Infra/WmsProductionFactAttribute.cs','CP6.Tests/WmsProductionSqlServerTests.cs')|ForEach-Object {[ordered]@{Path=$_;Sha256=(Get-FileHash -LiteralPath (Join-Path $worktree $_) -Algorithm SHA256).Hash}}
$binaryHash=(Get-FileHash -LiteralPath (Join-Path $worktree 'CP6.Tests/bin/Debug/net8.0/CP6.Tests.dll') -Algorithm SHA256).Hash
Push-Location -LiteralPath $worktree
try{
 foreach($name in $names){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 $env:CP6_TEST_DATABASE_OWNER=$receipt.Owner
 $env:CP6_CORE_TEST_PROVIDER='PostgreSql'
 $env:CP6_CORE_TEST_CONNECTION=$receipt.PostgreSqlConnection
 & dotnet test CP6.Tests/CP6.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~CP6.Tests.WmsProductionSqlServerTests' --logger 'trx;LogFileName=results.trx' --results-directory $directory *> (Join-Path $directory 'process.log')
 $exitCode=$LASTEXITCODE
}finally{
 Pop-Location
 foreach($name in $saved.Keys){[Environment]::SetEnvironmentVariable($name,$(if($null -eq $saved[$name]){[NullString]::Value}else{$saved[$name]}),'Process')}
}
[xml]$trx=Get-Content -LiteralPath (Join-Path $directory 'results.trx') -Raw
$counts=$trx.TestRun.ResultSummary.Counters|Select-Object total,executed,passed,failed,notExecuted
$okay=$exitCode -eq 0 -and [int]$counts.total -eq 8 -and [int]$counts.executed -eq 0 -and [int]$counts.failed -eq 0 -and [int]$counts.notExecuted -eq 8
$report=[ordered]@{Task='DB-COMPAT-01-WP4';Scope='WMS suite selection only; NOT native WMS acceptance';StartedUtc=$start.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');CoreInputsAndSharedOwnerPresent=$true;WmsInputsAndLegacySqlAbsent=$true;ExpectedUnselectedSkips=8;Passed=$okay;ProcessExitCode=$exitCode;Counts=$counts;SourcesBeforeRun=$source;TestAssemblySha256=$binaryHash;RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;NativeAcceptanceReusedFrom=@('wp4-wms-pg-first-run.json','wp4-wms-sql-first-run.json')}
$report|ConvertTo-Json -Depth 6|Set-Content -LiteralPath $reportPath -Encoding utf8
[ordered]@{Label=$label;Scope=$report.Scope;Passed=$okay;Counts=$counts}|ConvertTo-Json -Depth 4
if(!$okay){exit 1}
