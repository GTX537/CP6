$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\bug-147-snapshot-batch-collation'
$candidate='8f42a76be7a0de70170e1209bc3e534f0cae5da9'
$expectedMain='974e57c0650279565330a67c844355ba3e1b563d'
$smoke=Get-Content -LiteralPath 'D:\CP6\tmp\bug147-post-merge-business-smoke.json' -Raw|ConvertFrom-Json
$gate=Get-Content -LiteralPath 'D:\CP6\tmp\bug147-final-related-and-native.json' -Raw|ConvertFrom-Json
if($smoke.ExitCode -ne 0 -or $smoke.Counts.passed -ne '1' -or $smoke.Counts.failed -ne '0' -or $smoke.Counts.notExecuted -ne '0'){throw 'Postmerge smoke failed.'}
foreach($entry in $smoke.SourcesBeforeRun){
 $prior=@($gate.SourcesBeforeRun|Where-Object Path -eq $entry.Path)
 if($prior.Count -ne 1 -or $prior[0].Sha256 -ne $entry.Sha256 -or (Get-FileHash -LiteralPath (Join-Path $worktree $entry.Path)).Hash -ne $entry.Sha256){throw 'Smoke source differs from final gate.'}
}
foreach($entry in $smoke.BinariesBeforeRun){
 $prior=@($gate.BinariesBeforeRun|Where-Object Name -eq $entry.Name)
 if($prior.Count -ne 1 -or $prior[0].Sha256 -ne $entry.Sha256){throw 'Smoke binary differs from final gate.'}
}
$remoteLine=& git -C $worktree ls-remote origin refs/heads/main
if($LASTEXITCODE -ne 0){throw 'Remote main check failed.'}
$remoteMain=($remoteLine -split '\s+')[0]
if($remoteMain -ne $expectedMain){throw 'Remote main changed; inspect latest integration.'}
& git -C $worktree merge-base --is-ancestor $candidate $remoteMain
if($LASTEXITCODE -ne 0){throw 'Candidate absent from main.'}
$candidateTree=(& git -C $worktree rev-parse ($candidate+'^{tree}')).Trim()
$mainTree=(& git -C $worktree rev-parse ($remoteMain+'^{tree}')).Trim()
if($candidateTree -ne $mainTree){throw 'Candidate/main trees differ.'}
$prText=& gh pr view 148 --repo GTX537/CP6 --json number,state,mergedAt,mergeCommit,headRefOid,url
if($LASTEXITCODE -ne 0){throw 'PR inspection failed.'}
$pr=$prText|ConvertFrom-Json
if($pr.state -ne 'MERGED' -or $pr.headRefOid -ne $candidate -or $pr.mergeCommit.oid -ne $remoteMain){throw 'PR source/merge mismatch.'}
$queueText=& gh run list --repo GTX537/CP6 --status queued --json databaseId,workflowName,headSha
if($LASTEXITCODE -ne 0){throw 'Queued run inspection failed.'}
$activeText=& gh run list --repo GTX537/CP6 --status in_progress --json databaseId,workflowName,headSha
if($LASTEXITCODE -ne 0){throw 'Active run inspection failed.'}
$runs=@($queueText|ConvertFrom-Json)+@($activeText|ConvertFrom-Json)
if($runs.Count -ne 0){throw 'Inspect active runs before recording delivery.'}
$cleanupText=& sqlcmd -S 'localhost\KOUSQLSERVER' -E -d master -b -h -1 -W -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM sys.databases WHERE name LIKE 'CP6Test[_]Bug147[_]%';"
if($LASTEXITCODE -ne 0){throw 'Read-only cleanup verification failed.'}
$remaining=[int](($cleanupText -join '').Trim())
if($remaining -ne 0){throw 'Owned BUG147 test database remains.'}
$rootMain=(& git -C D:\CP6 rev-parse HEAD).Trim()
$rootLockHash=(Get-FileHash -LiteralPath 'D:\CP6\CP6.Space.IntegrationTests\packages.lock.json').Hash
if($rootLockHash -ne '71D6B6B9EC4446E92D25842310633AA9E49E994EC6211A353B5140251B1870A5'){throw 'Root pre-existing lock changed.'}
[ordered]@{
 Task='BUG147';Status='DeliveredToRemoteMain';ObservedUtc=[DateTime]::UtcNow.ToString('o')
 PullRequest=$pr;RemoteMain=$remoteMain;ValidatedCandidate=$candidate;CandidateAndMainTree=$mainTree;AncestorConfirmed=$true
 PostMergeSmoke='bug147-post-merge-business-smoke.json';PostMergeSmokeSha256=(Get-FileHash -LiteralPath 'D:\CP6\tmp\bug147-post-merge-business-smoke.json').Hash
 PostMergeBusinessPassed=1;PostMergeFailed=0;PostMergeSkipped=0;RemainingOwnedDatabaseCount=$remaining
 PriorGate='bug147-final-related-and-native.json';PriorGatePassed=118;PriorGateReexecutedAfterMerge=$false
 Reuse='Final 118-case evidence reused with all three source and binary hashes unchanged. Only the actual five-snapshot/five-outbox business case rerun after merge.'
 RootMain=$rootMain;RootMainSync='Pre-existing dirty root preserved; WP4 worktree integrated verified remote main.';UnrelatedRootLockSha256=$rootLockHash
 FirstPush='HTTP 408; branch absent on remote. Normal retry used per-command HTTP/1.1; no global configuration or history change.'
 ParentIssue=134;ParentState='Open';Remaining='WP4, WP5, WP6'
 ActionsQueuedOrRunning=$runs;ActionsDispatched=0;ActionsCancelled=0;WorkflowTriggerChanges=0;ProtectionChanges=0;ProductionDeployments=0
}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath 'D:\CP6\tmp\bug147-delivery-closure.json' -Encoding utf8NoBOM
Write-Output 'BUG147 remote inclusion, equal tree, unchanged binaries, postmerge business smoke and cleanup verified.'
