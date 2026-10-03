# Pure offline command/environment contracts; never starts a process or contacts a database.
[CmdletBinding()]
param([string]$Worktree=(Split-Path (Split-Path $PSScriptRoot -Parent) -Parent))
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$module=Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityEntries.psm1') -Force -PassThru
$script:checks=0
function Assert-Offline([bool]$Condition,[string]$Name){if(!$Condition){throw "Offline entry assertion failed: $Name"};$script:checks++}
$entries=@(Get-Cp6CompatibilityEntry -Worktree $Worktree)
Assert-Offline ($entries.Count -ge 57) 'complete original required entry table plus explicit additions'
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('cp6-entry-offline-'+[guid]::NewGuid().ToString('N'))
$values=@{SeedStatePath=(Join-Path $temporary 'seed.json');CapturedSeedStatePath=(Join-Path $temporary 'captured.json');ApplicationStatePath=(Join-Path $temporary 'app.json');TenantId=[guid]::NewGuid().ToString();UserId=[guid]::NewGuid().ToString();EventKey='offline-event'}
foreach($entry in $entries){
    foreach($provider in $entry.ProviderEligibility){
        $entryValues=$values.Clone();$entryExtra=@{}
        if($entry.Id -ceq 'application-signalr-user-delivery'){
            $entryValues.BaseUri='http://127.0.0.1:53123';$entryValues.SignalREventKey='offline-signalr-event'
            $entryExtra=@{CP6_COMPAT_ADMIN_PASSWORD='sentinel-admin';CP6_COMPAT_ORDINARY_USERNAME='offline-user';CP6_COMPAT_ORDINARY_PASSWORD='sentinel-user'}
        }
        $plan=& $module {param($e,$p,$w,$o,$v) New-Cp6EntryPlan $e $p $w $o $v Debug ('a'*40)} $entry $provider $Worktree $temporary $entryValues
        Assert-Offline ($plan.Arguments.Count -gt 0 -and @($plan.Arguments|Where-Object {$_ -match '[{}]'}).Count -eq 0) ('resolved tokens '+$entry.Id)
        Assert-Offline (@($plan.Arguments|Where-Object {$_ -match 'sentinel'}).Count -eq 0) 'no credential argv'
        $receipt=[pscustomobject]@{DatabaseName='CP6Compat_WP6_20261101_a1b2c3d4_'+$entry.Role;Owner='a1b2c3d4e5f60718293a4b5c6d7e8f90';Role=$entry.Role}
        $environment=& $module {param($e,$p,$r,$x) Get-Cp6EntryEnvironment $e $p $r 'sentinel-connection' $x} $entry $provider $receipt $entryExtra
        Assert-Offline ($environment.CP6_COMPAT_SCOPE -ceq 'WP6' -and $environment.CP6_COMPAT_DATABASE_NAME -ceq $receipt.DatabaseName -and $environment.CP6_TEST_DATABASE_OWNER -ceq $receipt.Owner) 'common WP6 contract'
        Assert-Offline (@($environment.Keys|Where-Object {$_ -match '^CP6_.*_TEST_PROVIDER$'}).Count -le 1) 'single fixture selector'
        if($entry.EntryKind -ceq 'Trx'){
            Assert-Offline ('--no-build' -cin $plan.Arguments -and '--no-restore' -cin $plan.Arguments -and $entry.Filter -cin $plan.Arguments) 'TRX exact filter no rebuild'
        }
        if($entry.Role -ceq 'spacehistory'){Assert-Offline ($environment.ContainsKey('CP6_SPACE_MIGRATION_TEST_PROVIDER') -and !$environment.ContainsKey('CP6_SPACE_TEST_PROVIDER')) 'history lane isolation'}
        if($entry.EntryKind -ceq 'IdentityChecks'){Assert-Offline ($plan.Arguments.Count -eq 6 -and $plan.Arguments[3] -ceq 'provider-case' -and $plan.Arguments[4] -ceq $provider) 'identity positional CLI'}
    }
}
$unknown='';try{$null=Get-Cp6CompatibilityEntry -Worktree $Worktree -Id not-a-case}catch{$unknown=$_.Exception.Message}
Assert-Offline ($unknown -ceq 'CP6_COMPAT_ENTRY_UNKNOWN') 'unknown id fails'
$errorCode='';try{$null=Invoke-Cp6CompatibilityBuild -Worktree $Worktree -OutputDirectory $temporary -Projects @('../outside.csproj') -SourceSha ('a'*40)}catch{$errorCode=$_.Exception.Message}
Assert-Offline ($errorCode -ceq 'CP6_COMPAT_PROJECT_INVALID') 'build rejects projects outside approved table before process execution'
$inputs=@(& $module {param($w) Get-Cp6ProjectInputs $w 'CP6.Tests/CP6.Tests.csproj'} $Worktree)
Assert-Offline (@($inputs|Where-Object Path -CEQ 'eng/database-compatibility/OwnedTestDatabase.cs').Count -eq 1) 'shared linked source hash is bound'
Assert-Offline (@($inputs|Where-Object Path -CLike 'eng/crm/legacy-model-fixture/*.cs').Count -gt 0) 'existing linked source wildcard is bound'
$signalr=(Get-Cp6CompatibilityEntry -Worktree $Worktree -Id 'application-histories-first')|ConvertTo-Json -Depth 30|ConvertFrom-Json
$signalr.Id='application-signalr-user-delivery';$signalr.CaseCommand=@('signalr-verification','--base-uri','{BaseUri}','--tenant','{TenantId}','--user','{UserId}','--event-key','{SignalREventKey}')
$signalrValues=$values.Clone();$signalrValues.BaseUri='http://127.0.0.1:53123';$signalrValues.SignalREventKey='offline-signalr-event'
$signalrPlan=& $module {param($e,$w,$o,$v) New-Cp6EntryPlan $e SqlServer $w $o $v Debug ('a'*40)} $signalr $Worktree $temporary $signalrValues
Assert-Offline ('http://127.0.0.1:53123' -cin $signalrPlan.Arguments -and 'offline-signalr-event' -cin $signalrPlan.Arguments) 'signalr exact loopback URL and distinct event key'
$extra=@{CP6_COMPAT_ADMIN_PASSWORD='sentinel-admin';CP6_COMPAT_ORDINARY_USERNAME='offline-user';CP6_COMPAT_ORDINARY_PASSWORD='sentinel-user'}
$signalrEnvironment=& $module {param($e,$x) Get-Cp6EntryEnvironment $e SqlServer ([pscustomobject]@{Role='application';DatabaseName='offline';Owner='offline'}) 'sentinel-connection' $x} $signalr $extra
Assert-Offline ($signalrEnvironment.CP6_COMPAT_ADMIN_PASSWORD -ceq 'sentinel-admin' -and $signalrEnvironment.CP6_COMPAT_ORDINARY_PASSWORD -ceq 'sentinel-user') 'credentials only in private child environment'
foreach($url in @('http://example.com:53123','http://127.0.0.1:53123/path','http://user:sentinel@127.0.0.1:53123','http://127.0.0.1:53123/?q=sentinel')){
    $bad=$signalrValues.Clone();$bad.BaseUri=$url;$errorCode=''
    try{$null=& $module {param($e,$w,$o,$v) New-Cp6EntryPlan $e SqlServer $w $o $v Debug ('a'*40)} $signalr $Worktree $temporary $bad}catch{$errorCode=$_.Exception.Message}
    Assert-Offline ($errorCode -ceq 'CP6_COMPAT_BASE_URI_INVALID') 'nonlocal or credential URI rejected safely'
}
foreach($badExtra in @(@{},@{CP6_COMPAT_ADMIN_PASSWORD='sentinel';CP6_COMPAT_ORDINARY_USERNAME='offline';CP6_TEST_DATABASE_OWNER='sentinel'},@{CP6_COMPAT_ADMIN_PASSWORD='sentinel';CP6_COMPAT_ORDINARY_USERNAME='offline';CP6_COMPAT_ORDINARY_PASSWORD=''})){
    $errorCode='';try{$null=& $module {param($e,$x) Get-Cp6EntryEnvironment $e SqlServer ([pscustomobject]@{Role='application';DatabaseName='offline';Owner='offline'}) 'sentinel-connection' $x} $signalr $badExtra}catch{$errorCode=$_.Exception.Message}
    Assert-Offline ($errorCode -cin @('CP6_COMPAT_SIGNALR_CREDENTIALS_REQUIRED','CP6_COMPAT_SIGNALR_CREDENTIALS_INVALID')) 'missing or non-allowlisted signalr credentials rejected'
}
$ordinary=Get-Cp6CompatibilityEntry -Worktree $Worktree -Id 'application-histories-first'
$errorCode='';try{$null=& $module {param($e,$x) Get-Cp6EntryEnvironment $e SqlServer ([pscustomobject]@{Role='application';DatabaseName='offline';Owner='offline'}) 'sentinel-connection' $x} $ordinary $extra}catch{$errorCode=$_.Exception.Message}
Assert-Offline ($errorCode -ceq 'CP6_COMPAT_EXTRA_ENVIRONMENT_FORBIDDEN') 'other entries reject all extra environment'
$sourceInputs=@(Get-Cp6CompatibilitySourceInputs -Worktree $Worktree -Projects @('CP6.Tests/CP6.Tests.csproj'))
Assert-Offline ($sourceInputs.Count -eq $inputs.Count -and @($sourceInputs|Where-Object Path -CEQ 'eng/database-compatibility/OwnedTestDatabase.cs').Count -eq 1) 'public readonly source-input union'
# Exercise the real entry orchestration with module-local fake ownership/process calls.
# The dummy .dll is never loaded. Synthetic reports are parser inputs only.
$fakeRoot=Join-Path $temporary 'worktree';$fakeProject='tools/CP6.DatabaseCompatibility.ApplicationProbe/CP6.DatabaseCompatibility.ApplicationProbe.csproj'
$fakeProjectPath=Join-Path $fakeRoot $fakeProject
$fakeManifest=Join-Path $fakeRoot 'eng/database-compatibility/required-cases.json'
$null=[IO.Directory]::CreateDirectory((Split-Path $fakeProjectPath -Parent));$null=[IO.Directory]::CreateDirectory((Split-Path $fakeManifest -Parent))
Copy-Item -LiteralPath (Join-Path $Worktree $fakeProject) -Destination $fakeProjectPath
Copy-Item -LiteralPath (Join-Path $Worktree 'eng/database-compatibility/required-cases.json') -Destination $fakeManifest
$fakeBinary=Join-Path (Split-Path $fakeProjectPath -Parent) 'bin/Debug/net8.0/CP6.DatabaseCompatibility.ApplicationProbe.dll'
$null=[IO.Directory]::CreateDirectory((Split-Path $fakeBinary -Parent));[IO.File]::WriteAllText($fakeBinary,'offline dummy; never executed')
$fakeDependency=Join-Path (Split-Path $fakeBinary -Parent) 'CP6.Core.dll'
[IO.File]::WriteAllText($fakeDependency,'offline dependency before')
$fakeNative=Join-Path (Split-Path $fakeBinary -Parent) 'runtimes/offline/native/provider.data'
$null=[IO.Directory]::CreateDirectory((Split-Path $fakeNative -Parent));[IO.File]::WriteAllText($fakeNative,'offline nested runtime resource')
try {
    & $module {
        $script:offlineOwnerFailure=$false;$script:offlineWrongDatabase=$false;$script:offlineExitCode=0;$script:offlineProcessCalls=0;$script:offlineOwnerChecked=$false;$script:offlineMutateDependency='';$script:offlineDeleteDependency=$false
        function script:Test-Cp6OwnedDatabase {param($Context,$Receipt,[switch]$RequireEmpty)
            if($script:offlineOwnerFailure){throw 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH'}
            $script:offlineOwnerChecked=$true;return [pscustomobject]@{Exists=$true}
        }
        function script:Get-Cp6OwnedDatabaseConnection {param($Context,$Receipt) return 'sentinel-private-connection'}
        function script:Get-Command {param($Name,$CommandType,$ErrorAction) return [pscustomobject]@{Source='offline-dotnet-never-executed'}}
        function script:Invoke-Cp6CompatibilityProcess {param($Executable,$ArgumentList,$WorkingDirectory,$LogDirectory,$ChildEnvironment,$TimeoutSeconds)
            if(!$script:offlineOwnerChecked){throw 'CP6_COMPAT_OFFLINE_OWNER_ORDER_INVALID'}
            $script:offlineProcessCalls++
            $reportIndex=[array]::IndexOf($ArgumentList,'--report')+1
            $database=if($script:offlineWrongDatabase){'different-database'}else{$ChildEnvironment.CP6_COMPAT_DATABASE_NAME}
            $report=[pscustomobject]@{SchemaVersion=1;Task='DB-COMPAT-01-WP6';Status='Passed';Provider='SqlServer';DatabaseName=$database;Command='histories';Role='Application';Counts=@{EffectiveProfiles=4};Assertions=@([pscustomobject]@{Name='owner-and-physical-database-verified';Passed=$true},[pscustomobject]@{Name='four-effective-history-profiles-complete';Passed=$true})}
            Write-Cp6EntryJson $ArgumentList[$reportIndex] $report
            if($script:offlineMutateDependency){
                if($script:offlineDeleteDependency){[IO.File]::Delete($script:offlineMutateDependency)}
                else{[IO.File]::WriteAllText($script:offlineMutateDependency,'offline dependency changed during fake execution')}
            }
            return [pscustomobject]@{ExitCode=$script:offlineExitCode;TimedOut=$false;OwnedProcessTerminated=$false;ProcessId=0}
        }
    }
    $context=[pscustomobject]@{Provider='SqlServer';PrivateDirectory=(Join-Path $temporary 'private')}
    $receipt=[pscustomobject]@{Provider='SqlServer';Role='application';DatabaseName='CP6Compat_WP6_20261101_a1b2c3d4_application';Owner='a1b2c3d4e5f60718293a4b5c6d7e8f90'}
    $good=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-success') -SourceSha ('a'*40)
    Assert-Offline ($good.Success -and $good.Validation.Counts.Passed -eq 2 -and $good.Process.ExitCode -eq 0) 'actual process return drives validation'
    Assert-Offline (($good|ConvertTo-Json -Depth 30) -cnotmatch 'sentinel') 'public record excludes private environment'
    & $module {$script:offlineWrongDatabase=$true}
    $wrong=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-wrong-database') -SourceSha ('a'*40)
    Assert-Offline (!$wrong.Success -and 'ActualDatabaseMismatch' -cin $wrong.Validation.FailureCodes) 'wrong actual report database fails'
    & $module {$script:offlineWrongDatabase=$false;$script:offlineExitCode=9}
    $exitFailure=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-exit-failure') -SourceSha ('a'*40)
    Assert-Offline (!$exitFailure.Success -and 'ProcessFailed' -cin $exitFailure.Validation.FailureCodes -and $exitFailure.Process.ExitCode -eq 9) 'passing-looking report cannot override failed process'
    & $module {$script:offlineOwnerFailure=$true}
    $ownerFailure=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-owner-failure') -SourceSha ('a'*40)
    Assert-Offline (!$ownerFailure.Success -and $ownerFailure.FailureCode -ceq 'CP6_COMPAT_ACTUAL_OWNER_MISMATCH' -and (& $module {$script:offlineProcessCalls}) -eq 3) 'owner failure preserves evidence and launches no process'
    & $module {param($p) $script:offlineOwnerFailure=$false;$script:offlineExitCode=0;$script:offlineMutateDependency=$p} $fakeDependency
    $changed=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-changed-dependency') -SourceSha ('a'*40)
    Assert-Offline (!$changed.Success -and $changed.FailureCode -ceq 'CP6_COMPAT_RUNTIME_ARTIFACT_CHANGED') 'dependency mutation during execution must fail despite passing process/report'
    Assert-Offline ($changed.Process.ExitCode -eq 0 -and $changed.Validation.Success -and !$changed.RuntimeArtifactsUnchanged) 'artifact failure preserves actual passing process and report evidence'
    Assert-Offline ($changed.BinarySha256 -ceq $good.BinarySha256 -and $changed.RuntimeArtifactManifestSha256 -cne $changed.RuntimeArtifactAfterManifestSha256) 'unchanged primary DLL cannot conceal dependency change'
    $runtime=Get-Content -LiteralPath $good.RuntimeArtifactManifestPath -Raw | ConvertFrom-Json
    Assert-Offline ($runtime.Files.Count -eq 3 -and $runtime.Files.Path -ccontains 'CP6.Core.dll' -and $runtime.Files.Path -ccontains 'runtimes/offline/native/provider.data') 'recursive runtime manifest includes dependencies and all resource extensions'
    Assert-Offline (@($runtime.Files | Where-Object {$_.Length -le 0 -or $_.Sha256 -cnotmatch '^[A-F0-9]{64}$'}).Count -eq 0) 'every runtime file records actual length and hash'
    Assert-Offline ($good.RuntimeArtifactsUnchanged -and $good.RuntimeArtifactManifestSha256 -ceq $good.RuntimeArtifactAfterManifestSha256) 'unmodified execution binds identical complete runtime manifests'
    $addedDependency=Join-Path (Split-Path $fakeBinary -Parent) 'runtimes/offline/native/new-provider.data'
    & $module {param($p) $script:offlineMutateDependency=$p} $addedDependency
    $added=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-added-runtime-file') -SourceSha ('a'*40)
    Assert-Offline (!$added.Success -and $added.FailureCode -ceq 'CP6_COMPAT_RUNTIME_ARTIFACT_CHANGED') 'new nested runtime file during execution fails'
    & $module {param($p) $script:offlineMutateDependency=$p;$script:offlineDeleteDependency=$true} $fakeNative
    $removed=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path $temporary 'entry-removed-runtime-file') -SourceSha ('a'*40)
    Assert-Offline (!$removed.Success -and $removed.FailureCode -ceq 'CP6_COMPAT_RUNTIME_ARTIFACT_CHANGED') 'removed nested runtime file during execution fails'
    $inside=Invoke-Cp6CompatibilityEntry -Entry $ordinary -Provider SqlServer -Context $context -Receipt $receipt -Worktree $fakeRoot -OutputDirectory (Join-Path (Split-Path $fakeBinary -Parent) 'bad-test-results') -SourceSha ('a'*40)
    Assert-Offline (!$inside.Success -and $inside.FailureCode -ceq 'CP6_COMPAT_ENTRY_OUTPUT_INSIDE_RUNTIME' -and (& $module {$script:offlineProcessCalls}) -eq 6) 'test logs must remain outside immutable runtime directory'
}finally{
    $resolved=[IO.Path]::GetFullPath($temporary);$tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if($resolved.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase)){Remove-Item -LiteralPath $resolved -Recurse -Force}
}
[pscustomobject]@{Scope='Offline entry contracts only';Checks=$script:checks;NativeExecuted=$false;DotnetExecuted=$false;Passed=$true}
