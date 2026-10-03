# Local, sequential WP6 verification. A failed run retains all databases and evidence.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('SqlServer','PostgreSql')][string]$Provider,
    [Parameter(Mandatory)][string]$RunDirectory,
    [string]$LocalConfigurationPath,
    [string]$PostgreSqlBinDirectory,
    [ValidateSet('Disable','Require','VerifyCA','VerifyFull')][string]$PostgreSqlSslMode,
    [ValidateSet('Full','Matrix','Application')][string]$Phase='Full',
    [string]$ArtifactDirectory,
    [switch]$SkipBuild,
    [ValidateSet('Debug','Release')][string]$Configuration='Debug',
    [switch]$PlanOnly
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$worktree=Split-Path $PSScriptRoot -Parent
$runnerPath=$PSCommandPath
$moduleDirectory=Join-Path $PSScriptRoot 'database-compatibility'
foreach($name in @('Results','Process','Lifecycle','Application','Http','Baseline','Entries')) {
    Import-Module (Join-Path $moduleDirectory ('DatabaseCompatibility'+$name+'.psm1'))
}
function Assert-Runner([bool]$Condition,[string]$Code){if(!$Condition){throw [InvalidOperationException]::new('CP6_COMPAT_'+$Code)}}
function Get-RunnerField($Object,[string]$Name){
    if($Object -is [Collections.IDictionary]){if($Object.Contains($Name)){return ,$Object[$Name]}}
    elseif($null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]){return ,$Object.$Name}
    return $null
}
function Get-RunnerHash($Value){
    $text=ConvertTo-Json -InputObject $Value -Depth 50 -Compress
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}
function Write-RunnerProof([string]$Name,$Value){
    Assert-Runner ($Name -cmatch '^[a-z0-9-]+$') 'REPORT_NAME_INVALID'
    $path=Join-Path $script:runnerContext.RunDirectory ($Name+'.json')
    $stream=[IO.FileStream]::new($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try{$bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 50));$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
    return $path
}
function Read-RunnerConfiguration {
    $local=$null
    if($LocalConfigurationPath){
        try{$local=Get-Content -LiteralPath $LocalConfigurationPath -Raw | ConvertFrom-Json -ErrorAction Stop}catch{throw 'CP6_COMPAT_LOCAL_CONFIGURATION_INVALID'}
        Assert-Runner ($local -isnot [array] -and $null -ne $local) 'LOCAL_CONFIGURATION_INVALID'
    }
    $envName=if($Provider -ceq 'SqlServer'){'CP6_TEST_SQLSERVER'}else{'CP6_TEST_POSTGRES'}
    $connection=[Environment]::GetEnvironmentVariable($envName,'Process')
    $connectionSource='ProcessEnvironment'
    if([string]::IsNullOrWhiteSpace($connection)){$connection=Get-RunnerField $local $Provider;$connectionSource='ExplicitLocalConfiguration'}
    Assert-Runner ($connection -is [string] -and ![string]::IsNullOrWhiteSpace($connection)) 'SELECTED_PROVIDER_CONNECTION_REQUIRED'
    if($Provider -ceq 'PostgreSql' -and $PostgreSqlSslMode){
        # Validate duplicates/aliases before normalization, then apply only the explicitly requested TLS mode.
        $lifecycle=Get-Module DatabaseCompatibilityLifecycle
        $null=& $lifecycle {param($p,$c) ConvertTo-Cp6LifecycleConfiguration $p $c} $Provider $connection
        $builder=[Data.Common.DbConnectionStringBuilder]::new();$builder.set_ConnectionString($connection)
        $builder['SSL Mode']=$PostgreSqlSslMode;$connection=$builder.get_ConnectionString()
    }
    $password=[Environment]::GetEnvironmentVariable('CP6_COMPAT_ADMIN_PASSWORD','Process')
    $passwordSource='ProcessEnvironment'
    if([string]::IsNullOrWhiteSpace($password)){$password=Get-RunnerField (Get-RunnerField $local 'DatabaseCompatibility') 'AdminPassword';$passwordSource='ExplicitLocalConfiguration'}
    # This is the existing production initializer's bootstrap value, used only on the new owned test database.
    if([string]::IsNullOrWhiteSpace($password)){$password='123456';$passwordSource='ExistingInitializerBootstrap'}
    return [pscustomobject]@{Connection=$connection;ConnectionSource=$connectionSource;AdminPassword=$password;AdminPasswordSource=$passwordSource}
}
function Get-RunnerSourceBinding([string[]]$Projects){
    $head=(& git -C $worktree rev-parse HEAD).Trim()
    Assert-Runner ($LASTEXITCODE -eq 0 -and $head -cmatch '^[a-f0-9]{40}$') 'GIT_HEAD_INVALID'
    $status=@(& git -C $worktree status --porcelain=v1 --untracked-files=all)
    Assert-Runner ($LASTEXITCODE -eq 0) 'GIT_STATUS_FAILED'
    $inputs=@(Get-Cp6CompatibilitySourceInputs -Worktree $worktree -Projects $Projects)
    $extra=@(Get-ChildItem -LiteralPath $moduleDirectory -File | Where-Object {$_.Extension -cin @('.ps1','.psm1')} | ForEach-Object FullName)
    $extra+=@($runnerPath,(Join-Path $worktree 'eng/database-compatibility/required-cases.json'))
    foreach($file in $extra){
        $inputs+=@([pscustomobject]@{Path=[IO.Path]::GetRelativePath($worktree,$file).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash})
    }
    $inputs=@($inputs | Sort-Object Path -Unique)
    return [pscustomobject]@{SourceSha=$head;WorkingTreeDirty=($status.Count -gt 0);GitStatusSha256=(Get-RunnerHash $status);SourceState=$(if($status.Count){'Dirty working tree; exact input files are bound separately'}else{'Clean committed source'});SourceInputs=$inputs;SourceInputsSha256=(Get-RunnerHash $inputs)}
}
function New-RunnerPlan {
    $all=@(Get-Cp6CompatibilityEntry -Worktree $worktree -Provider $Provider)
    $matrix=@($all | Where-Object {$_.Role -cnotin @('application','restore')} | Sort-Object Role,Order,Id)
    $applicationIds=@('application-seed-prepare','application-histories-first','application-histories-repeat','application-seed-verify',
        'application-notification-enqueue','application-notification-queued','application-notification-dispatched','application-signalr-user-delivery',
        'application-restore-pending-enqueue','application-restore-pending-queued','application-state-capture','application-seed-capture',
        'restore-state-verify','restore-catalog','restore-histories','restore-notification-dispatched','restore-notification-replay')
    $declared=@($all | Where-Object {$_.Role -cin @('application','restore')} | ForEach-Object Id)
    Assert-Runner (@(Compare-Object $applicationIds $declared -CaseSensitive).Count -eq 0) 'APPLICATION_ENTRY_PLAN_INCOMPLETE'
    $selected=@();if($Phase -cne 'Application'){$selected+=@($matrix)}
    if($Phase -cne 'Matrix'){foreach($id in $applicationIds){$selected+=@(Get-Cp6CompatibilityEntry -Worktree $worktree -Id $id -Provider $Provider)}}
    $projects=@($selected | ForEach-Object Project | Sort-Object -Unique)
    if($Phase -cne 'Matrix'){$projects=@($projects+'CP6.WebApi/CP6.WebApi.csproj' | Sort-Object -Unique)}
    $flow=@('database-init-first','application-seed-prepare','application-histories-first','database-init-repeat','application-histories-repeat','application-seed-verify',
        'api-first-start-and-health','admin-login-and-http-authorization','api-first-stop','application-notification-enqueue','application-notification-queued',
        'api-worker-restart-and-health','application-notification-dispatched','api-worker-stop','api-third-start-and-health','admin-third-login',
        'application-signalr-user-delivery','asset-cursor-near-backup','api-before-backup-stop','application-restore-pending-enqueue','application-restore-pending-queued',
        'application-state-capture','application-seed-capture','native-backup','new-independent-restore-database','native-restore',
        'restore-state-verify','restore-catalog','restore-histories','restored-api-start-and-health','restore-notification-dispatched','restore-notification-replay',
        'restored-admin-login','original-cursor-permissions-and-notifications','restored-api-stop')
    return [pscustomobject]@{Task='DB-COMPAT-01-WP6';Provider=$Provider;Phase=$Phase;RunDirectory=[IO.Path]::GetFullPath($RunDirectory);Status='Planned';NativeExecution=$false;
        Configuration=$Configuration;SkipBuildRequested=[bool]$SkipBuild;ArtifactDirectory=$ArtifactDirectory;Projects=$projects;
        RequiredEntryIds=@($selected | ForEach-Object Id);MatrixEntries=$(if($Phase -ceq 'Application'){@()}else{@($matrix | Select-Object Id,Role,DatabaseLifecycle,ExpectedCases,ProviderEligibility)});
        ApplicationFlow=$(if($Phase -ceq 'Matrix'){@()}else{$flow});Sql136BaselineRequired=($Phase -cne 'Application' -and $Provider -ceq 'SqlServer');
        FreshPerEntryIds=@($selected | Where-Object DatabaseLifecycle -CEQ 'FreshPerEntry' | ForEach-Object Id);
        Cleanup='Only after every selected gate and unchanged-input check passes; normal DROP of the explicit owned receipt list, followed by absence checks.';
        FullAcceptance=$false;Scope='Offline plan only. Partial execution phases never claim Full or cross-provider acceptance.'}
}
function New-RunnerDatabase([string]$Role){
    $receipt=New-Cp6OwnedDatabase -Context $script:runnerContext -Role $Role
    $script:runnerReceipts.Add($receipt)
    return $receipt
}
function Invoke-RunnerEntry([string]$Id,$Receipt,[hashtable]$Values=@{},[hashtable]$ExtraEnvironment=@{}){
    $script:runnerStage=$Id
    Assert-Runner (!$script:runnerExecuted.Contains($Id)) 'ENTRY_ALREADY_EXECUTED'
    $entry=Get-Cp6CompatibilityEntry -Worktree $worktree -Id $Id -Provider $Provider
    $needed=@{};foreach($token in $entry.CaseCommand){if($token -cmatch '^\{([A-Za-z]+)\}$' -and $Matches[1] -cnotin @('Provider','PublicOutputDirectory','PrivateDiagnosticDirectory')){
        $key=$Matches[1];Assert-Runner ($Values.ContainsKey($key)) 'ENTRY_PLACEHOLDER_MISSING';$needed[$key]=$Values[$key]
    }}
    $arguments=@{Entry=$entry;Provider=$Provider;Context=$script:runnerContext;Receipt=$Receipt;Worktree=$worktree;OutputDirectory=(Join-Path $script:runnerContext.RunDirectory ('entries/'+$Id));PlaceholderValues=$needed;Configuration=$Configuration;SourceSha=$script:runnerSource.SourceSha}
    if($ExtraEnvironment.Count){$arguments.ExtraEnvironment=$ExtraEnvironment}
    $result=Invoke-Cp6CompatibilityEntry @arguments
    $script:runnerResults.Add($result)
    Assert-Runner $result.Success 'REQUIRED_ENTRY_FAILED'
    $null=$script:runnerExecuted.Add($Id)
    return $result
}
function Start-RunnerApi($Receipt,[string]$Label){
    $script:runnerStage=$Label
    Assert-Runner ($null -eq $script:runnerHost) 'HOST_ALREADY_RUNNING'
    $script:runnerHost=Start-Cp6ApplicationHost -Context $script:runnerContext -Receipt $Receipt -Settings $script:runnerSettings -Label $Label
    $ready=Wait-Cp6ApplicationReady -HostInstance $script:runnerHost
    $null=Write-RunnerProof ($Label+'-ready') $ready
    Assert-Runner $ready.Success 'API_NOT_READY'
    Assert-Runner (!$script:runnerHostIds.Contains([int]$ready.ProcessId)) 'API_PROCESS_WAS_NOT_RESTARTED'
    $null=$script:runnerHostIds.Add([int]$ready.ProcessId)
}
function Stop-RunnerApi([string]$Label){
    if($null -ne $script:runnerSession){Close-Cp6ApiSession -Session $script:runnerSession;$script:runnerSession=$null}
    if($null -ne $script:runnerHost){
        $state=Get-Cp6CompatibilityProcessState -Handle $script:runnerHost.Handle
        $stopped=Complete-Cp6CompatibilityProcess -Handle $script:runnerHost.Handle -TerminateOwnedProcess:(!$state.HasExited)
        $script:runnerHost=$null
        $null=Write-RunnerProof $Label $stopped
        Assert-Runner (!$state.HasExited) 'API_EXITED_BEFORE_REQUESTED_STOP'
    }
}

