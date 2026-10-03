$ErrorActionPreference = 'Stop'
$taskRoot = 'D:\CP6\tmp'
$outputPath = Join-Path $taskRoot 'bug-143-cleanup-restoration-compare.json'
if (Test-Path -LiteralPath $outputPath) { throw 'Existing cleanup comparison evidence must not be overwritten.' }
$startedUtc = [DateTime]::UtcNow.ToString('O')
$stageNames = @('original-empty', 'after-repeat-final', 'after-cleanup-final')
$expectedInputHashes = @(
    '5AE3F3E93AFAFAC07B5226E9B329ADB72AC7129F62430AA3FA488136E3C6A7ED',
    'E994176DEA7106D42A8437EE08B7254917EC534B1BDD033553672187AAC0CFF5',
    'E994176DEA7106D42A8437EE08B7254917EC534B1BDD033553672187AAC0CFF5'
)
$inputPaths = @(
    (Join-Path $taskRoot 'bug-143-actual-input.json'),
    (Join-Path $taskRoot 'bug-143-actual-input-final.json'),
    (Join-Path $taskRoot 'bug-143-actual-input-final.json')
)
$capturePaths = @($stageNames | ForEach-Object { Join-Path $taskRoot "bug-143-sql-$_.json" })
$queryPaths = @($stageNames | ForEach-Object { Join-Path $taskRoot "bug-143-sql-$_.capture.sql" })
$captureScriptPath = Join-Path $taskRoot 'Capture-Bug143SqlState.ps1'
$cleanupSummaryPath = Join-Path $taskRoot 'bug-143-quotation-fixture-cleanup.json'
$fixtureScriptPath = Join-Path $taskRoot 'Invoke-Bug143QuotationFixture.ps1'
$fixtureLogPath = Join-Path $taskRoot 'bug-143-quotation-fixture-cleanup.log'
$allInputPaths = @(@($capturePaths) + @($queryPaths) + @($inputPaths) + @($captureScriptPath, $cleanupSummaryPath, $fixtureScriptPath, $fixtureLogPath) | Sort-Object -Unique)
$inputArtifactHashes = @($allInputPaths | ForEach-Object {
    if (-not (Test-Path -LiteralPath $_ -PathType Leaf)) { throw 'An explicit comparison input is unavailable.' }
    [ordered]@{ Path = $_; Sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash }
})
$states = @($capturePaths | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json -Depth 65 })
$nativeInputs = @($inputPaths | ForEach-Object { Get-Content -LiteralPath $_ -Raw | ConvertFrom-Json -Depth 30 })
$cleanup = Get-Content -LiteralPath $cleanupSummaryPath -Raw | ConvertFrom-Json -Depth 15
$queries = @($queryPaths | ForEach-Object { [IO.File]::ReadAllText($_) })
$captureSource = [IO.File]::ReadAllText($captureScriptPath)
$checks = [Collections.Generic.List[object]]::new()
function Add-Check([string]$Name, [bool]$Condition, [string]$Detail) {
    $checks.Add([ordered]@{ Name = $Name; Status = if ($Condition) { 'Passed' } else { 'Failed' }; Detail = $Detail })
}
function Canonical([object]$Value) { ConvertTo-Json -InputObject $Value -Depth 65 -Compress }
function Text-Sha256([string]$Value) { [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))) }
function Table-Map([object[]]$Rows) {
    $result = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($row in $Rows) {
        $key = @([string]$row.Schema, [string]$row.Name) -join ([char]0)
        if ($result.ContainsKey($key)) { throw 'A captured table identity occurs more than once.' }
        $result.Add($key, $row)
    }
    return ,$result
}
$migrationId = '20261002193500_RestoreQuotationAuditColumnCapacity'
$priorMigrationId = '20261002184500_RestoreMissingOrderModelForeignKeys'
$tableMaps = @($states | ForEach-Object { Table-Map @($_.Tables) })
$historyKey = @('dbo', '__EFMigrationsHistory') -join ([char]0)
$quotationNames = @('T_Quotation', 'T_QuotationCalc', 'T_QuotationDetail')
$quotationKeys = @($quotationNames | ForEach-Object { @('dbo', $_) -join ([char]0) })
$captureWitnesses = [Collections.Generic.List[object]]::new()
for ($ordinal = 0; $ordinal -lt 3; $ordinal++) {
    $state = $states[$ordinal]
    $historyCount = if ($ordinal -eq 0) { 139 } else { 140 }
    $lastMigration = if ($ordinal -eq 0) { $priorMigrationId } else { $migrationId }
    $validCapture = $state.Stage -ceq $stageNames[$ordinal] -and $state.Status -ceq 'Captured' -and
        $state.OwnerVerified -eq $true -and $state.Task -ceq 'DB-COMPAT-01-WP2' -and
        $state.Database -ceq 'CP6Compat_WP2_20261002_36ef9cae' -and
        @($state.Tables).Count -eq 352 -and @($state.TableMetadata).Count -eq 352 -and
        @($state.Columns).Count -eq 6832 -and @($state.Indexes).Count -eq 1378 -and
        @($state.ForeignKeys).Count -eq 202 -and @($state.Checks).Count -eq 127 -and
        @($state.Triggers).Count -eq 2 -and @($state.CoreHistory).Count -eq $historyCount -and
        $state.ExpectedHistoryCount -eq $historyCount -and
        $state.CoreHistory[-1].MigrationId -ceq $lastMigration -and
        $state.CoreHistory[-1].ProductVersion -ceq '8.0.30' -and
        @($state.Tables | Where-Object { $null -eq $_.Rows -or $_.Rows -lt 0 -or $_.ContentSha256 -cnotmatch '^[0-9A-F]{64}$' }).Count -eq 0
    Add-Check "Capture.$($stageNames[$ordinal]).CompleteOwnedNativeState" $validCapture 'Original stage/owner assertion/SQL36ef task and exact 352/6832/1378/202/127/2 catalog counts, normalized non-null data hashes and expected native history are required.'
    $actualInputSha = (Get-FileHash -LiteralPath $inputPaths[$ordinal] -Algorithm SHA256).Hash
    Add-Check "Capture.$($stageNames[$ordinal]).ExactNativeInputManifest" (
        $state.InputManifestPath -ceq $inputPaths[$ordinal] -and
        $state.InputSha256 -ceq $expectedInputHashes[$ordinal] -and
        $actualInputSha -ceq $expectedInputHashes[$ordinal]
    ) 'Each original input manifest is independently verified against its own actual bytes; original and final inputs remain distinct.'
    $actualQuerySha = (Get-FileHash -LiteralPath $queryPaths[$ordinal] -Algorithm SHA256).Hash
    Add-Check "Capture.$($stageNames[$ordinal]).OriginalQueryBytesMatchRecordedSha" ($actualQuerySha -ceq $state.QuerySha256) 'Verify the unmodified original native query file against the capture-recorded digest.'
    $captureWitnesses.Add([ordered]@{
        Stage = $state.Stage; CapturePath = $capturePaths[$ordinal]
        CaptureSha256 = (Get-FileHash -LiteralPath $capturePaths[$ordinal] -Algorithm SHA256).Hash
        CapturedUtc = $state.CapturedUtc; OwnerVerified = $state.OwnerVerified; Task = $state.Task
        Database = $state.Database; ExpectedHistoryCount = $historyCount
        ReceiptSha256RecordedInCapture = $state.ReceiptSha256
        InputManifestPath = $state.InputManifestPath; RecordedInputSha256 = $state.InputSha256; ActualInputSha256 = $actualInputSha
        QueryPath = $queryPaths[$ordinal]; RecordedQuerySha256 = $state.QuerySha256; ActualQuerySha256 = $actualQuerySha
        ApiRuntimeSha256 = @($nativeInputs[$ordinal].Runtime | Where-Object Name -CEQ 'CP6.WebApi')[0].Sha256
        CoreRuntimeSha256 = @($nativeInputs[$ordinal].Runtime | Where-Object Name -CEQ 'CP6.Core')[0].Sha256
        RuntimeUse = if ($ordinal -eq 0) { 'Historical native raw-data baseline only; original failing implementation runtime is not final-runtime acceptance.' } else { 'Parent final-runtime capture after actual upgrade/repeat or fixture cleanup; this script performs no new application execution.' }
    })
}
$sameReference = $states[0].ReceiptSha256 -cmatch '^[0-9A-F]{64}$'
foreach ($state in $states) {
    $sameReference = $sameReference -and $state.ReceiptSha256 -ceq $states[0].ReceiptSha256 -and
        $state.Version -ceq $states[0].Version -and $state.DatabaseCollation -ceq $states[0].DatabaseCollation -and
        ([string]$state.Version).StartsWith('16.', [StringComparison]::Ordinal)
}
Add-Check 'AllCapturesUseSameRecordedReceiptServerAndDatabaseReference' $sameReference 'Compare only recorded receipt hash/server/collation fields; no credential-bearing receipt is read.'
$templateMatches = [regex]::Matches($captureSource, '(?s)\$query\s*=\s*@''\r?\n(.*?)\r?\n''@')
$templateValid = $templateMatches.Count -eq 1
if ($templateValid) {
    $template = $templateMatches[0].Groups[1].Value
    for ($ordinal = 0; $ordinal -lt 3; $ordinal++) {
        $historyCount = if ($ordinal -eq 0) { '139' } else { '140' }
        $lastMigration = if ($ordinal -eq 0) { $priorMigrationId } else { $migrationId }
        $expectedQuery = $template.Replace('__HISTORY_COUNT__', $historyCount).Replace('__EXPECTED_LAST__', $lastMigration)
        $templateValid = $templateValid -and $expectedQuery -ceq $queries[$ordinal] -and
            (Text-Sha256 $expectedQuery) -ceq $states[$ordinal].QuerySha256
    }
}
Add-Check 'CurrentCaptureScriptQueryTemplateMatchesAllThreeOriginalQueries' $templateValid 'Current capture script body renders the exact original query bytes for declared 139/140 history guards. Capture artifacts did not record a historical capture-script SHA; this is a current source witness, not an invented historical script hash.'
$oldCount = '(SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>139'
$finalCount = '(SELECT COUNT_BIG(*) FROM dbo.__EFMigrationsHistory)<>140'
$reverseQuery = $queries[1].Replace($finalCount, $oldCount).Replace($migrationId, $priorMigrationId)
$exactGuardVariants = ([regex]::Matches($queries[0], [regex]::Escape($oldCount))).Count -eq 1 -and
    ([regex]::Matches($queries[0], [regex]::Escape($priorMigrationId))).Count -eq 1 -and
    ([regex]::Matches($queries[1], [regex]::Escape($finalCount))).Count -eq 1 -and
    ([regex]::Matches($queries[1], [regex]::Escape($migrationId))).Count -eq 1 -and
    $reverseQuery -ceq $queries[0] -and (Text-Sha256 $reverseQuery) -ceq $states[0].QuerySha256 -and
    $queries[1] -ceq $queries[2] -and $states[1].QuerySha256 -ceq $states[2].QuerySha256 -and
    $states[0].QuerySha256 -cne $states[1].QuerySha256
