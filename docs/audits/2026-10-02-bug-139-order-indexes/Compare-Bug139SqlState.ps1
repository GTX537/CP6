param([Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$Before,
 [Parameter(Mandatory)][ValidatePattern('^[a-z0-9-]+$')][string]$After,[switch]$Upgrade)
$ErrorActionPreference='Stop'
$beforePath="D:\CP6\tmp\bug-139-sql-$Before.json"
$afterPath="D:\CP6\tmp\bug-139-sql-$After.json"
$first=Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json -Depth 30
$second=Get-Content -LiteralPath $afterPath -Raw | ConvertFrom-Json -Depth 30
if($first.Database -ne $second.Database) {throw 'Exact same database required.'}
$expected=@(Get-Content -LiteralPath 'D:\CP6\tmp\wp2-sql137-missing-model-indexes.json' -Raw | ConvertFrom-Json | Where-Object ModelContract -Match ':False:')
if($expected.Count -ne 21) {throw 'Frozen 21-index omission oracle required.'}
$allowedNew=$expected | ForEach-Object {"$($_.Schema).$($_.Table).$($_.Index)"}
$checks=[Collections.Generic.List[object]]::new()
function Assert([string]$name,[bool]$condition,[string]$detail) {
 $checks.Add([ordered]@{Name=$name;Status=if($condition){'Passed'}else{'Failed'};Detail=$detail})
}
Assert 'AllBusinessContentAndCountsPreserved' ($first.Tables.Count -eq $second.Tables.Count -and @($first.Tables | Where-Object {
 $row=$_; $next=@($second.Tables | Where-Object {$_.Schema -eq $row.Schema -and $_.Name -eq $row.Name});
 $next.Count -ne 1 -or (($row.Schema -ne 'dbo' -or $row.Name -ne '__EFMigrationsHistory' -or -not $Upgrade) -and
 ($next[0].Rows -ne $row.Rows -or $next[0].ContentSha256 -ne $row.ContentSha256))
}).Count -eq 0) "Every business table retained its exact PK-ordered JSON SHA256 and count; only deliberate Core history append excluded during upgrade. $($first.Tables.Count) tables compared."
$oldHistory=@($first.CoreHistory | ConvertTo-Json -Compress -Depth 10)
$retainedHistory=@($second.CoreHistory | Select-Object -First $first.CoreHistory.Count | ConvertTo-Json -Compress -Depth 10)
$expectedHistoryDelta=if($Upgrade){1}else{0}
Assert 'CanonicalHistoryExactAppendOrRepeat' ($oldHistory[0] -ceq $retainedHistory[0] -and $second.CoreHistory.Count -eq $first.CoreHistory.Count+$expectedHistoryDelta -and
 (-not $Upgrade -or $second.CoreHistory[-1].MigrationId -ceq '20261002175000_RestoreMissingOrderModelIndexes')) "All original MigrationId/ProductVersion pairs retained; expected append=$expectedHistoryDelta, before=$($first.CoreHistory.Count), after=$($second.CoreHistory.Count)."
$indexRows=@{}
foreach($row in $second.Indexes) {$indexRows["$($row.Schema).$($row.Table).$($row.Name)"]=$row}
$changes=@($first.Indexes | Where-Object {
 $next=$indexRows["$($_.Schema).$($_.Table).$($_.Name)"];
 -not $next -or ($_ | ConvertTo-Json -Compress -Depth 10) -cne ($next | ConvertTo-Json -Compress -Depth 10)
})
Assert 'EveryOriginalIndexPreserved' ($changes.Count -eq 0) "All $($first.Indexes.Count) existing complete index metadata records remained exact, including PK/unique aliases; no DROP/rebuild."
$oldNames=@{}
foreach($row in $first.Indexes) {$oldNames["$($row.Schema).$($row.Table).$($row.Name)"]=$true}
$newNames=@($second.Indexes | ForEach-Object {"$($_.Schema).$($_.Table).$($_.Name)"} | Where-Object {-not $oldNames.ContainsKey($_)} | Sort-Object)
$expectedNames=if($Upgrade){@($allowedNew | Sort-Object)}else{@()}
Assert 'OnlyExpectedNewIndexes' (($newNames -join '|') -ceq ($expectedNames -join '|')) "Exactly $($expectedNames.Count) new index definitions expected; actual=$($newNames.Count)."
foreach($rule in $expected) {
 $row=$indexRows["$($rule.Schema).$($rule.Table).$($rule.Index)"]
 $expectedColumns=@($rule.ModelContract.Split(':')[1].Split(','))
 $actualKeys=@($row.Columns | Where-Object KeyOrdinal -GT 0 | Sort-Object KeyOrdinal)
 $valid=$null -ne $row -and $row.Type -eq 2 -and -not $row.Unique -and -not $row.PrimaryKey -and -not $row.UniqueConstraint -and
 -not $row.Disabled -and -not $row.IgnoreDuplicateKey -and -not $row.HasFilter -and $null -eq $row.Filter -and
 @($row.Columns | Where-Object Included).Count -eq 0 -and @($actualKeys | Where-Object Descending).Count -eq 0 -and
 (($actualKeys.Name -join '|') -ceq ($expectedColumns -join '|'))
 Assert "FrozenIndex.$($rule.Table).$($rule.Index)" $valid 'Native enabled nonunique type2/unfiltered/no-include/ASC ordered keys match independent frozen model omission oracle.'
}
$report=[ordered]@{Scope='BUG139 actual SQL forward/repeat original-step and all-table/index preservation comparison';StartedFrom=$first.Stage;ComparedTo=$second.Stage;Database=$first.Database;Upgrade=$Upgrade.IsPresent;CapturedUtc=[DateTime]::UtcNow.ToString('O');SourceCommit='083e9c4dcdb3d5b1aad3b57c32cb337f86859361';BeforeSha256=(Get-FileHash -LiteralPath $beforePath -Algorithm SHA256).Hash;AfterSha256=(Get-FileHash -LiteralPath $afterPath -Algorithm SHA256).Hash;FrozenOmissionOracleSha256=(Get-FileHash -LiteralPath 'D:\CP6\tmp\wp2-sql137-missing-model-indexes.json' -Algorithm SHA256).Hash;Checks=$checks}
$outputPath="D:\CP6\tmp\bug-139-sql-compare-$Before-to-$After.json"
if(Test-Path -LiteralPath $outputPath) {throw 'Existing comparison evidence must not be overwritten.'}
$report | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outputPath -Encoding utf8
$failures=@($checks | Where-Object Status -EQ 'Failed')
Write-Output "Native SQL comparison: $($checks.Count-$failures.Count) passed / $($failures.Count) failed."
if($failures.Count) {$failures | Select-Object Name,Detail | ConvertTo-Json -Depth 4; exit 1}
