[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('PostgreSql','SqlServer')][string]$Provider,
    [Parameter(Mandatory)][ValidatePattern('^bug159-[a-z0-9-]+$')][string]$Label,
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$manifestPath = 'D:\CP6\tmp\worktrees\db-compat-wp6-20261003\eng\database-compatibility\required-cases.json'
if ((Get-FileHash -LiteralPath $manifestPath).Hash -cne '1EA3B68A17F79DFACC3FF5A85D20D45DD7BDA4603A5566BD9BC9C76A1741307C') { throw 'BUG159_FROZEN_MANIFEST_REQUIRED' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$selected = @($manifest.Entries | Where-Object Id -CEQ 'space-jobs-generation-retention')[0]
if ($selected.ExpectedCases -ne 17 -or @($selected.RequiredCaseNames).Count -ne 17) { throw 'BUG159_ORIGINAL_GROUP_REQUIRED' }
$newMethods = @(
    'Claim_waiter_recovers_native_attempt_number_conflict',
    'Claim_unknown_unique_constraint_is_not_retried',
    'Claim_attempt_number_recovery_is_bounded_and_rolls_back',
    'Claim_attempt_number_recovery_honors_cancellation',
    'Claim_serialization_failure_retries_the_whole_transaction',
    'Claim_deadlock_retries_the_whole_transaction'
)
$required = @(@($selected.RequiredCaseNames) + @($newMethods | ForEach-Object { 'CP6.Space.IntegrationTests.SpaceJobSqlServerTests.' + $_ }) | Sort-Object -Unique)
if ($required.Count -ne 23) { throw 'BUG159_EXACT_CASE_SET_REQUIRED' }
$filter = ($required | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|'
& 'D:\CP6\tmp\Invoke-Bug159Tests.ps1' -Provider $Provider -Label $Label -Filter $filter -ExpectedCases 23 -Build:$Build
$run = Join-Path 'D:\CP6\tmp' $Label
$resultPath = Join-Path $run 'result.json'
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$actual = @($result.CaseNames | Sort-Object)
$exact = $actual.Count -eq 23 -and ($actual -join "`n") -ceq ($required -join "`n")
$audit = [ordered]@{
    Task='BUG-159';Provider=$Provider;FrozenManifestSha256=(Get-FileHash -LiteralPath $manifestPath).Hash
    Entry='space-jobs-generation-retention';OriginalRequiredCases=17;AdditionalRequiredCases=6;ExpectedTotal=23
    ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash;ExactCaseSetMatches=$exact;ActualCaseCount=$actual.Count
    RequiredCaseNames=$required;SourceInputsSha256=$result.SourceInputsSha256;RuntimeManifestSha256=$result.RuntimeManifestSha256
    CheckedUtc=[datetime]::UtcNow.ToString('o')
}
[IO.File]::WriteAllText((Join-Path $run 'exact-case-set-audit.json'),($audit | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
if (!$exact -or !$result.Success) { throw 'BUG159_EXACT_NATIVE_GROUP_REQUIRED' }
[pscustomobject]@{Provider=$Provider;Entry=$audit.Entry;Passed=23;ExactCaseSetMatches=$exact} | ConvertTo-Json -Compress
