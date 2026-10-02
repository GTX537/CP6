param(
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$Before,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9][a-z0-9-]{0,79}$')][string]$After,
    [switch]$Upgrade
)
$ErrorActionPreference = 'Stop'
$receiptPath = 'D:\CP6\tmp\db-compat.wp2-before-sql-final-upgrade-owned.json'
$receipt = Get-Content -LiteralPath $receiptPath -Raw | ConvertFrom-Json
if ($receipt.Task -cne 'DB-COMPAT-01-WP2' -or $receipt.Owner -cnotmatch '^[a-f0-9]{32}$' -or
    $receipt.SqlServerDatabase -cne 'CP6Compat_WP2_20261002_36ef9cae') { throw 'Exact backup task SQL receipt required.' }
$beforePath = "D:\CP6\tmp\bug-141-sql-$Before.json"
$afterPath = "D:\CP6\tmp\bug-141-sql-$After.json"
$outputPath = "D:\CP6\tmp\bug-141-sql-compare-$Before-to-$After.json"
if (Test-Path -LiteralPath $outputPath) { throw 'Existing comparison evidence must not be overwritten.' }
$first = Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json -Depth 40
$second = Get-Content -LiteralPath $afterPath -Raw | ConvertFrom-Json -Depth 40
$receiptHash = (Get-FileHash -LiteralPath $receiptPath -Algorithm SHA256).Hash
foreach ($capture in @($first,$second)) {
    if ($capture.Database -cne $receipt.SqlServerDatabase -or $capture.OwnerVerified -ne $true -or
        $capture.ReceiptSha256 -cne $receiptHash -or @($capture.Tables).Count -ne 352 -or
        @($capture.Tables | Where-Object { $_.Rows -lt 0 -or $_.ContentSha256 -cnotmatch '^[0-9A-F]{64}$' }).Count -ne 0 -or
        @($capture.Indexes).Count -eq 0 -or @($capture.ForeignKeys).Count -eq 0 -or
        @($capture.CoreHistory).Count -notin @(138,139)) { throw 'Complete owned SQL141 capture with 352 normalized table hashes required.' }
}
if ($first.Stage -cne $Before -or $second.Stage -cne $After) { throw 'Capture stages must match exact input filenames.' }
$migrationId = '20261002184500_RestoreMissingOrderModelForeignKeys'
$previousId = '20261002175000_RestoreMissingOrderModelIndexes'

