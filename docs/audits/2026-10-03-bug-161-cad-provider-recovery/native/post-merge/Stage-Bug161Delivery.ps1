$ErrorActionPreference = 'Stop'
$worktree = 'D:\CP6\tmp\worktrees\bug-161-cad-provider-recovery'
$base = '4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0'
$archive = 'docs/audits/2026-10-03-bug-161-cad-provider-recovery/native'
$manifest = Get-Content -LiteralPath (Join-Path $worktree ($archive + '/manifest.json')) -Raw | ConvertFrom-Json
$ordinary = @('.gitattributes','CP6.Space.Infrastructure/SpaceCadProviderCapabilityService.cs',
    'CP6.Space.IntegrationTests/SpaceCadProviderSqlServerTests.cs',
    'CP6.Space.IntegrationTests/SpaceCadProviderSqlServerTests.RecoveryTests.cs',
    'docs/audits/2026-10-03-bug-161-cad-provider-recovery/README.md',
    'docs/project-memory/PROJECT_STATE.md','docs/project-memory/05-Completed.md',
    'docs/project-memory/06-Todo.md','docs/project-memory/CHANGELOG-AI.md')
$evidence = @(@($manifest.Originals | ForEach-Object { $archive + '/' + $_.Path }) + @($archive + '/manifest.json'))
$expected = @($ordinary + $evidence | Sort-Object -Unique)
if ($manifest.Originals.Count -ne 71 -or $expected.Count -ne 81) { throw 'Unexpected task delivery file set.' }
$null = & git -C $worktree fetch origin main 2>&1
if ($LASTEXITCODE -ne 0 -or (& git -C $worktree rev-parse origin/main).Trim() -cne $base -or
    (& git -C $worktree rev-parse HEAD).Trim() -cne $base) { throw 'Fresh task baseline differs.' }
if (@(& git -C $worktree diff --cached --name-only).Count -ne 0) { throw 'Existing index changes need review.' }
foreach ($record in $manifest.Originals) {
    $target = Join-Path $worktree ($archive + '/' + $record.Path)
    if (!(Test-Path -LiteralPath $record.OriginalPath -PathType Leaf) -or
        (Get-FileHash -LiteralPath $record.OriginalPath).Hash -cne $record.Sha256 -or
        (Get-FileHash -LiteralPath $target).Hash -cne $record.Sha256 -or (Get-Item -LiteralPath $target).Length -ne $record.Length) {
        throw 'Archive or original byte proof changed.'
    }
}
& git -C $worktree diff --check
if ($LASTEXITCODE -ne 0) { throw 'Tracked diff check failed.' }
foreach ($path in $ordinary) {
    & git -C $worktree add -- $path
    if ($LASTEXITCODE -ne 0) { throw 'Explicit ordinary staging failed.' }
}
foreach ($path in $evidence) {
    if ([IO.Path]::GetExtension($path) -in @('.log','.trx')) { & git -C $worktree add -f -- $path }
    else { & git -C $worktree add -- $path }
    if ($LASTEXITCODE -ne 0) { throw 'Explicit known evidence staging failed.' }
}
$staged = @(& git -C $worktree diff --cached --name-only | Sort-Object)
if ($staged.Count -ne $expected.Count -or ($staged -join "`n") -cne ($expected -join "`n")) { throw 'Staged scope mismatch.' }
& git -C $worktree diff --cached --check
if ($LASTEXITCODE -ne 0) { throw 'Final staged diff check failed.' }
foreach ($path in $evidence) {
    # Exact original bytes must be the actual index blob. This Git object check
    # does not run text conversion and is independent of terminal encoding.
    $originalBlob = (& git -C $worktree hash-object --no-filters -- $path).Trim()
    $indexBlob = (& git -C $worktree rev-parse (':' + $path)).Trim()
    if ($LASTEXITCODE -ne 0 -or $originalBlob -cne $indexBlob) { throw 'Evidence index blob differs from original bytes.' }
}
$report = [ordered]@{Task='BUG-161';Status='ExactStagedScopeVerified';CheckedUtc=[datetime]::UtcNow.ToString('o');
    SourceBase=$base;RemoteMain=$base;ExpectedFiles=$expected;StagedFiles=$staged;StagedFileCount=$staged.Count;
    OriginalEvidenceFiles=$manifest.Originals.Count;AllEvidenceIndexBlobsMatchOriginalBytes=$true;
    NativeValidation='Two providers each exact 25/25 CAD/assets/collaboration, plus17 supplemental InMemory routing, zero Other; source/runtime applicability verified by separate evidence.';
    Review='One task-level source review has no substantive blockers. Root checked complete source/documentation/evidence diff, scope, exact archive bytes and index; no schema/dependency/workflow/public API change.';
    ReviewLimits='Controlled failures are injections, not native deadlock/serialization reproductions; explicit and ambient caller transactions/pending changes covered; direct enlisted protection source-reviewed only. Full WP6 acceptance, remote delivery, post-merge smoke and cleanup remain pending.'}
$output='D:\CP6\tmp\bug161-final-staging-proof.json'
if (Test-Path -LiteralPath $output) { throw 'Preserve original staging proof.' }
[IO.File]::WriteAllText($output, ($report | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{StagedFiles=$staged.Count;ArchiveOriginals=$manifest.Originals.Count;EvidenceIndexByteCheck=$true} | ConvertTo-Json -Compress