Add-Check 'QueryBodyExactWithOnlyTwoExplicitHistoryGuardArgumentsDifferent' $exactGuardVariants 'The original 139 and final 140 query SHA values differ. Reverse the one history-count literal and one expected-tail literal in memory to reproduce the original raw query and its SHA; all measurement SQL is exact. No artifact is changed and no same-input claim is made.'
Add-Check 'FinalCaptureInputsExactAndHistoricalInputHonestlyDistinct' (
    $states[0].InputSha256 -cne $states[1].InputSha256 -and $states[1].InputSha256 -ceq $states[2].InputSha256 -and
    $captureWitnesses[0].CoreRuntimeSha256 -cne $captureWitnesses[1].CoreRuntimeSha256 -and
    $captureWitnesses[1].CoreRuntimeSha256 -ceq $captureWitnesses[2].CoreRuntimeSha256 -and
    $captureWitnesses[0].ApiRuntimeSha256 -cne $captureWitnesses[1].ApiRuntimeSha256 -and
    $captureWitnesses[1].ApiRuntimeSha256 -ceq $captureWitnesses[2].ApiRuntimeSha256
) 'The old raw-data baseline has its original input and runtime identity. Repeat/cleanup share the final input/runtime identity; this read-only comparison is not a new API or migration execution.'
$tableIdentitiesExact = $tableMaps[0].Count -eq 352 -and $tableMaps[1].Count -eq 352 -and $tableMaps[2].Count -eq 352
foreach ($key in $tableMaps[0].Keys) { $tableIdentitiesExact = $tableIdentitiesExact -and $tableMaps[1].ContainsKey($key) -and $tableMaps[2].ContainsKey($key) }
Add-Check 'All352TableIdentitiesRemainExact' $tableIdentitiesExact 'Require ordinal schema/name identities without extra or missing tables.'
$businessDifferences = @($tableMaps[0].Keys | Where-Object {
    $_ -cne $historyKey -and (-not $tableMaps[2].ContainsKey($_) -or (Canonical $tableMaps[0][$_]) -cne (Canonical $tableMaps[2][$_]))
})
Add-Check 'CleanupRestoresAll351OriginalBusinessTableRowsAndContentHashesExactly' (
    $tableIdentitiesExact -and $businessDifferences.Count -eq 0
) 'Every original business table, including stored rowversion tokens and empty quotations, has identical Rows and PK-ordered raw ContentSha256 after cleanup. Only canonical history is exempt across the one-row migration append.'
$cleanupDataChanges = @($tableMaps[1].Keys | Where-Object {
    -not $tableMaps[2].ContainsKey($_) -or (Canonical $tableMaps[1][$_]) -cne (Canonical $tableMaps[2][$_])
})
$onlyThreeFixtureTables = $cleanupDataChanges.Count -eq 3 -and @($cleanupDataChanges | Where-Object { $quotationKeys -cnotcontains $_ }).Count -eq 0
Add-Check 'RepeatToCleanupOnlyThreeQuotationFixtureTableDataRecordsChange' ($tableIdentitiesExact -and $onlyThreeFixtureTables) 'Exactly three predeclared quotation fixture tables differ; all 349 other table records, including native history data, are exact.'
$quotationTraces = [Collections.Generic.List[object]]::new()
foreach ($table in $quotationNames) {
    $key = @('dbo', $table) -join ([char]0)
    $present = $tableMaps[0].ContainsKey($key) -and $tableMaps[1].ContainsKey($key) -and $tableMaps[2].ContainsKey($key)
    $valid = $present -and $tableMaps[0][$key].Rows -eq 0 -and $tableMaps[1][$key].Rows -eq 1 -and
        $tableMaps[2][$key].Rows -eq 0 -and $tableMaps[0][$key].ContentSha256 -ceq $tableMaps[2][$key].ContentSha256 -and
        $tableMaps[1][$key].ContentSha256 -cne $tableMaps[2][$key].ContentSha256
    Add-Check "QuotationFixture.$table.Original0Repeat1Cleanup0" $valid 'One committed native fixture row is removed; the original zero-row content hash is restored exactly.'
    if ($present) { $quotationTraces.Add([ordered]@{ Schema = 'dbo'; Table = $table; OriginalRows = $tableMaps[0][$key].Rows; AfterRepeatRows = $tableMaps[1][$key].Rows; AfterCleanupRows = $tableMaps[2][$key].Rows; OriginalAndCleanupContentHashExact = $tableMaps[0][$key].ContentSha256 -ceq $tableMaps[2][$key].ContentSha256 }) }
}
$oldHistory = @($states[0].CoreHistory)
$repeatHistory = @($states[1].CoreHistory)
$cleanHistory = @($states[2].CoreHistory)
$historyPrefixExact = $oldHistory.Count -eq 139 -and $repeatHistory.Count -eq 140 -and $cleanHistory.Count -eq 140 -and
    (Canonical $oldHistory) -ceq (Canonical @($repeatHistory | Select-Object -First 139)) -and
    (Canonical $repeatHistory) -ceq (Canonical $cleanHistory) -and
    $oldHistory[-1].MigrationId -ceq $priorMigrationId -and $repeatHistory[-1].MigrationId -ceq $migrationId -and
    $repeatHistory[-1].ProductVersion -ceq '8.0.30' -and
    @($oldHistory | Where-Object MigrationId -CEQ $migrationId).Count -eq 0 -and
    @($repeatHistory | Where-Object MigrationId -CEQ $migrationId).Count -eq 1 -and
    @($repeatHistory.MigrationId | Sort-Object -Unique).Count -eq 140
