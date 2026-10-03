param(
    [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
    [Parameter(Mandatory)][ValidatePattern('^[a-z0-9]+(?:-[a-z0-9]+)*$')][string]$Label,
    [ValidateRange(10,300)][int]$TimeoutSeconds=180
)
$ErrorActionPreference='Stop'
$PSNativeCommandUseErrorActionPreference=$false
$taskRoot='D:\CP6\tmp\worktrees\db-compat-wp3-20261003'
$receiptPath='D:\CP6\tmp\db-compat.wp3-owned.json'
$workingDir=Join-Path $taskRoot 'CP6.WebApi'
$outputRoot='D:\CP6\tmp'
$reportPath=Join-Path $outputRoot "wp3-init-$Label.json"
$stdoutPath=Join-Path $outputRoot "wp3-init-$Label.stdout.log"
$stderrPath=Join-Path $outputRoot "wp3-init-$Label.stderr.log"
foreach($path in @($reportPath,$stdoutPath,$stderrPath)){if(Test-Path -LiteralPath $path){throw 'Preserve existing initializer evidence; choose a new label.'}}
$reportStream=[IO.File]::Open($reportPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
$reportStream.Dispose()
$report=[ordered]@{
    Task='DB-COMPAT-01-WP3';Scope='Actual compiled application DatabaseInit first/repeat smoke on the existing dedicated WP3 database: startup seed/backfill lock paths, successful one-shot completion and no HTTP listener. No full catalog, seed consistency, API/worker or whole-business acceptance.'
    Provider=$Provider;Label=$Label;SourceWorktree=$taskRoot;SourceBase=$null;SourceDirty=$null;DirtyPaths=@();Database=$null
    ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;StartedHostUtc=[DateTime]::UtcNow.ToString('o');FinishedHostUtc=$null
    Status='Preparing';Stage='SourceObservation';FailureType=$null;OwnerVerified=$false;OwnerPreflightExitCode=$null
    LoopbackPort=$(if($Provider -eq 'PostgreSql'){57882}else{57883});ProcessId=$null;ExitCode=$null;TimedOut=$false;TimeoutKillAttempted=$false;CreatedChildExitConfirmed=$false
    ListenerObserved=$false;CompletionMessage=$false;RawOutputCaptureComplete=$false;RawStdoutPath=$stdoutPath;RawStderrPath=$stderrPath;StdoutSha256=$null;StderrSha256=$null
    OutputDllObservationBeforeLaunch=@();OutputDllHashesUnchangedAfterExit=$null;SourceObservationScope='Output DLL bytes observed before launch, not a complete compiler input manifest or instrumented Core/Space loader record; API is launched by its exact absolute DLL path.'
    EnvironmentOverrideNames=@();RawLogsArchiveAllowed=$false;Passed=$false
}
$process=$null
$stdoutStream=$null
$stderrStream=$null
$stdoutCopy=$null
$stderrCopy=$null
$exitCode=1
function Save-SafeReport {
    $report.FinishedHostUtc=[DateTime]::UtcNow.ToString('o')
    $report|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $reportPath -Encoding utf8NoBOM
}
try {
    if([IO.Path]::GetFullPath($taskRoot) -cne 'D:\CP6\tmp\worktrees\db-compat-wp3-20261003'){throw 'Fixed WP3 source worktree required.'}
    $sourceBase=(& git -C $taskRoot rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0 -or $sourceBase -notmatch '^[a-f0-9]{40}$'){throw 'Source base observation failed.'}
    $dirtyPaths=@(& git -C $taskRoot status --porcelain --untracked-files=all)
    if($LASTEXITCODE -ne 0){throw 'Source dirty observation failed.'}
    $report.SourceBase=$sourceBase
    $report.SourceDirty=$dirtyPaths.Count -gt 0
    $report.DirtyPaths=$dirtyPaths
    $outputDir=Join-Path $workingDir 'bin\Debug\net8.0'
    foreach($name in @('CP6.WebApi','CP6.Core','CP6.Space.Infrastructure')){
        $dllPath=Join-Path $outputDir ($name+'.dll')
        $file=Get-Item -LiteralPath $dllPath -ErrorAction Stop
        $report.OutputDllObservationBeforeLaunch+=@([ordered]@{Name=$name;Path=$dllPath;Bytes=[long]$file.Length;Sha256=(Get-FileHash -LiteralPath $dllPath -Algorithm SHA256).Hash})
    }
    $report.Stage='ReceiptAndOwnerPreflight'
    $receipt=Get-Content -LiteralPath $receiptPath -Raw|ConvertFrom-Json
    if($receipt.Task -cne 'DB-COMPAT-01-WP3' -or $receipt.Owner -cnotmatch '^[a-f0-9]{32}$' -or $receipt.SqlServerState -cne 'CreatedAndOwnerMarked' -or $receipt.PostgreSqlState -cne 'CreatedAndOwnerMarked'){throw 'Exact completed WP3 task receipt required.'}
    $expectedDatabase='CP6Compat_WP3_20261003_'+$receipt.Owner.Substring(0,8)
    if($receipt.SqlServerDatabase -cne $expectedDatabase -or $receipt.PostgreSqlDatabase -cne $expectedDatabase){throw 'Exact WP3 owner-derived database names required.'}
    $report.Database=$expectedDatabase
    if($Provider -eq 'SqlServer'){
        $options=[System.Data.Common.DbConnectionStringBuilder]::new()
        $options.set_ConnectionString($receipt.SqlServerConnection)
        if($options['Server'] -cne 'localhost\KOUSQLSERVER' -or $options['Database'] -cne $expectedDatabase -or [string]$options['Integrated Security'] -ine 'True' -or $options.ContainsKey('Password') -or $options.ContainsKey('Pwd')){throw 'Recorded loopback integrated SQL connection required.'}
        $ownerQuery="SET NOCOUNT ON; SELECT CONVERT(nvarchar(200),value) FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatOwner' AND EXISTS(SELECT 1 FROM sys.extended_properties WHERE class=0 AND name=N'CP6CompatTask' AND CONVERT(nvarchar(200),value)=N'DB-COMPAT-01-WP3');"
        $ownerOutput=@(& sqlcmd -S 'localhost\KOUSQLSERVER' -E -C -d $expectedDatabase -b -l 5 -t 10 -h -1 -W -Q $ownerQuery 2>&1)
        $report.OwnerPreflightExitCode=$LASTEXITCODE
        if($LASTEXITCODE -ne 0 -or ($ownerOutput -join '').Trim() -cne $receipt.Owner){throw 'SQL owner preflight rejected.'}
        $connection=$receipt.SqlServerConnection
    }else{
        Add-Type -Path 'C:\Users\tt\.nuget\packages\npgsql\8.0.8\lib\net8.0\Npgsql.dll'
        $options=[Npgsql.NpgsqlConnectionStringBuilder]::new($receipt.PostgreSqlConnection)
        if($options.Host -notin @('localhost','127.0.0.1','::1') -or $options.Port -ne 5432 -or $options.Database -cne $expectedDatabase -or $options.Username -cnotmatch '^cp6compat_wp1_[a-f0-9]{8}$' -or $options.SearchPath -cne 'public'){throw 'Recorded dedicated loopback PostgreSQL connection required.'}
        $savedPgEnvironment=@{}
        foreach($name in @('PGPASSWORD','PGCONNECT_TIMEOUT','PGHOSTADDR','PGSERVICE','PGSERVICEFILE','PGOPTIONS')){$savedPgEnvironment[$name]=[Environment]::GetEnvironmentVariable($name,'Process')}
        try{
            foreach($name in @('PGHOSTADDR','PGSERVICE','PGSERVICEFILE')){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}
            $env:PGPASSWORD=$options.Password
            $env:PGCONNECT_TIMEOUT='5'
            $env:PGOPTIONS='-c statement_timeout=10000'
            $ownerQuery="SELECT shobj_description(oid,'pg_database') FROM pg_database WHERE datname=current_database() AND pg_get_userbyid(datdba)=current_user;"
            $ownerOutput=@(& 'C:\Program Files\PostgreSQL\18\bin\psql.exe' -w -h $options.Host -p "$($options.Port)" -U $options.Username -d $expectedDatabase -v ON_ERROR_STOP=1 -At -c $ownerQuery 2>&1)
            $report.OwnerPreflightExitCode=$LASTEXITCODE
            if($LASTEXITCODE -ne 0 -or ($ownerOutput -join '').Trim() -cne ('DB-COMPAT-01-WP3:'+$receipt.Owner)){throw 'PostgreSQL owner preflight rejected.'}
        }finally{
            foreach($name in $savedPgEnvironment.Keys){if($null -eq $savedPgEnvironment[$name]){[Environment]::SetEnvironmentVariable($name,[NullString]::Value,'Process')}else{[Environment]::SetEnvironmentVariable($name,$savedPgEnvironment[$name],'Process')}}
        }
        $connection=$receipt.PostgreSqlConnection
    }
    $report.OwnerVerified=$true
    $port=$report.LoopbackPort
    if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){throw 'Isolated local initializer port is already occupied.'}
    $startInfo=[Diagnostics.ProcessStartInfo]::new('dotnet')
    $startInfo.WorkingDirectory=$workingDir
    $startInfo.UseShellExecute=$false
    $startInfo.CreateNoWindow=$true
    $startInfo.RedirectStandardOutput=$true
    $startInfo.RedirectStandardError=$true
    $startInfo.ArgumentList.Add((Join-Path $outputDir 'CP6.WebApi.dll'))
    $overrides=[ordered]@{
        DOTNET_ENVIRONMENT='Development';ASPNETCORE_ENVIRONMENT='Development';ASPNETCORE_URLS="http://127.0.0.1:$port"
        Database__Provider=$Provider;ConnectionStrings__DefaultConnection=$connection;ConnectionStrings__Redis=''
        Startup__Mode='DatabaseInit';Startup__SkipDatabaseInitialization='false';Startup__SkipHostedServices='true'
        CrmOidc__Enabled='false';CrmIdentity__Enabled='false';ErpIntegration__Enabled='false'
        SpaceObservability__LegacyIntegrationEventTimeZoneId='UTC'
        JWT__Secret=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64));JWT__Issuer='CP6Compat.WP3.LocalInit';JWT__Audience='CP6Compat.WP3.LocalInit'
        Logging__LogLevel__Default='Information';Logging__LogLevel__Microsoft='Warning';'Logging__LogLevel__Microsoft.EntityFrameworkCore'='Warning'
    }
    foreach($name in $overrides.Keys){$startInfo.Environment[$name]=[string]$overrides[$name]}
    $report.EnvironmentOverrideNames=@($overrides.Keys)
    $stdoutStream=[IO.File]::Open($stdoutPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $stderrStream=[IO.File]::Open($stderrPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    $report.Stage='ApplicationDatabaseInit'
    $report.Status='Running'
    $process=[Diagnostics.Process]::Start($startInfo)
    $report.ProcessId=$process.Id
    $stdoutCopy=$process.StandardOutput.BaseStream.CopyToAsync($stdoutStream)
    $stderrCopy=$process.StandardError.BaseStream.CopyToAsync($stderrStream)
    $timer=[Diagnostics.Stopwatch]::StartNew()
    while(-not $process.HasExited){
        if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){$report.ListenerObserved=$true}
        if($timer.Elapsed.TotalSeconds -ge $TimeoutSeconds){$report.TimedOut=$true;$report.TimeoutKillAttempted=$true;$process.Kill();break}
        Start-Sleep -Milliseconds 200
    }
    if(-not $process.WaitForExit(5000)){throw 'Created initializer child did not exit within its termination budget.'}
    $report.CreatedChildExitConfirmed=$true
    $report.ExitCode=$process.ExitCode
    $report.RawOutputCaptureComplete=$stdoutCopy.Wait(5000) -and $stderrCopy.Wait(5000)
    if(-not $report.RawOutputCaptureComplete){throw 'Created initializer output capture did not complete.'}
    $stdoutStream.Flush();$stderrStream.Flush()
    $stdoutStream.Dispose();$stdoutStream=$null
    $stderrStream.Dispose();$stderrStream=$null
    if(Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue){$report.ListenerObserved=$true}
    $stdout=Get-Content -LiteralPath $stdoutPath -Raw
    $report.CompletionMessage=$stdout -match 'Database initialization completed; the one-shot process will exit'
    $report.OutputDllHashesUnchangedAfterExit=@($report.OutputDllObservationBeforeLaunch|Where-Object {(Get-FileHash -LiteralPath $_.Path -Algorithm SHA256).Hash -cne $_.Sha256}).Count -eq 0
    $report.Passed=$report.OwnerVerified -and $report.ExitCode -eq 0 -and $report.CompletionMessage -and -not $report.ListenerObserved -and -not $report.TimedOut -and $report.OutputDllHashesUnchangedAfterExit
    $report.Status=if($report.Passed){'Passed'}else{'Failed'}
    $exitCode=if($report.Passed){0}else{1}
}catch{
    $report.Status='Failed'
    $report.FailureType=$_.Exception.GetType().FullName
    $report.Passed=$false
}finally{
    if($null -ne $process){
        if(-not $process.HasExited){
            $report.TimeoutKillAttempted=$true
            try{$process.Kill();[void]$process.WaitForExit(5000)}catch{$report.Status='Failed';$report.Passed=$false;$exitCode=1}
        }
        $report.CreatedChildExitConfirmed=$process.HasExited
        if($process.HasExited){$report.ExitCode=$process.ExitCode}
    }
    if($null -ne $stdoutCopy -and $null -ne $stderrCopy){try{$report.RawOutputCaptureComplete=$stdoutCopy.Wait(5000) -and $stderrCopy.Wait(5000)}catch{$report.RawOutputCaptureComplete=$false}}
    if($null -ne $stdoutStream){$stdoutStream.Dispose()}
    if($null -ne $stderrStream){$stderrStream.Dispose()}
    foreach($pair in @(@($stdoutPath,'StdoutSha256'),@($stderrPath,'StderrSha256'))){if(Test-Path -LiteralPath $pair[0]){$report[$pair[1]]=(Get-FileHash -LiteralPath $pair[0] -Algorithm SHA256).Hash}}
    Save-SafeReport
    if($null -ne $process){$process.Dispose()}
}
[ordered]@{Provider=$Provider;Status=$report.Status;Passed=$report.Passed;ExitCode=$report.ExitCode;TimedOut=$report.TimedOut;CompletionMessage=$report.CompletionMessage;ListenerObserved=$report.ListenerObserved;ReportPath=$reportPath;RawLogsArchiveAllowed=$false}|ConvertTo-Json
exit $exitCode
