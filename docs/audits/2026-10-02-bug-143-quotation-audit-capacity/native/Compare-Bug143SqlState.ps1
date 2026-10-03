param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$Before,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$After,
    [switch]$Upgrade
)
$ErrorActionPreference='Stop'
$taskRoot='D:\CP6\tmp'
$beforePath=Join-Path $taskRoot "bug-143-sql-$Before.json"
$afterPath=Join-Path $taskRoot "bug-143-sql-$After.json"
$outputPath=Join-Path $taskRoot "bug-143-sql-compare-$Before-to-$After.json"
if (Test-Path -LiteralPath $outputPath) { throw 'Existing BUG143 comparison evidence must not be overwritten.' }
$first=Get-Content -LiteralPath $beforePath -Raw|ConvertFrom-Json -Depth 65
$second=Get-Content -LiteralPath $afterPath -Raw|ConvertFrom-Json -Depth 65
$receiptPath=Join-Path $taskRoot 'db-compat.wp2-before-sql-final-upgrade-owned.json'
$receiptSha256=(Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
$oraclePath='D:\CP6\tmp\worktrees\db-compat-wp2-20261002\CP6.Tests\Persistence\Fixtures\sql-server-d6074aaa-contracts.json'
$nativeAuditPath=Join-Path $taskRoot 'wp2-sql-column-audit-report.json'
$oracleSha256=(Get-FileHash -LiteralPath $oraclePath -Algorithm SHA256).Hash
$nativeAuditSha256=(Get-FileHash -LiteralPath $nativeAuditPath -Algorithm SHA256).Hash
if ($oracleSha256 -cne '441CBCB228624370995EE735C8CEC52104A8B1481A5A6457589547B78EC52618' -or
    $nativeAuditSha256 -cne '81BE921FEC03AB8F4D316A6AEFD0320C88F93CDB77BFF3CA8C5A9EE9140AA94C') {
    throw 'Independent frozen d607/native six-column audit bytes changed.'
}
$oracle=Get-Content -LiteralPath $oraclePath -Raw|ConvertFrom-Json -Depth 15
$audit=Get-Content -LiteralPath $nativeAuditPath -Raw|ConvertFrom-Json -Depth 30
$rules=@(
    [ordered]@{Schema='dbo';Table='T_Quotation';Column='Creator'},
    [ordered]@{Schema='dbo';Table='T_Quotation';Column='Modifier'},
    [ordered]@{Schema='dbo';Table='T_QuotationCalc';Column='Creator'},
    [ordered]@{Schema='dbo';Table='T_QuotationCalc';Column='Modifier'},
    [ordered]@{Schema='dbo';Table='T_QuotationDetail';Column='Creator'},
    [ordered]@{Schema='dbo';Table='T_QuotationDetail';Column='Modifier'}
)
if ($oracle.BaseSha -cne 'd6074aaad3098b61adaeda902b92bf24b3eb0c04' -or
    $audit.BaseSha -cne $oracle.BaseSha -or $audit.Database -cne 'CP6Compat_WP2_20261002_36ef9cae' -or
    @($audit.Differences).Count -ne 6 -or
    $audit.NativeCaptureSha256 -cne 'FFD3CB71D3DCE7FB7F03722326BE4006629CD8A41F85908A0C4C657177FC73DF') {
    throw 'Exact independent native six-capacity-only evidence required.'
}
foreach($rule in $rules){
    $entry=@($oracle.Contexts.Core.psobject.Properties|Where-Object {
        $lines=([string]$_.Value).Split([char]10)
        $lines[2] -ceq $rule.Table -and ($lines[1] -ceq '' -or $lines[1] -ceq 'dbo')
    })
    if($entry.Count -ne 1){throw 'Missing/duplicate exact frozen quotation entity.'}
    $prefix="P:$($rule.Column)|System.String|True|100||,||$($rule.Column):nvarchar(100):False:Never:"
    if(-not (([string]$entry[0].Value).Split([char]10) -ccontains $prefix)){throw 'Frozen nullable Unicode100 property contract differs.'}
    $difference=@($audit.Differences|Where-Object {$_.Schema -ceq $rule.Schema -and $_.Table -ceq $rule.Table -and $_.Column -ceq $rule.Column})
    if($difference.Count -ne 1 -or $difference[0].Kind -cne 'Capacity' -or
        $difference[0].Expected -ne 100 -or $difference[0].Actual -ne -1 -or
        $difference[0].FrozenStoreType -cne 'nvarchar(100)' -or $difference[0].NativeMaxLengthBytes -ne -1) {
        throw 'Native frozen capacity delta manifest differs.'
    }
}
foreach($capture in @($first,$second)){
    if($capture.Status -cne 'Captured' -or $capture.OwnerVerified -ne $true -or
        $capture.Database -cne 'CP6Compat_WP2_20261002_36ef9cae' -or $capture.Task -cne 'DB-COMPAT-01-WP2' -or
        $capture.ReceiptSha256 -cne $receiptSha256 -or @($capture.Tables).Count -ne 352 -or
        @($capture.Columns).Count -ne 6832 -or @($capture.Indexes).Count -ne 1378 -or
        @($capture.ForeignKeys).Count -ne 202 -or @($capture.TableMetadata).Count -ne 352 -or
        @($capture.CoreHistory).Count -notin @(139,140) -or
        @($capture.Tables|Where-Object {$null -eq $_.Rows -or $_.Rows -lt 0 -or $_.ContentSha256 -cnotmatch '^[0-9A-F]{64}$'}).Count -ne 0) {
        throw 'Complete owned BUG143 capture with exact catalog counts and normalized non-NULL hashes required.'
    }
    if(-not(Test-Path -LiteralPath $capture.InputManifestPath -PathType Leaf) -or
        $capture.InputSha256 -cne (Get-FileHash -LiteralPath $capture.InputManifestPath -Algorithm SHA256).Hash){
        throw 'Captured native input manifest changed or is unavailable.'
    }
}
if($first.Stage -cne $Before -or $second.Stage -cne $After){throw 'Capture stage/filename mismatch.'}
if($first.InputSha256 -cne $second.InputSha256 -or $first.InputManifestPath -cne $second.InputManifestPath){
    throw 'Before/after evidence must use the same exact native input manifest.'
}
function Canonical([object]$value){ConvertTo-Json -InputObject $value -Depth 65 -Compress}
function Identity([object]$row,[ValidateSet('Table','Column','Object')][string]$Kind){
    if($Kind -ceq 'Table'){return @([string]$row.Schema,[string]$row.Name)-join([char]0)}
    if($Kind -ceq 'Column'){return @([string]$row.Schema,[string]$row.Table,[string]$row.Column)-join([char]0)}
    return @([string]$row.Schema,[string]$row.Table,[string]$row.name)-join([char]0)
}
function Map([object[]]$rows,[string]$Kind){
    $map=[Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach($row in $rows){$key=Identity $row $Kind;if($map.ContainsKey($key)){throw 'Duplicate captured identity.'};$map.Add($key,$row)}
    return ,$map
}
$checks=[Collections.Generic.List[object]]::new()
function Assert([string]$Name,[bool]$Condition,[string]$Detail){
    $checks.Add([ordered]@{Name=$Name;Status=if($Condition){'Passed'}else{'Failed'};Detail=$Detail})
}
$firstTables=Map @($first.Tables) 'Table';$secondTables=Map @($second.Tables) 'Table'
$historyKey=@('dbo','__EFMigrationsHistory')-join([char]0)
$tableChanges=@($firstTables.Keys|Where-Object {
    -not $secondTables.ContainsKey($_) -or
    (-not($Upgrade -and $_ -ceq $historyKey) -and (Canonical $firstTables[$_]) -cne (Canonical $secondTables[$_]))
})
Assert 'All352TableIdentitiesDataCountsAndTokensPreserved' ($firstTables.Count -eq $secondTables.Count -and $tableChanges.Count -eq 0) 'Only the canonical history table hash/count is exempt during the one-row upgrade. Every other PK-ordered hash/count, including stored tokens and nonempty quotations, must be identical.'
Assert 'ServerVersionAndDatabaseCollationUnchanged' ($first.Version -ceq $second.Version -and $first.DatabaseCollation -ceq $second.DatabaseCollation) 'No server/provider/collation reference drift.'
$migrationId='20261002193500_RestoreQuotationAuditColumnCapacity'
$previousId='20261002184500_RestoreMissingOrderModelForeignKeys'
$beforeHistory=@($first.CoreHistory);$afterHistory=@($second.CoreHistory)
$historyValid=($first.ExpectedHistoryCount -eq $beforeHistory.Count -and $second.ExpectedHistoryCount -eq $afterHistory.Count -and
    $firstTables[$historyKey].Rows -eq $beforeHistory.Count -and $secondTables[$historyKey].Rows -eq $afterHistory.Count)
if($Upgrade){
    $historyValid=$historyValid -and $beforeHistory.Count -eq 139 -and $afterHistory.Count -eq 140 -and
        $beforeHistory[-1].MigrationId -ceq $previousId -and
        (Canonical $beforeHistory) -ceq (Canonical @($afterHistory|Select-Object -First 139)) -and
        $afterHistory[-1].MigrationId -ceq $migrationId -and $afterHistory[-1].ProductVersion -ceq '8.0.30'
}else{
    $expectedTail=if($beforeHistory.Count -eq 139){$previousId}else{$migrationId}
    $historyValid=$historyValid -and (Canonical $beforeHistory) -ceq (Canonical $afterHistory) -and
        $beforeHistory[-1].MigrationId -ceq $expectedTail
}
Assert 'CoreHistoryExactly139To140OrIdentical' $historyValid 'Exactly one specified MigrationId/ProductVersion append on upgrade; repeat/guard restoration has no history difference.'
$tableMetadataChanges=[Collections.Generic.List[object]]::new()
if($Upgrade){
    $firstTableMetadata=@($first.TableMetadata)
    $secondTableMetadata=@($second.TableMetadata)
    $metadataValid=$firstTableMetadata.Count -eq 352 -and $secondTableMetadata.Count -eq 352
    $quotationNames=@('T_Quotation','T_QuotationCalc','T_QuotationDetail')
    for($ordinal=0;$ordinal -lt $firstTableMetadata.Count;$ordinal++){
        $oldTable=$firstTableMetadata[$ordinal]
        $newTable=if($ordinal -lt $secondTableMetadata.Count){$secondTableMetadata[$ordinal]}else{$null}
        if($null -eq $newTable -or $oldTable.Schema -cne $newTable.Schema -or $oldTable.Table -cne $newTable.Table){
            $metadataValid=$false
            continue
        }
        if($oldTable.Schema -ceq 'dbo' -and $quotationNames -ccontains $oldTable.Table){
            $oldNormalized=$oldTable|ConvertTo-Json -Depth 65|ConvertFrom-Json -Depth 65
            $oldNormalized.modify_date=$newTable.modify_date
            $dateChanged=(Canonical $oldTable.modify_date) -cne (Canonical $newTable.modify_date)
            $forwardDate=$null -ne $oldTable.modify_date -and $null -ne $newTable.modify_date -and
                [DateTime]$newTable.modify_date -gt [DateTime]$oldTable.modify_date
            $restExact=(Canonical $oldNormalized) -ceq (Canonical $newTable)
            if(-not($dateChanged -and $forwardDate -and $restExact)){$metadataValid=$false}
            $tableMetadataChanges.Add([ordered]@{
                Schema='dbo';Table=$oldTable.Table;Field='modify_date'
                Before=$oldTable.modify_date;After=$newTable.modify_date
                StrictlyAdvanced=$forwardDate;EveryOtherTableFacetExact=$restExact
            })
        }elseif((Canonical $oldTable) -cne (Canonical $newTable)){$metadataValid=$false}
    }
    Assert 'OnlyThreeQuotationTableModifyDatesStrictlyAdvance' ($metadataValid -and $tableMetadataChanges.Count -eq 3) 'ALTER COLUMN advances only these three table modify_date values. All other table fields, IDs and unaffected table records remain exact.'
}else{
    Assert 'AllTableMetadataExact' ((Canonical @($first.TableMetadata)) -ceq (Canonical @($second.TableMetadata))) 'Repeat/guard restoration permits no table modify_date or other metadata change.'
}
foreach($section in @('Indexes','ForeignKeys','Checks','Triggers')){
    Assert "All$($section)Exact" ((Canonical @($first.$section)) -ceq (Canonical @($second.$section))) 'Complete captured metadata and ordered nested records are exact; missing/new/rebuilt objects are rejected.'
}
$firstColumns=Map @($first.Columns) 'Column';$secondColumns=Map @($second.Columns) 'Column'
$expectedDeltaKeys=if($Upgrade){@($rules|ForEach-Object {@($_.Schema,$_.Table,$_.Column)-join([char]0)}|Sort-Object)}else{@()}
$actualDeltaKeys=@($firstColumns.Keys|Where-Object {-not $secondColumns.ContainsKey($_) -or (Canonical $firstColumns[$_]) -cne (Canonical $secondColumns[$_])}|Sort-Object)
Assert 'OnlySixExactColumnChangesOrNoColumnChange' ($firstColumns.Count -eq $secondColumns.Count -and
    ($actualDeltaKeys-join '|') -ceq ($expectedDeltaKeys-join '|')) "Predeclared allowed column delta count=$($expectedDeltaKeys.Count); no extra/missing tuple is accepted."
foreach($rule in $rules){
    $key=@($rule.Schema,$rule.Table,$rule.Column)-join([char]0)
    $old=if($firstColumns.ContainsKey($key)){$firstColumns[$key]}else{$null}
    $new=if($secondColumns.ContainsKey($key)){$secondColumns[$key]}else{$null}
    $valid=$null -ne $old -and $null -ne $new -and $old.TypeName -ceq 'nvarchar' -and $new.TypeName -ceq 'nvarchar' -and
        $old.TypeUserDefined -eq $false -and $new.TypeUserDefined -eq $false -and
        $old.is_nullable -eq $true -and $new.is_nullable -eq $true -and
        $old.is_identity -eq $false -and $new.is_identity -eq $false -and
        $old.is_computed -eq $false -and $new.is_computed -eq $false -and
        $old.generated_always_type -eq 0 -and $new.generated_always_type -eq 0 -and
        $null -eq $old.encryption_type -and $null -eq $new.encryption_type
    if($Upgrade -and $valid){
        $oldNormalized=$old|ConvertTo-Json -Depth 65|ConvertFrom-Json -Depth 65
        $oldNormalized.max_length=200
        $valid=$old.max_length -eq -1 -and $new.max_length -eq 200 -and
            (Canonical $oldNormalized) -ceq (Canonical $new)
    }elseif($valid){$valid=(Canonical $old) -ceq (Canonical $new)}
    Assert "FrozenColumn.$($rule.Table).$($rule.Column)" $valid 'Only native max_length -1 to 200 is allowed on upgrade; column_id/type/nullable/collation/identity/computed/default/generation and all remaining sys.columns fields must remain exact.'
}
$failures=@($checks|Where-Object Status -CEQ 'Failed')
$report=[ordered]@{
    Scope='BUG143 SQL139->140 capacity-only upgrade or exact repeat/guard restoration; immutable six-column oracle from d607 plus independent native column audit.'
    StartedFrom=$Before;ComparedTo=$After;Upgrade=$Upgrade.IsPresent;Database=$first.Database
    CapturedUtc=[DateTime]::UtcNow.ToString('O');ExpectedMigrationId=$migrationId;ExpectedProductVersion='8.0.30'
    ExpectedColumnDeltaCount=$expectedDeltaKeys.Count;ExpectedHistoryAppendCount=if($Upgrade){1}else{0}
    BeforeSha256=(Get-FileHash -LiteralPath $beforePath -Algorithm SHA256).Hash
    AfterSha256=(Get-FileHash -LiteralPath $afterPath -Algorithm SHA256).Hash
    ReceiptSha256=$receiptSha256;InputSha256=$first.InputSha256
    FrozenOracleSha256=$oracleSha256;NativeSixColumnAuditSha256=$nativeAuditSha256
    TableMetadataChanges=@($tableMetadataChanges)
    Checks=@($checks);Passed=$checks.Count-$failures.Count;Failed=$failures.Count
    Boundary='Task-owned isolated SQL36ef only. No production migration/acceptance; no row values/credentials emitted. Whole-server DBTS/sequence rollback is not asserted.'
}
[IO.File]::WriteAllText($outputPath,($report|ConvertTo-Json -Depth 25),[Text.UTF8Encoding]::new($false))
Write-Output "BUG143 native comparison: $($report.Passed) passed / $($report.Failed) failed; allowed columns=$($expectedDeltaKeys.Count)."
if($failures.Count){$failures|Select-Object Name,Detail|ConvertTo-Json -Depth 4;exit 1}
