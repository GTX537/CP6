$ErrorActionPreference = 'Stop'
$worktree = 'D:\CP6\tmp\worktrees\bug-155-space-validation-retry'
$output = 'D:\CP6\tmp\bug155-exact-local-verification.json'
if (Test-Path -LiteralPath $output) { throw 'Preserve existing evidence verification.' }
$runs = @(
    @{Label='bug155-pg-validation-green-v2';Provider='PostgreSql';Count=9},
    @{Label='bug155-sql-validation-green';Provider='SqlServer';Count=9},
    @{Label='bug155-pg-design-publish-green';Provider='PostgreSql';Count=41},
    @{Label='bug155-sql-design-publish-green';Provider='SqlServer';Count=41}
)
$records = @()
foreach ($spec in $runs) {
    $run = Join-Path 'D:\CP6\tmp' $spec.Label
    $result = Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw | ConvertFrom-Json
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
    if ($spec.Count -eq 41) {
        $audit = Get-Content -LiteralPath (Join-Path $run 'exact-case-set-audit.json') -Raw | ConvertFrom-Json
        if (!$audit.ExactCaseSetMatches -or $audit.ResultSha256 -cne (Get-FileHash -LiteralPath (Join-Path $run 'result.json')).Hash -or
            (@($cases.testName | Sort-Object) -join "`n") -cne (@($audit.RequiredCaseNames | Sort-Object) -join "`n")) { throw 'Required 41-case group mismatch.' }
    }
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
        ResultSha256=(Get-FileHash -LiteralPath (Join-Path $run 'result.json')).Hash;TrxSha256=$result.TrxSha256;
        CurrentSourceFileCount=$inputs.Count;SourceInputsSha256=$result.SourceInputsSha256;
        CurrentRuntimeFileCount=$runtime.Count;RuntimeManifestSha256=$result.RuntimeManifestSha256;ExitCode=$result.Process.ExitCode}
}
if (@($records.SourceInputsSha256 | Sort-Object -Unique).Count -ne 1 -or
    @($records.RuntimeManifestSha256 | Sort-Object -Unique).Count -ne 1) { throw 'Provider inputs differ.' }
$reviewPath = 'D:\CP6\tmp\bug155-review.md'
$review = [IO.File]::ReadAllText($reviewPath)
foreach ($relative in @('CP6.Space.Infrastructure/SpaceValidationInfrastructure.cs','CP6.Space.IntegrationTests/SpaceValidationSqlServerTests.cs')) {
    if (!$review.Contains((Get-FileHash -LiteralPath (Join-Path $worktree $relative)).Hash)) { throw 'Review source applicability not established.' }
}
$report = [ordered]@{Task='BUG-155';Status='LocalVerifiedRemotePending';CheckedUtc=[datetime]::UtcNow.ToString('o');
    SourceBase='59e09f6e68a36734c64144abfcefc81b7c9c8d9d';Runs=$records;AllCurrentSourcesAndRuntimeMatch=$true;
    SourceReviewSha256=(Get-FileHash -LiteralPath $reviewPath).Hash;
    Scope='Raw native TRX and exact required sets verified; all four executions use the same current source/runtime. The 9-case suites are subsets of the 41-case groups and are not added as distinct coverage. Injected recovery controls are not native deadlock reproductions. Full WP6, post-merge smoke, cleanup and remote delivery remain pending.'}
[IO.File]::WriteAllText($output, ($report | ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
[pscustomobject]@{Status=$report.Status;Groups=@($records | Select-Object Provider,Total,Passed);CurrentInputsMatch=$true} | ConvertTo-Json -Depth 4 -Compress
