param(
 [Parameter(Mandatory)][string]$Worktree,
 [Parameter(Mandatory)][string]$Task,
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Label,
 [Parameter(Mandatory)][string]$Filter,
 [Parameter(Mandatory)][ValidateRange(1,1000)][int]$ExpectedCases
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$Worktree=[IO.Path]::GetFullPath($Worktree)
if(-not $Worktree.StartsWith('D:\CP6\tmp\worktrees\',[StringComparison]::OrdinalIgnoreCase)){throw 'Explicit isolated task worktree required.'}
$outputRoot=Join-Path 'D:\CP6\tmp\local-required-tests' $Label
$reportPath='D:\CP6\tmp\'+$Label+'.json'
if((Test-Path -LiteralPath $outputRoot) -or (Test-Path -LiteralPath $reportPath)){throw 'Preserve prior results.'}
$null=New-Item -ItemType Directory -Path $outputRoot
$logPath=Join-Path $outputRoot 'process.log'
$paths=@()
foreach($relative in @('CP6.Core/Persistence','CP6.Space.Infrastructure','CP6.Space.IntegrationTests','CP6.Persistence.PostgreSql')){
 $paths+=Get-ChildItem -LiteralPath (Join-Path $Worktree $relative) -Recurse -File|Where-Object{$_.FullName -notmatch '[\\/](bin|obj)[\\/]' -and ($_.Extension -in @('.cs','.csproj','.sql') -or $_.Name -eq 'packages.lock.json')}
}
$sources=@($paths|Sort-Object FullName -Unique|ForEach-Object{[ordered]@{Path=[IO.Path]::GetRelativePath($Worktree,$_.FullName).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$binaries=@(Get-ChildItem -LiteralPath (Join-Path $Worktree 'CP6.Space.IntegrationTests/bin/Debug/net8.0') -Filter 'CP6*.dll' -File|Sort-Object Name|ForEach-Object{[ordered]@{Name=$_.Name;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}})
$names=@('CP6_BUG150_TEST_PROVIDER','CP6_BUG150_TEST_CONNECTION','CP6_SPACE_MIGRATION_TEST_PROVIDER','CP6_SPACE_MIGRATION_TEST_CONNECTION','CP6_SPACE_TEST_PROVIDER','CP6_SPACE_TEST_CONNECTION','CP6_WP5_CORE_TEST_PROVIDER','CP6_WP5_CORE_TEST_CONNECTION','CP6_CORE_TEST_PROVIDER','CP6_CORE_TEST_CONNECTION','CP6_TEST_DATABASE_OWNER','CP6_TEST_SQLSERVER')
$saved=@{}
foreach($name in $names){$saved[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
$started=[DateTime]::UtcNow
Push-Location -LiteralPath $Worktree
try{
 foreach($name in $names){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
 & dotnet test CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj --no-build --no-restore --filter $Filter --logger 'trx;LogFileName=results.trx' --results-directory $outputRoot *> $logPath
 $processExit=$LASTEXITCODE
}finally{
 Pop-Location
 foreach($name in $saved.Keys){if($null -eq $saved[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$saved[$name],'Process')}}
}
$counts=$null
$trxPath=Join-Path $outputRoot 'results.trx'
if(Test-Path -LiteralPath $trxPath){
 [xml]$trx=Get-Content -LiteralPath $trxPath -Raw
 $results=@($trx.TestRun.Results.UnitTestResult|Where-Object{$null -ne $_})
 $counts=[ordered]@{Total=$results.Count;Passed=@($results|Where-Object outcome -eq 'Passed').Count;Failed=@($results|Where-Object outcome -eq 'Failed').Count;Other=@($results|Where-Object outcome -notin @('Passed','Failed')).Count;Expected=$ExpectedCases}
}
$valid=$null -ne $counts -and $counts.Total -eq $ExpectedCases -and $counts.Passed -eq $ExpectedCases -and $counts.Other -eq 0
$exitCode=if($processExit -eq 0 -and $valid){0}else{1}
$report=[ordered]@{Task=$Task;Suite='Selected local non-native cases';Label=$Label;StartedUtc=$started.ToString('o');FinishedUtc=[DateTime]::UtcNow.ToString('o');ExitCode=$exitCode;ProcessExitCode=$processExit;Selection=$Filter;Counts=$counts;RequiredCaseCountValidated=$valid;SourcesBeforeRun=$sources;BinariesBeforeRun=$binaries;ProcessLog=$logPath;ProcessLogSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;RunnerSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;Scope='Only selected cases with native test environment cleared. No native database or complete task acceptance inferred.'}
$report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
[pscustomobject]$report|Select-Object Label,ExitCode,ProcessExitCode,Counts|ConvertTo-Json -Depth 5
exit $exitCode
