param()
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$receiptPath = 'D:\CP6\tmp\db-compat.wp2-before-sql-final-upgrade-owned.json'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.Task -cne 'DB-COMPAT-01-WP2' -or $receipt.Owner -cnotmatch '^[a-f0-9]{32}$' -or
    $receipt.SqlServerDatabase -cne 'CP6Compat_WP2_20261002_36ef9cae') { throw 'Exact backup task SQL receipt required.' }
$receiptHash = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
$generatedPath = 'D:\CP6\tmp\bug-141-generated-migration.sql'
$generated = Get-Content -LiteralPath $generatedPath -Raw
$blocks = @([regex]::Split($generated, '(?m)^GO\s*\r?$') | Where-Object { $_ -match 'DECLARE @cp6_child_object_id' })
$rules = @(
    [ordered]@{Table='T_OrderDetail';Name='FK_T_OrderDetail_T_Order_WebOrderNo';Principal='T_Order';PrincipalClr='Order';Columns=@('WebOrderNo');PrincipalIndex='AK_T_Order_WebOrderNo'},
    [ordered]@{Table='T_OrderMaterial';Name='FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';Principal='T_OrderDetail';PrincipalClr='OrderDetail';Columns=@('WebOrderNo','WebOrderDetailNo','ProductCd');PrincipalIndex='UX_T_OrderDetail_OrderProduct'},
    [ordered]@{Table='T_OrderProcess';Name='FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';Principal='T_OrderDetail';PrincipalClr='OrderDetail';Columns=@('WebOrderNo','WebOrderDetailNo','ProductCd');PrincipalIndex='UX_T_OrderDetail_OrderProduct'},
    [ordered]@{Table='T_OrderProcessNote';Name='FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';Principal='T_OrderDetail';PrincipalClr='OrderDetail';Columns=@('WebOrderNo','WebOrderDetailNo','ProductCd');PrincipalIndex='UX_T_OrderDetail_OrderProduct'}
)
if ($blocks.Count -ne 4) { throw 'Actual EF8 BUG141 migration must contain exactly four independent FK guard batches.' }
for ($ordinal=0; $ordinal -lt 4; $ordinal++) {
    $block = $blocks[$ordinal]
    $rule = $rules[$ordinal]
    if ([regex]::Matches($block, 'ALTER TABLE \[dbo\]\.\[[^\]]+\] WITH CHECK ADD CONSTRAINT').Count -ne 1 -or
        [regex]::Matches($block, 'THROW 51041').Count -ne 2 -or
        -not $block.Contains("ADD CONSTRAINT [$($rule.Name)]") -or
        -not $block.Contains("OBJECT_ID(N'[dbo].[$($rule.Table)]', N'U')") -or
        $block -match '(?i)BEGIN\s+TRANSACTION|COMMIT\s*;|ROLLBACK\s+TRANSACTION|INSERT\s+INTO|__EFMigrationsHistory') {
        throw 'Unexpected actual guard ordering/content; migration transaction/history commands must never enter the outer fixture transaction.'
    }
}
$oraclePath = 'D:\CP6\tmp\wp2-sql138-missing-model-keys-fks.json'
$oracle = @(Get-Content -LiteralPath $oraclePath -Raw | ConvertFrom-Json -Depth 15)
if ($oracle.Count -ne 6 -or @($oracle | Where-Object Kind -CEQ 'FK').Count -ne 4 -or @($oracle | Where-Object Kind -CEQ 'K').Count -ne 2) {
    throw 'Independent frozen four-FK/two-existing-equivalent-key oracle required.'
}
foreach ($rule in $rules) {
    $frozen = @($oracle | Where-Object { $_.Kind -ceq 'FK' -and $_.Schema -ceq 'dbo' -and $_.Table -ceq $rule.Table -and $_.Name -ceq $rule.Name })
    $columns = $rule.Columns -join ','
    $expected = "FK:${columns}->CP6.Entity.DomainModels.Erp.$($rule.PrincipalClr):${columns}:False:True:Cascade:$($rule.Name)"
    if ($frozen.Count -ne 1 -or $frozen[0].FrozenContract -cne $expected) { throw 'Independent frozen FK fixture manifest changed.' }
}
$baselinePath = 'D:\CP6\tmp\bug-141-sql-after-repeat.json'
$baseline = Get-Content -LiteralPath $baselinePath -Raw | ConvertFrom-Json -Depth 40
if ($baseline.Database -cne $receipt.SqlServerDatabase -or $baseline.OwnerVerified -ne $true -or $baseline.ReceiptSha256 -cne $receiptHash -or
    @($baseline.Tables).Count -ne 352 -or @($baseline.Indexes).Count -ne 1378 -or @($baseline.ForeignKeys).Count -ne 202 -or
    @($baseline.CoreHistory).Count -ne 139 -or $baseline.CoreHistory[-1].MigrationId -cne '20261002184500_RestoreMissingOrderModelForeignKeys' -or
    @($baseline.Tables | Where-Object { $_.Rows -lt 0 -or $_.ContentSha256 -cnotmatch '^[0-9A-F]{64}$' }).Count -ne 0) {
    throw 'Actual complete owned SQL Core139 after-repeat baseline required.'
}

