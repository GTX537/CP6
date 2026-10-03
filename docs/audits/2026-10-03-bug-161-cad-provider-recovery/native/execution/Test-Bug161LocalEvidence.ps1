$ErrorActionPreference = 'Stop'
$worktree = 'D:\CP6\tmp\worktrees\bug-161-cad-provider-recovery'
$output = 'D:\CP6\tmp\bug161-exact-local-verification.json'
if (Test-Path -LiteralPath $output) { throw 'Preserve existing evidence verification.' }
$runs = @(
    @{Label='bug161-pg-cad-exact-green';Provider='PostgreSql';Count=25},
    @{Label='bug161-sql-cad-exact-green';Provider='SqlServer';Count=25}
)
$records = @()
$failureProof = Get-Content -LiteralPath 'D:\CP6\tmp\bug161-original-failure-verification.json' -Raw | ConvertFrom-Json
foreach ($artifact in @(
    @{Path='D:\CP6\tmp\wp6-pg-formal-matrix-claim-fix\summary.json';Hash=$failureProof.OriginalSummarySha256},
    @{Path='D:\CP6\tmp\wp6-pg-formal-matrix-claim-fix\entries\space-cad-assets-collaboration\private\trx\results.trx';Hash=$failureProof.OriginalTrxSha256},
    @{Path='D:\CP6\tmp\bug161-pg-coordinated-native-red\private\trx\results.trx';Hash=$failureProof.CoordinatedNativeRedTrxSha256}
)) {
    if ((Get-FileHash -LiteralPath $artifact.Path).Hash -cne $artifact.Hash) { throw 'Original failure artifact changed.' }
}
if (!$failureProof.BothRedCasesObservedNative40001 -or !$failureProof.RedProductionMatchesUnfixedMain) { throw 'Actual native RED required.' }
foreach ($spec in $runs) {
    $run = Join-Path 'D:\CP6\tmp' $spec.Label
    $resultPath = Join-Path $run 'result.json'
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if (!$result.Success -or $result.Provider -cne $spec.Provider -or $result.Counts.Total -ne $spec.Count -or
        $result.Counts.Passed -ne $spec.Count -or $result.Counts.Failed -ne 0 -or $result.Counts.Other -ne 0 -or
        $result.Process.ExitCode -ne 0 -or $result.Process.TimedOut -or $result.Process.OwnedProcessTerminated -or
        !$result.SourcesUnchanged -or !$result.RuntimeUnchanged) { throw 'Native result is incomplete.' }
    $trxPath = Join-Path $run 'private/trx/results.trx'
    if ((Get-FileHash -LiteralPath $trxPath).Hash -cne $result.TrxSha256) { throw 'TRX hash mismatch.' }
    [xml]$trx = [IO.File]::ReadAllText($trxPath)
    $cases = @($trx.TestRun.Results.UnitTestResult)
    if ($cases.Count -ne $spec.Count -or @($cases | Where-Object outcome -CNE 'Passed').Count -ne 0 -or
        (@($cases.testName | Sort-Object) -join "`n") -cne (@($result.CaseNames | Sort-Object) -join "`n")) { throw 'Raw TRX case set mismatch.' }
    $audit = Get-Content -LiteralPath (Join-Path $run 'exact-case-set-audit.json') -Raw | ConvertFrom-Json
    if (!$audit.ExactCaseSetMatches -or $audit.ResultSha256 -cne (Get-FileHash -LiteralPath $resultPath).Hash -or
        (@($cases.testName | Sort-Object) -join "`n") -cne (@($audit.RequiredCaseNames | Sort-Object) -join "`n")) { throw 'Exact required group mismatch.' }
    $sourcePath = Join-Path $run 'source-inputs.json'
    if ((Get-FileHash -LiteralPath $sourcePath).Hash -cne $result.SourceInputsSha256) { throw 'Source manifest changed.' }
    $inputs = @(Get-Content -LiteralPath $sourcePath -Raw | ConvertFrom-Json)
    foreach ($input in $inputs) {
        $path = [IO.Path]::GetFullPath((Join-Path $worktree $input.Path))
        if (!$path.StartsWith($worktree + '\', [StringComparison]::OrdinalIgnoreCase) -or
            !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path).Hash -cne $input.Sha256) { throw 'Current source differs from actual execution.' }
    }
    $beforePath = Join-Path $run 'runtime-artifact-before.json'
    $afterPath = Join-Path $run 'runtime-artifact-after.json'
    if ((Get-FileHash -LiteralPath $beforePath).Hash -cne $result.RuntimeManifestSha256 -or
        (Get-FileHash -LiteralPath $beforePath).Hash -cne (Get-FileHash -LiteralPath $afterPath).Hash) { throw 'Runtime manifest changed.' }
    $runtime = @(Get-Content -LiteralPath $beforePath -Raw | ConvertFrom-Json)
    $runtimeRoot = Join-Path $worktree 'CP6.Space.IntegrationTests/bin/Debug/net8.0'
    if (@(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse).Count -ne $runtime.Count) { throw 'Current runtime file set differs.' }
    foreach ($file in $runtime) {
        $path = [IO.Path]::GetFullPath((Join-Path $runtimeRoot $file.Path))
        if (!$path.StartsWith([IO.Path]::GetFullPath($runtimeRoot) + '\', [StringComparison]::OrdinalIgnoreCase) -or
            !(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $file.Length -or
            (Get-FileHash -LiteralPath $path).Hash -cne $file.Sha256) { throw 'Current runtime differs from actual execution.' }
    }
    $records += [pscustomobject]@{Label=$spec.Label;Provider=$spec.Provider;Total=$cases.Count;Passed=$cases.Count;Other=0;
        ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash;TrxSha256=$result.TrxSha256;
        CurrentSourceFileCount=$inputs.Count;SourceInputsSha256=$result.SourceInputsSha256;
        CurrentRuntimeFileCount=$runtime.Count;RuntimeManifestSha256=$result.RuntimeManifestSha256;ExitCode=$result.Process.ExitCode}
}
if (@($records.SourceInputsSha256 | Sort-Object -Unique).Count -ne 1 -or
    @($records.RuntimeManifestSha256 | Sort-Object -Unique).Count -ne 1) { throw 'Provider inputs differ.' }
$routingRun = 'D:\CP6\tmp\bug161-inmemory-routing-green'
$routing = Get-Content -LiteralPath (Join-Path $routingRun 'result.json') -Raw | ConvertFrom-Json
$routingTrx = Join-Path $routingRun 'private/trx/results.trx'
[xml]$routingXml = [IO.File]::ReadAllText($routingTrx)
$routingCases = @($routingXml.TestRun.Results.UnitTestResult)
if (!$routing.Success -or $routing.Process.ExitCode -ne 0 -or $routing.Process.TimedOut -or
    $routing.Process.OwnedProcessTerminated -or $routingCases.Count -ne 17 -or
    @($routingCases | Where-Object { $_.outcome -cne 'Passed' -or $_.testName -notlike 'CP6.Space.IntegrationTests.SpaceCadProviderRoutingTests.*' }).Count -gt 0 -or
    (Get-FileHash -LiteralPath $routingTrx).Hash -cne $routing.TrxSha256 -or
    $routing.SourceInputsSha256 -cne $records[0].SourceInputsSha256 -or
    $routing.RuntimeManifestSha256 -cne $records[0].RuntimeManifestSha256 -or
    !$routing.SourcesUnchanged -or !$routing.RuntimeUnchanged -or $routing.Scope -notmatch 'use InMemory') { throw 'Supplemental InMemory routing regression incomplete.' }
$reviewPath = 'D:\CP6\tmp\bug161-review.md'
$review = [IO.File]::ReadAllText($reviewPath)
foreach ($relative in @('CP6.Space.Infrastructure/SpaceCadProviderCapabilityService.cs',
    'CP6.Space.IntegrationTests/SpaceCadProviderSqlServerTests.cs',
    'CP6.Space.IntegrationTests/SpaceCadProviderSqlServerTests.RecoveryTests.cs')) {
    if (!$review.Contains((Get-FileHash -LiteralPath (Join-Path $worktree $relative)).Hash)) { throw 'Review source applicability not established.' }
}
$report = [ordered]@{Task='BUG-161';Status='LocalVerifiedRemotePending';CheckedUtc=[datetime]::UtcNow.ToString('o');
    SourceBase='4a654320c7c41cbdf6ed5bf7bd4fcba459f720f0';Runs=$records;AllCurrentSourcesAndRuntimeMatch=$true;
    SourceReviewSha256=(Get-FileHash -LiteralPath $reviewPath).Hash;
    OriginalNativeFailuresRetained=$true;CoordinatedNativeRedCases=2;
    SupplementalInMemoryRouting=@{Passed=17;TrxSha256=$routing.TrxSha256;Scope=$routing.Scope};
    Scope='Raw native TRX and exact required sets verified; two executions use the same current source/runtime. The exact 25 cases include the original 15 required CAD/assets/collaboration cases and ten focused transaction-recovery regressions. Injected recovery controls are not native deadlock reproductions. Full WP6, post-merge smoke, cleanup and remote delivery remain pending.'}
[IO.File]::WriteAllText($output, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Status=$report.Status;Groups=@($records | Select-Object Provider,Total,Passed);CurrentInputsMatch=$true} | ConvertTo-Json -Depth 4 -Compress
