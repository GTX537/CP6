[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('PostgreSql','SqlServer')][string]$Provider,
    [Parameter(Mandatory)][ValidatePattern('^bug155-[a-z0-9-]+$')][string]$Label
)
$ErrorActionPreference = 'Stop'
$manifestPath = 'D:\CP6\tmp\worktrees\db-compat-wp6-20261003\eng\database-compatibility\required-cases.json'
if ((Get-FileHash -LiteralPath $manifestPath).Hash -cne '706D889A29D3BDF02A43B2D9D5DD01115DEC66A9E0881A87DB4694FD4DF65330') {
    throw 'BUG155_ORIGINAL_35_CASE_MANIFEST_REQUIRED'
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$entry = @($manifest.Entries | Where-Object Id -CEQ 'space-design-publish')[0]
if ($entry.ExpectedCases -ne 35 -or @($entry.RequiredCaseNames).Count -ne 35) {
    throw 'BUG155_ORIGINAL_CASE_SET_REQUIRED'
}
$newMethods = @(
    'Concurrent_waiter_recovers_native_conflict_and_reuses_validation',
    'Validation_request_preserves_unsaved_caller_changes',
    'Validation_request_preserves_caller_owned_transaction',
    'Validation_unknown_failure_is_not_retried',
    'Validation_recovery_honors_cancellation',
    'Validation_recovery_is_bounded_and_rolls_back'
)
$required = @(@($entry.RequiredCaseNames) + @($newMethods | ForEach-Object {
    'CP6.Space.IntegrationTests.SpaceValidationSqlServerTests.' + $_
}) | Sort-Object -Unique)
if ($required.Count -ne 41) { throw 'BUG155_REQUIRED_CASE_SET_MISMATCH' }
$filter = ($required | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|'
& 'D:\CP6\tmp\Invoke-Bug155Tests.ps1' -Provider $Provider -Label $Label -Filter $filter -ExpectedCases 41
$run = Join-Path 'D:\CP6\tmp' $Label
$result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
$actual = @($result.CaseNames | Sort-Object)
$exact = $actual.Count -eq 41 -and ($actual -join "`n") -ceq ($required -join "`n")
$audit = [ordered]@{
    Task = 'BUG-155'
    Provider = $Provider
    OriginalManifestSha256 = (Get-FileHash -LiteralPath $manifestPath).Hash
    OriginalEntry = $entry.Id
    OriginalRequiredCases = 35
    AdditionalRequiredCases = 6
    ExpectedTotal = 41
    ResultSha256 = (Get-FileHash -LiteralPath (Join-Path $run 'result.json')).Hash
    ExactCaseSetMatches = $exact
    ActualCaseCount = $actual.Count
    RequiredCaseNames = $required
    SourceInputsSha256 = $result.SourceInputsSha256
    RuntimeManifestSha256 = $result.RuntimeManifestSha256
    CheckedUtc = [datetime]::UtcNow.ToString('o')
}
[IO.File]::WriteAllText((Join-Path $run 'exact-case-set-audit.json'), ($audit | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
if (!$exact -or !$result.Success) { throw 'BUG155_EXACT_NATIVE_GROUP_REQUIRED' }
[pscustomobject]@{ Provider = $Provider; Passed = 41; ExactCaseSetMatches = $exact } | ConvertTo-Json -Compress