Add-Check 'NativeHistoryKeepsAll139OriginalRowsWithOnlyExactCapacityAppendTo140' $historyPrefixExact 'Preserve the complete MigrationId/ProductVersion prefix and append only 20261002193500_RestoreQuotationAuditColumnCapacity/8.0.30; repeat and cleanup histories must be exact.'
$historyDataExact = $tableMaps[0].ContainsKey($historyKey) -and $tableMaps[1].ContainsKey($historyKey) -and $tableMaps[2].ContainsKey($historyKey) -and
    $tableMaps[0][$historyKey].Rows -eq 139 -and $tableMaps[1][$historyKey].Rows -eq 140 -and $tableMaps[2][$historyKey].Rows -eq 140 -and
    $tableMaps[0][$historyKey].ContentSha256 -cne $tableMaps[1][$historyKey].ContentSha256 -and
    (Canonical $tableMaps[1][$historyKey]) -ceq (Canonical $tableMaps[2][$historyKey])
Add-Check 'CanonicalHistoryTableNativeCountsAndPostUpgradeHashRemainExact' $historyDataExact 'Native table count follows the one-row history append; fixture deletion preserves the final history hash/count.'
foreach ($section in @('TableMetadata', 'Columns', 'Indexes', 'ForeignKeys', 'Checks', 'Triggers')) {
    Add-Check "RepeatToCleanup.All$($section)Exact" (
        (Canonical @($states[1].$section)) -ceq (Canonical @($states[2].$section))
    ) 'No field is normalized or exempt: full ordered native metadata, object IDs, table modify_date, column/default/identity/computed/collation and nested constraint/index fields must match exactly.'
}
$cleanupValid = $cleanup.Action -ceq 'cleanup' -and $cleanup.Status -ceq 'Passed' -and $cleanup.NativeExitCode -eq 0 -and
    $cleanup.NativeSummary.FixtureAction -ceq 'cleanup' -and $cleanup.NativeSummary.OwnerVerified -eq $true -and
    $cleanup.NativeSummary.OwnedGraphRows -eq 0 -and $cleanup.NativeSummary.BusinessValuesReturned -eq $false -and
    $cleanup.ScriptSha256 -ceq (Get-FileHash -LiteralPath $fixtureScriptPath -Algorithm SHA256).Hash -and
    $cleanup.LogSha256 -ceq (Get-FileHash -LiteralPath $fixtureLogPath -Algorithm SHA256).Hash