# Independent frozen model/native gap manifest; do not derive expected relations from the new DDL helper.
$rules = @(
    [ordered]@{Table='T_OrderDetail';Name='FK_T_OrderDetail_T_Order_WebOrderNo';Principal='T_Order';PrincipalClr='Order';Columns=@('WebOrderNo');PrincipalIndex='AK_T_Order_WebOrderNo'},
    [ordered]@{Table='T_OrderMaterial';Name='FK_T_OrderMaterial_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';Principal='T_OrderDetail';PrincipalClr='OrderDetail';Columns=@('WebOrderNo','WebOrderDetailNo','ProductCd');PrincipalIndex='UX_T_OrderDetail_OrderProduct'},
    [ordered]@{Table='T_OrderProcess';Name='FK_T_OrderProcess_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';Principal='T_OrderDetail';PrincipalClr='OrderDetail';Columns=@('WebOrderNo','WebOrderDetailNo','ProductCd');PrincipalIndex='UX_T_OrderDetail_OrderProduct'},
    [ordered]@{Table='T_OrderProcessNote';Name='FK_T_OrderProcessNote_T_OrderDetail_WebOrderNo_WebOrderDetailNo_ProductCd';Principal='T_OrderDetail';PrincipalClr='OrderDetail';Columns=@('WebOrderNo','WebOrderDetailNo','ProductCd');PrincipalIndex='UX_T_OrderDetail_OrderProduct'}
)
$oraclePath = 'D:\CP6\tmp\wp2-sql138-missing-model-keys-fks.json'
$oracle = @(Get-Content -LiteralPath $oraclePath -Raw | ConvertFrom-Json -Depth 15)
if ($oracle.Count -ne 6 -or @($oracle | Where-Object Kind -CEQ 'FK').Count -ne 4 -or @($oracle | Where-Object Kind -CEQ 'K').Count -ne 2) {
    throw 'Frozen four-FK/two-existing-equivalent-key gap oracle required.'
}
foreach ($rule in $rules) {
    $frozen = @($oracle | Where-Object { $_.Kind -ceq 'FK' -and $_.Schema -ceq 'dbo' -and $_.Table -ceq $rule.Table -and $_.Name -ceq $rule.Name })
    $columns = $rule.Columns -join ','
    $contract = "FK:$columns->CP6.Entity.DomainModels.Erp.$($rule.PrincipalClr):${columns}:False:True:Cascade:$($rule.Name)"
    if ($frozen.Count -ne 1 -or $frozen[0].FrozenContract -cne $contract) { throw 'Independent frozen SQL141 relation oracle changed.' }
}
function Json([object]$value) { ConvertTo-Json -InputObject $value -Compress -Depth 35 }
function Key([object]$row,[switch]$TableOnly) {
    if ($TableOnly) { return @([string]$row.Schema,[string]$row.Name) -join ([char]0) }
    return @([string]$row.Schema,[string]$row.Table,[string]$row.Name) -join ([char]0)
}
function Map([object[]]$rows,[switch]$TableOnly) {
    $result = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
    foreach ($row in $rows) {
        $rowKey = Key $row -TableOnly:$TableOnly
        if ($result.ContainsKey($rowKey)) { throw 'Duplicate object identity in SQL141 captured metadata.' }
        $result.Add($rowKey,$row)
    }
    return ,$result
}
$firstTables = Map @($first.Tables) -TableOnly
$secondTables = Map @($second.Tables) -TableOnly
$firstIndexes = Map @($first.Indexes)
$secondIndexes = Map @($second.Indexes)
$firstFks = Map @($first.ForeignKeys)
$secondFks = Map @($second.ForeignKeys)
$checks = [Collections.Generic.List[object]]::new()
function Assert([string]$name,[bool]$condition,[string]$detail) {
    $checks.Add([ordered]@{Name=$name;Status=if($condition){'Passed'}else{'Failed'};Detail=$detail})
}
$changedTables = @()
foreach ($entry in $firstTables.GetEnumerator()) {
    $next = if ($secondTables.ContainsKey($entry.Key)) { $secondTables[$entry.Key] } else { $null }
    $historyExcluded = $Upgrade -and $entry.Value.Schema -ceq 'dbo' -and $entry.Value.Name -ceq '__EFMigrationsHistory'
    if ($null -eq $next -or (-not $historyExcluded -and (Json $entry.Value) -cne (Json $next))) { $changedTables += $entry.Key }
}
Assert 'All352BusinessHashesCountsAndTableSetPreserved' ($firstTables.Count -eq $secondTables.Count -and $changedTables.Count -eq 0) 'All 352 captured table identities/counts/PK-ordered UTF16 JSON hashes retained; only canonical history row/hash is excluded during the deliberate one-row upgrade. Empty-table SQL NULL is normalized to [].'
$beforeHistory = @($first.CoreHistory)
$afterHistory = @($second.CoreHistory)
$historyDelta = if ($Upgrade) { 1 } else { 0 }
$beforeCountExpected = if ($Upgrade) { 138 } else { $beforeHistory.Count }
$expectedBeforeTail = if ($beforeCountExpected -eq 138) { $previousId } else { $migrationId }
$historyValid = $beforeHistory.Count -eq $beforeCountExpected -and $beforeHistory[-1].MigrationId -ceq $expectedBeforeTail -and
    $afterHistory.Count -eq $beforeHistory.Count+$historyDelta -and
    (Json $beforeHistory) -ceq (Json @($afterHistory | Select-Object -First $beforeHistory.Count)) -and
    ($firstTables[[string]('dbo'+[char]0+'__EFMigrationsHistory')].Rows -eq $beforeHistory.Count) -and
    ($secondTables[[string]('dbo'+[char]0+'__EFMigrationsHistory')].Rows -eq $afterHistory.Count)
