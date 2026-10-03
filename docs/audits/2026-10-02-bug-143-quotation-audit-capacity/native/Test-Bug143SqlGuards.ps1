param(
    [string]$MigrationSqlPath='D:\CP6\tmp\bug-143-generated-migration-verified.sql',
    [string]$InputManifestPath='D:\CP6\tmp\bug-143-actual-input-verified.json',
    [ValidatePattern('^[a-z0-9][a-z0-9-]{0,39}$')][string]$BaselineStage='after-repeat-verified',
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,25}$')][string]$StagePrefix,
    [ValidateSet('correct-existing','missing-column','wrong-type','wrong-nullable','overlong-ascii','overlong-supplementary','overlong-trailing-spaces','last-column-late-atomic','exact-100-ascii','exact-100-supplementary','exact-100-spaces')]
    [string[]]$Case=@('correct-existing','missing-column','wrong-type','wrong-nullable','overlong-ascii','overlong-supplementary','overlong-trailing-spaces','last-column-late-atomic','exact-100-ascii','exact-100-supplementary','exact-100-spaces')
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$taskRoot='D:\CP6\tmp'
$receiptPath=Join-Path $taskRoot 'db-compat.wp2-before-sql-final-upgrade-owned.json'
try{$receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json}catch{throw 'Fixed owned SQL backup receipt unavailable.'}
if($receipt.Task -cne 'DB-COMPAT-01-WP2' -or $receipt.Owner -cne '36ef9cae14704ac88eb994416f628e24' -or
    $receipt.SqlServerDatabase -cne 'CP6Compat_WP2_20261002_36ef9cae'){throw 'Exact owned SQL36ef backup receipt required.'}
