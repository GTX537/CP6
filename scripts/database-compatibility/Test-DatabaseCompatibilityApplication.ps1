# Pure offline artifact-identity regression checks. Ownership and process calls are replaced
# before use; neither initializer nor host may reach a real database, process or listener.
[CmdletBinding()]
param([string]$ReportPath)
$ErrorActionPreference='Stop';Set-StrictMode -Version Latest
$module=Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityApplication.psm1') -Force -PassThru
$temporary=Join-Path ([IO.Path]::GetTempPath()) ('cp6-application-offline-'+[guid]::NewGuid().ToString('N'))
$checks=[Collections.Generic.List[object]]::new()
$null=& $module {
    $script:OfflineOwnerCalls=0
    function script:Test-Cp6OwnedDatabase {param($Context,$Receipt,[switch]$RequireNoSessions)
        $script:OfflineOwnerCalls++;throw 'CP6_COMPAT_OFFLINE_AFTER_ARTIFACT'
    }
    function script:Get-Cp6OwnedDatabaseConnection {throw 'CP6_COMPAT_OFFLINE_DATABASE_FORBIDDEN'}
    function script:Invoke-Cp6CompatibilityProcess {throw 'CP6_COMPAT_OFFLINE_PROCESS_FORBIDDEN'}
    function script:Start-Cp6CompatibilityProcess {throw 'CP6_COMPAT_OFFLINE_PROCESS_FORBIDDEN'}
}
try {
    foreach($entry in @('Initialization','Host')) {
        foreach($scenario in @('Unchanged','AddedFile','MissingFile','SameLengthChangedFile','ArtifactDirectoryChanged','ManifestChanged','ManifestIdentityChanged','PrivateArtifactChanged')) {
            $run=Join-Path $temporary ($entry+'-'+$scenario)
            $private=Join-Path $run 'private';$artifact=Join-Path $run 'artifact'
            $null=[IO.Directory]::CreateDirectory($private);$null=[IO.Directory]::CreateDirectory($artifact)
            [IO.File]::WriteAllText((Join-Path $artifact 'CP6.WebApi.dll'),'offline-main-not-an-assembly')
            [IO.File]::WriteAllText((Join-Path $artifact 'CP6.Core.dll'),'original-offline-bytes')
            $context=[pscustomobject]@{Task='DB-COMPAT-01-WP6';RunId=[guid]::NewGuid().ToString('N');Provider='SqlServer';RunDirectory=$run;PrivateDirectory=$private}
            $receipt=[pscustomobject]@{DatabaseName='offline-never-connected';Role='application'}
            $settings=New-Cp6ApplicationSettings -Context $context -ArtifactDirectory $artifact -SourceSha ('a'*40)
            $expected='CP6_COMPAT_API_ARTIFACT_CHANGED';$expectedOwnerCalls=0
            switch($scenario) {
                Unchanged {$expected='CP6_COMPAT_OFFLINE_AFTER_ARTIFACT';$expectedOwnerCalls=1}
                AddedFile {[IO.File]::WriteAllText((Join-Path $artifact 'unexpected.json'),'{}')}
                MissingFile {Remove-Item -LiteralPath (Join-Path $artifact 'CP6.Core.dll')}
                SameLengthChangedFile {[IO.File]::WriteAllText((Join-Path $artifact 'CP6.Core.dll'),'modified-offline-bytes')}
                ArtifactDirectoryChanged {$settings.ArtifactDirectory=Join-Path $run 'other-artifact';$expected='CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
                ManifestChanged {[IO.File]::AppendAllText($settings.ArtifactManifestPath,' ')}
                ManifestIdentityChanged {
                    $manifest=Get-Content -LiteralPath $settings.ArtifactManifestPath -Raw | ConvertFrom-Json
                    $manifest.SourceSha='b'*40
                    [IO.File]::WriteAllText($settings.ArtifactManifestPath,($manifest|ConvertTo-Json -Depth 8))
                    $settings.ArtifactManifestSha256=(Get-FileHash -LiteralPath $settings.ArtifactManifestPath).Hash
                    $expected='CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'
                }
                PrivateArtifactChanged {
                    $saved=Get-Content -LiteralPath $settings.SettingsPath -Raw | ConvertFrom-Json
                    $saved.ArtifactDirectory=Join-Path $run 'other-artifact'
                    [IO.File]::WriteAllText($settings.SettingsPath,($saved|ConvertTo-Json))
                    $expected='CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'
                }
            }
            & $module {$script:OfflineOwnerCalls=0}
            $actual='NO_FAILURE'
            try {
                if($entry -ceq 'Initialization'){$null=Invoke-Cp6ApplicationInitialization -Context $context -Receipt $receipt -Settings $settings -Label offline}
                else{$null=Start-Cp6ApplicationHost -Context $context -Receipt $receipt -Settings $settings -Label offline}
            }catch{$actual=if($_.Exception.Message -cmatch '^CP6_COMPAT_[A-Z0-9_]+$'){$_.Exception.Message}else{'UNEXPECTED_ERROR'}}
            $ownerCalls=& $module {$script:OfflineOwnerCalls}
            $checks.Add([pscustomobject]@{Entry=$entry;Scenario=$scenario;Passed=($actual -ceq $expected -and $ownerCalls -eq $expectedOwnerCalls);Expected=$expected;Actual=$actual;OwnerCalls=$ownerCalls})
        }
    }
    $failed=@($checks | Where-Object Passed -NE $true).Count
    $report=[pscustomobject]@{Status=$(if($failed -eq 0){'Passed'}else{'Failed'});Scope='Pure offline fake artifact files; zero real process, listener, database or native execution';ExpectedCases=$checks.Count;Passed=$checks.Count-$failed;Failed=$failed;Checks=@($checks)}
    if($ReportPath){
        if(Test-Path -LiteralPath $ReportPath){throw 'CP6_COMPAT_OFFLINE_REPORT_EXISTS'}
        [IO.File]::WriteAllText([IO.Path]::GetFullPath($ReportPath),($report|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))
    }
    $report
    if($failed -gt 0){throw 'CP6_COMPAT_APPLICATION_OFFLINE_FAILED'}
}
finally {
    Remove-Module DatabaseCompatibilityApplication -Force
    $tempRoot=[IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    $owned=[IO.Path]::GetFullPath($temporary)
    if(!$owned.StartsWith($tempRoot,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetFileName($owned) -cnotmatch '^cp6-application-offline-[a-f0-9]{32}$'){throw 'CP6_COMPAT_OFFLINE_CLEANUP_PATH_INVALID'}
    if(Test-Path -LiteralPath $owned){Remove-Item -LiteralPath $owned -Recurse -Force}
}
