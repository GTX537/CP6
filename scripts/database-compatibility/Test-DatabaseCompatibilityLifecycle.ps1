# Pure offline contract tests. These never execute a native tool or contact a database.
[CmdletBinding()]
param()
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$module=Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityLifecycle.psm1') -Force -PassThru
$script:checks=0
function Assert-Offline([bool]$Value,[string]$Name) { if(!$Value){throw "Offline lifecycle assertion failed: $Name"};$script:checks++ }
function Get-OfflineConfiguration($Provider,$Connection) { & $module { param($p,$c) ConvertTo-Cp6LifecycleConfiguration $p $c } $Provider $Connection }
function Reject($Provider,$Connection) {
    $caught=$null
    try { $null=Get-OfflineConfiguration $Provider $Connection } catch { $caught=$_.Exception.Message }
    Assert-Offline ($null -ne $caught -and $caught -cmatch '^CP6_COMPAT_[A-Z_]+$' -and $caught -cnotmatch 'sentinel') 'safe configuration rejection'
}
$sql=Get-OfflineConfiguration SqlServer 'Server=localhost,1433;Database=master;User Id=sa;Password=sentinel;Encrypt=True;TrustServerCertificate=True'
Assert-Offline ($sql.Endpoint -ceq 'tcp:localhost,1433') 'explicit SQL TCP endpoint'
Assert-Offline ($sql.Values.password -ceq 'sentinel') 'private password retained'
$pg=Get-OfflineConfiguration PostgreSql 'Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=sentinel;SSL Mode=Disable'
Assert-Offline ($pg.Endpoint -ceq '127.0.0.1:5432') 'explicit PG endpoint'
$quotedSql=Get-OfflineConfiguration SqlServer 'Server=localhost;User Id=sa;Password="sentinel;Server=other;""quoted""";Encrypt=True'
Assert-Offline ($quotedSql.Values.password -ceq 'sentinel;Server=other;"quoted"') 'double quoted password is not a routing key'
$quotedPg=Get-OfflineConfiguration PostgreSql "Host=localhost;Username=postgres;Password='sentinel;Host=other;''quoted''';SSL Mode=Disable"
Assert-Offline ($quotedPg.Values.password -ceq "sentinel;Host=other;'quoted'") 'single quoted password is not a routing key'
Reject SqlServer 'Server=localhost;Data Source=localhost;User Id=sa;Password="sentinel;Server=other"'
foreach($c in @('Server=remote;Password=sentinel','Server=localhost;AttachDbFilename=x;Password=sentinel','Server=localhost;MultipleActiveResultSets=True;Password=sentinel','Server=localhost;Failover Partner=localhost;Password=sentinel','Server=localhost;Unknown=sentinel','Server=localhost;Server=127.0.0.1;Password=sentinel')) { Reject SqlServer $c }
foreach($c in @('Host=remote;Password=sentinel','Host=localhost,127.0.0.1;Password=sentinel','Host=/tmp;Password=sentinel','Host=localhost;Multiplexing=True;Password=sentinel','Host=localhost;Search Path=other;Password=sentinel','Host=localhost;Options=sentinel','Host=localhost;SSL Mode=Allow;Password=sentinel')) { Reject PostgreSql $c }
Reject Invalid 'Password=sentinel'
# Additional orchestration tests use module-local fakes, never executable stubs.
$offlineRoot=Join-Path ([IO.Path]::GetTempPath()) ('cp6-lifecycle-offline-'+[guid]::NewGuid().ToString('N'))
$null=[IO.Directory]::CreateDirectory($offlineRoot)
try {
    & $module {
        $script:fakeDatabases=@{};$script:drops=0;$script:plannedSeen=0
        function script:Get-Cp6ActualDatabase($Context,[string]$Name){
            if($script:fakeDatabases.ContainsKey($Name)){return $script:fakeDatabases[$Name]}
            return [pscustomobject]@{Exists=$false}
        }
        function script:Invoke-Cp6Statement($Context,[string]$Database,[string]$Query){
            if($Query -match '^CREATE DATABASE [\["](?<name>[^\]"]+)'){
                $receipt=Get-Content -LiteralPath (Join-Path $Context.ReceiptDirectory ($Matches.name+'.json')) -Raw | ConvertFrom-Json
                if($receipt.Status -cne 'Planned'){throw 'CP6_COMPAT_OFFLINE_PLANNED_MISSING'}
                $script:plannedSeen++
            }elseif($Query -match '^DROP DATABASE [\["](?<name>[^\]"]+)'){$script:drops++;$script:fakeDatabases.Remove($Matches.name)}
        }
        function script:Set-Cp6Marker($Context,$Receipt){
            $script:fakeDatabases[$Receipt.DatabaseName]=[pscustomobject]@{Exists=$true;DatabaseName=$Receipt.DatabaseName;Task=$Receipt.Task;Owner=$Receipt.Owner;Marker=($Receipt.Task+':'+$Receipt.Owner);DatabaseId=($script:fakeDatabases.Count+100);CreatedAt='2026-11-01T00:00:00';Files=@();Principal='offline';DatabaseOwner='offline';ServerVersion='offline';UserTables=0;Sessions=0;SessionVisibility=$true}
        }
    }
    foreach($provider in @('SqlServer','PostgreSql')){
        $directory=Join-Path $offlineRoot $provider;$null=[IO.Directory]::CreateDirectory($directory)
        $context=& $module {
            param($p,$d)
            $public=[pscustomobject]@{RunId=[guid]::NewGuid().ToString('N');Provider=$p;Task=$script:task;Endpoint='offline';RunDirectory=$d;PrivateDirectory=$d;ReceiptDirectory=$d}
            $script:contexts[$public.RunId]=@{Public=($public|ConvertTo-Json|ConvertFrom-Json)}
            return $public
        } $provider $directory
        $one=New-Cp6OwnedDatabase $context application
        $two=New-Cp6OwnedDatabase $context restore
        Assert-Offline ($one.Status -ceq 'Owned' -and $one.Owner -cmatch '^[a-f0-9]{32}$' -and $one.DatabaseName.EndsWith('_application')) 'new owned receipt'
        Assert-Offline ((Test-Cp6OwnedDatabase $context $one -RequireEmpty -RequireNoSessions).Exists) 'verify exact owner identity'
        if($provider -ceq 'PostgreSql'){
            & $module {param($n) $script:fakeDatabases[$n].Marker+=':extra'} $two.DatabaseName
            $errorCode='';try{$null=Test-Cp6OwnedDatabase $context $two}catch{$errorCode=$_.Exception.Message}
            Assert-Offline ($errorCode -ceq 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH') 'PG marker rejects trailing segments'
            & $module {param($n,$m) $script:fakeDatabases[$n].Marker=$m} $two.DatabaseName ($two.Task+':'+$two.Owner)
        }
        & $module {param($n) $script:fakeDatabases[$n].Sessions=1} $two.DatabaseName
        $errorCode='';try{$null=Remove-Cp6OwnedDatabases $context @($one,$two) -SessionWaitSeconds 0}catch{$errorCode=$_.Exception.Message}
        Assert-Offline ($errorCode -ceq 'CP6_COMPAT_DATABASE_HAS_SESSIONS') 'cleanup rejects active session'
        Assert-Offline ((& $module {$script:drops}) -eq $(if($provider -ceq 'SqlServer'){0}else{2})) 'all-target preflight before any drop'
        & $module {param($n) $script:fakeDatabases[$n].Sessions=0;$script:fakeDatabases[$n].Owner='wrong'} $two.DatabaseName
        $errorCode='';try{$null=Remove-Cp6OwnedDatabases $context @($one,$two)}catch{$errorCode=$_.Exception.Message}
        Assert-Offline ($errorCode -ceq 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH') 'cleanup rejects wrong owner'
        & $module {param($n,$o) $script:fakeDatabases[$n].Owner=$o} $two.DatabaseName $two.Owner
        $invalidBackup=[pscustomobject]@{RunId='other-run'}
        $errorCode='';try{$null=Restore-Cp6OwnedDatabase $context $one $two $invalidBackup}catch{$errorCode=$_.Exception.Message}
        Assert-Offline ($errorCode -ceq 'CP6_COMPAT_BACKUP_RECEIPT_MISMATCH') 'foreign backup rejected before native restore'
        $errorCode='';try{$null=Restore-Cp6OwnedDatabase $context $two $one $invalidBackup}catch{$errorCode=$_.Exception.Message}
        Assert-Offline ($errorCode -ceq 'CP6_COMPAT_RESTORE_TARGET_INVALID') 'restore role is mandatory'
        & $module {param($n) $script:fakeDatabases[$n].UserTables=1} $two.DatabaseName
        $errorCode='';try{$null=Restore-Cp6OwnedDatabase $context $one $two $invalidBackup}catch{$errorCode=$_.Exception.Message}
        Assert-Offline ($errorCode -ceq 'CP6_COMPAT_DATABASE_NOT_EMPTY') 'populated restore target rejected'
        & $module {param($n) $script:fakeDatabases[$n].UserTables=0;$script:fakeDatabases[$n].DatabaseId++} $two.DatabaseName
        $errorCode='';try{$null=Test-Cp6OwnedDatabase $context $two}catch{$errorCode=$_.Exception.Message}
        Assert-Offline ($errorCode -ceq 'CP6_COMPAT_PHYSICAL_IDENTITY_MISMATCH') 'same name and owner cannot replace physical identity'
        & $module {param($n) $script:fakeDatabases[$n].DatabaseId--} $two.DatabaseName
        $result=@(Remove-Cp6OwnedDatabases $context @($one,$two))
        Assert-Offline ($result.Count -eq 2 -and @($result|Where-Object Status -CNE 'Absent').Count -eq 0) 'explicit normal drop and absence'
        $null=Remove-Cp6OwnedDatabases $context @($one,$two)
    }
    Assert-Offline ((& $module {$script:plannedSeen}) -eq 4) 'every create has planned receipt'
}finally{
    # Only the exact freshly created offline directory is removed; no native resources exist.
    if([IO.Path]::GetFullPath($offlineRoot).StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()),[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $offlineRoot -Recurse -Force}
}
[pscustomobject]@{Scope='Offline lifecycle contracts only';Checks=$script:checks;NativeExecuted=$false;Passed=$true}