foreach($path in @($MigrationSqlPath,$InputManifestPath)){
    $absolute=[IO.Path]::GetFullPath($path)
    if(-not$absolute.StartsWith('D:\CP6\tmp\',[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($absolute)-match '(?i)receipt|secret|credential' -or
        -not(Test-Path -LiteralPath $absolute -PathType Leaf)){throw 'Existing non-secret owned generated SQL and input manifest required.'}
}
$generatedSha256=(Get-FileHash -LiteralPath $MigrationSqlPath -Algorithm SHA256).Hash
$inputSha256=(Get-FileHash -LiteralPath $InputManifestPath -Algorithm SHA256).Hash
$receiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
$baselinePath=Join-Path $taskRoot "bug-143-sql-$BaselineStage.json"
$baseline=Get-Content -LiteralPath $baselinePath -Raw|ConvertFrom-Json -Depth 65
if($baseline.Status -cne 'Captured' -or $baseline.OwnerVerified -ne $true -or
    $baseline.ReceiptSha256 -cne $receiptSha256 -or $baseline.InputSha256 -cne $inputSha256 -or
    $baseline.Database -cne $receipt.SqlServerDatabase -or @($baseline.Tables).Count -ne 352 -or
    @($baseline.Columns).Count -ne 6832 -or @($baseline.Indexes).Count -ne 1378 -or
    @($baseline.ForeignKeys).Count -ne 202 -or @($baseline.CoreHistory).Count -ne 140 -or
    $baseline.CoreHistory[-1].MigrationId -cne '20261002193500_RestoreQuotationAuditColumnCapacity'){
    throw 'Complete exact owned Core140 after-repeat capture with the same verified input required.'
}
$rules=@(
    [ordered]@{Table='T_Quotation';Column='Creator'},[ordered]@{Table='T_Quotation';Column='Modifier'},
    [ordered]@{Table='T_QuotationCalc';Column='Creator'},[ordered]@{Table='T_QuotationCalc';Column='Modifier'},
    [ordered]@{Table='T_QuotationDetail';Column='Creator'},[ordered]@{Table='T_QuotationDetail';Column='Modifier'}
)
$generated=Get-Content -LiteralPath $MigrationSqlPath -Raw
$blocks=@([regex]::Split($generated,'(?m)^GO\s*\r?$')|Where-Object{$_-match 'DECLARE @cp6_object_id'})
if($blocks.Count -ne 6){throw 'Actual generated EF migration must contain exactly six separate BUG143 guard batches.'}
for($ordinal=0;$ordinal -lt 6;$ordinal++){
    $rule=$rules[$ordinal];$block=$blocks[$ordinal]
    if(-not$block.Contains("OBJECT_ID(N'[dbo].[$($rule.Table)]', N'U')") -or
        -not$block.Contains("N'$($rule.Column)' COLLATE Latin1_General_100_BIN2") -or
        -not$block.Contains("ALTER TABLE [dbo].[$($rule.Table)] ALTER COLUMN [$($rule.Column)] nvarchar(100) COLLATE ") -or
        -not$block.Contains('THROW 51043') -or
        $block-match '(?i)\bBEGIN\s+TRANSACTION\b|\bCOMMIT\b|\bROLLBACK\b|__EFMigrationsHistory'){
        throw 'Unexpected actual guard batch/order; outer fixture owns the transaction, never migration history.'
    }
    $column=@($baseline.Columns|Where-Object{$_.Schema -ceq 'dbo' -and $_.Table -ceq $rule.Table -and $_.Column -ceq $rule.Column})
    if($column.Count -ne 1 -or $column[0].TypeName -cne 'nvarchar' -or $column[0].max_length -ne 200 -or
        $column[0].is_nullable -ne $true -or $column[0].is_computed -ne $false -or $column[0].is_identity -ne $false){
        throw 'Six native nullable Unicode100 baseline columns required.'
    }
}
# The standalone graph has a strict independently captured required-column manifest.
# Reuse only declarations/three inserts, never its committed prepare/cleanup orchestration.
$fixturePath=Join-Path $taskRoot 'bug-143-quotation-fixture.sql'
$fixture=Get-Content -LiteralPath $fixturePath -Raw
$requiredStart=$fixture.IndexOf('DECLARE @cp6_provided TABLE')
$declarationsStart=$fixture.IndexOf('DECLARE @cp6_qtn_id uniqueidentifier')
$declarationsEnd=$fixture.IndexOf('BEGIN TRY',$declarationsStart)
$insertStart=$fixture.IndexOf('  INSERT dbo.T_Quotation(')
$insertEnd=$fixture.IndexOf(' END',$insertStart)
if($requiredStart -lt 0 -or $declarationsStart -le $requiredStart -or $declarationsEnd -le $declarationsStart -or
    $insertStart -le $declarationsEnd -or $insertEnd -le $insertStart){throw 'Audited native graph extraction markers changed.'}
$required=$fixture.Substring($requiredStart,$declarationsStart-$requiredStart)
$declarations=$fixture.Substring($declarationsStart,$declarationsEnd-$declarationsStart)
$inserts=$fixture.Substring($insertStart,$insertEnd-$insertStart)
if([regex]::Matches($inserts,'INSERT dbo.T_Quotation(?:Calc|Detail)?\(').Count -ne 3 -or
    $inserts-match '(?i)\bUPDATE\b|\bDELETE\b|\bCOMMIT\b|\bROLLBACK\b'){throw 'Exactly three audited INSERT primitives required.'}
$declarations=$declarations.Replace('14300000-','14399999-').Replace('CP6-BUG143-QTN-01','CP6-BUG143-GQ-01').Replace('CP6-BUG143-CALC-01','CP6-BUG143-GC-01')
$graph=$declarations+@'
IF EXISTS(SELECT 1 FROM dbo.T_Quotation WHERE Id=@cp6_qtn_id OR QtnNo=@cp6_qtn_no)
 OR EXISTS(SELECT 1 FROM dbo.T_QuotationCalc WHERE Id=@cp6_calc_id OR QtnCalcNo=@cp6_calc_no)
 OR EXISTS(SELECT 1 FROM dbo.T_QuotationDetail WHERE Id=@cp6_detail_id OR QtnNo=@cp6_qtn_no)
 THROW 51005,'Pre-existing guard fixture identifiers cannot be overwritten.',1;
'@+$inserts
$definitions=[ordered]@{
    'correct-existing'=@{Setup='';ExpectedError=0;ExpectedOrdinal=0;SuccessBytes=0}
    'missing-column'=@{Setup='ALTER TABLE dbo.T_Quotation DROP COLUMN Creator;';ExpectedError=51043;ExpectedOrdinal=1;SuccessBytes=0}
    'wrong-type'=@{Setup='ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator varchar(100) NULL;';ExpectedError=51043;ExpectedOrdinal=1;SuccessBytes=0}
    'wrong-nullable'=@{Setup='ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(100) NOT NULL;';ExpectedError=51043;ExpectedOrdinal=1;SuccessBytes=0}
    'overlong-ascii'=@{Setup=$graph+"ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(max) NULL; UPDATE dbo.T_Quotation SET Creator=REPLICATE(N'X',101) WHERE Id=@cp6_qtn_id;";ExpectedError=51043;ExpectedOrdinal=1;SuccessBytes=202}
    'overlong-supplementary'=@{Setup=$graph+"ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(max) NULL; UPDATE dbo.T_Quotation SET Creator=REPLICATE(N'😀',50)+N'X' WHERE Id=@cp6_qtn_id;";ExpectedError=51043;ExpectedOrdinal=1;SuccessBytes=202}
    'overlong-trailing-spaces'=@{Setup=$graph+"ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(max) NULL; UPDATE dbo.T_Quotation SET Creator=REPLICATE(N' ',101) WHERE Id=@cp6_qtn_id;";ExpectedError=51043;ExpectedOrdinal=1;SuccessBytes=202}
    'last-column-late-atomic'=@{Setup=$graph+(@($rules|ForEach-Object{"ALTER TABLE dbo.[$($_.Table)] ALTER COLUMN [$($_.Column)] nvarchar(max) NULL;"})-join([char]10))+" UPDATE dbo.T_QuotationDetail SET Modifier=REPLICATE(N'X',101) WHERE Id=@cp6_detail_id;";ExpectedError=51043;ExpectedOrdinal=6;SuccessBytes=202}
    'exact-100-ascii'=@{Setup=$graph+"ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(max) NULL;";ExpectedError=0;ExpectedOrdinal=0;SuccessBytes=200}
    'exact-100-supplementary'=@{Setup=$graph+"ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(max) NULL; UPDATE dbo.T_Quotation SET Creator=REPLICATE(N'😀',50) WHERE Id=@cp6_qtn_id;";ExpectedError=0;ExpectedOrdinal=0;SuccessBytes=200}
    'exact-100-spaces'=@{Setup=$graph+"ALTER TABLE dbo.T_Quotation ALTER COLUMN Creator nvarchar(max) NULL; UPDATE dbo.T_Quotation SET Creator=REPLICATE(N' ',100) WHERE Id=@cp6_qtn_id;";ExpectedError=0;ExpectedOrdinal=0;SuccessBytes=200}
}
if(@($Case|Select-Object -Unique).Count -ne $Case.Count){throw 'Duplicate requested case names.'}
$aggregatePath=Join-Path $taskRoot "bug-143-native-guards-$StagePrefix.json"
if(Test-Path -LiteralPath $aggregatePath){throw 'Existing aggregate guard evidence must not be overwritten.'}
$reports=[Collections.Generic.List[object]]::new()
foreach($caseName in $Case){
    $definition=$definitions[$caseName]
    $stage="$StagePrefix-$caseName"
    $queryPath=Join-Path $taskRoot "bug-143-native-$stage.sql"
    $reportPath=Join-Path $taskRoot "bug-143-native-$stage.json"
    $logPath=Join-Path $taskRoot "bug-143-native-$stage.log"
    $restoredStage="$stage-restored"
    foreach($path in @($queryPath,$reportPath,$logPath,(Join-Path $taskRoot "bug-143-sql-$restoredStage.json"),(Join-Path $taskRoot "bug-143-sql-$restoredStage.capture.sql"),(Join-Path $taskRoot "bug-143-sql-compare-$BaselineStage-to-$restoredStage.json"))){
        if(Test-Path -LiteralPath $path){throw 'Existing case/capture/comparison artifact must not be overwritten.'}
    }
    $expectedError=[int]$definition.ExpectedError
    $expectedOrdinal=[int]$definition.ExpectedOrdinal
    $setup=[string]$definition.Setup
    if($definition.SuccessBytes -gt 0){
        $fixtureTable=if($caseName -ceq 'last-column-late-atomic'){'T_QuotationDetail'}else{'T_Quotation'}
        $fixtureColumn=if($caseName -ceq 'last-column-late-atomic'){'Modifier'}else{'Creator'}
        $fixtureId=if($caseName -ceq 'last-column-late-atomic'){'14399999-0000-0000-0000-000000000003'}else{'14399999-0000-0000-0000-000000000001'}
        $setup+=" IF COALESCE((SELECT DATALENGTH([$fixtureColumn]) FROM dbo.[$fixtureTable] WHERE Id='$fixtureId'),-1)<>$($definition.SuccessBytes) THROW 51005,'Native boundary fixture must have exactly its planned UTF16 byte length before guard execution.',1;"
    }
    $header=@"
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'36ef9cae14704ac88eb994416f628e24')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,'Exact owned SQL36ef backup required before any fixture.',1;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>140
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261002193500_RestoreQuotationAuditColumnCapacity')
 THROW 51005,'Exact canonical Core140 required before any guard fixture.',1;
DECLARE @cp6_fixture_action nvarchar(20)=N'cleanup';
$required
CREATE TABLE #cp6_bug143_case(Done bit NOT NULL,ActualError int NOT NULL,ActualOrdinal int NOT NULL,PrefixProved bit NOT NULL,SetupError int NOT NULL);
INSERT #cp6_bug143_case VALUES(0,0,0,0,0);
CREATE TABLE #cp6_bug143_columns(TableName sysname,ColumnName sysname,ColumnId int,CollationName sysname);
INSERT #cp6_bug143_columns
 SELECT tab.name,col.name,col.column_id,col.collation_name FROM sys.tables tab JOIN sys.columns col ON col.object_id=tab.object_id
 WHERE tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name IN(N'T_Quotation',N'T_QuotationCalc',N'T_QuotationDetail') AND col.name IN(N'Creator',N'Modifier');
BEGIN TRY
 BEGIN TRANSACTION;
 $setup
END TRY
BEGIN CATCH
 DECLARE @setupError int=ERROR_NUMBER();
 IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
 UPDATE #cp6_bug143_case SET Done=1,SetupError=@setupError;
END CATCH;
GO
"@
    $run=[Collections.Generic.List[string]]::new()
    $run.Add($header)
    for($ordinal=0;$ordinal -lt 6;$ordinal++){
        $number=$ordinal+1
        $prefixAssertion=if($caseName -ceq 'last-column-late-atomic' -and $number -eq 6){@'
 IF (SELECT COUNT(*) FROM sys.tables tab JOIN sys.columns col ON col.object_id=tab.object_id
  JOIN #cp6_bug143_columns expected ON expected.TableName=tab.name AND expected.ColumnName=col.name
  WHERE tab.schema_id=SCHEMA_ID(N'dbo') AND col.max_length=200
   AND NOT(tab.name=N'T_QuotationDetail' AND col.name=N'Modifier'))<>5
  THROW 51006,'Late atomic fixture must have executed and narrowed all five preceding commands.',1;
 UPDATE #cp6_bug143_case SET PrefixProved=1;
'@}else{''}
        $actual=$blocks[$ordinal]
        $run.Add(@"
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
 $prefixAssertion
 $actual
 END TRY
 BEGIN CATCH
  DECLARE @guardError int=ERROR_NUMBER();
  DECLARE @prefixWasProved bit=(SELECT PrefixProved FROM #cp6_bug143_case);
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,ActualError=@guardError,ActualOrdinal=$number,PrefixProved=@prefixWasProved;
 END CATCH;
END;
GO
"@)
    }
    $successAssertion=if($definition.SuccessBytes -eq 200){@'
IF COALESCE((SELECT DATALENGTH(Creator) FROM dbo.T_Quotation WHERE Id='14399999-0000-0000-0000-000000000001'),-1)<>200
 THROW 51006,'Exact legal Unicode boundary must survive narrowing without truncation.',1;
'@}else{''}
    $run.Add(@"
IF (SELECT Done FROM #cp6_bug143_case)=0
BEGIN
 BEGIN TRY
  IF EXISTS(SELECT 1 FROM #cp6_bug143_columns expected LEFT JOIN sys.tables tab ON tab.schema_id=SCHEMA_ID(N'dbo') AND tab.name=expected.TableName
   LEFT JOIN sys.columns col ON col.object_id=tab.object_id AND col.name=expected.ColumnName
   WHERE col.column_id IS NULL OR col.column_id<>expected.ColumnId OR col.max_length<>200 OR col.is_nullable<>1
    OR col.collation_name COLLATE Latin1_General_100_BIN2<>expected.CollationName COLLATE Latin1_General_100_BIN2)
   THROW 51006,'Successful guards must preserve six physical columns/collations and finish at nullable Unicode100.',1;
  $successAssertion
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1;
 END TRY
 BEGIN CATCH
  DECLARE @successError int=ERROR_NUMBER();
  IF @@TRANCOUNT>0 ROLLBACK TRANSACTION;
  UPDATE #cp6_bug143_case SET Done=1,SetupError=@successError;
 END CATCH;
END;
IF @@TRANCOUNT<>0 THROW 51006,'Guard fixture transaction must be fully rolled back.',1;
SELECT Done,ActualError,ActualOrdinal,PrefixProved,SetupError FROM #cp6_bug143_case FOR JSON PATH,WITHOUT_ARRAY_WRAPPER;
GO
"@)
    $sql=$run-join([char]10)
    [IO.File]::WriteAllText($queryPath,$sql,[Text.UTF8Encoding]::new($false))
    $nativeOutput=& sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d 'CP6Compat_WP2_20261002_36ef9cae' -l 15 -t 120 -b -y 0 -w 65535 -f 65001 -i $queryPath 2>&1
    $nativeExitCode=$LASTEXITCODE
    $state=$null
    if($nativeExitCode -eq 0){
        $jsonParts=@($nativeOutput|ForEach-Object{[string]$_}|Where-Object{$_.TrimStart().StartsWith('{')})
        if($jsonParts.Count -eq 1){try{$state=$jsonParts[0]|ConvertFrom-Json}catch{$state=$null}}
    }
    $setupFailed=$nativeExitCode -ne 0 -or $null -eq $state -or $state.SetupError -ne 0
    $casePassed=-not$setupFailed -and $state.Done -eq $true -and $state.ActualError -eq $expectedError -and
        $state.ActualOrdinal -eq $expectedOrdinal -and ($caseName -cne 'last-column-late-atomic' -or $state.PrefixProved -eq $true)
    $report=[ordered]@{
        Case=$caseName;Stage=$stage;Status=if($casePassed){'Passed'}else{'Failed'}
        FailureKind=if($setupFailed){'SetupOrNativeExecutionFailure'}elseif(-not$casePassed){'GuardResultMismatch'}else{$null}
        IsExpectedMigrationRejection=$casePassed -and $expectedError -eq 51043
        ExpectedError=$expectedError;ExpectedOrdinal=$expectedOrdinal
        ActualError=if($null-ne$state){$state.ActualError}else{$null}
        ActualOrdinal=if($null-ne$state){$state.ActualOrdinal}else{$null}
        PrefixFiveCommandsProved=if($null-ne$state){$state.PrefixProved}else{$false}
        SetupError=if($null-ne$state){$state.SetupError}else{$null}
        NativeExitCode=$nativeExitCode;CapturedUtc=[DateTime]::UtcNow.ToString('O')
        GeneratedMigrationSqlSha256=$generatedSha256;InputSha256=$inputSha256
        FixturePrimitiveSha256=(Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash
        BaselineSha256=(Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash
        QuerySha256=(Get-FileHash -LiteralPath $queryPath -Algorithm SHA256).Hash
        BusinessValuesReturned=$false;RestorationVerified=$false
    }
    # Log only sanitized phase/error numbers. Driver output may contain source or values.
    [IO.File]::WriteAllText($logPath,($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($reportPath,($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    & (Join-Path $taskRoot 'Capture-Bug143SqlState.ps1') -Stage $restoredStage -ExpectedHistoryCount 140 -InputManifestPath $InputManifestPath
    & pwsh -NoProfile -File (Join-Path $taskRoot 'Compare-Bug143SqlState.ps1') -Before $BaselineStage -After $restoredStage
    $compareExitCode=$LASTEXITCODE
    $comparisonPath=Join-Path $taskRoot "bug-143-sql-compare-$BaselineStage-to-$restoredStage.json"
    $comparison=Get-Content -LiteralPath $comparisonPath -Raw|ConvertFrom-Json -Depth 30
    $report.RestorationVerified=$compareExitCode -eq 0 -and $comparison.Failed -eq 0
    $report.RestorationComparisonSha256=(Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash
    if(-not$report.RestorationVerified){$report.Status='Failed';$report.FailureKind='FullStateRestorationFailure'}
    [IO.File]::WriteAllText($reportPath,($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    $reports.Add($report)
    [IO.File]::WriteAllText($aggregatePath,([ordered]@{Scope='BUG143 task-owned transactional native fixtures; actual six EF batches; every case rolled back and full 352-table/syscatalog restored';Cases=@($reports);GeneratedMigrationSqlSha256=$generatedSha256;InputSha256=$inputSha256;Boundary='No production effects; no global DBTS/sequence rollback promise; setup failures are not migration rejections.'}|ConvertTo-Json -Depth 15),[Text.UTF8Encoding]::new($false))
    if($report.Status -cne 'Passed'){throw "BUG143 case $caseName failed; sanitized evidence retained, no setup error counted as migration rejection."}
}
Write-Output "BUG143 native guards: $($reports.Count) cases passed and full state restored."
