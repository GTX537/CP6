$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\bug-163-wms-local-transactions'
$output='D:\CP6\tmp\bug163-postmerge-verification.json'
if(Test-Path -LiteralPath $output){throw 'Preserve post-merge verification.'}
$merged=Get-Content -LiteralPath 'D:\CP6\tmp\bug163-remote-main-verification.json' -Raw|ConvertFrom-Json
if($merged.PR -ne 164 -or !$merged.CandidateAncestor -or !$merged.FullTreesEqual -or $merged.RemoteMain -cne '6322ac152cab7d462719169fb0301f49e9ee39ba'){throw 'Exact normal merge required.'}
$expected=@('Bug163_LpnCreate_UsesOneLocalTransactionAndReplaysOnce','Bug163_LabelClaim_UsesLocalTransactionAndReplaysOnce','Bug163_LabelComplete_UsesLocalTransactionAndReplaysOnce')|ForEach-Object{'CP6.Tests.WmsProductionSqlServerTests.'+$_}|Sort-Object
$records=@(foreach($provider in @('SqlServer','PostgreSql')){
    $label=if($provider -ceq 'SqlServer'){'bug163-sql-postmerge-smoke'}else{'bug163-pg-postmerge-smoke'}
    $run=Join-Path 'D:\CP6\tmp' $label
    $resultPath=Join-Path $run 'result.json'
    $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
    if(!$result.Success -or $result.Provider -cne $provider -or $result.ExecutionHead -cne $merged.RemoteMain -or $result.Process.ExitCode -ne 0 -or $result.Process.TimedOut -or $result.Process.OwnedProcessTerminated -or $result.Counts.Total -ne 3 -or $result.Counts.Passed -ne 3 -or $result.Counts.Other -ne 0 -or !$result.SourcesUnchanged -or !$result.RuntimeUnchanged -or $result.SourceInputsSha256 -cne '59146ED86062FFF6F5A89A09D28EFB25C3B94BF1AA73F87478B034CA5ABA8867' -or $result.RuntimeManifestSha256 -cne '4987C66727F92BDA846B7089FD28F2AFE1A2ED830D5A1C23E57D983166FB9D2E'){throw 'Exact current native smoke required.'}
    $trxPath=Join-Path $run 'private/trx/results.trx'
    if((Get-FileHash -LiteralPath $trxPath).Hash -cne $result.TrxSha256){throw 'Smoke raw TRX changed.'}
    [xml]$trx=Get-Content -LiteralPath $trxPath -Raw
    $cases=@($trx.TestRun.Results.UnitTestResult)
    if($cases.Count -ne 3 -or @($cases|Where-Object outcome -CNE 'Passed').Count -or (@($cases.testName|Sort-Object) -join "`n") -cne ($expected -join "`n")){throw 'Smoke exact raw case set mismatch.'}
    $sourcePath=Join-Path $run 'source-inputs.json'
    if((Get-FileHash -LiteralPath $sourcePath).Hash -cne $result.SourceInputsSha256){throw 'Source manifest changed.'}
    $source=@(Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json)
    foreach($file in $source){if((Get-FileHash -LiteralPath (Join-Path $worktree $file.Path)).Hash -cne $file.Sha256){throw 'Current smoke source differs.'}}
    $before=Join-Path $run 'runtime-artifact-before.json'; $after=Join-Path $run 'runtime-artifact-after.json'
    if((Get-FileHash -LiteralPath $before).Hash -cne $result.RuntimeManifestSha256 -or (Get-FileHash -LiteralPath $after).Hash -cne $result.RuntimeManifestSha256){throw 'Smoke runtime manifest changed.'}
    $runtime=@(Get-Content -LiteralPath $before -Raw|ConvertFrom-Json)
    $runtimeRoot=Join-Path $worktree 'CP6.Tests/bin/Debug/net8.0'
    if(@(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse).Count -ne $runtime.Count){throw 'Current runtime file set differs.'}
    foreach($file in $runtime){$path=Join-Path $runtimeRoot $file.Path; if((Get-FileHash -LiteralPath $path).Hash -cne $file.Sha256 -or (Get-Item -LiteralPath $path).Length -ne $file.Length){throw 'Current runtime bytes differ.'}}
    [pscustomobject]@{Provider=$provider;Label=$label;Passed=3;Other=0;ExecutionHead=$result.ExecutionHead;TrxSha256=$result.TrxSha256;SourceInputsSha256=$result.SourceInputsSha256;RuntimeManifestSha256=$result.RuntimeManifestSha256;ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash;SourceCount=$source.Count;RuntimeCount=$runtime.Count;ExactNames=$expected}
})
[pscustomobject]@{Task='BUG-163';PR=164;RemoteMain=$merged.RemoteMain;Candidate=$merged.Candidate;Status='RemoteMergedPostSmokePassedCleanupPending';Runs=$records;CheckedUtc=[datetime]::UtcNow.ToString('o');Scope='Actual three-case native smoke per provider on merged main, reusing byte-verified unchanged real build. Earlier25/25 is retained as its historical run, not rerun. Owned cleanup, full WP6 and production remain pending.'}|ConvertTo-Json -Depth 7|Set-Content -LiteralPath $output -Encoding utf8NoBOM
[pscustomobject]@{Status='RemoteMergedPostSmokePassedCleanupPending';ProviderCount=2;PassedPerProvider=3;CurrentSourceAndRuntimeMatch=$true}|ConvertTo-Json