Assert-Runner ([IO.Path]::IsPathFullyQualified($RunDirectory)) 'ABSOLUTE_RUN_DIRECTORY_REQUIRED'
$run=[IO.Path]::GetFullPath($RunDirectory)
Assert-Runner (!(Test-Path -LiteralPath $run)) 'NEW_RUN_DIRECTORY_REQUIRED'
Assert-Runner (!$run.StartsWith($worktree.TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'RUN_DIRECTORY_MUST_BE_OUTSIDE_WORKTREE'
Assert-Runner ($Provider -ceq 'PostgreSql' -or (!$PostgreSqlBinDirectory -and !$PostgreSqlSslMode)) 'POSTGRES_OPTIONS_WITH_SQLSERVER'
$plan=New-RunnerPlan
if($PlanOnly){return $plan}
Assert-Runner (!$SkipBuild -or $Phase -ceq 'Matrix' -or ![string]::IsNullOrWhiteSpace($ArtifactDirectory)) 'SKIP_BUILD_REQUIRES_API_ARTIFACT'
$privateConfiguration=Read-RunnerConfiguration
$script:runnerSource=Get-RunnerSourceBinding $plan.Projects
$script:runnerContext=$null;$script:runnerSettings=$null;$script:runnerHost=$null;$script:runnerSession=$null;$script:runnerStage='initialize-context'
$script:runnerReceipts=[Collections.Generic.List[object]]::new();$script:runnerResults=[Collections.Generic.List[object]]::new()
$script:runnerExecuted=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$script:runnerHostIds=[Collections.Generic.HashSet[int]]::new()
$summary=[ordered]@{Task='DB-COMPAT-01-WP6';Provider=$Provider;Phase=$Phase;Status='Running';Success=$false;FullAcceptance=$false;Scope='Selected provider only; local verification is not production or cross-provider acceptance.';
    StartedUtc=[DateTimeOffset]::UtcNow.ToString('o');FinishedUtc=$null;Source=$script:runnerSource;SkipBuildRequested=[bool]$SkipBuild;BuildExecuted=$false;PublishExecuted=$false;
    ArtifactSource=$(if($ArtifactDirectory){'Externally supplied artifact; bound by actual file hashes, no new publish claimed'}else{'Current run publish'});
    ConnectionSource=$privateConfiguration.ConnectionSource;AdminPasswordSource=$privateConfiguration.AdminPasswordSource;PostgreSqlSslModeOverride=$PostgreSqlSslMode;
    RequiredEntryIds=$plan.RequiredEntryIds;ExecutedEntryIds=@();Results=@();ReceiptPaths=@();CleanupStatus='NotStarted';FailureCode=$null;FailureStage=$null;ApplicationHostProcessIds=@();ArtifactManifestSha256=$null}
try {
    # Directory creation is exclusive. Never silently resume a prior failed run.
    $null=New-Item -ItemType Directory -Path $run
    $script:runnerContext=New-Cp6LifecycleContext -Provider $Provider -RunDirectory $run -ConnectionString $privateConfiguration.Connection -PostgreSqlBinDirectory $PostgreSqlBinDirectory
    $null=Write-RunnerProof 'plan' $plan
    $null=Write-RunnerProof 'source-inputs' $script:runnerSource
    if(!$SkipBuild){
        $script:runnerStage='build'
        $build=Invoke-Cp6CompatibilityBuild -Worktree $worktree -OutputDirectory (Join-Path $run 'build') -Projects $plan.Projects -Configuration $Configuration -SourceSha $script:runnerSource.SourceSha
        Assert-Runner $build.Success 'BUILD_FAILED';$summary.BuildExecuted=$true
    }
    if($Phase -cne 'Matrix'){
        if(!$ArtifactDirectory){
            $script:runnerStage='publish-api';$ArtifactDirectory=Join-Path $script:runnerContext.PrivateDirectory 'published-api'
            Assert-Runner (!(Test-Path -LiteralPath $ArtifactDirectory)) 'PUBLISH_DIRECTORY_EXISTS'
            $publish=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @('publish',(Join-Path $worktree 'CP6.WebApi/CP6.WebApi.csproj'),'--no-build','--no-restore','--configuration',$Configuration,'--output',$ArtifactDirectory) -WorkingDirectory $worktree -LogDirectory (Join-Path $script:runnerContext.PrivateDirectory 'publish-process') -ChildEnvironment @{DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_NOLOGO='1'} -TimeoutSeconds 600
            $null=Write-RunnerProof 'publish-api' $publish
            Assert-Runner ($publish.ExitCode -eq 0 -and !$publish.TimedOut -and !$publish.OwnedProcessTerminated) 'PUBLISH_FAILED';$summary.PublishExecuted=$true
        }
        $script:runnerSettings=New-Cp6ApplicationSettings -Context $script:runnerContext -ArtifactDirectory $ArtifactDirectory -SourceSha $script:runnerSource.SourceSha
        $summary.ArtifactManifestSha256=$script:runnerSettings.ArtifactManifestSha256
    }
    if($Phase -cne 'Application'){
        $shared=@{}
        foreach($item in $plan.MatrixEntries){
            if($item.DatabaseLifecycle -ceq 'FreshPerEntry'){$receipt=New-RunnerDatabase $item.Role}
            else{if(!$shared.ContainsKey($item.Role)){$shared[$item.Role]=New-RunnerDatabase $item.Role};$receipt=$shared[$item.Role]}
            if($item.Id -ceq 'sql-populated-upgrade'){
                $script:runnerStage='sql136-baseline'
                $socket=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
                try{$socket.Start();$baselinePort=$socket.LocalEndpoint.Port}finally{$socket.Stop()}
                $environment=@{JWT__Secret=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64));JWT__Issuer='CP6.WP6.Baseline';JWT__Audience='CP6.WP6.Local';Security__Cookie__Secure='false';Security__Csrf__Enabled='true';ErpIntegration__Enabled='false';SpaceObservability__LegacyIntegrationEventTimeZoneId='UTC'}
                $baseline=Invoke-Cp6Sql136Baseline -SourceRepository $worktree -RunDirectory $run -Context $script:runnerContext -Receipt $receipt -ChildEnvironment $environment -ReportPath (Join-Path $run 'sql136-baseline.json') -InitializerPort $baselinePort
                Assert-Runner ($baseline.Status -ceq 'Passed') 'SQL136_BASELINE_FAILED'
            }
            $null=Invoke-RunnerEntry $item.Id $receipt
        }
    }
    if($Phase -cne 'Matrix'){
        $application=New-RunnerDatabase 'application'
        $seedState=Join-Path $script:runnerContext.PrivateDirectory 'application-seed-first.private.json'
        $state=Join-Path $script:runnerContext.PrivateDirectory 'application-before-backup.private.json'
        $capturedSeed=Join-Path $script:runnerContext.PrivateDirectory 'seed-before-backup.private.json'
        $script:runnerStage='database-init-first'
        $initialized=Invoke-Cp6ApplicationInitialization -Context $script:runnerContext -Receipt $application -Settings $script:runnerSettings -Label 'database-init-first'
        Assert-Runner $initialized.Success 'FIRST_INITIALIZATION_FAILED'
        $null=Invoke-RunnerEntry 'application-seed-prepare' $application @{SeedStatePath=$seedState}
        $null=Invoke-RunnerEntry 'application-histories-first' $application
        $script:runnerStage='database-init-repeat'
        $initialized=Invoke-Cp6ApplicationInitialization -Context $script:runnerContext -Receipt $application -Settings $script:runnerSettings -Label 'database-init-repeat'
        Assert-Runner $initialized.Success 'REPEAT_INITIALIZATION_FAILED'
        $null=Invoke-RunnerEntry 'application-histories-repeat' $application
        $null=Invoke-RunnerEntry 'application-seed-verify' $application @{SeedStatePath=$seedState}
        Start-RunnerApi $application 'api-first'
        $script:runnerSession=New-Cp6ApiSession -BaseUri $script:runnerHost.BaseUri -AdminPassword $privateConfiguration.AdminPassword
        $null=Write-RunnerProof 'admin-first-login' $script:runnerSession.Public
        $authorization=Test-Cp6ApiAuthorization -Session $script:runnerSession
        $null=Write-RunnerProof 'http-authorization' $authorization.Public;Assert-Runner $authorization.Public.Success 'HTTP_AUTHORIZATION_FAILED'
        Stop-RunnerApi 'api-first-stopped'
        $notification=@{TenantId=$authorization.Private.AdminTenantId;UserId=$authorization.Private.AdminUserId;EventKey=('wp6-first-'+[guid]::NewGuid().ToString('N'))}
        $null=Invoke-RunnerEntry 'application-notification-enqueue' $application $notification
        $null=Invoke-RunnerEntry 'application-notification-queued' $application $notification
        Start-RunnerApi $application 'api-worker-restarted'
        $dispatch=Invoke-RunnerEntry 'application-notification-dispatched' $application $notification
        $notificationReport=Get-Content -LiteralPath $dispatch.ReportPath -Raw | ConvertFrom-Json
        Stop-RunnerApi 'api-worker-restarted-stopped'
        Start-RunnerApi $application 'api-third-restarted'
        $script:runnerSession=New-Cp6ApiSession -BaseUri $script:runnerHost.BaseUri -AdminPassword $privateConfiguration.AdminPassword
        $null=Write-RunnerProof 'admin-third-login' $script:runnerSession.Public
        $signal=@{TenantId=$authorization.Private.AdminTenantId;UserId=$authorization.Private.AdminUserId;BaseUri=$script:runnerHost.BaseUri;SignalREventKey=('wp6-signalr-'+[guid]::NewGuid().ToString('N'))}
        Assert-Runner (Assert-Cp6CompatibilityListener -Handle $script:runnerHost.Handle -Port $script:runnerHost.Port) 'SIGNALR_PID_LISTENER_MISMATCH'
        $null=Invoke-RunnerEntry 'application-signalr-user-delivery' $application $signal @{CP6_COMPAT_ADMIN_PASSWORD=$privateConfiguration.AdminPassword;CP6_COMPAT_ORDINARY_USERNAME=$authorization.Private.UserName;CP6_COMPAT_ORDINARY_PASSWORD=$authorization.Private.Password}
        $asset=New-Cp6AssetCursorFixture -Session $script:runnerSession
        $null=Write-RunnerProof 'http-asset-cursor' $asset.Public;Assert-Runner $asset.Public.Success 'HTTP_ASSET_CURSOR_FAILED'
        Stop-RunnerApi 'api-before-backup-stopped'
        $pending=@{TenantId=$authorization.Private.AdminTenantId;UserId=$authorization.Private.AdminUserId;EventKey=('wp6-restore-'+[guid]::NewGuid().ToString('N'))}
        $null=Invoke-RunnerEntry 'application-restore-pending-enqueue' $application $pending
        $null=Invoke-RunnerEntry 'application-restore-pending-queued' $application $pending
        $null=Invoke-RunnerEntry 'application-state-capture' $application @{ApplicationStatePath=$state}
        $null=Invoke-RunnerEntry 'application-seed-capture' $application @{CapturedSeedStatePath=$capturedSeed}
        $script:runnerStage='native-backup'
        $backup=Backup-Cp6OwnedDatabase -Context $script:runnerContext -Receipt $application
        Assert-Runner ($backup.Status -ceq 'BackedUp' -and $backup.NativeExitCode -eq 0) 'BACKUP_FAILED'
        $null=Write-RunnerProof 'native-backup' $backup
        $restore=New-RunnerDatabase 'restore';$script:runnerStage='native-restore'
        $restored=Restore-Cp6OwnedDatabase -Context $script:runnerContext -SourceReceipt $application -TargetReceipt $restore -Backup $backup
        $null=Write-RunnerProof 'native-restore' $restored;Assert-Runner ($restored.Status -ceq 'Restored' -and $restored.NativeExitCode -eq 0) 'RESTORE_FAILED'
        $null=Invoke-RunnerEntry 'restore-state-verify' $restore @{ApplicationStatePath=$state}
        $null=Invoke-RunnerEntry 'restore-catalog' $restore
        $null=Invoke-RunnerEntry 'restore-histories' $restore
        Start-RunnerApi $restore 'api-restored'
        $null=Invoke-RunnerEntry 'restore-notification-dispatched' $restore $pending
        $null=Invoke-RunnerEntry 'restore-notification-replay' $restore $pending
        $script:runnerSession=New-Cp6ApiSession -BaseUri $script:runnerHost.BaseUri -AdminPassword $privateConfiguration.AdminPassword
        $null=Write-RunnerProof 'admin-restored-login' $script:runnerSession.Public
        $http=Test-Cp6RestoredApiFixture -Session $script:runnerSession -AuthorizationFixture $authorization -AssetFixture $asset -NotificationIdSha256 $notificationReport.Hashes.NotificationIdSha256
        $null=Write-RunnerProof 'http-after-restore' $http.Public
        Assert-Runner ($http.Public.Success -and $http.Public.NotificationIdentityChecked) 'HTTP_RESTORE_FAILED'
        Stop-RunnerApi 'api-restored-stopped'
        # Rebind all published bytes after the final launch; the same artifact served every host.
        $sameArtifact=New-Cp6ApplicationSettings -Context $script:runnerContext -ArtifactDirectory $ArtifactDirectory -SourceSha $script:runnerSource.SourceSha
        Assert-Runner ($sameArtifact.ArtifactManifestSha256 -ceq $summary.ArtifactManifestSha256) 'APPLICATION_ARTIFACT_CHANGED'
    }
    $script:runnerStage='final-input-and-entry-check'
    Assert-Runner ($script:runnerExecuted.SetEquals([string[]]$plan.RequiredEntryIds)) 'SELECTED_ENTRY_SET_INCOMPLETE'
    $finalSource=Get-RunnerSourceBinding $plan.Projects
    $null=Write-RunnerProof 'source-inputs-final' $finalSource
    Assert-Runner ($finalSource.SourceSha -ceq $script:runnerSource.SourceSha -and $finalSource.SourceInputsSha256 -ceq $script:runnerSource.SourceInputsSha256) 'SOURCE_INPUTS_CHANGED_DURING_RUN'
    $script:runnerStage='owned-database-cleanup';$summary.CleanupStatus='InProgress'
    $cleaned=@(Remove-Cp6OwnedDatabases -Context $script:runnerContext -Receipts $script:runnerReceipts.ToArray())
    $null=Write-RunnerProof 'owned-database-cleanup' $cleaned
    Assert-Runner ($cleaned.Count -eq $script:runnerReceipts.Count -and @($cleaned | Where-Object Status -CNE 'Absent').Count -eq 0) 'CLEANUP_INCOMPLETE'
    $summary.CleanupStatus='AbsentVerified';$summary.Status='Passed';$summary.Success=$true;$summary.FullAcceptance=($Phase -ceq 'Full')
}
catch {
    $summary.Status='Failed';$summary.Success=$false;$summary.FailureStage=$script:runnerStage
    $summary.FailureCode=if($_.Exception.Message -cmatch '^CP6_[A-Z0-9_]+$'){$_.Exception.Message}else{'CP6_COMPAT_RUN_FAILED'}
    if($null -ne $script:runnerContext){[IO.File]::WriteAllText((Join-Path $script:runnerContext.PrivateDirectory 'runner-exception.private.txt'),$_.ToString())}
}
finally {
    if($null -ne $script:runnerContext){
        if($null -ne $script:runnerHost -or $null -ne $script:runnerSession){
            try{Stop-RunnerApi 'api-stopped-after-failure'}catch{$summary.Status='Failed';$summary.Success=$false;$summary.FailureCode='CP6_COMPAT_OWNED_API_STOP_FAILED'}
        }
        [IO.File]::WriteAllText((Join-Path $script:runnerContext.PrivateDirectory 'http-diagnostics.private.json'),((Get-Cp6HttpPrivateDiagnostics)|ConvertTo-Json -Depth 40))
        $summary.FinishedUtc=[DateTimeOffset]::UtcNow.ToString('o');$summary.Results=$script:runnerResults.ToArray();$summary.ExecutedEntryIds=@($script:runnerExecuted | Sort-Object)
        $summary.ReceiptPaths=@($script:runnerReceipts | ForEach-Object ReceiptPath);$summary.ApplicationHostProcessIds=@($script:runnerHostIds)
        if(!$summary.Success){$summary.FullAcceptance=$false}
        $summaryPath=Write-RunnerProof 'summary' $summary
    }
}
if(!$summary.Success){throw [InvalidOperationException]::new($summary.FailureCode)}
[pscustomobject]@{Status=$summary.Status;Success=$summary.Success;Provider=$Provider;Phase=$Phase;FullAcceptance=$summary.FullAcceptance;RunDirectory=$run;SummaryPath=$summaryPath;SummarySha256=(Get-FileHash -LiteralPath $summaryPath -Algorithm SHA256).Hash;CleanupStatus=$summary.CleanupStatus;NativeExecution=$true}
