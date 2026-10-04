$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\db-compat-wp6-20261003'
$destination=Join-Path $worktree 'docs/audits/2026-10-03-bug-163-wms-local-transactions/native/post-merge'
if(Test-Path -LiteralPath $destination){throw 'Preserve original post-merge archive.'}
$files=[Collections.Generic.List[object]]::new()
foreach($name in @(
    'bug163-final-staging-proof.json',
    'bug163-review-documentation-followup.json',
    'bug163-premerge-protection-note.json',
    'wp6-sql-wms-cleanup-recovery.json',
    'Complete-Wp6WmsSqlCleanup.ps1',
    'bug163-pre-push-workflow-input-inspection.json',
    'bug163-pre-pr-workflow-input-inspection.json',
    'bug163-pre-merge-workflow-input-inspection.json',
    'bug163-pr-full-file-verification.json',
    'bug163-remote-main-verification.json',
    'bug163-postmerge-verification.json',
    'bug163-cleanup-owned-two.json',
    'bug163-closure.json',
    'bug163-closure-comment.md',
    'bug163-pr-body.md',
    'bug163-issue-before-closure.md',
    'bug163-issue-completed-checklist.md',
    'Remove-Bug163OwnedDatabases.ps1',
    'Invoke-Bug163Tests.ps1',
    'Test-Bug163PostMerge.ps1',
    'Stage-Bug163Delivery.ps1',
    'Archive-Bug163PostMerge.ps1'
)){
    $files.Add([pscustomobject]@{Source=(Join-Path 'D:\CP6\tmp' $name);Target=$name})
}
foreach($label in @('bug163-pg-postmerge-smoke','bug163-sql-postmerge-smoke')){
    foreach($name in @('result.json','runtime-artifact-before.json','runtime-artifact-after.json','source-inputs.json',
        'private/trx/results.trx','private/test/stdout.log','private/test/stderr.log')){
        $files.Add([pscustomobject]@{Source=(Join-Path ('D:\CP6\tmp\'+$label) $name);Target=($label+'/'+$name)})
    }
}
$builder=[Data.Common.DbConnectionStringBuilder]::new()
$builder.set_ConnectionString((Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw|ConvertFrom-Json).PostgreSql)
$privatePassword=[string]$builder['Password']
foreach($file in $files){
    if(!(Test-Path -LiteralPath $file.Source -PathType Leaf)){throw 'Required original missing.'}
    $content=[IO.File]::ReadAllText($file.Source)
    if(($privatePassword -and $content.Contains($privatePassword)) -or
        $content -match '(?im)(Password|Pwd)\s*=\s*[^;$\s"'']+;' -or
        $content -match '\beyJ[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\b'){
        throw 'Sensitive-looking value found; nothing copied.'
    }
}
$records=@(foreach($file in $files){
    $target=Join-Path $destination $file.Target
    $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($file.Source,$target,$false)
    $sha=(Get-FileHash -LiteralPath $file.Source).Hash
    if((Get-FileHash -LiteralPath $target).Hash -cne $sha){throw 'Original byte mismatch.'}
    [pscustomobject]@{Path=$file.Target;OriginalPath=$file.Source;Sha256=$sha;Length=(Get-Item -LiteralPath $target).Length}
})
$manifest=[ordered]@{Task='BUG-163';PullRequest='https://github.com/GTX537/CP6/pull/164';RemoteMain='6322ac152cab7d462719169fb0301f49e9ee39ba';
    ArchivedUtc=[datetime]::UtcNow.ToString('o');Originals=$records;
    Scope='Normal PR/main inclusion, complete122-file paginated remote blob comparison, same-input fresh two-provider post-merge 3/3 smoke, exact two BUG163 database ordinary cleanup plus original failed WP6 SQL database cleanup with native bounded-wait controls and Issue closure. Original111-file archive remains unchanged; no rerun of full 25-case groups, Actions or production acceptance is claimed.'}
[IO.File]::WriteAllText((Join-Path $destination 'manifest.json'),($manifest|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
[pscustomobject]@{Originals=$records.Count;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $destination 'manifest.json')).Hash}|ConvertTo-Json -Compress
