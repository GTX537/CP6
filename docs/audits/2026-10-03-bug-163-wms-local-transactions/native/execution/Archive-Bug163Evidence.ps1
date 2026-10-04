$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\bug-163-wms-local-transactions'
$destination=Join-Path $worktree 'docs/audits/2026-10-03-bug-163-wms-local-transactions/native'
if(Test-Path -LiteralPath $destination){throw 'Preserve original archive.'}
$files=[Collections.Generic.List[object]]::new()
function Add-Original([string]$Source,[string]$Relative){$files.Add([pscustomobject]@{Source=$Source;Target=$Relative})}
$original='D:\CP6\tmp\wp6-sql-formal-matrix-cad-cleanup-fix'
foreach($entry in @('core-business','wms-business')){
    foreach($name in @('entry-result.json','runtime-artifact-before.json','runtime-artifact-after.json','private/trx/results.trx','private/process/stdout.log','private/process/stderr.log')){
        Add-Original (Join-Path $original ('entries/'+$entry+'/'+$name)) ('original-wp6-failure/entries/'+$entry+'/'+$name)
    }
}
foreach($name in @('summary.json','source-inputs.json')){Add-Original (Join-Path $original $name) ('original-wp6-failure/'+$name)}
foreach($label in @('bug163-sql-native-red','bug163-sql-native-red-corrected','bug163-sql-native-red-isolated','bug163-sql-native-red-valid-seed','bug163-pg-native-red','bug163-sql-wms-green','bug163-pg-wms-green')){
    $run=Join-Path 'D:\CP6\tmp' $label
    foreach($file in Get-ChildItem -LiteralPath $run -File -Recurse){
        $relative=[IO.Path]::GetRelativePath($run,$file.FullName).Replace('\','/')
        if($file.Extension -notin @('.json','.log','.trx') -or $file.Name -like '*.private.json' -or
            ($relative -like 'private/*' -and $relative -notmatch '^private/(test/(stdout|stderr)\.log|trx/results\.trx)$') -or
            ($relative -like 'build/private/*' -and $relative -notmatch '^build/private/(01-build|01-restore|sdk-version)/(stdout|stderr)\.log$')){throw 'Unexpected evidence file requires review.'}
        Add-Original $file.FullName ($label+'/'+$relative)
    }
}
foreach($name in @('Invoke-Bug163Tests.ps1','New-Bug163Databases.ps1','Test-Bug163LocalEvidence.ps1','Archive-Bug163Evidence.ps1',
    'bug163-review.md','bug163-exact-local-verification.json','bug163-databases-created.json','bug163-task-start-workflow-input-inspection.json',
    'bug163-unfixed-production-source-proof.json','bug163-native-red-proof.json','bug163-native-connection-preflight.json',
    'bug163-test-runner-preflight-rejection.json','bug163-runner-module-snapshot.json')){
    Add-Original (Join-Path 'D:\CP6\tmp' $name) ('execution/'+$name)
}
foreach($module in Get-ChildItem -LiteralPath 'D:\CP6\tmp\bug163-runner-modules' -Filter '*.psm1' -File){
    Add-Original $module.FullName ('execution/modules/'+$module.Name)
}
$passwords=@()
$configuration=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.local.json' -Raw|ConvertFrom-Json
$owned=Get-Content -LiteralPath 'D:\CP6\tmp\bug163-wp4-owned.private.json' -Raw|ConvertFrom-Json
foreach($connection in @($configuration.PostgreSql,$owned.PostgreSqlConnection)){
    $builder=[Data.Common.DbConnectionStringBuilder]::new()
    $builder.set_ConnectionString($connection)
    if($builder.ContainsKey('Password')){$passwords += [string]$builder.get_Item('Password')}
}
if(@($passwords|Where-Object{$_}).Count -ne 2){throw 'Private password screening inputs missing.'}
foreach($file in $files){
    if(!(Test-Path -LiteralPath $file.Source -PathType Leaf)){throw ('Required original missing: '+$file.Target)}
    $text=[IO.File]::ReadAllText($file.Source)
    if(@($passwords|Where-Object{$_ -and $text.Contains($_)}).Count -or
        $text -match '(?im)(Password|Pwd)\s*=\s*[^;$\s"'']+;' -or
        $text -match '\beyJ[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\.[A-Za-z0-9_-]{16,}\b'){
        throw ('Sensitive-looking value found; nothing archived: '+$file.Target)
    }
}
$records=@(foreach($file in $files){
    $target=Join-Path $destination $file.Target
    $null=[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($file.Source,$target,$false)
    $hash=(Get-FileHash -LiteralPath $file.Source).Hash
    if((Get-FileHash -LiteralPath $target).Hash -cne $hash){throw 'Original byte mismatch.'}
    [pscustomobject]@{Path=$file.Target;OriginalPath=$file.Source;Sha256=$hash;Length=(Get-Item -LiteralPath $target).Length}
})
$counts=@(foreach($record in $records|Where-Object Path -Like '*.trx'){
    [xml]$trx=[IO.File]::ReadAllText((Join-Path $destination $record.Path))
    $cases=@($trx.TestRun.Results.UnitTestResult)
    [pscustomobject]@{Path=$record.Path;Sha256=$record.Sha256;Total=$cases.Count;Passed=@($cases|Where-Object outcome -CEQ 'Passed').Count;
        Failed=@($cases|Where-Object outcome -CEQ 'Failed').Count;Other=@($cases|Where-Object outcome -CNotIn @('Passed','Failed')).Count;CaseNames=@($cases.testName|Sort-Object)}
})
[pscustomobject]@{Task='BUG-163';SourceBase='ce49012d37da4b1b01f074d5b5b7fd227780e656';ArchivedUtc=[datetime]::UtcNow.ToString('o');
    Originals=$records;DerivedTrxCounts=$counts;
    Scope='Original WP6 SQL failure and core success retained; initial wrapper rejection and two intermediate seed failures remain separate from valid SQL17/PG17 RED and exact two-provider original8/new17 GREEN. Actual source/runtime manifests, trace, review, execution helpers and frozen modules retained byte-for-byte. Remote delivery, post-merge smoke, owned cleanup and full WP6 remain pending.';
    Excluded='Private connection/ownership receipts, credentials, database state, JWT/Cookie and backups excluded.'}|ConvertTo-Json -Depth 10|Set-Content -LiteralPath (Join-Path $destination 'manifest.json') -Encoding utf8NoBOM
[pscustomobject]@{Originals=$records.Count;ManifestSha256=(Get-FileHash -LiteralPath (Join-Path $destination 'manifest.json')).Hash;
    TrxCounts=$counts|Select-Object Path,Total,Passed,Failed,Other}|ConvertTo-Json -Depth 5
