# Rebuild the actual historical Core136 application tree, then run its real initializer.
# All executable operations are opt-in through Invoke-Cp6Sql136Baseline. Planning is offline.
Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityProcess.psm1')
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityLifecycle.psm1')
$script:Cp6Sql136SourceCommit = '993e844df180f9a110eb4a3bea2904a0ba70ba6c'
$script:Cp6Sql136LastMigration = '20260916130000_RetireLegacyCrmModel'

function Assert-Cp6Baseline([bool]$Condition, [string]$Code) {
    if (!$Condition) { throw [InvalidOperationException]::new('CP6_COMPAT_SQL136_'+$Code) }
}

function Resolve-Cp6BaselinePath([string]$Path) {
    Assert-Cp6Baseline (![string]::IsNullOrWhiteSpace($Path) -and [IO.Path]::IsPathFullyQualified($Path)) 'ABSOLUTE_PATH_REQUIRED'
    return [IO.Path]::GetFullPath($Path).TrimEnd([IO.Path]::DirectorySeparatorChar)
}

function Test-Cp6BaselineContained([string]$Parent, [string]$Child) {
    return $Child.StartsWith($Parent.TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar,
        [StringComparison]::OrdinalIgnoreCase)
}

function Assert-Cp6BaselineNoReparse([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            Assert-Cp6Baseline (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'REPARSE_PATH_REFUSED'
        }
        $parent = [IO.Path]::GetDirectoryName($cursor)
        if ($parent -ceq $cursor) { break }
        $cursor = $parent
    }
}

function New-Cp6Sql136BaselinePlan {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceRepository,
        [Parameter(Mandatory)][string]$RunDirectory,
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][ValidateRange(1024,65535)][int]$InitializerPort,
        [ValidatePattern('\Asql136-baseline(?:-[a-z0-9]{1,32})?\z')][string]$AttemptName = 'sql136-baseline',
        [string]$GitExecutable = 'git',
        [string]$DotNetExecutable = 'dotnet'
    )
    $repo = Resolve-Cp6BaselinePath $SourceRepository
    $run = Resolve-Cp6BaselinePath $RunDirectory
    $report = Resolve-Cp6BaselinePath $ReportPath
    Assert-Cp6Baseline (Test-Path -LiteralPath $repo -PathType Container) 'SOURCE_DIRECTORY_MISSING'
    Assert-Cp6Baseline (Test-Path -LiteralPath (Join-Path $repo '.git')) 'GIT_SOURCE_REQUIRED'
    Assert-Cp6Baseline (Test-Cp6BaselineContained $run $report) 'REPORT_OUTSIDE_RUN'
    Assert-Cp6Baseline (![string]::IsNullOrWhiteSpace($GitExecutable) -and ![string]::IsNullOrWhiteSpace($DotNetExecutable)) 'EXECUTABLE_REQUIRED'
    $private = Join-Path $run 'private'
    $work = Join-Path $private $AttemptName
    Assert-Cp6Baseline (!(Test-Cp6BaselineContained $private $report)) 'PUBLIC_REPORT_REQUIRED'
    Assert-Cp6Baseline (!(Test-Cp6BaselineContained $work $repo)) 'SOURCE_INSIDE_NEW_WORK_DIRECTORY'
    foreach ($path in @($repo,$run,$report,$work)) { Assert-Cp6BaselineNoReparse $path }
    $archive = Join-Path $work 'source.zip'
    $source = Join-Path $work 'source'
    $publish = Join-Path $work 'publish'
    $project = Join-Path $source 'CP6.WebApi/CP6.WebApi.csproj'
    $manifest = $report+'.publish-manifest.json'
    foreach ($path in @($work,$report,$manifest)) { Assert-Cp6Baseline (!(Test-Path -LiteralPath $path)) 'OUTPUT_ALREADY_EXISTS' }
    $steps = @(
        [pscustomobject]@{ Name='ArchivePinnedSource'; Executable=$GitExecutable; Arguments=@('-C',$repo,'archive','--format=zip','--output',$archive,$script:Cp6Sql136SourceCommit); WorkingDirectory=$repo; LogDirectory=(Join-Path $work 'logs/archive') },
        [pscustomobject]@{ Name='ObserveSdk'; Executable=$DotNetExecutable; Arguments=@('--version'); WorkingDirectory=$source; LogDirectory=(Join-Path $work 'logs/sdk') },
        [pscustomobject]@{ Name='LockedRestore'; Executable=$DotNetExecutable; Arguments=@('restore',$project,'--locked-mode'); WorkingDirectory=$source; LogDirectory=(Join-Path $work 'logs/restore') },
        [pscustomobject]@{ Name='PublishHistoricalApplication'; Executable=$DotNetExecutable; Arguments=@('publish',$project,'--no-restore','--configuration','Release','--output',$publish); WorkingDirectory=$source; LogDirectory=(Join-Path $work 'logs/publish') },
        [pscustomobject]@{ Name='RunHistoricalDatabaseInit'; Executable=$DotNetExecutable; Arguments=@((Join-Path $publish 'CP6.WebApi.dll')); WorkingDirectory=$publish; LogDirectory=(Join-Path $work 'logs/initializer') }
    )
    return [pscustomobject]@{
        SourceCommit=$script:Cp6Sql136SourceCommit; SourceRepository=$repo; RunDirectory=$run
        PrivateWorkDirectory=$work; ArchivePath=$archive; SourceDirectory=$source; PublishDirectory=$publish
        ApplicationDll=(Join-Path $publish 'CP6.WebApi.dll'); ReportPath=$report; PublishManifestPath=$manifest
        InitializerPort=$InitializerPort; Steps=$steps
        LockPaths=@('CP6.Core/packages.lock.json','CP6.Entity/packages.lock.json','CP6.WebApi/packages.lock.json',
            'CP6.Space.Infrastructure/packages.lock.json','CP6.Tests/packages.lock.json',
            'CP6.Space.IntegrationTests/packages.lock.json','CP6.Oidc.IntegrationTests/packages.lock.json')
        HistoricalReference='docs/audits/database-compatibility/wp2-native/wp2-init-sql-final-upgrade-legacy136-first-seed.json'
    }
}