if ($Upgrade) { $historyValid = $historyValid -and $afterHistory[-1].MigrationId -ceq $migrationId -and $afterHistory[-1].ProductVersion -ceq '8.0.30' }
Assert 'CanonicalCoreHistoryExact138To139OrIdentical' $historyValid "Original MigrationId/ProductVersion pairs exact; expected append=$historyDelta, before=$($beforeHistory.Count), after=$($afterHistory.Count)."
$indexChanged = @($firstIndexes.GetEnumerator() | Where-Object { -not $secondIndexes.ContainsKey($_.Key) -or (Json $_.Value) -cne (Json $secondIndexes[$_.Key]) })
Assert 'EveryIndexDefinitionAndIdentityUnchanged' ($firstIndexes.Count -eq $secondIndexes.Count -and $indexChanged.Count -eq 0) "All $($firstIndexes.Count) complete index records, including historical unique aliases, remain exact; no new/drop/rebuild permitted."
$fkChanged = @($firstFks.GetEnumerator() | Where-Object { -not $secondFks.ContainsKey($_.Key) -or (Json $_.Value) -cne (Json $secondFks[$_.Key]) })
Assert 'EveryOriginalForeignKeyDefinitionAndIdentityUnchanged' ($fkChanged.Count -eq 0) "All $($firstFks.Count) pre-existing FK records retain object identity, schemas, principals/indexes, ordered columns/actions/enabled/trust/NFR."
$addedFks = @($secondFks.Keys | Where-Object { -not $firstFks.ContainsKey($_) } | Sort-Object)
$expectedAdded = if ($Upgrade) { @($rules | ForEach-Object { @('dbo',$_.Table,$_.Name) -join ([char]0) } | Sort-Object) } else { @() }
Assert 'OnlyFourFrozenForeignKeysAddedOrNoChanges' (($addedFks -join '|') -ceq ($expectedAdded -join '|') -and $secondFks.Count -eq $firstFks.Count+$expectedAdded.Count) "Expected only $($expectedAdded.Count) exact frozen new FKs; actual added=$($addedFks.Count). No additional key/index/object mutation is accepted."
foreach ($rule in $rules) {
    $ruleKey = @('dbo',$rule.Table,$rule.Name) -join ([char]0)
    $row = if ($secondFks.ContainsKey($ruleKey)) { $secondFks[$ruleKey] } else { $null }
    if ($afterHistory.Count -eq 138) {
        Assert "FrozenForeignKeyAbsentAtCore138.$($rule.Table)" ($null -eq $row) 'Rollback/no-change Core138 retains the four deliberate pre-upgrade FK omissions.'
        continue
    }
    $columnsValid = $null -ne $row -and @($row.Columns).Count -eq $rule.Columns.Count
    if ($columnsValid) {
        for ($ordinal=0; $ordinal -lt $rule.Columns.Count; $ordinal++) {
            $column = @($row.Columns)[$ordinal]
            if ($column.Ordinal -ne $ordinal+1 -or $column.DependentColumn -cne $rule.Columns[$ordinal] -or $column.PrincipalColumn -cne $rule.Columns[$ordinal]) { $columnsValid=$false }
        }
    }
    $valid = $null -ne $row -and $row.Schema -ceq 'dbo' -and $row.Table -ceq $rule.Table -and $row.Name -ceq $rule.Name -and
        $row.PrincipalSchema -ceq 'dbo' -and $row.PrincipalTable -ceq $rule.Principal -and $row.PrincipalIndex -ceq $rule.PrincipalIndex -and
        $row.DeleteAction -eq 1 -and $row.DeleteActionName -ceq 'CASCADE' -and $row.UpdateAction -eq 0 -and $row.UpdateActionName -ceq 'NO_ACTION' -and
        $row.Disabled -eq $false -and $row.NotTrusted -eq $false -and $row.NotForReplication -eq $false -and $row.SystemNamed -eq $false -and $columnsValid
    Assert "FrozenForeignKey.$($rule.Table).$($rule.Name)" $valid 'Native exact dbo parent/principal and ordered column mapping, existing global unique index, CASCADE delete/NO_ACTION update, enabled/trusted/non-NFR match the independent frozen contract.'
}
$generatedPath = 'D:\CP6\tmp\bug-141-generated-migration.sql'
$report = [ordered]@{
    Scope='BUG141 captured SQL138-to139 upgrade or identical repeat/guard rollback; all 352 table hashes/counts, indexes and original FK definitions retained; only four exact frozen FKs/history append allowed'
    StartedFrom=$first.Stage; ComparedTo=$second.Stage; Database=$first.Database; Upgrade=$Upgrade.IsPresent
    CapturedUtc=[DateTime]::UtcNow.ToString('O'); ExpectedMigrationId=$migrationId; ExpectedProductVersion='8.0.30'
    BeforeSha256=(Get-FileHash -LiteralPath $beforePath -Algorithm SHA256).Hash; AfterSha256=(Get-FileHash -LiteralPath $afterPath -Algorithm SHA256).Hash
    FrozenOmissionOracleSha256=(Get-FileHash -LiteralPath $oraclePath -Algorithm SHA256).Hash
    ActualGeneratedMigrationSqlSha256=if(Test-Path -LiteralPath $generatedPath){(Get-FileHash -LiteralPath $generatedPath -Algorithm SHA256).Hash}else{$null}
    ReceiptSha256=$receiptHash; Checks=@($checks)
    Boundary='Captured isolated database evidence only; no production upgrade/business acceptance, no DBTS/global identity or sequence rollback promise.'
}
[IO.File]::WriteAllText($outputPath, ($report | ConvertTo-Json -Depth 25), [Text.UTF8Encoding]::new($false))
$failures = @($checks | Where-Object Status -CEQ 'Failed')
Write-Output "Native SQL141 comparison: $($checks.Count-$failures.Count) passed / $($failures.Count) failed."
if ($failures.Count) { $failures | Select-Object Name,Detail | ConvertTo-Json -Depth 4; exit 1 }
