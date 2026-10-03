[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('PostgreSql','SqlServer')][string]$Provider,
    [Parameter(Mandatory)][ValidateSet('space-clone','space-design-publish')][string]$Entry,
    [Parameter(Mandatory)][ValidatePattern('^bug157-[a-z0-9-]+$')][string]$Label
)
$ErrorActionPreference = 'Stop'
$manifestPath = 'D:\CP6\tmp\worktrees\db-compat-wp6-20261003\eng\database-compatibility\required-cases.json'
if ((Get-FileHash -LiteralPath $manifestPath).Hash -cne '7CA38B226DB019D2C8A50F695640E6608D3B73888A4437BECF9B2F93956108ED') {
    throw 'BUG157_FROZEN_REQUIRED_CASE_MANIFEST_REQUIRED'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$selected = @($manifest.Entries | Where-Object Id -CEQ $Entry)[0]
$required = @($selected.RequiredCaseNames)
if ($Entry -ceq 'space-clone') {
    if ($selected.ExpectedCases -ne 16 -or $required.Count -ne 16) { throw 'BUG157_ORIGINAL_CLONE_SET_REQUIRED' }
    $newMethods = @(
        'Design_v1_floor_waiter_recovers_native_conflict_as_revision_conflict',
        'Design_v1_floor_waiter_recovers_native_conflict_and_replays_same_key',
        'Design_v1_floor_creation_preserves_unsaved_caller_changes',
        'Design_v1_floor_creation_preserves_caller_owned_transaction',
        'Design_v1_floor_creation_unknown_failure_is_not_retried',
        'Design_v1_floor_creation_recovery_honors_cancellation',
        'Design_v1_floor_creation_recovery_is_bounded_and_rolls_back'
    )
    $required += @($newMethods | ForEach-Object { 'CP6.Space.IntegrationTests.SpaceVersionCloneSqlServerTests.' + $_ })
    $expected = 23
} else {
    if ($selected.ExpectedCases -ne 41 -or $required.Count -ne 41) { throw 'BUG157_DESIGN_PUBLISH_SET_REQUIRED' }
    $expected = 41
}
$required = @($required | Sort-Object -Unique)
if ($required.Count -ne $expected) { throw 'BUG157_EXACT_CASE_SET_REQUIRED' }
$filter = ($required | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|'
& 'D:\CP6\tmp\Invoke-Bug157Tests.ps1' -Provider $Provider -Label $Label -Filter $filter -ExpectedCases $expected
$run = Join-Path 'D:\CP6\tmp' $Label
$resultPath = Join-Path $run 'result.json'
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$actual = @($result.CaseNames | Sort-Object)
$exact = $actual.Count -eq $expected -and ($actual -join "`n") -ceq ($required -join "`n")
$audit = [ordered]@{
    Task = 'BUG-157'
    Provider = $Provider
    FrozenManifestSha256 = (Get-FileHash -LiteralPath $manifestPath).Hash
    Entry = $Entry
    OriginalRequiredCases = $selected.ExpectedCases
    AdditionalRequiredCases = $expected - $selected.ExpectedCases
    ExpectedTotal = $expected
    ResultSha256 = (Get-FileHash -LiteralPath $resultPath).Hash
    ExactCaseSetMatches = $exact
    ActualCaseCount = $actual.Count
    RequiredCaseNames = $required
    SourceInputsSha256 = $result.SourceInputsSha256
    RuntimeManifestSha256 = $result.RuntimeManifestSha256
    CheckedUtc = [datetime]::UtcNow.ToString('o')
}
[IO.File]::WriteAllText((Join-Path $run 'exact-case-set-audit.json'), ($audit | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
if (!$exact -or !$result.Success) { throw 'BUG157_EXACT_NATIVE_GROUP_REQUIRED' }
[pscustomobject]@{ Provider=$Provider; Entry=$Entry; Passed=$expected; ExactCaseSetMatches=$exact } | ConvertTo-Json -Compress
