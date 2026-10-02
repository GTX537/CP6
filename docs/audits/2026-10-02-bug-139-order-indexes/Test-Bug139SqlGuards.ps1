param([switch]$ResumeFirstThree)
$ErrorActionPreference='Stop'
$receipt=Get-Content -LiteralPath 'D:\CP6\tmp\db-compat.wp2-owned.json' -Raw | ConvertFrom-Json
if($receipt.Task -ne 'DB-COMPAT-01-WP2' -or $receipt.Owner -notmatch '^[a-f0-9]{32}$' -or $receipt.SqlServerDatabase -notmatch '^CP6Compat_WP2_[0-9]{8}_[a-f0-9]{8}$') {throw 'Owned isolated receipt required.'}
$generatedPath='D:\CP6\tmp\bug-139-actual-ef8-migration.sql'
$generated=Get-Content -LiteralPath $generatedPath -Raw
$blocks=@([regex]::Split($generated,'(?m)^GO\s*\r?$') | Where-Object {$_ -match 'DECLARE @cp6_object_id'})
if($blocks.Count -ne 21) {throw 'Actual EF8 generated repair must have exactly 21 guarded batches.'}
$oracle=@(Get-Content -LiteralPath 'D:\CP6\tmp\wp2-sql137-missing-model-indexes.json' -Raw | ConvertFrom-Json | Where-Object ModelContract -Match ':False:')
if($oracle.Count -ne 21) {throw 'Independent frozen omission oracle required.'}
$last=$oracle[-1]
if($last.Table -ne 'T_SheetUnitPriceEstimate' -or $last.Index -ne 'IX_T_SheetUnitPriceEstimate_RevisionDate') {throw 'Explicit last-command negative-control target changed.'}
$target='[dbo].[T_SheetUnitPriceEstimate]'
$index='[IX_T_SheetUnitPriceEstimate_RevisionDate]'
$drop="DROP INDEX $index ON $target;"
$definitions=[ordered]@{
 'correct-existing'=''
 'wrong-column'="$drop CREATE NONCLUSTERED INDEX $index ON $target ([IsDeleted] ASC);"
 'unexpected-unique'="$drop CREATE UNIQUE NONCLUSTERED INDEX $index ON $target ([RevisionDate] ASC);"
 'unexpected-filter-correct-set-options'="$drop CREATE NONCLUSTERED INDEX $index ON $target ([RevisionDate] ASC) WHERE [RevisionDate] IS NOT NULL;"
 'unexpected-include'="$drop CREATE NONCLUSTERED INDEX $index ON $target ([RevisionDate] ASC) INCLUDE ([BaseCd]);"
 'wrong-direction'="$drop CREATE NONCLUSTERED INDEX $index ON $target ([RevisionDate] DESC);"
 'disabled-index'="ALTER INDEX $index ON $target DISABLE;"
 'late-conflict-atomic-rollback'=(@($oracle | ForEach-Object {"DROP INDEX [$($_.Index)] ON [$($_.Schema)].[$($_.Table)];"}) -join "`n")+"`nCREATE NONCLUSTERED INDEX $index ON $target ([IsDeleted] ASC);"
}
$header=@'
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
IF NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'__OWNER__')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Owned isolated SQL database required.',1;
BEGIN TRANSACTION;
GO
'@
$header=$header.Replace('__OWNER__',$receipt.Owner)
$cases=[Collections.Generic.List[object]]::new()
foreach($case in $definitions.GetEnumerator()) {
 $label=$case.Key
 $path="D:\CP6\tmp\bug-139-native-guard-$label.sql"
 if($ResumeFirstThree -and $label -in @('correct-existing','wrong-column','unexpected-unique')) {
  $logPath="D:\CP6\tmp\bug-139-native-guard-$label.log"
  $comparisonPath="D:\CP6\tmp\bug-139-sql-compare-after-repeat-to-guard-$label-evidence.json"
  $earlierLog=Get-Content -LiteralPath $logPath -Raw
  $comparison=Get-Content -LiteralPath $comparisonPath -Raw | ConvertFrom-Json
  if(($label -eq 'correct-existing' -and $earlierLog.Length -ne 0) -or ($label -ne 'correct-existing' -and $earlierLog -notmatch '51039') -or
   @($comparison.Checks | Where-Object Status -NE 'Passed').Count) {throw 'Earlier successful execution evidence must be complete.'}
  $cases.Add([ordered]@{Name=$label;Status='Passed';ReusedEarlierCheckedExecution=$true;ExpectedConflict51039=($label -ne 'correct-existing');AllTableHashesCountsHistoryAndIndexMetadataRestored=$true;ChecksAfterRollback=$comparison.Checks.Count;ExecutedInputSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;NativeOutputSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;RestorationReportSha256=(Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash})
  continue
 }
 $input=$header+"`n"+$case.Value+"`nGO`n"+($blocks -join "`nGO`n")+"`nGO`nROLLBACK TRANSACTION;`nGO`n"
 if(Test-Path -LiteralPath $path) {
  if((Get-Content -LiteralPath $path -Raw) -cne $input) {throw 'Prior negative-control script must not be changed.'}
 } else {[IO.File]::WriteAllText($path,$input,[Text.UTF8Encoding]::new($false))}
 $stdout=& sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d $receipt.SqlServerDatabase -b -r 0 -f 65001 -i $path 2>&1
 $code=$LASTEXITCODE
 $logPath="D:\CP6\tmp\bug-139-native-guard-$label.log"
 if(Test-Path -LiteralPath $logPath) {throw 'Prior native output must not be overwritten.'}
 [IO.File]::WriteAllText($logPath,($stdout -join "`n"),[Text.UTF8Encoding]::new($false))
 $isPositive=$label -eq 'correct-existing'
 $expected=if($isPositive){$code -eq 0}else{$code -ne 0 -and ($stdout -join "`n") -match '51039'}
 & 'D:\CP6\tmp\Capture-Bug139SqlState.ps1' -Stage "guard-$label-evidence"
 & 'D:\CP6\tmp\Compare-Bug139SqlState.ps1' -Before after-repeat -After "guard-$label-evidence"
 if($LASTEXITCODE -ne 0) {throw 'Native guard must restore all original business values/history/index metadata.'}
 $comparisonPath="D:\CP6\tmp\bug-139-sql-compare-after-repeat-to-guard-$label-evidence.json"
 $comparison=Get-Content -LiteralPath $comparisonPath -Raw | ConvertFrom-Json
 $restored=@($comparison.Checks | Where-Object Status -NE 'Passed').Count -eq 0
 $cases.Add([ordered]@{Name=$label;Status=if($expected -and $restored){'Passed'}else{'Failed'};NativeExitCode=$code;ExpectedConflict51039=(-not $isPositive -and $expected);AllTableHashesCountsHistoryAndIndexMetadataRestored=$restored;ChecksAfterRollback=$comparison.Checks.Count;ExecutedInputSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash;NativeOutputSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash;RestorationReportSha256=(Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash})
 if(-not $expected -or -not $restored) {throw "Native guard case $label failed its expected rejection/preservation."}
}
$outputPath='D:\CP6\tmp\bug-139-native-guards.json'
if(Test-Path -LiteralPath $outputPath) {throw 'Prior guard evidence must not be overwritten.'}
[ordered]@{Scope='Actual EF8 generated 21-command guarded SQL; same-correct replay plus six conflicting definitions and late-command rollback of earlier repaired indexes; all fixtures transactionally restored';SourceCommit='083e9c4dcdb3d5b1aad3b57c32cb337f86859361';Database=$receipt.SqlServerDatabase;FinishedUtc=[DateTime]::UtcNow.ToString('O');ActualMigrationScriptSha256=(Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256).Hash;Cases=$cases} | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outputPath -Encoding utf8
Write-Output "Guard actual-execution checks: $($cases.Count) passed; original all-table content/history/index definitions restored after every case."
