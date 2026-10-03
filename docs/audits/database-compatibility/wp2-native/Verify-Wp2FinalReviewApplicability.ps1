$ErrorActionPreference='Stop'
$taskRoot='D:\CP6\tmp\worktrees\db-compat-wp2-20261002'
$output='D:\CP6\tmp\wp2-final-review-source-applicability.json'
if(Test-Path -LiteralPath $output){throw 'Preserve existing proof.'}
$mapping=Get-Content -LiteralPath 'D:\CP6\tmp\wp2-review-mapping-migrations.json' -Raw|ConvertFrom-Json
$initialization=Get-Content -LiteralPath 'D:\CP6\tmp\wp2-review-initialization-probes.json' -Raw|ConvertFrom-Json
$delta=Get-Content -LiteralPath 'D:\CP6\tmp\wp2-review-quotation-and-final-forward-delta.json' -Raw|ConvertFrom-Json
$expected=[ordered]@{}
foreach($item in $mapping.sourceScope.files){$expected[$item.path]=[ordered]@{Sha256=$item.sha256;Source='Original mapping/migrations task review'}}
foreach($item in $initialization.OriginalTaskLevelReview.FileHashes){$expected[$item.Path]=[ordered]@{Sha256=$item.Sha256;Source='Original initialization/probe task review'}}
foreach($item in $initialization.TargetedFixRecheck.FileHashes){$expected[$item.Path]=[ordered]@{Sha256=$item.Sha256;Source='Targeted two P2 fix recheck'}}
foreach($item in $delta.Files){$expected[$item.Path]=[ordered]@{Sha256=$item.Sha256;Source='Targeted quotation/final-upgrade delta review'}}
$expected[$delta.TargetedReaderFix.Path]=[ordered]@{Sha256=$delta.TargetedReaderFix.CurrentSha256;Source='Targeted SQL-only two-line reader fix review'}
$records=@(foreach($path in $expected.Keys){
 $absolute=Join-Path $taskRoot $path
 $actual=if(Test-Path -LiteralPath $absolute){(Get-FileHash -LiteralPath $absolute -Algorithm SHA256).Hash}else{$null}
 [ordered]@{Path=$path;ExpectedSha256=$expected[$path].Sha256;ActualSha256=$actual;ReviewSource=$expected[$path].Source;ByteIdentical=($actual -ceq $expected[$path].Sha256);DocumentationOnly=($path -like 'docs/*' -or $path -like '*/README.md')}
})
$different=@($records|Where-Object{-not $_.ByteIdentical})
$blocking=@($different|Where-Object{-not $_.DocumentationOnly})
$proof=[ordered]@{Task='DB-COMPAT-01 WP2';ObservedUtc=[DateTime]::UtcNow.ToString('o');Method='Read-only byte comparison to existing original and targeted review inputs; this is not a new AI review, build, test or database execution.';MainBase=(& git -C $taskRoot rev-parse HEAD);CurrentSourceState='Uncommitted WP2 source with final evidence/documentation additions';UniqueRecordedFiles=$records.Count;IdenticalFiles=@($records|Where-Object ByteIdentical).Count;DocumentationDeltas=$different;UnreviewedSourceDeltas=$blocking;Records=$records;PriorReviewArchives=@('wp2-review-mapping-migrations.json','wp2-review-initialization-probes.json','wp2-review-quotation-and-final-forward-delta.json');SeparateMainBugSources='BUG139/141/143 were reviewed, tested and delivered independently; not attributed to the original WP2 reviews.';Passed=($blocking.Count -eq 0)}
$proof|ConvertTo-Json -Depth 15|Set-Content -LiteralPath $output -Encoding utf8NoBOM
[ordered]@{Passed=$proof.Passed;UniqueFiles=$records.Count;Identical=$proof.IdenticalFiles;DocumentationDeltas=$different;BlockingSourceDeltas=$blocking;Output=$output;Sha256=(Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash}|ConvertTo-Json -Depth 6
if($blocking.Count){throw 'Source changed after its applicable review.'}
