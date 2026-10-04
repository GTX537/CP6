$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\bug-161-cad-provider-recovery'
$output='D:\CP6\tmp\bug161-postmerge-verification.json'
if(Test-Path -LiteralPath $output){throw 'Preserve post-merge verification'}
$merged=Get-Content -LiteralPath D:\CP6\tmp\bug161-remote-main-verification.json -Raw|ConvertFrom-Json
if($merged.PR -ne 162 -or !$merged.CandidateAncestor -or !$merged.FullTreesEqual -or
   $merged.RemoteMain -cne 'ce49012d37da4b1b01f074d5b5b7fd227780e656'){throw 'Exact merged candidate required'}
$expected=@(
    'CP6.Space.IntegrationTests.SpaceCadProviderSqlServerTests.Concurrent_replace_preserves_one_current_revision_and_immutable_evidence',
    'CP6.Space.IntegrationTests.SpaceCadProviderSqlServerTests.Replace_waiter_recovers_native_serialization_conflict',
    'CP6.Space.IntegrationTests.SpaceCadProviderSqlServerTests.Replace_waiter_replays_same_key_after_native_serialization_conflict'
)|Sort-Object
$records=@(foreach($provider in @('PostgreSql','SqlServer')){
    $label=if($provider -ceq 'PostgreSql'){'bug161-pg-postmerge-smoke'}else{'bug161-sql-postmerge-smoke'}
    $run=Join-Path 'D:\CP6\tmp' $label
    $resultPath=Join-Path $run 'result.json'
    $r=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
    if(!$r.Success -or $r.Provider -cne $provider -or $r.ExecutionHead -cne $merged.RemoteMain -or
       $r.Process.ExitCode -ne 0 -or $r.Process.TimedOut -or $r.Process.OwnedProcessTerminated -or
       $r.Counts.Total -ne 3 -or $r.Counts.Passed -ne 3 -or $r.Counts.Other -ne 0 -or
       !$r.SourcesUnchanged -or !$r.RuntimeUnchanged -or
       $r.SourceInputsSha256 -cne '3D8A0E51E1C015F6F10830A9B75D68C34390876DAAD07965ADE160EE391BFE60' -or
       $r.RuntimeManifestSha256 -cne '455ACB762F61A88D7F7DA7ED17B7558FC3EA624BFE81B3D440DB290991AFDB18'){throw 'Exact current smoke required'}
    $trxPath=Join-Path $run 'private/trx/results.trx'
    if((Get-FileHash -LiteralPath $trxPath).Hash -cne $r.TrxSha256){throw 'Smoke TRX hash differs'}
    [xml]$trx=[IO.File]::ReadAllText($trxPath)
    $cases=@($trx.TestRun.Results.UnitTestResult)
    if($cases.Count -ne 3 -or @($cases|Where-Object outcome -CNE 'Passed').Count -or
       (@($cases.testName|Sort-Object) -join "`n") -cne ($expected -join "`n")){throw 'Smoke raw case set differs'}
    if($provider -ceq 'PostgreSql' -and @($r.NativeEvidence|Where-Object {$_ -match 'SQLSTATE=40001'}).Count -lt 2){throw 'Actual two native 40001 recoveries required'}
    $sourcePath=Join-Path $run 'source-inputs.json'
    if((Get-FileHash -LiteralPath $sourcePath).Hash -cne $r.SourceInputsSha256){throw 'Smoke source manifest differs'}
    $source=@(Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json)
    foreach($file in $source){if((Get-FileHash -LiteralPath (Join-Path $worktree $file.Path)).Hash -cne $file.Sha256){throw 'Current smoke source differs'}}
    $before=Join-Path $run 'runtime-artifact-before.json';$after=Join-Path $run 'runtime-artifact-after.json'
    if((Get-FileHash -LiteralPath $before).Hash -cne $r.RuntimeManifestSha256 -or
       (Get-FileHash -LiteralPath $after).Hash -cne $r.RuntimeManifestSha256){throw 'Smoke runtime manifests differ'}
    $runtime=@(Get-Content -LiteralPath $before -Raw|ConvertFrom-Json)
    $runtimeRoot=Join-Path $worktree 'CP6.Space.IntegrationTests/bin/Debug/net8.0'
    if(@(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse).Count -ne $runtime.Count){throw 'Current runtime file set differs'}
    foreach($file in $runtime){$p=Join-Path $runtimeRoot $file.Path;if((Get-FileHash -LiteralPath $p).Hash -cne $file.Sha256 -or (Get-Item -LiteralPath $p).Length -ne $file.Length){throw 'Current runtime bytes differ'}}
    [pscustomobject]@{Provider=$provider;Label=$label;ExecutionHead=$r.ExecutionHead;Passed=3;Other=0;TrxSha256=$r.TrxSha256;
       SourceInputsSha256=$r.SourceInputsSha256;RuntimeManifestSha256=$r.RuntimeManifestSha256;ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash;
       ExactNames=$expected;SourceCount=$source.Count;RuntimeCount=$runtime.Count;ActualNativeEvidence=$r.NativeEvidence}
})
$report=[ordered]@{Task='BUG-161';PR=162;RemoteMain=$merged.RemoteMain;Candidate=$merged.Candidate;Status='RemoteMergedPostSmokePassedCleanupPending';
    Runs=$records;CheckedUtc=[datetime]::UtcNow.ToString('o');Scope='Fresh two-provider original-step smoke on merged main using verified unchanged actual code/runtime; earlier 25-case results are reused as their historical executions, not rerun. Full WP6 and production remain pending.'}
[IO.File]::WriteAllText($output,($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Status=$report.Status;BothProviderSmokePassed=3;SourceAndRuntimeMatch=$true}|ConvertTo-Json -Compress