function Get-Cp6BaselineJsonHash($Value) {
    $text = ConvertTo-Json -InputObject $Value -Depth 50 -Compress
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text)))
}

function Write-Cp6BaselineExclusive([string]$Path, $Value) {
    $stream = [IO.FileStream]::new($Path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $Value -Depth 50))
        $stream.Write($bytes,0,$bytes.Length)
        $stream.Flush($true)
    }
    finally { $stream.Dispose() }
}

function Get-Cp6BaselineFileManifest([string]$Directory) {
    $root = Resolve-Cp6BaselinePath $Directory
    $entries = @(Get-ChildItem -LiteralPath $root -File -Recurse | Sort-Object FullName | ForEach-Object {
        Assert-Cp6Baseline (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'ARTIFACT_REPARSE_REFUSED'
        [pscustomobject]@{
            Path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/')
            Bytes=$_.Length; Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
        }
    })
    Assert-Cp6Baseline ($entries.Count -gt 0) 'PUBLISHED_ARTIFACT_EMPTY'
    return ,$entries
}

function Invoke-Cp6BaselineStep($Step, [hashtable]$Environment, [int]$TimeoutSeconds, $Report) {
    $parent = [IO.Path]::GetDirectoryName($Step.LogDirectory)
    $null = [IO.Directory]::CreateDirectory($parent)
    $result = Invoke-Cp6CompatibilityProcess -Executable $Step.Executable -ArgumentList $Step.Arguments `
        -WorkingDirectory $Step.WorkingDirectory -LogDirectory $Step.LogDirectory `
        -ChildEnvironment $Environment -TimeoutSeconds $TimeoutSeconds
    $Report.Processes += @([pscustomobject]@{ Name=$Step.Name; Actual=$result })
    Assert-Cp6Baseline ($result.ExitCode -eq 0 -and !$result.TimedOut -and !$result.OwnedProcessTerminated) ($Step.Name.ToUpperInvariant()+'_FAILED')
    return $result
}

function Invoke-Cp6BaselineInitializer($Plan, [hashtable]$Environment, [int]$TimeoutSeconds, $Report) {
    Assert-Cp6Baseline $IsWindows 'LISTENER_OBSERVATION_REQUIRES_WINDOWS'
    Assert-Cp6Baseline ($null -ne (Get-Command Get-NetTCPConnection -ErrorAction SilentlyContinue)) 'LISTENER_OBSERVER_UNAVAILABLE'
    $step = $Plan.Steps[4]
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($step.LogDirectory))
    $handle = Start-Cp6CompatibilityProcess -Executable $step.Executable -ArgumentList $step.Arguments `
        -WorkingDirectory $step.WorkingDirectory -LogDirectory $step.LogDirectory -ChildEnvironment $Environment
    $deadline = [datetime]::UtcNow.AddSeconds($TimeoutSeconds)
    $observed = $false
    $completed = $false
    try {
        while (!(Get-Cp6CompatibilityProcessState $handle).HasExited -and [datetime]::UtcNow -lt $deadline) {
            $listeners = @(Get-NetTCPConnection -State Listen -ErrorAction Stop | Where-Object OwningProcess -EQ $handle.ProcessId)
            if ($listeners.Count -gt 0) { $observed=$true; break }
            Start-Sleep -Milliseconds 100
        }
        $state = Get-Cp6CompatibilityProcessState $handle
        $timedOut = !$state.HasExited -and !$observed
        $result = Complete-Cp6CompatibilityProcess -Handle $handle -TerminateOwnedProcess:(!$state.HasExited)
        $completed = $true
        $result | Add-Member -NotePropertyName TimedOut -NotePropertyValue $timedOut
        $Report.Processes += @([pscustomobject]@{ Name=$step.Name; Actual=$result })
        $stdout = [IO.File]::ReadAllText((Join-Path $step.LogDirectory 'stdout.log'))
        $completion = $stdout.Contains('Database initialization completed; the one-shot process will exit.',[StringComparison]::Ordinal)
        $Report.Initializer = [pscustomobject]@{
            ProcessId=$result.ProcessId; ExitCode=$result.ExitCode; CompletionObserved=$completion
            ListenerObserved=$observed; ListenerObservation='Windows owned PID poll every 100 ms while process runs'
            BoundLoopbackPort=$Plan.InitializerPort; TimedOut=$timedOut
            OwnedProcessTerminated=$result.OwnedProcessTerminated
        }
        Assert-Cp6Baseline (!$observed -and !$timedOut -and !$result.OwnedProcessTerminated -and $result.ExitCode -eq 0 -and $completion) 'INITIALIZER_FAILED'
    }
    finally {
        if (!$completed) { $null=Complete-Cp6CompatibilityProcess -Handle $handle -TerminateOwnedProcess }
    }
}

function Invoke-Cp6Sql136Baseline {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$SourceRepository,
        [Parameter(Mandatory)][string]$RunDirectory,
        [Parameter(Mandatory)]$Context,
        [Parameter(Mandatory)]$Receipt,
        [Parameter(Mandatory)][hashtable]$ChildEnvironment,
        [Parameter(Mandatory)][string]$ReportPath,
        [Parameter(Mandatory)][ValidateRange(1024,65535)][int]$InitializerPort,
        [ValidatePattern('\Asql136-baseline(?:-[a-z0-9]{1,32})?\z')][string]$AttemptName='sql136-baseline',
        [string]$GitExecutable='git', [string]$DotNetExecutable='dotnet',
        [ValidateRange(10,3600)][int]$StepTimeoutSeconds=600
    )
    $plan = New-Cp6Sql136BaselinePlan -SourceRepository $SourceRepository -RunDirectory $RunDirectory `
        -ReportPath $ReportPath -InitializerPort $InitializerPort -AttemptName $AttemptName `
        -GitExecutable $GitExecutable -DotNetExecutable $DotNetExecutable
    Assert-Cp6Baseline ($Context.Provider -ceq 'SqlServer' -and $Receipt.Provider -ceq 'SqlServer' -and
        $Receipt.Role -ceq 'sqlupgrade' -and $Receipt.Task -ceq 'DB-COMPAT-01-WP6' -and
        $Context.RunDirectory -ceq $plan.RunDirectory) 'OWNED_SQLUPGRADE_RECEIPT_REQUIRED'
    $report = [pscustomobject]@{
        SchemaVersion=1; Task='DB-COMPAT-01-WP6'; Status='Preparing'; Command='PreparePinnedSql136Baseline'
        SourceCommit=$plan.SourceCommit; SourceRepository=$plan.SourceRepository; DatabaseName=$Receipt.DatabaseName
        Role='SqlUpgrade'; Provider='SqlServer'; StartedUtc=[datetime]::UtcNow.ToString('o'); FinishedUtc=$null
        ArchiveSha256=$null; SdkVersion=$null; LockManifest=@(); SourceMigrationCount=$null; SourceLastMigration=$null
        SourceManifestSha256=$null; PublishManifestSha256=$null; PublishFiles=$null; PublishBytes=$null
        ApplicationSha256=$null; CoreSha256=$null; Processes=@(); Initializer=$null
        EmptyOwnerPreflightSha256=$null; FinalOwnerPreflightSha256=$null; FailureKind=$null
        HistoricalReference=[pscustomobject]@{Path=$plan.HistoricalReference; HistoricalOnly=$true; NotCurrentExecution=$true}
        Scope='New execution of the pinned historical Git tree and locked packages in one empty WP6 SqlUpgrade database. Real historical Core+Space migrations and application seed, no DOWN/no manual SQL seed. Exact live Core136 history/seed assertions and current 136-to-140 forward gate remain separately required. No claim of old compiled-artifact byte equality or full application acceptance.'
    }
    # Refuse every existing output before creating any workspace or starting a child process.
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($plan.ReportPath))
    $reportStream = [IO.FileStream]::new($plan.ReportPath,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::Read)
    try {
        $actual = Test-Cp6OwnedDatabase -Context $Context -Receipt $Receipt -RequireEmpty -RequireNoSessions
        Assert-Cp6Baseline $actual.Exists 'DATABASE_ABSENT'
        $report.EmptyOwnerPreflightSha256 = Get-Cp6BaselineJsonHash $actual
        $connection = Get-Cp6OwnedDatabaseConnection -Context $Context -Receipt $Receipt
        $environment = @{}
        foreach ($key in $ChildEnvironment.Keys) { $environment[$key]=$ChildEnvironment[$key] }
        $environment['CP6_COMPAT_SCOPE']='WP6'
        $environment['CP6_COMPAT_DATABASE_NAME']=$Receipt.DatabaseName
        $environment['CP6_TEST_DATABASE_OWNER']=$Receipt.Owner
        $environment['CP6_TEST_SQLSERVER']=$connection
        $environment['CP6_TEST_POSTGRES']=$null
        $environment['ConnectionStrings__DefaultConnection']=$connection
        $environment['Database__Provider']='SqlServer'
        $environment['ASPNETCORE_ENVIRONMENT']='Development'
        $environment['Startup__Mode']='DatabaseInit'
        $environment['Startup__SkipHostedServices']='true'
        $environment['Startup__SkipDatabaseInitialization']='false'
        $environment['CrmIdentity__Enabled']='false'
        $environment['CrmOidc__Enabled']='false'
        $environment['ASPNETCORE_URLS']='http://127.0.0.1:'+$InitializerPort
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($plan.PrivateWorkDirectory))
        Assert-Cp6Baseline (!(Test-Path -LiteralPath $plan.PrivateWorkDirectory)) 'OUTPUT_ALREADY_EXISTS'
        $null = New-Item -ItemType Directory -Path $plan.PrivateWorkDirectory
        $null = Invoke-Cp6BaselineStep $plan.Steps[0] $environment $StepTimeoutSeconds $report
        Assert-Cp6Baseline (Test-Path -LiteralPath $plan.ArchivePath -PathType Leaf) 'ARCHIVE_MISSING'
        $report.ArchiveSha256=(Get-FileHash -LiteralPath $plan.ArchivePath -Algorithm SHA256).Hash
        Expand-Archive -LiteralPath $plan.ArchivePath -DestinationPath $plan.SourceDirectory -ErrorAction Stop
        Assert-Cp6Baseline (!(Test-Path -LiteralPath (Join-Path $plan.SourceDirectory 'CP6.WebApi/appsettings.Local.json'))) 'LOCAL_CONFIGURATION_IN_ARCHIVE'
        $sourceManifest = Get-Cp6BaselineFileManifest $plan.SourceDirectory
        $report.SourceManifestSha256=Get-Cp6BaselineJsonHash $sourceManifest
        foreach ($relative in $plan.LockPaths) {
            $lock = Join-Path $plan.SourceDirectory $relative
            Assert-Cp6Baseline (Test-Path -LiteralPath $lock -PathType Leaf) 'LOCK_MISSING'
            $report.LockManifest += @([pscustomobject]@{Path=$relative;Bytes=(Get-Item -LiteralPath $lock).Length;Sha256=(Get-FileHash -LiteralPath $lock -Algorithm SHA256).Hash})
        }
        $migrations = @(Get-ChildItem -LiteralPath (Join-Path $plan.SourceDirectory 'CP6.Core/Migrations') -File |
            Where-Object { $_.Name -cmatch '^[0-9]{14}_.+\.cs$' -and $_.Name -cnotmatch '\.Designer\.cs$' } | Sort-Object Name)
        $report.SourceMigrationCount=$migrations.Count
        $report.SourceLastMigration=if($migrations.Count){[IO.Path]::GetFileNameWithoutExtension($migrations[-1].Name)}else{$null}
        Assert-Cp6Baseline ($migrations.Count -eq 136 -and $report.SourceLastMigration -ceq $script:Cp6Sql136LastMigration) 'SOURCE_HISTORY_NOT_136'
        $null=Invoke-Cp6BaselineStep $plan.Steps[1] $environment $StepTimeoutSeconds $report
        $sdk=[IO.File]::ReadAllText((Join-Path $plan.Steps[1].LogDirectory 'stdout.log')).Trim()
        Assert-Cp6Baseline ($sdk -cmatch '\A[0-9]+\.[0-9]+\.[0-9]+(?:[-+][A-Za-z0-9.-]+)?\z') 'SDK_OBSERVATION_INVALID'
        $report.SdkVersion=$sdk
        $null=Invoke-Cp6BaselineStep $plan.Steps[2] $environment $StepTimeoutSeconds $report
        $null=Invoke-Cp6BaselineStep $plan.Steps[3] $environment $StepTimeoutSeconds $report
        foreach($lock in $report.LockManifest){
            Assert-Cp6Baseline ((Get-FileHash -LiteralPath (Join-Path $plan.SourceDirectory $lock.Path) -Algorithm SHA256).Hash -ceq $lock.Sha256) 'LOCK_CHANGED'
        }
        Assert-Cp6Baseline (Test-Path -LiteralPath $plan.ApplicationDll -PathType Leaf) 'APPLICATION_DLL_MISSING'
        $core = Join-Path $plan.PublishDirectory 'CP6.Core.dll'
        Assert-Cp6Baseline (Test-Path -LiteralPath $core -PathType Leaf) 'CORE_DLL_MISSING'
        $published = Get-Cp6BaselineFileManifest $plan.PublishDirectory
        Write-Cp6BaselineExclusive $plan.PublishManifestPath ([pscustomobject]@{SourceCommit=$plan.SourceCommit;ArchiveSha256=$report.ArchiveSha256;SdkVersion=$report.SdkVersion;Files=$published})
        $report.PublishManifestSha256=(Get-FileHash -LiteralPath $plan.PublishManifestPath -Algorithm SHA256).Hash
        $report.PublishFiles=$published.Count
        $report.PublishBytes=($published|Measure-Object Bytes -Sum).Sum
        $report.ApplicationSha256=(Get-FileHash -LiteralPath $plan.ApplicationDll -Algorithm SHA256).Hash
        $report.CoreSha256=(Get-FileHash -LiteralPath $core -Algorithm SHA256).Hash
        $actual=Test-Cp6OwnedDatabase -Context $Context -Receipt $Receipt -RequireEmpty -RequireNoSessions
        Assert-Cp6Baseline $actual.Exists 'DATABASE_ABSENT'
        $null=Invoke-Cp6BaselineInitializer $plan $environment $StepTimeoutSeconds $report
        $final=Test-Cp6OwnedDatabase -Context $Context -Receipt $Receipt -RequireNoSessions
        Assert-Cp6Baseline ($final.Exists -and $final.UserTables -gt 0) 'INITIALIZED_DATABASE_EMPTY'
        $report.FinalOwnerPreflightSha256=Get-Cp6BaselineJsonHash $final
        Assert-Cp6Baseline ((Get-Cp6BaselineJsonHash (Get-Cp6BaselineFileManifest $plan.PublishDirectory)) -ceq (Get-Cp6BaselineJsonHash $published)) 'PUBLISHED_ARTIFACT_CHANGED'
        $report.Status='Passed'
    }
    catch {
        $report.Status='Failed'
        $report.FailureKind=if($_.Exception.Message -cmatch '^CP6_COMPAT_[A-Z0-9_]+$'){$_.Exception.Message}else{'CP6_COMPAT_SQL136_PREPARATION_FAILED'}
    }
    finally {
        $report.FinishedUtc=[datetime]::UtcNow.ToString('o')
        try {
            $bytes=[Text.UTF8Encoding]::new($false).GetBytes((ConvertTo-Json -InputObject $report -Depth 50))
            $reportStream.Write($bytes,0,$bytes.Length)
            $reportStream.Flush($true)
        }
        finally { $reportStream.Dispose() }
    }
    if($report.Status -cne 'Passed'){throw $report.FailureKind}
    return [pscustomobject]@{
        Status='Passed'; SourceCommit=$plan.SourceCommit; SourceDirectory=$plan.SourceDirectory
        ArchivePath=$plan.ArchivePath; PublishDirectory=$plan.PublishDirectory; ApplicationDll=$plan.ApplicationDll
        ReportPath=$plan.ReportPath; ReportSha256=(Get-FileHash -LiteralPath $plan.ReportPath -Algorithm SHA256).Hash
        PublishManifestPath=$plan.PublishManifestPath; PublishManifestSha256=$report.PublishManifestSha256
        SdkVersion=$report.SdkVersion; DatabaseName=$Receipt.DatabaseName
        RequiredNextGate='Current MigrationProbe strict populated Core136-to-140, including live history and seed assertions'
    }
}

Export-ModuleMember -Function New-Cp6Sql136BaselinePlan,Invoke-Cp6Sql136Baseline
