$ErrorActionPreference='Stop'
$worktree='D:\CP6\tmp\worktrees\bug-163-wms-local-transactions'
$output='D:\CP6\tmp\bug163-exact-local-verification.json'
if(Test-Path -LiteralPath $output){throw 'Preserve previous verification.'}
$originalSummary='D:\CP6\tmp\wp6-sql-formal-matrix-cad-cleanup-fix\summary.json'
if((Get-FileHash -LiteralPath $originalSummary).Hash -cne '8259FAEA4EEEF0786705CB4501F1072F79D0CA7DC116606B9EEB5364A7205F50'){throw 'Original failure changed.'}
$required=@()
foreach($relative in @('CP6.Tests/WmsProductionSqlServerTests.cs','CP6.Tests/WmsProductionLocalTransactionTests.cs')){
    $source=[IO.File]::ReadAllText((Join-Path $worktree $relative))
    $required+=@([regex]::Matches($source,'\[WmsProductionFact\]\s+public\s+(?:async\s+)?Task\s+(\w+)\s*\(')|ForEach-Object{'CP6.Tests.WmsProductionSqlServerTests.'+$_.Groups[1].Value})
}
if($required.Count -ne 25 -or @($required|Sort-Object -Unique).Count -ne 25 -or @($required|Where-Object{$_ -like '*.Bug163_*'}).Count -ne 17){throw 'Required original8/new17 case set invalid.'}
$records=@()
foreach($provider in @('SqlServer','PostgreSql')){
    $label=if($provider -ceq 'SqlServer'){'bug163-sql-wms-green'}else{'bug163-pg-wms-green'}
    $run=Join-Path 'D:\CP6\tmp' $label
    $resultPath=Join-Path $run 'result.json'
    $result=Get-Content -LiteralPath $resultPath -Raw|ConvertFrom-Json
    if(!$result.Success -or $result.Provider -cne $provider -or $result.Counts.Total -ne 25 -or $result.Counts.Passed -ne 25 -or $result.Counts.Failed -ne 0 -or $result.Counts.Other -ne 0 -or $result.Process.ExitCode -ne 0 -or $result.Process.TimedOut -or $result.Process.OwnedProcessTerminated -or !$result.SourcesUnchanged -or !$result.RuntimeUnchanged){throw 'Native green incomplete.'}
    $trxPath=Join-Path $run 'private/trx/results.trx'
    if((Get-FileHash -LiteralPath $trxPath).Hash -cne $result.TrxSha256){throw 'Native TRX changed.'}
    [xml]$trx=[IO.File]::ReadAllText($trxPath)
    $cases=@($trx.TestRun.Results.UnitTestResult)
    if($cases.Count -ne 25 -or @($cases|Where-Object outcome -CNE 'Passed').Count -ne 0 -or (@($cases.testName|Sort-Object) -join "`n") -cne (@($required|Sort-Object) -join "`n")){throw 'Raw required case set mismatch.'}
    $sourcePath=Join-Path $run 'source-inputs.json'
    if((Get-FileHash -LiteralPath $sourcePath).Hash -cne $result.SourceInputsSha256){throw 'Source manifest changed.'}
    $inputs=@(Get-Content -LiteralPath $sourcePath -Raw|ConvertFrom-Json)
    foreach($input in $inputs){
        $path=[IO.Path]::GetFullPath((Join-Path $worktree $input.Path))
        if(!$path.StartsWith($worktree+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-FileHash -LiteralPath $path).Hash -cne $input.Sha256){throw 'Current source differs from native execution.'}
    }
    $before=Join-Path $run 'runtime-artifact-before.json'
    $after=Join-Path $run 'runtime-artifact-after.json'
    if((Get-FileHash -LiteralPath $before).Hash -cne $result.RuntimeManifestSha256 -or (Get-FileHash -LiteralPath $before).Hash -cne (Get-FileHash -LiteralPath $after).Hash){throw 'Runtime manifests differ.'}
    $files=@(Get-Content -LiteralPath $before -Raw|ConvertFrom-Json)
    $runtimeRoot=Join-Path $worktree 'CP6.Tests/bin/Debug/net8.0'
    if(@(Get-ChildItem -LiteralPath $runtimeRoot -File -Recurse).Count -ne $files.Count){throw 'Current runtime file set differs.'}
    foreach($file in $files){
        $path=[IO.Path]::GetFullPath((Join-Path $runtimeRoot $file.Path))
        if(!$path.StartsWith([IO.Path]::GetFullPath($runtimeRoot)+'\',[StringComparison]::OrdinalIgnoreCase) -or (Get-Item -LiteralPath $path).Length -ne $file.Length -or (Get-FileHash -LiteralPath $path).Hash -cne $file.Sha256){throw 'Current runtime differs from native execution.'}
    }
    $diagnosticCases=@($cases|Where-Object{ $_.testName -match '\.Bug163_(LpnCreate_UsesOne|Label(Claim|Complete|Fail)_UsesLocal)' })
    if($diagnosticCases.Count -ne 4){throw 'Native transaction diagnostic cases missing.'}
    foreach($case in $diagnosticCases){
        $lines=@($case.Output.StdOut -split '\r?\n'|Where-Object{$_ -like 'BUG163 native command*'})
        $local=@($lines|Where-Object{$_ -match 'ambient=False local=True$'})
        $physical=@($local|ForEach-Object{if($_ -match 'physical=(\S+)'){$Matches[1]}}|Sort-Object -Unique)
        if($local.Count -le 1 -or $physical.Count -ne 1 -or @($lines|Where-Object{$_ -match 'ambient=True'}).Count){throw 'One local physical connection evidence incomplete.'}
    }
    $records+=[pscustomobject]@{Label=$label;Provider=$provider;Total=25;Passed=25;Failed=0;Other=0;OriginalCases=8;NewCases=17;NativeDiagnosticCases=4;SourceFiles=$inputs.Count;RuntimeFiles=$files.Count;SourceInputsSha256=$result.SourceInputsSha256;RuntimeManifestSha256=$result.RuntimeManifestSha256;ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash;TrxSha256=$result.TrxSha256;ExitCode=0}
    [pscustomobject]@{ExactCaseSetMatches=$true;RequiredCaseNames=@($required|Sort-Object);ResultSha256=(Get-FileHash -LiteralPath $resultPath).Hash}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath (Join-Path $run 'exact-case-set-audit.json') -Encoding utf8NoBOM
}
if(@($records.SourceInputsSha256|Sort-Object -Unique).Count -ne 1 -or @($records.RuntimeManifestSha256|Sort-Object -Unique).Count -ne 1){throw 'Provider source/runtime inputs differ.'}
$build=Get-Content -LiteralPath 'D:\CP6\tmp\bug163-sql-wms-green\build\build-result.json' -Raw|ConvertFrom-Json
if(!$build.Success -or $build.Projects[0].BuildProcess.ExitCode -ne 0){throw 'Successful native build required.'}
$red=@()
foreach($label in @('bug163-sql-native-red-valid-seed','bug163-pg-native-red')){
    $run=Join-Path 'D:\CP6\tmp' $label
    $result=Get-Content -LiteralPath (Join-Path $run 'result.json') -Raw|ConvertFrom-Json
    [xml]$trx=Get-Content -LiteralPath (Join-Path $run 'private/trx/results.trx') -Raw
    $cases=@($trx.TestRun.Results.UnitTestResult)
    if($result.Success -or $cases.Count -ne 17 -or $result.Counts.Other -ne 0 -or !$result.SourcesUnchanged -or !$result.RuntimeUnchanged -or (Get-FileHash -LiteralPath (Join-Path $run 'private/trx/results.trx')).Hash -cne $result.TrxSha256){throw 'Actual native RED changed or incomplete.'}
    $red+=[pscustomobject]@{Label=$label;Passed=@($cases|Where-Object outcome -CEQ 'Passed').Count;Failed=@($cases|Where-Object outcome -CEQ 'Failed').Count;TrxSha256=$result.TrxSha256}
}
if($red[0].Passed -ne 3 -or $red[0].Failed -ne 14 -or $red[1].Passed -ne 10 -or $red[1].Failed -ne 7){throw 'Native RED counts differ.'}
[pscustomobject]@{Task='BUG-163';Status='LocalNativeVerifiedRemotePending';CheckedUtc=[datetime]::UtcNow.ToString('o');SourceBase='ce49012d37da4b1b01f074d5b5b7fd227780e656';NativeRed=$red;Runs=$records;RequiredCaseNames=@($required|Sort-Object);AllCurrentSourcesAndRuntimeMatch=$true;Scope='Two native providers, exact original8 plus new17, one actual build reused with complete source/runtime byte proof. Injected failures occur after real business SaveChanges. Original WP6 and intermediate preflight/fixture failures retained. Task review, remote delivery, post-merge smoke, owned cleanup and full WP6 pending.'}|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $output -Encoding utf8NoBOM
[pscustomobject]@{Status='LocalNativeVerifiedRemotePending';Providers=$records|Select-Object Provider,Total,Passed;SourcesMatch=$true;RuntimeMatch=$true}|ConvertTo-Json -Depth 5
