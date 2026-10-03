[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('PostgreSql','SqlServer')][string]$Provider,
    [Parameter(Mandatory)][ValidatePattern('^bug161-[a-z0-9-]+$')][string]$Label,
    [switch]$Build
)
$ErrorActionPreference = 'Stop'
$manifestPath = 'D:\CP6\tmp\bug161-required-cases-before-fix.json'
if ((Get-FileHash -LiteralPath $manifestPath).Hash -cne '5B1898EC51E93C03AAC6B826DDBD9B574B0E28F33EFDCF470693B9B12379C4FD') { throw 'BUG161_FROZEN_MANIFEST_REQUIRED' }
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$selected = @($manifest.Entries | Where-Object Id -CEQ 'space-cad-assets-collaboration')[0]
if ($selected.ExpectedCases -ne 15 -or @($selected.RequiredCaseNames).Count -ne 15) { throw 'BUG161_ORIGINAL_GROUP_REQUIRED' }
$newMethods = @(
    'Replace_waiter_recovers_native_serialization_conflict',
    'Replace_waiter_replays_same_key_after_native_serialization_conflict',
    'Replace_serialization_failure_retries_the_whole_transaction',
    'Replace_deadlock_retries_the_whole_transaction',
    'Replace_unknown_unique_constraint_is_not_retried',
    'Replace_recovery_is_bounded_and_rolls_back',
    'Replace_recovery_honors_cancellation',
    'Replace_preserves_caller_pending_changes',
    'Replace_preserves_caller_transaction',
    'Replace_preserves_caller_ambient_transaction'
)
$required = @(@($selected.RequiredCaseNames) + @($newMethods | ForEach-Object { 'CP6.Space.IntegrationTests.SpaceCadProviderSqlServerTests.' + $_ }) | Sort-Object -Unique)
if ($required.Count -ne 25) { throw 'BUG161_EXACT_CASE_SET_REQUIRED' }
$filter = ($required | ForEach-Object { 'FullyQualifiedName=' + $_ }) -join '|'
& 'D:\CP6\tmp\Invoke-Bug161Tests.ps1' -Provider $Provider -Label $Label -Filter $filter -ExpectedCases 25 -Build:$Build
$run = Join-Path 'D:\CP6\tmp' $Label
$resultPath = Join-Path $run 'result.json'
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
$actual = @($result.CaseNames | Sort-Object)
$exact = $actual.Count -eq 25 -and ($actual -join "`n") -ceq ($required -join "`n")
$audit = [ordered]@{
    Task='BUG-161';Provider=$Provider;FrozenManifestSha256=(Get-FileHash -LiteralPath $manifestPath).Hash
    Entry='space-cad-assets-collaboration';OriginalRequiredCases=15;AdditionalRequiredCases=10;ExpectedTotal=25
    ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash;ExactCaseSetMatches=$exact;ActualCaseCount=$actual.Count
    RequiredCaseNames=$required;SourceInputsSha256=$result.SourceInputsSha256;RuntimeManifestSha256=$result.RuntimeManifestSha256
    CheckedUtc=[datetime]::UtcNow.ToString('o')
}
[IO.File]::WriteAllText((Join-Path $run 'exact-case-set-audit.json'),($audit | ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
if (!$exact -or !$result.Success) { throw 'BUG161_EXACT_NATIVE_GROUP_REQUIRED' }
[pscustomobject]@{Provider=$Provider;Entry=$audit.Entry;Passed=25;ExactCaseSetMatches=$exact} | ConvertTo-Json -Compress