Add-Check 'SanitizedCleanupSummaryMatchesItsOriginalScriptAndLogBytes' $cleanupValid 'Native cleanup summary reports exit0, exact owned graph rows0 and no business values; its unmodified script/log bytes are independently hashed, never executed or printed.'
$artifactsStable = @($inputArtifactHashes | Where-Object { $_.Sha256 -cne (Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash }).Count -eq 0
Add-Check 'EveryOriginalInputArtifactUnchangedDuringReadOnlyComparison' $artifactsStable 'Rehash all explicit input files; the only output is this new comparison report.'
$failures = @($checks | Where-Object Status -CEQ 'Failed')
$report = [ordered]@{
    Scope = 'BUG143 fixture cleanup restoration: original raw business-data baseline versus final capture, plus exact final-repeat to final-cleanup catalogs/history. Cross-runtime data baseline reuse is explicit, not a same-input upgrade assertion.'
    Issue = 143; StartedUtc = $startedUtc; CompletedUtc = [DateTime]::UtcNow.ToString('O')
    ComparisonScriptSha256 = (Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash
    ReadOnly = $true; DatabaseConnectionsOpened = $false; DotnetCommandsRun = $false; CredentialReceiptsRead = $false
    Database = 'CP6Compat_WP2_20261002_36ef9cae'; Task = 'DB-COMPAT-01-WP2'
    SourceCaptures = @($captureWitnesses); InputArtifactHashes = $inputArtifactHashes
    CaptureScriptWitness = [ordered]@{
        Path = $captureScriptPath; CurrentActualSha256 = (Get-FileHash -LiteralPath $captureScriptPath -Algorithm SHA256).Hash
        HistoricalScriptSha256RecordedInCapture = $false; HistoricalScriptSha256 = $null
        EvidenceScope = 'Capture artifacts record QuerySha256, not capture-script SHA. Current script query template is verified against all original native query bytes; no historical script hash is invented.'
    }
    QueryGuardDifference = [ordered]@{
        OriginalQuerySha256 = $states[0].QuerySha256; FinalRepeatQuerySha256 = $states[1].QuerySha256; FinalCleanupQuerySha256 = $states[2].QuerySha256
        RawQueryShaEqualAcrossOriginalAndFinal = $false
        ExplicitDifferences = @('Native __EFMigrationsHistory count precondition 139->140, exactly one occurrence', 'Expected last migration precondition 184500 foreign-keys->193500 quotation-capacity, exactly one occurrence')
        ReverseFinalGuardArgumentsReproducesOriginalBytes = $reverseQuery -ceq $queries[0]
        ReverseFinalGuardArgumentsSha256 = Text-Sha256 $reverseQuery
        OriginalArtifactsModified = $false; InputManifestNormalized = $false
    }
    QuotationFixtureTraces = @($quotationTraces)
    BusinessTableDifferenceCountOriginalToCleanupExcludingCanonicalHistory = $businessDifferences.Count
    DataRecordDifferenceCountRepeatToCleanup = $cleanupDataChanges.Count
    CleanupSummarySha256 = (Get-FileHash -LiteralPath $cleanupSummaryPath -Algorithm SHA256).Hash
    Checks = @($checks); Passed = $checks.Count - $failures.Count; Failed = $failures.Count
    Limitations = @(
        'Original-empty capture is a native raw data/hash baseline from an earlier runtime/input. It is not counted as execution or acceptance of the final runtime.',
        'Only the two declared capture-query history preconditions differ; all native measurement SQL is byte-exact. Original query/input SHA values remain distinct and unchanged.',
        'Historical capture-script SHA was not recorded in the source captures; current script-template/query-byte evidence does not invent that missing historical field.',
        'This is an offline artifact comparison, not a new migration, full source review, PostgreSQL or production acceptance.',
        'No whole-server DBTS/sequence rollback or production database condition is asserted; actual captured table data and final metadata are compared.'
    )
}
[IO.File]::WriteAllText($outputPath, ($report | ConvertTo-Json -Depth 25), [Text.UTF8Encoding]::new($false))
Write-Output "BUG143 cleanup comparison: $($report.Passed) passed / $($report.Failed) failed; original-to-cleanup business differences=$($businessDifferences.Count); repeat-to-cleanup data changes=$($cleanupDataChanges.Count)."
if ($failures.Count -gt 0) { $failures | Select-Object Name, Detail | ConvertTo-Json -Depth 4; exit 1 }
