$ErrorActionPreference = 'Stop'
$worktree = 'D:\CP6\tmp\worktrees\bug-161-cad-provider-recovery'
$destination = Join-Path $worktree 'docs/audits/2026-10-03-bug-161-cad-provider-recovery/native'
if (Test-Path -LiteralPath $destination) { throw 'Preserve original archive.' }
$files = [Collections.Generic.List[object]]::new()
function Add-Original([string]$Source, [string]$Relative) { $files.Add([pscustomobject]@{Source=$Source;Target=$Relative}) }
$originalRun = 'D:\CP6\tmp\wp6-pg-formal-matrix-claim-fix'
foreach ($name in @('entry-result.json','runtime-artifact-before.json','runtime-artifact-after.json','private/trx/results.trx','private/process/stdout.log','private/process/stderr.log')) {
    Add-Original (Join-Path $originalRun ('entries/space-cad-assets-collaboration/' + $name)) ('original-wp6-failure/' + $name)
}
foreach ($name in @('summary.json','source-inputs.json')) { Add-Original (Join-Path $originalRun $name) ('original-wp6-failure/' + $name) }
foreach ($label in @('bug161-pg-coordinated-native-red','bug161-pg-cad-exact-green','bug161-sql-cad-exact-green','bug161-inmemory-routing-green')) {
    $run = Join-Path 'D:\CP6\tmp' $label
    foreach ($file in Get-ChildItem -LiteralPath $run -File -Recurse) {
        $relative = [IO.Path]::GetRelativePath($run, $file.FullName).Replace('\','/')
        if ($file.Extension -notin @('.json','.log','.trx') -or $file.Name -like '*.private.json' -or
            ($relative -like 'private/*' -and $relative -notmatch '^private/(test/(stdout|stderr)\.log|trx/results\.trx)$') -or
            ($relative -like 'build/private/*' -and $relative -notmatch '^build/private/(01-build|01-restore|sdk-version)/(stdout|stderr)\.log$')) {
            throw 'Unexpected evidence file requires review.'
        }
        Add-Original $file.FullName ($label + '/' + $relative)
    }
}
foreach ($name in @('Invoke-Bug161Tests.ps1','Invoke-Bug161Groups.ps1','New-Bug161Databases.ps1','Test-Bug161LocalEvidence.ps1','Invoke-Bug161Routing.ps1','Archive-Bug161Evidence.ps1',
    'bug161-review.md','bug161-exact-local-verification.json','bug161-databases-created.json','bug161-task-start-workflow-input-inspection-workflow-input-inspection.json',
    'bug161-original-failure-verification.json','wp6-failed-cad-matrix-owned-cleanup.json')) {
    Add-Original (Join-Path 'D:\CP6\tmp' $name) ('execution/' + $name)
}
$moduleRoot = 'D:\CP6\tmp\worktrees\db-compat-wp6-20261003\scripts\database-compatibility'
foreach ($name in @('DatabaseCompatibilityEntries.psm1','DatabaseCompatibilityProcess.psm1','DatabaseCompatibilityResults.psm1','DatabaseCompatibilityLifecycle.psm1')) {
    Add-Original (Join-Path $moduleRoot $name) ('execution/modules/' + $name)
}
Add-Original 'D:\CP6\tmp\bug161-required-cases-before-fix.json' 'execution/original-required-cases.json'
$privatePasswords = @()
$local = Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw | ConvertFrom-Json
$owned = Get-Content -LiteralPath 'D:\CP6\tmp\bug161-wp5-owned.private.json' -Raw | ConvertFrom-Json
foreach ($connection in @($local.PostgreSql,$owned.PostgreSqlConnection)) {
    $builder = [Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($connection)
    if ($builder.ContainsKey('Password')) { $privatePasswords += [string]$builder['Password'] }
}
foreach ($file in $files) {
    if (!(Test-Path -LiteralPath $file.Source -PathType Leaf)) { throw 'Required original missing.' }
    $content = [IO.File]::ReadAllText($file.Source)
    if (@($privatePasswords | Where-Object { $_ -and $content.Contains($_) }).Count -gt 0 -or
        $content -match '(?im)(Password|Pwd)\s*=\s*[^;$\s"'']+;' -or
        $content -match '\beyJ[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\b') {
        throw 'Sensitive-looking value found; nothing archived.'
    }
}
$records = @(foreach ($file in $files) {
    $target = Join-Path $destination $file.Target
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($file.Source, $target, $false)
    $sha = (Get-FileHash -LiteralPath $file.Source).Hash
    if ((Get-FileHash -LiteralPath $target).Hash -cne $sha) { throw 'Original byte mismatch.' }
    [pscustomobject]@{Path=$file.Target;OriginalPath=$file.Source;Sha256=$sha;Length=(Get-Item -LiteralPath $target).Length}
})
$counts = @(foreach ($record in $records | Where-Object Path -Like '*.trx') {
    [xml]$trx = [IO.File]::ReadAllText((Join-Path $destination $record.Path))
    $cases = @($trx.TestRun.Results.UnitTestResult)
    [pscustomobject]@{Path=$record.Path;Sha256=$record.Sha256;Total=$cases.Count;
        Passed=@($cases | Where-Object outcome -CEQ 'Passed').Count;Failed=@($cases | Where-Object outcome -CEQ 'Failed').Count;
        Other=@($cases | Where-Object outcome -CNotIn @('Passed','Failed')).Count;CaseNames=@($cases.testName | Sort-Object)}
})
$manifest = [ordered]@{Task='BUG-161';SourceBase='4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0';ArchivedUtc=[datetime]::UtcNow.ToString('o');
    Originals=$records;DerivedTrxCounts=$counts;
    Scope='Original WP6 native 40001 failure, two deterministic actual native 40001 RED cases, exact two-provider 25-case GREEN and supplemental 17 InMemory routing results, review and actual execution modules are retained byte-for-byte. Original 15 and ten new cases are checked as one exact group per provider; injected recovery controls are not native deadlock reproductions. Remote delivery, post-merge smoke and cleanup remain pending.';
    Excluded='Private credentials, connection/ownership receipts, database state, JWT/Cookie and backups are excluded.'}
[IO.File]::WriteAllText((Join-Path $destination 'manifest.json'), ($manifest | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Originals=$records.Count;Bytes=($records | Measure-Object Length -Sum).Sum;
    ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $destination 'manifest.json')).Hash;
    TrxCounts=@($counts | Select-Object Path,Total,Passed,Failed,Other)} | ConvertTo-Json -Depth 5
