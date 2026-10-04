# Offline state transitions exercise cleanup orchestration; no native tools or database connections.
[CmdletBinding()]
param([string]$ReportPath)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$module=Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityLifecycle.psm1') -Force -PassThru
$records=[Collections.Generic.List[object]]::new()
$offlineRoot=Join-Path ([IO.Path]::GetTempPath()) ('cp6-cleanup-offline-'+[guid]::NewGuid().ToString('N'))
$resolvedRoot=[IO.Path]::GetFullPath($offlineRoot)
if(!$resolvedRoot.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)) {throw 'CP6_CLEANUP_OFFLINE_DIRECTORY'}
$null=[IO.Directory]::CreateDirectory($resolvedRoot)
function Check([bool]$Condition,[string]$Case) {
    $records.Add([pscustomobject]@{Case=$Case;Passed=$Condition})
    if(!$Condition) {throw 'CP6_CLEANUP_OFFLINE_ASSERTION'}
}
function Set-Scenario($Provider,[string]$Label) {
    $directory=Join-Path $resolvedRoot ($Provider+'-'+$Label)
    $null=[IO.Directory]::CreateDirectory($directory)
    & $module {
        $script:fakeDatabases=@{};$script:readCounts=@{};$script:sessionSchedules=@{};$script:changes=@{};$script:drops=0
        function script:Get-Cp6ActualDatabase($Context,[string]$Name) {
            if(!$script:fakeDatabases.ContainsKey($Name)) {return [pscustomobject]@{Exists=$false}}
            $script:readCounts[$Name]=1+[int]$script:readCounts[$Name]
            $actual=$script:fakeDatabases[$Name]
            if($script:sessionSchedules.ContainsKey($Name) -and $script:sessionSchedules[$Name].Count -gt 0) {$actual.Sessions=$script:sessionSchedules[$Name].Dequeue()}
            if($script:changes.ContainsKey($Name) -and $script:readCounts[$Name] -eq $script:changes[$Name].Read) {
                $change=$script:changes[$Name]
                $actual.($change.Property)=$change.Value
            }
            return $actual
        }
        function script:Invoke-Cp6Statement($Context,[string]$Database,[string]$Query) {
            if($Query -match '^DROP DATABASE [\["](?<name>[^\]"]+)') {$script:drops++;$script:fakeDatabases.Remove($Matches.name)}
        }
        function script:Set-Cp6Marker($Context,$Receipt) {
            $script:fakeDatabases[$Receipt.DatabaseName]=[pscustomobject]@{Exists=$true;DatabaseName=$Receipt.DatabaseName;Task=$Receipt.Task;Owner=$Receipt.Owner;Marker=($Receipt.Task+':'+$Receipt.Owner);DatabaseId=100+$script:fakeDatabases.Count;CreatedAt='2026-10-03T00:00:00';Files=@();Principal='offline';DatabaseOwner='offline';ServerVersion='offline';UserTables=0;Sessions=0;SessionVisibility=$true}
        }
    }
    $context=& $module {
        param($provider,$directory)
        $public=[pscustomobject]@{RunId=[guid]::NewGuid().ToString('N');Provider=$provider;Task=$script:task;Endpoint='offline';RunDirectory=$directory;PrivateDirectory=$directory;ReceiptDirectory=$directory}
        $script:contexts[$public.RunId]=@{Public=($public|ConvertTo-Json|ConvertFrom-Json)}
        $public
    } $Provider $directory
    $one=New-Cp6OwnedDatabase $context application
    $two=New-Cp6OwnedDatabase $context restore
    & $module {param($names) foreach($name in $names){$script:readCounts[$name]=0}} @($one.DatabaseName,$two.DatabaseName)
    return [pscustomobject]@{Context=$context;Receipts=@($one,$two)}
}
function Schedule($Receipt,[int[]]$Sessions) {
    & $module {param($name,$sessions) $queue=[Collections.Generic.Queue[int]]::new();foreach($count in $sessions){$queue.Enqueue($count)};$script:sessionSchedules[$name]=$queue} $Receipt.DatabaseName $Sessions
}
function Change($Receipt,[int]$Read,[string]$Property,$Value) {
    & $module {param($name,$read,$property,$value) $script:changes[$name]=[pscustomobject]@{Read=$read;Property=$property;Value=$value}} $Receipt.DatabaseName $Read $Property $Value
}
function Expect-Rejection($Scenario,[string]$Expected,[int]$Seconds=1) {
    $code=$null
    try {$null=Remove-Cp6OwnedDatabases -Context $Scenario.Context -Receipts $Scenario.Receipts -SessionWaitSeconds $Seconds} catch {$code=$_.Exception.Message}
    Check ($code -ceq $Expected) ($Scenario.Context.Provider+':'+$Expected)
    Check ((& $module {$script:drops}) -eq 0) ($Scenario.Context.Provider+':failure-prevents-every-drop')
}
$status='Running';$failureCode=$null
try {
    foreach($provider in @('SqlServer','PostgreSql')) {
        $s=Set-Scenario $provider 'preflight-transient';Schedule $s.Receipts[1] @(1,0)
        $result=@(Remove-Cp6OwnedDatabases -Context $s.Context -Receipts $s.Receipts)
        Check ($result.Count -eq 2 -and @($result|Where-Object Status -CNE 'Absent').Count -eq 0) ($provider+':transient-preflight-session-finishes')
        Check ((& $module {$script:drops}) -eq 2) ($provider+':preflight-normal-drop-only')
        $s=Set-Scenario $provider 'drop-transient';Schedule $s.Receipts[0] @(0,1,0)
        $result=@(Remove-Cp6OwnedDatabases -Context $s.Context -Receipts $s.Receipts)
        Check ($result.Count -eq 2 -and @($result|Where-Object Status -CNE 'Absent').Count -eq 0) ($provider+':new-session-between-preflight-and-drop-finishes')
        $s=Set-Scenario $provider 'persistent';Schedule $s.Receipts[1] @(1)
        $watch=[Diagnostics.Stopwatch]::StartNew();Expect-Rejection $s 'CP6_COMPAT_DATABASE_HAS_SESSIONS' 1
        Check ($watch.Elapsed.TotalSeconds -ge 0.9 -and $watch.Elapsed.TotalSeconds -lt 5) ($provider+':persistent-session-has-real-bounded-timeout')
        Check ((& $module {param($name) $script:readCounts[$name]} $s.Receipts[1].DatabaseName) -gt 1) ($provider+':persistent-session-was-polled')
        $s=Set-Scenario $provider 'owner-changes';Schedule $s.Receipts[1] @(1,0);Change $s.Receipts[1] 2 Owner 'foreign'
        Expect-Rejection $s 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH'
        Check ((& $module {param($name) $script:readCounts[$name]} $s.Receipts[1].DatabaseName) -eq 2) ($provider+':owner-mismatch-stops-wait-immediately')
        $s=Set-Scenario $provider 'physical-changes';Schedule $s.Receipts[1] @(1,0);Change $s.Receipts[1] 2 DatabaseId 999
        Expect-Rejection $s 'CP6_COMPAT_PHYSICAL_IDENTITY_MISMATCH'
        Check ((& $module {param($name) $script:readCounts[$name]} $s.Receipts[1].DatabaseName) -eq 2) ($provider+':physical-mismatch-stops-wait-immediately')
        $s=Set-Scenario $provider 'wrong-owner';Change $s.Receipts[1] 1 Owner 'foreign'
        Expect-Rejection $s 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH'
        Check ((& $module {param($name) $script:readCounts[$name]} $s.Receipts[1].DatabaseName) -eq 1) ($provider+':wrong-owner-is-never-retried')
        $s=Set-Scenario $provider 'zero-timeout';Schedule $s.Receipts[1] @(1)
        Expect-Rejection $s 'CP6_COMPAT_DATABASE_HAS_SESSIONS' 0
        Check ((& $module {param($name) $script:readCounts[$name]} $s.Receipts[1].DatabaseName) -eq 1) ($provider+':zero-timeout-keeps-immediate-rejection')
        if($provider -ceq 'SqlServer') {
            $s=Set-Scenario $provider 'session-visibility';Change $s.Receipts[1] 1 SessionVisibility $false
            Expect-Rejection $s 'CP6_COMPAT_SESSION_VISIBILITY_REQUIRED'
            Check ((& $module {param($name) $script:readCounts[$name]} $s.Receipts[1].DatabaseName) -eq 1) 'SqlServer:missing-session-visibility-is-never-retried'
        }
    }
    $status='Passed'
} catch {
    $status='Failed';$failureCode=if($_.Exception.Message -cmatch '^CP6_[A-Z_]+$'){$_.Exception.Message}else{'CP6_CLEANUP_OFFLINE_FAILURE'}
} finally {
    $resolvedDelete=[IO.Path]::GetFullPath($offlineRoot)
    if($resolvedDelete -cne $resolvedRoot -or !$resolvedDelete.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)) {throw 'CP6_CLEANUP_OFFLINE_DELETE_DIRECTORY'}
    Remove-Item -LiteralPath $resolvedDelete -Recurse -Force
}
$report=[ordered]@{Task='DB-COMPAT-01-WP6';Status=$status;CheckedUtc=[datetime]::UtcNow.ToString('o');Scope='Offline cleanup state-transition controls only. Module-local fake state; no native DB acceptance.';NativeExecuted=$false;ScriptSha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;LifecycleModuleSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'DatabaseCompatibilityLifecycle.psm1')).Hash;Checks=$records.Count;Assertions=$records.ToArray();FailureCode=$failureCode}
if($ReportPath) {
    if(![IO.Path]::IsPathFullyQualified($ReportPath) -or (Test-Path -LiteralPath $ReportPath)) {throw 'CP6_CLEANUP_OFFLINE_NEW_REPORT_REQUIRED'}
    [IO.File]::WriteAllText($ReportPath,($report|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($false))
}
[pscustomobject]@{Status=$status;Checks=$records.Count;NativeExecuted=$false;FailureCode=$failureCode}|ConvertTo-Json -Compress
if($status -cne 'Passed') {exit 1}