# Expected definitions come from the independent frozen contract, not from the helper under test.
# Use this native assertion both before changing fixtures and after successful guard installation.
$verifyFour = [Collections.Generic.List[string]]::new()
foreach ($rule in $rules) {
    $expectedColumns = @()
    for ($i=0; $i -lt $rule.Columns.Count; $i++) {
        $column = $rule.Columns[$i]
        $expectedColumns += "($($i+1),N'$column',N'$column')"
    }
    $columnsSql = $expectedColumns -join ','
    $verifyFour.Add(@"
IF NOT EXISTS (
 SELECT 1 FROM sys.foreign_keys fk
 JOIN sys.tables childTable ON childTable.object_id=fk.parent_object_id
 JOIN sys.schemas childSchema ON childSchema.schema_id=childTable.schema_id
 JOIN sys.tables parentTable ON parentTable.object_id=fk.referenced_object_id
 JOIN sys.schemas parentSchema ON parentSchema.schema_id=parentTable.schema_id
 JOIN sys.indexes principalIndex ON principalIndex.object_id=fk.referenced_object_id AND principalIndex.index_id=fk.key_index_id
 WHERE childSchema.name=N'dbo' AND childTable.name=N'$($rule.Table)' AND fk.name=N'$($rule.Name)'
  AND parentSchema.name=N'dbo' AND parentTable.name=N'$($rule.Principal)' AND principalIndex.name=N'$($rule.PrincipalIndex)'
  AND fk.delete_referential_action=1 AND fk.update_referential_action=0
  AND fk.is_disabled=0 AND fk.is_not_trusted=0 AND fk.is_not_for_replication=0 AND fk.is_system_named=0
  AND (SELECT COUNT(*) FROM sys.foreign_key_columns WHERE constraint_object_id=fk.object_id)=$($rule.Columns.Count)
  AND NOT EXISTS (
   SELECT 1 FROM (VALUES $columnsSql) expected(ordinal,dependent_name,principal_name)
   LEFT JOIN sys.foreign_key_columns actual ON actual.constraint_object_id=fk.object_id AND actual.constraint_column_id=expected.ordinal
   LEFT JOIN sys.columns dependentColumn ON dependentColumn.object_id=actual.parent_object_id AND dependentColumn.column_id=actual.parent_column_id
   LEFT JOIN sys.columns principalColumn ON principalColumn.object_id=actual.referenced_object_id AND principalColumn.column_id=actual.referenced_column_id
   WHERE dependentColumn.column_id IS NULL OR principalColumn.column_id IS NULL
    OR dependentColumn.name COLLATE Latin1_General_100_BIN2<>expected.dependent_name COLLATE Latin1_General_100_BIN2
    OR principalColumn.name COLLATE Latin1_General_100_BIN2<>expected.principal_name COLLATE Latin1_General_100_BIN2))
 THROW 51004,N'Independent frozen four-FK native definition assertion failed.',1;
"@)
}
$verifyFourSql = $verifyFour -join "`n"
$noteName = $rules[3].Name
$note = '[dbo].[T_OrderProcessNote]'
$noteFk = "[$noteName]"
$dropNote = "ALTER TABLE $note DROP CONSTRAINT $noteFk;"
$addNote = "ALTER TABLE $note WITH CHECK ADD CONSTRAINT $noteFk FOREIGN KEY ([WebOrderNo],[WebOrderDetailNo],[ProductCd]) REFERENCES [dbo].[T_OrderDetail] ([WebOrderNo],[WebOrderDetailNo],[ProductCd])"
$dropFour = @($rules | ForEach-Object { "ALTER TABLE [dbo].[$($_.Table)] DROP CONSTRAINT [$($_.Name)];" }) -join "`n"
$assertNone = "IF EXISTS(SELECT 1 FROM sys.foreign_keys WHERE schema_id=SCHEMA_ID(N'dbo') AND name IN (" +
    (@($rules | ForEach-Object { "N'$($_.Name)'" }) -join ',') + ")) THROW 51003,N'All four fixture FKs must have been dropped before reinstall.',1;"
$definitions = [ordered]@{
    'correct-existing' = [ordered]@{Sql='';ExpectedError=0}
    'wrong-column' = [ordered]@{Sql=@"
$dropNote
ALTER TABLE $note WITH CHECK ADD CONSTRAINT $noteFk
 FOREIGN KEY ([ProductCd],[WebOrderDetailNo],[WebOrderNo]) REFERENCES [dbo].[T_OrderDetail] ([WebOrderNo],[WebOrderDetailNo],[ProductCd])
 ON DELETE CASCADE ON UPDATE NO ACTION;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_key_columns c JOIN sys.columns p ON p.object_id=c.parent_object_id AND p.column_id=c.parent_column_id
 WHERE c.constraint_object_id=OBJECT_ID(N'dbo.$noteName',N'F') AND c.constraint_column_id=1 AND p.name=N'ProductCd')
 THROW 51003,N'Wrong-column fixture not established.',1;
"@;ExpectedError=51041}
    'wrong-delete-action' = [ordered]@{Sql=@"
$dropNote
$addNote ON DELETE NO ACTION ON UPDATE NO ACTION;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.$noteName',N'F') AND delete_referential_action=0)
 THROW 51003,N'Wrong-delete-action fixture not established.',1;
"@;ExpectedError=51041}
    'disabled' = [ordered]@{Sql=@"
ALTER TABLE $note NOCHECK CONSTRAINT $noteFk;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.$noteName',N'F') AND is_disabled=1)
 THROW 51003,N'Disabled fixture not established.',1;
"@;ExpectedError=51041}
    'untrusted' = [ordered]@{Sql=@"
ALTER TABLE $note NOCHECK CONSTRAINT $noteFk;
ALTER TABLE $note WITH NOCHECK CHECK CONSTRAINT $noteFk;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.$noteName',N'F') AND is_disabled=0 AND is_not_trusted=1)
 THROW 51003,N'Enabled/untrusted fixture not established.',1;
"@;ExpectedError=51041}
    'not-for-replication-untrusted' = [ordered]@{Sql=@"
$dropNote
$addNote ON DELETE CASCADE ON UPDATE NO ACTION NOT FOR REPLICATION;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.$noteName',N'F') AND is_disabled=0 AND is_not_trusted=1 AND is_not_for_replication=1)
 THROW 51003,N'Native combined NOT FOR REPLICATION/untrusted fixture not established.',1;
"@;ExpectedError=51041}
    'wrong-parent' = [ordered]@{Sql=@"
$dropNote
IF EXISTS(SELECT 1 FROM sys.objects WHERE schema_id=SCHEMA_ID(N'dbo') AND name=N'CP6Bug141ForeignKeyParentFixture')
 THROW 51003,N'Unexpected pre-existing fixture name; no object will be replaced.',1;
CREATE TABLE dbo.CP6Bug141ForeignKeyParentFixture(
 WebOrderNo nvarchar(20) NOT NULL, WebOrderDetailNo int NOT NULL, ProductCd nvarchar(20) NOT NULL,
 CONSTRAINT PK_CP6Bug141ForeignKeyParentFixture PRIMARY KEY(WebOrderNo,WebOrderDetailNo,ProductCd));
ALTER TABLE $note WITH CHECK ADD CONSTRAINT $noteFk
 FOREIGN KEY ([WebOrderNo],[WebOrderDetailNo],[ProductCd]) REFERENCES dbo.CP6Bug141ForeignKeyParentFixture(WebOrderNo,WebOrderDetailNo,ProductCd)
 ON DELETE CASCADE ON UPDATE NO ACTION;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.$noteName',N'F')
 AND referenced_object_id=OBJECT_ID(N'dbo.CP6Bug141ForeignKeyParentFixture',N'U'))
 THROW 51003,N'Wrong-parent fixture not established.',1;
"@;ExpectedError=51041}
    'schema-same-name-wrong-type' = [ordered]@{Sql=@"
$dropNote
GO
CREATE PROCEDURE [dbo].$noteFk AS SELECT 1 AS FixtureOnly;
GO
IF OBJECT_ID(N'dbo.$noteName',N'P') IS NULL OR OBJECT_ID(N'dbo.$noteName',N'F') IS NOT NULL
 THROW 51003,N'Expected-schema same-name procedure fixture not established.',1;
"@;ExpectedError=51041}
    'late-conflict-atomic-rollback' = [ordered]@{Sql=@"
$dropFour
$assertNone
$addNote ON DELETE NO ACTION ON UPDATE NO ACTION;
IF NOT EXISTS(SELECT 1 FROM sys.foreign_keys WHERE object_id=OBJECT_ID(N'dbo.$noteName',N'F') AND delete_referential_action=0)
 THROW 51003,N'Last-command conflicting fixture not established.',1;
"@;ExpectedError=51041}
    'real-orphan-547' = [ordered]@{Sql=@"
$dropFour
$assertNone
IF EXISTS(
 SELECT 1 FROM sys.columns c
 WHERE c.object_id=OBJECT_ID(N'dbo.T_OrderProcessNote',N'U') AND c.is_nullable=0 AND c.is_identity=0 AND c.is_computed=0
  AND c.default_object_id=0 AND c.system_type_id<>189
  AND c.name NOT IN (N'Id',N'WebOrderNo',N'WebOrderDetailNo',N'ProductCd',N'OperationCd',N'TenantId'))
 THROW 51003,N'Unknown required native orphan fixture column; do not infer missing input values.',1;
INSERT dbo.T_OrderProcessNote(Id,WebOrderNo,WebOrderDetailNo,ProductCd,OperationCd,TenantId)
 VALUES(NEWID(),N'BUG141ORPHAN',1,N'BUG141-P',N'BUG141-OP',CONVERT(uniqueidentifier,N'69a83c21-0c06-4fc0-81b9-141000000001'));
IF (SELECT COUNT_BIG(*) FROM dbo.T_OrderProcessNote)<>1
 OR EXISTS(SELECT 1 FROM dbo.T_OrderDetail WHERE WebOrderNo=N'BUG141ORPHAN' AND WebOrderDetailNo=1 AND ProductCd=N'BUG141-P')
 THROW 51003,N'One real child-without-parent orphan fixture must exist before executing guard DDL.',1;
"@;ExpectedError=547}
    'missing-install' = [ordered]@{Sql="$dropFour`n$assertNone";ExpectedError=0}
}
$outputPath = 'D:\CP6\tmp\bug-141-native-guards-verified.json'
if (Test-Path -LiteralPath $outputPath) { throw 'Existing aggregate guard evidence must not be overwritten.' }
$reusedLabels = @('correct-existing','wrong-column','wrong-delete-action','disabled','untrusted')
$originalHarnessPath = 'D:\CP6\tmp\Test-Bug141SqlGuards.ps1'
$originalHarnessHash = 'C91413DB2042430048E80E2A1EBA9FCAF4EEBB03727861353C1D54ABFAFD86DC'
if ((Get-FileHash -LiteralPath $originalHarnessPath -Algorithm SHA256).Hash -cne $originalHarnessHash) {
    throw 'Original failed harness must remain byte-for-byte preserved.'
}
foreach ($label in $definitions.Keys) {
    if ($label -in $reusedLabels) { continue }
    foreach ($path in @(
        "D:\CP6\tmp\bug-141-native-guard-$label.sql",
        "D:\CP6\tmp\bug-141-native-guard-$label.log",
        "D:\CP6\tmp\bug-141-native-guard-$label.json",
        "D:\CP6\tmp\bug-141-sql-guard-$label-evidence.json",
        "D:\CP6\tmp\bug-141-sql-guard-$label-evidence.capture.sql",
        "D:\CP6\tmp\bug-141-sql-compare-after-repeat-to-guard-$label-evidence.json")) {
        if (Test-Path -LiteralPath $path) { throw 'Existing individual guard evidence must not be overwritten; preserve the original execution.' }
    }
}
$header = @'
SET NOCOUNT ON;
SET ANSI_NULLS ON; SET QUOTED_IDENTIFIER ON; SET ANSI_PADDING ON; SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON; SET ARITHABORT ON; SET NUMERIC_ROUNDABORT OFF;
SET XACT_ABORT ON; SET LOCK_TIMEOUT 10000;
IF DB_NAME()<>N'CP6Compat_WP2_20261002_36ef9cae'
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND CONVERT(nvarchar(200),value)=N'__OWNER__')
 OR NOT EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP2')
 THROW 51000,N'Exact owned isolated backup SQL database required.',1;
SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;
BEGIN TRANSACTION;
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>139
 OR NOT EXISTS(SELECT 1 FROM dbo.__EFMigrationsHistory WHERE MigrationId=N'20261002184500_RestoreMissingOrderModelForeignKeys' AND ProductVersion=N'8.0.30')
 THROW 51002,N'Actual accepted Core139 canonical history required.',1;
IF EXISTS(SELECT 1 FROM dbo.T_Order) OR EXISTS(SELECT 1 FROM dbo.T_OrderDetail)
 OR EXISTS(SELECT 1 FROM dbo.T_OrderMaterial) OR EXISTS(SELECT 1 FROM dbo.T_OrderProcess) OR EXISTS(SELECT 1 FROM dbo.T_OrderProcessNote)
 THROW 51003,N'This representative guard fixture requires the five isolated Order tables to be empty.',1;
__VERIFY_FOUR__
PRINT N'CP6_BUG141_OWNED_BASELINE_VERIFIED';
GO
'@
$header = $header.Replace('__OWNER__',$receipt.Owner).Replace('__VERIFY_FOUR__',$verifyFourSql)
$footer = @"
IF @@TRANCOUNT<>1 THROW 51005,N'Exactly one outer fixture transaction required.',1;
$verifyFourSql
IF (SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>139
 THROW 51005,N'Generated FK-only batches must not append migration history.',1;
PRINT N'CP6_BUG141_ALL_FOUR_NATIVE_DEFINITIONS_VERIFIED';
ROLLBACK TRANSACTION;
IF @@TRANCOUNT<>0 THROW 51005,N'Outer fixture rollback required.',1;
PRINT N'CP6_BUG141_EXPLICIT_ROLLBACK_COMPLETE';
GO
"@
$cases = [Collections.Generic.List[object]]::new()
$executedSourceHash = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
$inputMigrationHash = (Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256).Hash
$oracleHash = (Get-FileHash -LiteralPath $oraclePath -Algorithm SHA256).Hash
$baselineHash = (Get-FileHash -LiteralPath $baselinePath -Algorithm SHA256).Hash
foreach ($case in $definitions.GetEnumerator()) {
    $label = $case.Key
    if ($label -in $reusedLabels) {
        $earlierPath = "D:\CP6\tmp\bug-141-native-guard-$label.json"
        $earlier = Get-Content -LiteralPath $earlierPath -Raw | ConvertFrom-Json -Depth 15
        $earlierScript = "D:\CP6\tmp\bug-141-native-guard-$label.sql"
        $earlierLog = "D:\CP6\tmp\bug-141-native-guard-$label.log"
        $earlierCompare = "D:\CP6\tmp\bug-141-sql-compare-after-repeat-to-guard-$label-evidence.json"
        $earlierCapture = "D:\CP6\tmp\bug-141-sql-guard-$label-evidence.json"
        $savedCompare = Get-Content -LiteralPath $earlierCompare -Raw | ConvertFrom-Json -Depth 30
        $savedCapture = Get-Content -LiteralPath $earlierCapture -Raw | ConvertFrom-Json -Depth 40
        $earlierNative = Get-Content -LiteralPath $earlierLog -Raw
        if ($earlier.Name -cne $label -or $earlier.Status -cne 'Passed' -or $earlier.ExpectedSqlError -ne $case.Value.ExpectedError -or
            $earlier.GuardHarnessSourceSha256 -cne $originalHarnessHash -or $earlier.ActualGeneratedMigrationSqlSha256 -cne $inputMigrationHash -or
            $earlier.ExecutedInputSha256 -cne (Get-FileHash -LiteralPath $earlierScript -Algorithm SHA256).Hash -or
            $earlier.NativeOutputSha256 -cne (Get-FileHash -LiteralPath $earlierLog -Algorithm SHA256).Hash -or
            $earlier.RestorationReportSha256 -cne (Get-FileHash -LiteralPath $earlierCompare -Algorithm SHA256).Hash -or
            $earlier.FixtureEstablishedBeforeGuard -ne $true -or $earlier.GuardsOneThroughThreeCompleted -ne $true -or
            $earlier.GuardFourEntered -ne $true -or $earlier.IntendedGuardOutcome -ne $true -or
            $earlier.All352TableHashesCountsHistoryIndexesAndForeignKeysRestored -ne $true -or $earlier.ChecksAfterRollback -ne 9 -or
            @($savedCompare.Checks).Count -ne 9 -or @($savedCompare.Checks | Where-Object Status -CNE 'Passed').Count -ne 0 -or
            $savedCompare.BeforeSha256 -cne $baselineHash -or $savedCompare.AfterSha256 -cne (Get-FileHash -LiteralPath $earlierCapture -Algorithm SHA256).Hash -or
            $savedCompare.ReceiptSha256 -cne $receiptHash -or $savedCompare.FrozenOmissionOracleSha256 -cne $oracleHash -or
            $savedCompare.ActualGeneratedMigrationSqlSha256 -cne $inputMigrationHash -or
            $savedCapture.Database -cne $receipt.SqlServerDatabase -or $savedCapture.OwnerVerified -ne $true -or $savedCapture.ReceiptSha256 -cne $receiptHash -or
            @($savedCapture.Tables).Count -ne 352 -or @($savedCapture.CoreHistory).Count -ne 139 -or @($savedCapture.ForeignKeys).Count -ne 202 -or
            -not $earlierNative.Contains('CP6_BUG141_OWNED_BASELINE_VERIFIED') -or -not $earlierNative.Contains('CP6_BUG141_FIXTURE_READY')) {
            throw 'Earlier successful case input/output/native preservation evidence must be complete and unchanged.'
        }
        $reusedCase = [ordered]@{}
        foreach ($property in $earlier.PSObject.Properties) { $reusedCase[$property.Name] = $property.Value }
        $reusedCase['ReusedEarlierCheckedExecution'] = $true
        $reusedCase['OriginalCaseReportSha256'] = (Get-FileHash -LiteralPath $earlierPath -Algorithm SHA256).Hash
        $reusedCase['VerifiedOriginalHarnessSha256'] = $originalHarnessHash
        $cases.Add($reusedCase)
        Write-Output "Reused original actual success: $label; input/output hashes and 9/9 native restoration verified."
        continue
    }
    $expectedError = $case.Value.ExpectedError
    $path = "D:\CP6\tmp\bug-141-native-guard-$label.sql"
    $logPath = "D:\CP6\tmp\bug-141-native-guard-$label.log"
    $casePath = "D:\CP6\tmp\bug-141-native-guard-$label.json"
    $stage = "guard-$label-evidence"
    $comparisonPath = "D:\CP6\tmp\bug-141-sql-compare-after-repeat-to-$stage.json"
    $script = $header + "`n" + $case.Value.Sql + "`nPRINT N'CP6_BUG141_FIXTURE_READY';`nGO`n"
    for ($ordinal=0; $ordinal -lt 4; $ordinal++) {
        $number = $ordinal+1
        $script += "PRINT N'CP6_BUG141_GUARD_${number}_BEGIN';`nGO`n" + $blocks[$ordinal] +
            "`nGO`nPRINT N'CP6_BUG141_GUARD_${number}_END';`nGO`n"
    }
    $script += $footer
    [IO.File]::WriteAllText($path,$script,[Text.UTF8Encoding]::new($false))
    $nativeOutput = & sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d $receipt.SqlServerDatabase -l 15 -t 120 -b -r 0 -f 65001 -i $path 2>&1
    $nativeExit = $LASTEXITCODE
    $nativeText = (@($nativeOutput | ForEach-Object { [string]$_ }) -join "`n")
    [IO.File]::WriteAllText($logPath,$nativeText,[Text.UTF8Encoding]::new($false))
    $fixtureReady = $nativeText.Contains('CP6_BUG141_OWNED_BASELINE_VERIFIED') -and $nativeText.Contains('CP6_BUG141_FIXTURE_READY')
    $priorGuardsCompleted = (1..3 | Where-Object { -not $nativeText.Contains("CP6_BUG141_GUARD_${_}_END") }).Count -eq 0
    $lastGuardEntered = $nativeText.Contains('CP6_BUG141_GUARD_4_BEGIN')
    $lastGuardCompleted = $nativeText.Contains('CP6_BUG141_GUARD_4_END')
    $expectedOutcome = if ($expectedError -eq 0) {
        $nativeExit -eq 0 -and $fixtureReady -and $priorGuardsCompleted -and $lastGuardEntered -and $lastGuardCompleted -and
        $nativeText.Contains('CP6_BUG141_ALL_FOUR_NATIVE_DEFINITIONS_VERIFIED') -and $nativeText.Contains('CP6_BUG141_EXPLICIT_ROLLBACK_COMPLETE')
    } elseif ($expectedError -eq 51041) {
        $nativeExit -ne 0 -and $fixtureReady -and $priorGuardsCompleted -and $lastGuardEntered -and -not $lastGuardCompleted -and
        $nativeText -match '\b51041\b' -and
        $nativeText.Contains("BUG141 object dbo.$noteName conflicts with the frozen FK definition; existing object preserved.")
    } else {
        $nativeExit -ne 0 -and $fixtureReady -and $priorGuardsCompleted -and $lastGuardEntered -and -not $lastGuardCompleted -and
        $nativeText -match '\b547\b' -and $nativeText.Contains($noteName) -and $nativeText.Contains('T_OrderDetail')
    }
    $restored = $false
    $restoreChecks = 0
    $restoreError = $null
    try {
        & 'D:\CP6\tmp\Capture-Bug141SqlState.ps1' -Stage $stage -ExpectedHistoryCount 139
        & 'D:\CP6\tmp\Compare-Bug141SqlState.ps1' -Before after-repeat -After $stage
        if (Test-Path -LiteralPath $comparisonPath) {
            $comparison = Get-Content -LiteralPath $comparisonPath -Raw | ConvertFrom-Json -Depth 25
            $restoreChecks = @($comparison.Checks).Count
            $restored = $restoreChecks -eq 9 -and @($comparison.Checks | Where-Object Status -CNE 'Passed').Count -eq 0
        }
    } catch {
        $restoreError = 'State capture/comparison did not produce a complete all-Passed restoration proof.'
    }
    $caseReport = [ordered]@{
        Name=$label; Status=if($expectedOutcome -and $restored){'Passed'}else{'Failed'}
        NativeExitCode=$nativeExit; ExpectedSqlError=$expectedError; FixtureEstablishedBeforeGuard=$fixtureReady
        GuardsOneThroughThreeCompleted=$priorGuardsCompleted; GuardFourEntered=$lastGuardEntered; GuardFourCompleted=$lastGuardCompleted
        IntendedGuardOutcome=$expectedOutcome; All352TableHashesCountsHistoryIndexesAndForeignKeysRestored=$restored
        ChecksAfterRollback=$restoreChecks; RestorationError=$restoreError
        ExecutedInputSha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        NativeOutputSha256=(Get-FileHash -LiteralPath $logPath -Algorithm SHA256).Hash
        RestorationReportSha256=if(Test-Path -LiteralPath $comparisonPath){(Get-FileHash -LiteralPath $comparisonPath -Algorithm SHA256).Hash}else{$null}
        ActualGeneratedMigrationSqlSha256=$inputMigrationHash; GuardHarnessSourceSha256=$executedSourceHash
    }
    [IO.File]::WriteAllText($casePath,($caseReport | ConvertTo-Json -Depth 10),[Text.UTF8Encoding]::new($false))
    $cases.Add($caseReport)
    if (-not $expectedOutcome -or -not $restored) { break }
}
$failures = @($cases | Where-Object Status -CNE 'Passed')
$report = [ordered]@{
    Scope='BUG141 actual EF8 four guarded batches only, inside owned SQL Core139 outer transactions: correct replay, seven conflicting FK definitions, late fourth-command atomic rollback, real orphan 547 and missing-four installation; every case verifies full captured restoration'
    Database=$receipt.SqlServerDatabase; FinishedUtc=[DateTime]::UtcNow.ToString('O'); ReceiptSha256=$receiptHash
    ExpectedMigrationId='20261002184500_RestoreMissingOrderModelForeignKeys'; ExpectedHistoryCount=139
    ExpectedCases=11; ExecutedCases=$cases.Count; PassedCases=$cases.Count-$failures.Count; FailedCases=$failures.Count
    Complete=$cases.Count -eq 11 -and $failures.Count -eq 0
    ActualGeneratedMigrationSqlSha256=$inputMigrationHash; GuardHarnessSourceSha256=$executedSourceHash
    IndependentFrozenOmissionOracleSha256=$oracleHash; ActualCore139AfterRepeatBaselineSha256=$baselineHash
    ReusedCases=5; NewlyExecutedCases=$cases.Count-5; OriginalHarnessSha256=$originalHarnessHash
    PreservedOriginalSetupFailure='bug-141-native-guards.json: native NOT FOR REPLICATION fixture also untrusted; original expected trusted fixture failed setup, not a migration guard failure'
    NotForReplicationProof='Combined native NFR=1/untrusted=1 fixture; SQL WITH CHECK CHECK leaves this combination untrusted. Does not isolate NFR alone.'
    Cases=@($cases)
    Boundary='Representative isolated native migration-guard and transactional preservation evidence; no production upgrade/full business acceptance; does not promise DBTS/global identity or sequence rollback. Setup/fixture failures never count as intended guard rejection.'
}
[IO.File]::WriteAllText($outputPath,($report | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
Write-Output "SQL141 guard checks: $($cases.Count-$failures.Count) passed / $($failures.Count) failed; $($cases.Count)/11 executed."
if ($failures.Count -ne 0 -or $cases.Count -ne 11) { throw 'SQL141 representative guard evidence incomplete or failed; all original execution artifacts preserved.' }
