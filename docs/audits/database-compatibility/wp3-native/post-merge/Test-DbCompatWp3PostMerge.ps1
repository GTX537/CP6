$ErrorActionPreference='Stop'
$taskRoot='D:\CP6\tmp\worktrees\db-compat-wp3-20261003'
$reportPath='D:\CP6\tmp\wp3-post-merge-runtime-guard-smoke.json'
if(Test-Path -LiteralPath $reportPath){throw 'Preserve prior smoke evidence'}
$main=(& git -C $taskRoot rev-parse origin/main).Trim()
$mainTree=(& git -C $taskRoot rev-parse 'origin/main^{tree}').Trim()
$candidateTree=(& git -C $taskRoot rev-parse 'HEAD^{tree}').Trim()
if($main -cne '0b0ab74a04c4d840a2c0bb05e36395aff8b89d32' -or $mainTree -cne $candidateTree){throw 'Expected merged main and validated candidate tree required'}
$init=Get-Content -LiteralPath 'D:\CP6\tmp\wp3-init-pg-repeat.json' -Raw|ConvertFrom-Json
$api=$init.OutputDllObservationBeforeLaunch|Where-Object Name -CEQ 'CP6.WebApi'
if((Get-FileHash -LiteralPath $api.Path -Algorithm SHA256).Hash -cne $api.Sha256){throw 'Validated API output changed'}
$port=57884
if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){throw 'Smoke port already occupied'}
$info=[Diagnostics.ProcessStartInfo]::new('dotnet')
$info.WorkingDirectory=Join-Path $taskRoot 'CP6.WebApi'
$info.UseShellExecute=$false; $info.CreateNoWindow=$true
$info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
$info.ArgumentList.Add($api.Path)
$info.Environment['DOTNET_ENVIRONMENT']='Development'
$info.Environment['ASPNETCORE_ENVIRONMENT']='Development'
$info.Environment['Database__Provider']='PostgreSql'
$info.Environment['Startup__Mode']='Application'
$info.Environment['Startup__SkipHostedServices']='true'
$info.Environment['ASPNETCORE_URLS']="http://127.0.0.1:$port"
# The current guard executes before connection parsing/registration. No database is needed.
$info.Environment['ConnectionStrings__DefaultConnection']='Host=127.0.0.1;Port=1;Database=CP6Compat_WP3_GuardSentinel;Username=cp6compat_guard;Timeout=1'
$started=[DateTime]::UtcNow
$process=[Diagnostics.Process]::Start($info)
$stdout=$process.StandardOutput.ReadToEndAsync(); $stderr=$process.StandardError.ReadToEndAsync()
$listener=$false; $timedOut=$false
try {
    while(-not $process.HasExited){
        if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){$listener=$true}
        if(([DateTime]::UtcNow-$started).TotalSeconds -gt 30){$timedOut=$true;$process.Kill();break}
        Start-Sleep -Milliseconds 100
    }
    if(-not $process.WaitForExit(5000)){throw 'Owned smoke process failed to exit'}
    $out=$stdout.GetAwaiter().GetResult(); $err=$stderr.GetAwaiter().GetResult()
    $outPath='D:\CP6\tmp\wp3-post-merge-guard.stdout.log'; $errPath='D:\CP6\tmp\wp3-post-merge-guard.stderr.log'
    [IO.File]::WriteAllText($outPath,$out); [IO.File]::WriteAllText($errPath,$err)
    if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){$listener=$true}
    $rejected=$err.Contains('Database:Provider=PostgreSql is not yet supported by the application runtime')
    $passed=$process.ExitCode -ne 0 -and $rejected -and -not $listener -and -not $timedOut
    [ordered]@{Scope='Actual same validated API after remote main tree containment: ordinary PG Application mode must still reject before database work. Sentinel loopback connection, no database required. No new full business/native suite executed.';SourceMain=$main;SourceCandidate='4ddc6c9d8d75f47711fce1c5e56fbb0ac8733e21';SourceTree=$mainTree;StartedHostUtc=$started.ToString('o');FinishedHostUtc=[DateTime]::UtcNow.ToString('o');ApiPath=$api.Path;ApiSha256=$api.Sha256;ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;ProcessId=$process.Id;ExitCode=$process.ExitCode;Port=$port;TimedOut=$timedOut;ListenerObserved=$listener;RuntimeGuardRejected=$rejected;Passed=$passed;StdoutSha256=(Get-FileHash -LiteralPath $outPath -Algorithm SHA256).Hash;StderrSha256=(Get-FileHash -LiteralPath $errPath -Algorithm SHA256).Hash}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
    if(-not $passed){throw 'Post-merge guard smoke failed; see local evidence'}
    Write-Output 'Post-merge same API rejected ordinary PostgreSQL runtime, nonzero exit, no HTTP listener observed.'
} finally {
    if(-not $process.HasExited){$process.Kill();[void]$process.WaitForExit(5000)}
    $process.Dispose()
}
