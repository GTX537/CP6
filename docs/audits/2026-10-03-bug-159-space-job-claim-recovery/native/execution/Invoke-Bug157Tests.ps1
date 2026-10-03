[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
    [Parameter(Mandatory)][ValidatePattern('^bug157-[a-z0-9-]+$')][string]$Label,
    [Parameter(Mandatory)][string]$Filter,
    [Parameter(Mandatory)][int]$ExpectedCases,
    [switch]$Build
)
$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\bug-157-floor-initialization-retry'
$moduleRoot='D:\CP6\tmp\worktrees\db-compat-wp6-20261003\scripts\database-compatibility'
Import-Module (Join-Path $moduleRoot 'DatabaseCompatibilityEntries.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $moduleRoot 'DatabaseCompatibilityProcess.psm1') -Force
$run=Join-Path 'D:\CP6\tmp' $Label
if(Test-Path -LiteralPath $run){throw 'BUG157_NEW_RUN_REQUIRED'}
$null=[IO.Directory]::CreateDirectory($run)
$project='CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj'
$head=(& git -C $worktree rev-parse HEAD).Trim()
$inputs=@(Get-Cp6CompatibilitySourceInputs -Worktree $worktree -Projects @($project))
[IO.File]::WriteAllText((Join-Path $run 'source-inputs.json'),(ConvertTo-Json -InputObject $inputs -Depth 8),[Text.UTF8Encoding]::new($false))
if($Build){
    $buildResult=Invoke-Cp6CompatibilityBuild -Worktree $worktree -OutputDirectory (Join-Path $run 'build') -Projects @($project) -SourceSha $head
    if(!$buildResult.Success){throw 'BUG157_BUILD_FAILED'}
}
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\bug157-wp5-owned.private.json' -Raw|ConvertFrom-Json
if($receipt.Task -cne 'DB-COMPAT-01-WP5' -or $receipt.SourceBase -cne '2e1f90c629340944d95fadd7aee428f639304ed6' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$' -or $receipt.SqlServerDatabase -cne ('CP6Compat_WP5_20261003_'+$receipt.Owner.Substring(0,8)) -or $receipt.PostgreSqlDatabase -cne $receipt.SqlServerDatabase -or $receipt.SqlServerState -cne 'CreatedAndOwnerMarked' -or $receipt.PostgreSqlState -cne 'CreatedAndOwnerMarked'){throw 'BUG157_RECEIPT_REQUIRED'}
$connection=if($Provider -ceq 'PostgreSql'){$receipt.PostgreSqlConnection}else{$receipt.SqlServerConnection}
$environment=@{CP6_SPACE_TEST_PROVIDER=$Provider;CP6_SPACE_TEST_CONNECTION=$connection;CP6_TEST_DATABASE_OWNER=$receipt.Owner;DOTNET_NOLOGO='1'}
$binaryRoot=Join-Path $worktree 'CP6.Space.IntegrationTests\bin\Debug\net8.0'
$runtime=@(Get-ChildItem -LiteralPath $binaryRoot -File -Recurse|ForEach-Object{[pscustomobject]@{Path=[IO.Path]::GetRelativePath($binaryRoot,$_.FullName).Replace('\','/');Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}|Sort-Object Path)
[IO.File]::WriteAllText((Join-Path $run 'runtime-artifact-before.json'),(ConvertTo-Json -InputObject $runtime -Depth 5),[Text.UTF8Encoding]::new($false))
$process=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @('test',(Join-Path $worktree $project),'--no-build','--no-restore','--filter',$Filter,'--logger','trx;LogFileName=results.trx','--results-directory',(Join-Path $run 'private\trx')) -WorkingDirectory $worktree -LogDirectory (Join-Path $run 'private\test') -ChildEnvironment $environment -TimeoutSeconds 1200
$trxPath=Join-Path $run 'private\trx\results.trx'
$results=@()
if(Test-Path -LiteralPath $trxPath){$trx=[xml][IO.File]::ReadAllText($trxPath);$results=@($trx.TestRun.Results.UnitTestResult)}
$after=@(Get-ChildItem -LiteralPath $binaryRoot -File -Recurse|ForEach-Object{[pscustomobject]@{Path=[IO.Path]::GetRelativePath($binaryRoot,$_.FullName).Replace('\','/');Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}|Sort-Object Path)
[IO.File]::WriteAllText((Join-Path $run 'runtime-artifact-after.json'),(ConvertTo-Json -InputObject $after -Depth 5),[Text.UTF8Encoding]::new($false))
$finalInputs=@(Get-Cp6CompatibilitySourceInputs -Worktree $worktree -Projects @($project))
$runtimeSame=($runtime|ConvertTo-Json -Depth 5 -Compress) -ceq ($after|ConvertTo-Json -Depth 5 -Compress)
$sourceSame=($inputs|ConvertTo-Json -Depth 5 -Compress) -ceq ($finalInputs|ConvertTo-Json -Depth 5 -Compress)
$counts=[pscustomobject]@{Expected=$ExpectedCases;Total=$results.Count;Passed=@($results|Where-Object outcome -CEQ 'Passed').Count;Failed=@($results|Where-Object outcome -CEQ 'Failed').Count;Other=@($results|Where-Object outcome -CNotIn @('Passed','Failed')).Count}
$success=$process.ExitCode -eq 0 -and !$process.TimedOut -and !$process.OwnedProcessTerminated -and $counts.Total -eq $ExpectedCases -and $counts.Passed -eq $ExpectedCases -and $counts.Other -eq 0 -and $runtimeSame -and $sourceSame
$report=[pscustomobject]@{Task='BUG-157';Provider=$Provider;ExecutionHead=$head;WorkingTreeDirty=(@(& git -C $worktree status --porcelain).Count -gt 0);DatabaseName=$receipt.PostgreSqlDatabase;OwnershipProtocol=$receipt.Task;BuildExecuted=[bool]$Build;Filter=$Filter;Counts=$counts;Process=$process;Success=$success;SourcesUnchanged=$sourceSame;RuntimeUnchanged=$runtimeSame;SourceInputsSha256=(Get-FileHash -LiteralPath (Join-Path $run 'source-inputs.json')).Hash;RuntimeManifestSha256=(Get-FileHash -LiteralPath (Join-Path $run 'runtime-artifact-before.json')).Hash;TrxSha256=$(if(Test-Path -LiteralPath $trxPath){(Get-FileHash -LiteralPath $trxPath).Hash});CaseNames=@($results.testName);NativeEvidence=@($results|ForEach-Object{if($_.Output -and $_.Output.StdOut){$_.Output.StdOut -split '\r?\n'|Where-Object{$_ -match 'BUG157 coordinated native failure|SQLSTATE=40001'}}});Scope='Selected actual native database tests; controlled unknown/cancellation/exhaustion injections are distinguished in source from the coordinated native serialization regression';FinishedUtc=[datetime]::UtcNow.ToString('o')}
[IO.File]::WriteAllText((Join-Path $run 'result.json'),($report|ConvertTo-Json -Depth 12),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Provider=$Provider;Label=$Label;Success=$success;Counts=$counts;ProcessExitCode=$process.ExitCode;SourceUnchanged=$sourceSame;RuntimeUnchanged=$runtimeSame}|ConvertTo-Json -Compress
if(!$success){exit 1}
