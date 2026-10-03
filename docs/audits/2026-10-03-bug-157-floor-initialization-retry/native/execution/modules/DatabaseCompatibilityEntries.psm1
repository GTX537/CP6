Set-StrictMode -Version Latest
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityResults.psm1')
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityProcess.psm1')
Import-Module (Join-Path $PSScriptRoot 'DatabaseCompatibilityLifecycle.psm1')
$script:projects=@('CP6.Tests/CP6.Tests.csproj','CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj','CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj','eng/crm/erp-integration-tests/CP6.ErpIntegration.SqlTests.csproj','eng/crm/identity-events-fixture/CP6.IdentityEvents.Fixture.csproj','tools/CP6.DatabaseCompatibility.MigrationProbe/CP6.DatabaseCompatibility.MigrationProbe.csproj','tools/CP6.DatabaseCompatibility.RuntimeProbe/CP6.DatabaseCompatibility.RuntimeProbe.csproj','tools/CP6.DatabaseCompatibility.ApplicationProbe/CP6.DatabaseCompatibility.ApplicationProbe.csproj','CP6.WebApi/CP6.WebApi.csproj')
$script:placeholders=@('Provider','PublicOutputDirectory','PrivateDiagnosticDirectory','SeedStatePath','CapturedSeedStatePath','ApplicationStatePath','TenantId','UserId','EventKey','BaseUri','SignalREventKey')
function Assert-Cp6Entry([bool]$Condition,[string]$Code){if(!$Condition){throw $Code}}
function Write-Cp6EntryJson([string]$Path,$Value){[IO.File]::WriteAllText($Path,($Value|ConvertTo-Json -Depth 40),[Text.UTF8Encoding]::new($false))}
function Get-Cp6RuntimeArtifactManifest([string]$Directory){
    $root=[IO.Path]::GetFullPath($Directory)
    Assert-Cp6Entry (Test-Path -LiteralPath $root -PathType Container) 'CP6_COMPAT_RUNTIME_DIRECTORY_MISSING'
    $items=@(Get-ChildItem -LiteralPath $root -Recurse -Force -ErrorAction Stop)
    Assert-Cp6Entry (((Get-Item -LiteralPath $root -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0 -and @($items | Where-Object {($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0}).Count -eq 0) 'CP6_COMPAT_RUNTIME_REPARSE_POINT_FORBIDDEN'
    $files=@($items | Where-Object {!$_.PSIsContainer} | ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($root,$_.FullName).Replace('\','/');Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256 -ErrorAction Stop).Hash}
    } | Sort-Object Path)
    Assert-Cp6Entry ($files.Count -gt 0) 'CP6_COMPAT_RUNTIME_DIRECTORY_EMPTY'
    return [pscustomobject]@{Directory=$root;Files=$files}
}
function Get-Cp6CompatibilityEntry {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Worktree,[string]$Id,[string]$Provider)
    $manifest=Read-Cp6RequiredCaseManifest (Join-Path $Worktree 'eng/database-compatibility/required-cases.json')
    $entries=@($manifest.Entries)
    if($Id){$entries=@($entries|Where-Object Id -CEQ $Id);Assert-Cp6Entry ($entries.Count -eq 1) 'CP6_COMPAT_ENTRY_UNKNOWN'}
    if($Provider){
        Assert-Cp6Entry ($Provider -cin @('SqlServer','PostgreSql')) 'CP6_COMPAT_PROVIDER_INVALID'
        if($Id){Assert-Cp6Entry ($Provider -cin $entries[0].ProviderEligibility) 'CP6_COMPAT_ENTRY_PROVIDER_INELIGIBLE'}
        $entries=@($entries|Where-Object {$Provider -cin $_.ProviderEligibility})
    }
    return $entries
}
function Get-Cp6ProjectLayout([string]$Worktree,[string]$Project,[string]$Configuration){
    Assert-Cp6Entry ($Project -cin $script:projects -and $Configuration -cin @('Debug','Release')) 'CP6_COMPAT_PROJECT_INVALID'
    $path=[IO.Path]::GetFullPath((Join-Path $Worktree $Project))
    Assert-Cp6Entry (Test-Path -LiteralPath $path -PathType Leaf) 'CP6_COMPAT_PROJECT_MISSING'
    $xml=[xml][IO.File]::ReadAllText($path)
    $framework=$xml.SelectSingleNode('/Project/PropertyGroup/TargetFramework')
    Assert-Cp6Entry ($null -ne $framework -and $framework.InnerText -cmatch '^net[0-9]+\.[0-9]+$') 'CP6_COMPAT_PROJECT_FRAMEWORK_INVALID'
    $nameNode=$xml.SelectSingleNode('/Project/PropertyGroup/AssemblyName')
    $name=if($null -ne $nameNode){$nameNode.InnerText}else{[IO.Path]::GetFileNameWithoutExtension($path)}
    Assert-Cp6Entry ($name -cmatch '^[A-Za-z0-9_.-]+$') 'CP6_COMPAT_ASSEMBLY_NAME_INVALID'
    $directory=Join-Path (Split-Path $path -Parent) ('bin/'+$Configuration+'/'+$framework.InnerText)
    return [pscustomobject]@{ProjectPath=$path;BinaryPath=(Join-Path $directory ($name+'.dll'));BinaryDirectory=$directory}
}
function New-Cp6EntryPlan {
    param($Entry,[string]$Provider,[string]$Worktree,[string]$OutputDirectory,[hashtable]$PlaceholderValues,[string]$Configuration,[string]$SourceSha)
    Assert-Cp6Entry ($Provider -cin $Entry.ProviderEligibility -and $SourceSha -cmatch '^[a-f0-9]{40}$') 'CP6_COMPAT_ENTRY_INPUT_INVALID'
    $layout=Get-Cp6ProjectLayout $Worktree $Entry.Project $Configuration
    $output=[IO.Path]::GetFullPath($OutputDirectory);$public=Join-Path $output 'reports';$private=Join-Path $output 'private'
    $runtimeRoot=[IO.Path]::GetFullPath($layout.BinaryDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)
    Assert-Cp6Entry ($output -ine $runtimeRoot -and !$output.StartsWith($runtimeRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) 'CP6_COMPAT_ENTRY_OUTPUT_INSIDE_RUNTIME'
    $values=@{}
    foreach($key in $PlaceholderValues.Keys){
        Assert-Cp6Entry ($key -cin $script:placeholders -and $key -cnotin @('Provider','PublicOutputDirectory','PrivateDiagnosticDirectory')) 'CP6_COMPAT_PLACEHOLDER_INVALID'
        if($key -cin @('BaseUri','SignalREventKey')){Assert-Cp6Entry ($Entry.Id -ceq 'application-signalr-user-delivery') 'CP6_COMPAT_PLACEHOLDER_INVALID'}
        Assert-Cp6Entry (![string]::IsNullOrWhiteSpace([string]$PlaceholderValues[$key])) 'CP6_COMPAT_PLACEHOLDER_MISSING'
        $values[$key]=[string]$PlaceholderValues[$key]
    }
    if($values.ContainsKey('BaseUri')){
        $uri=$null
        Assert-Cp6Entry ([Uri]::TryCreate($values.BaseUri,[UriKind]::Absolute,[ref]$uri) -and $uri.Scheme -ceq 'http' -and $uri.Host -cin @('localhost','127.0.0.1','[::1]','::1') -and $uri.Port -ge 1024 -and $uri.Port -le 65535 -and $uri.UserInfo -ceq '' -and $uri.AbsolutePath -ceq '/' -and $uri.Query -ceq '' -and $uri.Fragment -ceq '') 'CP6_COMPAT_BASE_URI_INVALID'
    }
    $values.Provider=$Provider;$values.PublicOutputDirectory=$public;$values.PrivateDiagnosticDirectory=Join-Path $private 'identity'
    $tokens=[Collections.Generic.List[string]]::new()
    foreach($token in $Entry.CaseCommand){
        Assert-Cp6Entry ($token -is [string]) 'CP6_COMPAT_CASE_TOKEN_INVALID'
        if($token -cmatch '^\{([A-Za-z]+)\}$'){
            $key=$Matches[1]
            Assert-Cp6Entry ($key -cin $script:placeholders -and $values.ContainsKey($key)) 'CP6_COMPAT_PLACEHOLDER_MISSING'
            $tokens.Add($values[$key])
        }else{Assert-Cp6Entry ($token -cnotmatch '[{}]') 'CP6_COMPAT_CASE_TOKEN_INVALID';$tokens.Add($token)}
    }
    switch -CaseSensitive ($Entry.EntryKind){
        'Trx' {
            Assert-Cp6Entry ($tokens.Count -eq 2 -and $tokens[0] -ceq '--filter' -and $tokens[1] -ceq $Entry.Filter) 'CP6_COMPAT_TRX_FILTER_INVALID'
            $trx=Join-Path $private 'trx';$report=Join-Path $trx 'results.trx'
            $arguments=@('test',$layout.ProjectPath,'--no-build','--no-restore','--configuration',$Configuration,'--logger','trx;LogFileName=results.trx','--results-directory',$trx)+$tokens.ToArray()
        }
        {$_ -cin @('NativeMigration','NativeRuntime')} {
            $report=Join-Path $public 'report.json'
            $arguments=@($layout.BinaryPath)+$tokens.ToArray()+@('--provider',$Provider,'--output',$report,'--source-sha',$SourceSha)
        }
        'IdentityChecks' {$report=Join-Path $public 'summary.json';$arguments=@($layout.BinaryPath)+$tokens.ToArray()}
        'ApplicationChecks' {$report=Join-Path $public 'report.json';$arguments=@($layout.BinaryPath)+$tokens.ToArray()+@('--provider',$Provider,'--report',$report)}
        default {throw 'CP6_COMPAT_ENTRY_KIND_UNSUPPORTED'}
    }
    return [pscustomobject]@{Arguments=$arguments;ReportPath=$report;PublicDirectory=$public;PrivateDirectory=$private;ProcessLogDirectory=(Join-Path $private 'process');BinaryPath=$layout.BinaryPath;BinaryDirectory=$layout.BinaryDirectory;ProjectPath=$layout.ProjectPath}
}
function Get-Cp6EntryEnvironment {
    param($Entry,[string]$Provider,$Receipt,[string]$Connection,[hashtable]$ExtraEnvironment=@{})
    Assert-Cp6Entry ($Receipt.Role -ceq $Entry.Role) 'CP6_COMPAT_ENTRY_ROLE_MISMATCH'
    $environment=@{CP6_COMPAT_SCOPE='WP6';CP6_COMPAT_DATABASE_NAME=$Receipt.DatabaseName;CP6_TEST_DATABASE_OWNER=$Receipt.Owner;DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_NOLOGO='1'}
    $credentialKeys=@('CP6_COMPAT_ADMIN_PASSWORD','CP6_COMPAT_ORDINARY_USERNAME','CP6_COMPAT_ORDINARY_PASSWORD')
    if($Entry.Id -ceq 'application-signalr-user-delivery'){
        Assert-Cp6Entry ($ExtraEnvironment.Count -eq $credentialKeys.Count) 'CP6_COMPAT_SIGNALR_CREDENTIALS_REQUIRED'
        foreach($key in $ExtraEnvironment.Keys){Assert-Cp6Entry ($key -cin $credentialKeys -and $ExtraEnvironment[$key] -is [string] -and ![string]::IsNullOrWhiteSpace($ExtraEnvironment[$key])) 'CP6_COMPAT_SIGNALR_CREDENTIALS_INVALID'}
        foreach($key in $credentialKeys){Assert-Cp6Entry ($ExtraEnvironment.ContainsKey($key)) 'CP6_COMPAT_SIGNALR_CREDENTIALS_REQUIRED';$environment[$key]=$ExtraEnvironment[$key]}
    }else{Assert-Cp6Entry ($ExtraEnvironment.Count -eq 0) 'CP6_COMPAT_EXTRA_ENVIRONMENT_FORBIDDEN'}
    $environment[$(if($Provider -ceq 'SqlServer'){'CP6_TEST_SQLSERVER'}else{'CP6_TEST_POSTGRES'})]=$Connection
    if($Entry.EntryKind -ceq 'IdentityChecks'){$environment[$(if($Provider -ceq 'SqlServer'){'CP6_C02_TEST_SQL'}else{'CP6_C02_TEST_POSTGRES'})]=$Connection}
    elseif($Entry.EntryKind -ceq 'Trx'){
        $prefix=switch -CaseSensitive ($Entry.Project){
            'CP6.Oidc.IntegrationTests/CP6.Oidc.IntegrationTests.csproj' {'CP6_OIDC_TEST_'}
            'eng/crm/erp-integration-tests/CP6.ErpIntegration.SqlTests.csproj' {'CP6_ERP_TEST_'}
            'CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj' {if($Entry.Role -ceq 'spacehistory'){'CP6_SPACE_MIGRATION_TEST_'}else{'CP6_SPACE_TEST_'}}
            'CP6.Tests/CP6.Tests.csproj' {if($Entry.Role -ceq 'reports'){'CP6_WP5_CORE_TEST_'}elseif($Entry.Id -ceq 'wms-business'){'CP6_WMS_TEST_'}else{'CP6_CORE_TEST_'}}
            default {throw 'CP6_COMPAT_FIXTURE_UNKNOWN'}
        }
        $environment[$prefix+'PROVIDER']=$Provider;$environment[$prefix+'CONNECTION']=$Connection
    }
    return $environment
}
function Invoke-Cp6CompatibilityEntry {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Entry,[Parameter(Mandatory)][string]$Provider,[Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt,[Parameter(Mandatory)][string]$Worktree,[Parameter(Mandatory)][string]$OutputDirectory,[hashtable]$PlaceholderValues=@{},[hashtable]$ExtraEnvironment=@{},[ValidateSet('Debug','Release')][string]$Configuration='Debug',[Parameter(Mandatory)][string]$SourceSha,[ValidateRange(1,3600)][int]$TimeoutSeconds=1200)
    $output=[IO.Path]::GetFullPath($OutputDirectory)
    Assert-Cp6Entry (!(Test-Path -LiteralPath $output)) 'CP6_COMPAT_ENTRY_OUTPUT_EXISTS'
    $null=[IO.Directory]::CreateDirectory($output)
    $process=$null;$validation=$null;$binaryHash=$null;$reportHash=$null;$failure=$null;$reportPath=$null
    $runtimePath=$null;$runtimeHash=$null;$runtimeAfterPath=$null;$runtimeAfterHash=$null;$runtimeUnchanged=$false
    try {
        $canonical=Get-Cp6CompatibilityEntry $Worktree -Id $Entry.Id -Provider $Provider
        Assert-Cp6Entry (($Entry|ConvertTo-Json -Depth 30 -Compress) -ceq ($canonical|ConvertTo-Json -Depth 30 -Compress)) 'CP6_COMPAT_ENTRY_MANIFEST_MISMATCH'
        Assert-Cp6Entry ($Context.Provider -ceq $Provider -and $Receipt.Provider -ceq $Provider -and $Receipt.Role -ceq $Entry.Role) 'CP6_COMPAT_ENTRY_ROLE_MISMATCH'
        $privateRoot=[IO.Path]::GetFullPath($Context.PrivateDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
        foreach($key in $PlaceholderValues.Keys|Where-Object {$_ -clike '*StatePath'}){
            Assert-Cp6Entry ([IO.Path]::IsPathFullyQualified([string]$PlaceholderValues[$key]) -and [IO.Path]::GetFullPath([string]$PlaceholderValues[$key]).StartsWith($privateRoot,[StringComparison]::OrdinalIgnoreCase)) 'CP6_COMPAT_STATE_PATH_NOT_PRIVATE'
        }
        $plan=New-Cp6EntryPlan $Entry $Provider $Worktree $output $PlaceholderValues $Configuration $SourceSha
        $reportPath=$plan.ReportPath;$null=[IO.Directory]::CreateDirectory($plan.PublicDirectory);$null=[IO.Directory]::CreateDirectory($plan.PrivateDirectory)
        Assert-Cp6Entry (Test-Path -LiteralPath $plan.BinaryPath -PathType Leaf) 'CP6_COMPAT_ENTRY_BINARY_MISSING'
        $actual=Test-Cp6OwnedDatabase -Context $Context -Receipt $Receipt -RequireEmpty:($Entry.DatabaseLifecycle -ceq 'FreshPerEntry')
        Assert-Cp6Entry $actual.Exists 'CP6_COMPAT_ENTRY_DATABASE_ABSENT'
        $connection=Get-Cp6OwnedDatabaseConnection -Context $Context -Receipt $Receipt
        $environment=Get-Cp6EntryEnvironment $Entry $Provider $Receipt $connection $ExtraEnvironment
        $dotnet=Get-Command dotnet -CommandType Application -ErrorAction Stop|Select-Object -First 1
        $runtime=Get-Cp6RuntimeArtifactManifest $plan.BinaryDirectory
        $primary=@($runtime.Files | Where-Object Path -CEQ ([IO.Path]::GetFileName($plan.BinaryPath)))
        Assert-Cp6Entry ($primary.Count -eq 1) 'CP6_COMPAT_ENTRY_BINARY_MISSING'
        $binaryHash=$primary[0].Sha256
        $runtimePath=Join-Path $output 'runtime-artifact-before.json';Write-Cp6EntryJson $runtimePath $runtime
        $runtimeHash=(Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
        $process=Invoke-Cp6CompatibilityProcess -Executable $dotnet.Source -ArgumentList $plan.Arguments -WorkingDirectory $Worktree -LogDirectory $plan.ProcessLogDirectory -ChildEnvironment $environment -TimeoutSeconds $TimeoutSeconds
        $runtimeAfter=Get-Cp6RuntimeArtifactManifest $plan.BinaryDirectory
        $runtimeAfterPath=Join-Path $output 'runtime-artifact-after.json';Write-Cp6EntryJson $runtimeAfterPath $runtimeAfter
        $runtimeAfterHash=(Get-FileHash -LiteralPath $runtimeAfterPath -Algorithm SHA256).Hash
        $runtimeUnchanged=$runtimeHash -ceq $runtimeAfterHash
        $validation=Test-Cp6RequiredResults -Entry $Entry -Provider $Provider -ResultPath $reportPath -ProcessExitCode $process.ExitCode
        if($process.TimedOut -or $process.OwnedProcessTerminated){$validation.Success=$false;$validation.Status='Failed';$validation.FailureCodes+=@('ProcessTimedOutOrTerminated')}
        if($Entry.EntryKind -ceq 'ApplicationChecks' -and $validation.ReportDatabaseName -cne $Receipt.DatabaseName){$validation.Success=$false;$validation.Status='Failed';$validation.FailureCodes+=@('ActualDatabaseMismatch')}
        $reportHash=$validation.ReportSha256
        Assert-Cp6Entry $runtimeUnchanged 'CP6_COMPAT_RUNTIME_ARTIFACT_CHANGED'
    }catch{
        $failure=if($_.Exception.Message -cmatch '^CP6_COMPAT_[A-Z_]+$'){$_.Exception.Message}else{'CP6_COMPAT_ENTRY_SETUP_OR_EXECUTION_FAILED'}
    }
    $record=[pscustomobject]@{EntryId=$Entry.Id;Provider=$Provider;DatabaseName=$Receipt.DatabaseName;Role=$Receipt.Role;SourceSha=$SourceSha;Project=$Entry.Project;BinarySha256=$binaryHash;RuntimeArtifactManifestPath=$runtimePath;RuntimeArtifactManifestSha256=$runtimeHash;RuntimeArtifactAfterManifestPath=$runtimeAfterPath;RuntimeArtifactAfterManifestSha256=$runtimeAfterHash;RuntimeArtifactsUnchanged=$runtimeUnchanged;Process=$process;Validation=$validation;ReportPath=$reportPath;ReportSha256=$reportHash;FailureCode=$failure;Success=($null -eq $failure -and $null -ne $validation -and $validation.Success -and $runtimeUnchanged)}
    Write-Cp6EntryJson (Join-Path $output 'entry-result.json') $record
    return $record
}
function Get-Cp6ProjectInputs([string]$Worktree,[string]$Project){
    $root=[IO.Path]::GetFullPath($Worktree).TrimEnd([IO.Path]::DirectorySeparatorChar)+[IO.Path]::DirectorySeparatorChar
    $files=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $visited=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $queue=[Collections.Generic.Queue[string]]::new();$queue.Enqueue([IO.Path]::GetFullPath((Join-Path $root $Project)))
    while($queue.Count -gt 0){
        $projectPath=$queue.Dequeue()
        Assert-Cp6Entry ($projectPath.StartsWith($root,[StringComparison]::OrdinalIgnoreCase) -and (Test-Path -LiteralPath $projectPath -PathType Leaf)) 'CP6_COMPAT_SOURCE_PROJECT_INVALID'
        if(!$visited.Add($projectPath)){continue}
        $directory=Split-Path $projectPath -Parent
        foreach($file in Get-ChildItem -LiteralPath $directory -Recurse -File){
            $relative=[IO.Path]::GetRelativePath($directory,$file.FullName).Replace('\','/')
            if($relative -match '(^|/)(bin|obj|node_modules|\.git|\.vs)/'){continue}
            if($file.Extension -cin @('.cs','.csproj','.props','.targets','.json','.sql','.resx','.razor','.cshtml','.config')){$null=$files.Add($file.FullName)}
        }
        $xml=[xml][IO.File]::ReadAllText($projectPath)
        foreach($node in $xml.SelectNodes('//ProjectReference[@Include]')){
            $include=$node.GetAttribute('Include')
            Assert-Cp6Entry ($include -notmatch '[$*?]') 'CP6_COMPAT_SOURCE_REFERENCE_UNRESOLVED'
            $queue.Enqueue([IO.Path]::GetFullPath((Join-Path $directory $include)))
        }
        foreach($node in $xml.SelectNodes('//Compile[@Include]')){
            $include=$node.GetAttribute('Include').Replace('\',[IO.Path]::DirectorySeparatorChar)
            Assert-Cp6Entry ($include -notmatch '[$]' -and (Split-Path $include -Parent) -notmatch '[*?]') 'CP6_COMPAT_SOURCE_REFERENCE_UNRESOLVED'
            $linked=[IO.Path]::GetFullPath((Join-Path $directory $include))
            Assert-Cp6Entry ($linked.StartsWith($root,[StringComparison]::OrdinalIgnoreCase)) 'CP6_COMPAT_SOURCE_LINK_INVALID'
            if($include -match '[*?]'){
                $matches=@(Get-ChildItem -LiteralPath (Split-Path $linked -Parent) -Filter ([IO.Path]::GetFileName($linked)) -File)
                Assert-Cp6Entry ($matches.Count -gt 0) 'CP6_COMPAT_SOURCE_LINK_INVALID'
                foreach($file in $matches){$null=$files.Add($file.FullName)}
            }else{
                Assert-Cp6Entry (Test-Path -LiteralPath $linked -PathType Leaf) 'CP6_COMPAT_SOURCE_LINK_INVALID'
                $null=$files.Add($linked)
            }
        }
        $ancestor=$directory
        while(($ancestor+[IO.Path]::DirectorySeparatorChar).StartsWith($root,[StringComparison]::OrdinalIgnoreCase)){
            foreach($name in @('Directory.Build.props','Directory.Build.targets','Directory.Packages.props','global.json','NuGet.Config','nuget.config')){
                $path=Join-Path $ancestor $name;if(Test-Path -LiteralPath $path -PathType Leaf){$null=$files.Add($path)}
            }
            $ancestor=Split-Path $ancestor -Parent
        }
    }
    return @($files|Sort-Object|ForEach-Object {[pscustomobject]@{Path=[IO.Path]::GetRelativePath($root,$_).Replace('\','/');Sha256=(Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash}})
}
function Invoke-Cp6CompatibilityBuild {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Worktree,[Parameter(Mandatory)][string]$OutputDirectory,[Parameter(Mandatory)][string[]]$Projects,[ValidateSet('Debug','Release')][string]$Configuration='Debug',[Parameter(Mandatory)][string]$SourceSha,[ValidateRange(1,3600)][int]$TimeoutSeconds=1200)
    Assert-Cp6Entry ($Projects.Count -gt 0 -and @($Projects|Sort-Object -Unique).Count -eq $Projects.Count) 'CP6_COMPAT_PROJECT_LIST_INVALID'
    foreach($project in $Projects){Assert-Cp6Entry ($project -cin $script:projects) 'CP6_COMPAT_PROJECT_INVALID'}
    Assert-Cp6Entry ($SourceSha -cmatch '^[a-f0-9]{40}$') 'CP6_COMPAT_SOURCE_SHA_INVALID'
    $output=[IO.Path]::GetFullPath($OutputDirectory)
    Assert-Cp6Entry (!(Test-Path -LiteralPath $output)) 'CP6_COMPAT_BUILD_OUTPUT_EXISTS'
    $null=[IO.Directory]::CreateDirectory($output);$private=Join-Path $output 'private';$null=[IO.Directory]::CreateDirectory($private)
    $results=[Collections.Generic.List[object]]::new();$failure=$null;$version=$null;$versionProcess=$null
    $buildEnvironment=@{DOTNET_CLI_TELEMETRY_OPTOUT='1';DOTNET_NOLOGO='1'}
    try {
        $dotnet=Get-Command dotnet -CommandType Application -ErrorAction Stop|Select-Object -First 1
        $versionLogs=Join-Path $private 'sdk-version'
        $versionProcess=Invoke-Cp6CompatibilityProcess -Executable $dotnet.Source -ArgumentList @('--version') -WorkingDirectory $Worktree -LogDirectory $versionLogs -ChildEnvironment $buildEnvironment -TimeoutSeconds 60
        Assert-Cp6Entry ($versionProcess.ExitCode -eq 0 -and !$versionProcess.TimedOut -and !$versionProcess.OwnedProcessTerminated) 'CP6_COMPAT_SDK_VERSION_FAILED'
        $version=[IO.File]::ReadAllText((Join-Path $versionLogs 'stdout.log')).Trim()
        Assert-Cp6Entry ($version -cmatch '^[0-9]+\.[0-9]+\.[A-Za-z0-9.+-]+$') 'CP6_COMPAT_SDK_VERSION_INVALID'
        $index=0
        foreach($project in $Projects){
            $index++;$layout=Get-Cp6ProjectLayout $Worktree $project $Configuration
            $inputs=@(Get-Cp6ProjectInputs $Worktree $project)
            $item=[pscustomobject]@{Project=$project;Configuration=$Configuration;SourceInputs=$inputs;RestoreProcess=$null;BuildProcess=$null;Binaries=@();Success=$false}
            $results.Add($item)
            $restoreLogs=Join-Path $private ($index.ToString('D2')+'-restore')
            $item.RestoreProcess=Invoke-Cp6CompatibilityProcess -Executable $dotnet.Source -ArgumentList @('restore',$layout.ProjectPath,'--locked-mode','--disable-parallel') -WorkingDirectory $Worktree -LogDirectory $restoreLogs -ChildEnvironment $buildEnvironment -TimeoutSeconds $TimeoutSeconds
            Assert-Cp6Entry ($item.RestoreProcess.ExitCode -eq 0 -and !$item.RestoreProcess.TimedOut -and !$item.RestoreProcess.OwnedProcessTerminated) 'CP6_COMPAT_LOCKED_RESTORE_FAILED'
            $buildLogs=Join-Path $private ($index.ToString('D2')+'-build')
            $item.BuildProcess=Invoke-Cp6CompatibilityProcess -Executable $dotnet.Source -ArgumentList @('build',$layout.ProjectPath,'--no-restore','--configuration',$Configuration,'-m:1','-nr:false') -WorkingDirectory $Worktree -LogDirectory $buildLogs -ChildEnvironment $buildEnvironment -TimeoutSeconds $TimeoutSeconds
            Assert-Cp6Entry ($item.BuildProcess.ExitCode -eq 0 -and !$item.BuildProcess.TimedOut -and !$item.BuildProcess.OwnedProcessTerminated) 'CP6_COMPAT_PROJECT_BUILD_FAILED'
            $after=@(Get-Cp6ProjectInputs $Worktree $project)
            Assert-Cp6Entry (($inputs|ConvertTo-Json -Depth 5 -Compress) -ceq ($after|ConvertTo-Json -Depth 5 -Compress)) 'CP6_COMPAT_BUILD_INPUT_CHANGED'
            Assert-Cp6Entry (Test-Path -LiteralPath $layout.BinaryPath -PathType Leaf) 'CP6_COMPAT_BUILD_BINARY_MISSING'
            $runtime=Get-Cp6RuntimeArtifactManifest $layout.BinaryDirectory
            $item.Binaries=@($runtime.Files | ForEach-Object {[pscustomobject]@{Path=[IO.Path]::GetRelativePath($Worktree,(Join-Path $runtime.Directory $_.Path)).Replace('\','/');Length=$_.Length;Sha256=$_.Sha256}})
            $item.Success=$true
            Write-Cp6EntryJson (Join-Path $output ('project-'+$index.ToString('D2')+'.json')) $item
        }
    }catch{$failure=if($_.Exception.Message -cmatch '^CP6_COMPAT_[A-Z_]+$'){$_.Exception.Message}else{'CP6_COMPAT_BUILD_SETUP_OR_EXECUTION_FAILED'}}
    $record=[pscustomobject]@{SourceSha=$SourceSha;Configuration=$Configuration;DotnetSdkVersion=$version;VersionProcess=$versionProcess;RequestedProjects=$Projects;Projects=$results.ToArray();FailureCode=$failure;Success=($null -eq $failure -and $results.Count -eq $Projects.Count -and @($results|Where-Object {!$_.Success}).Count -eq 0);Scope='Local locked restore/build only; no publish, database access, remote Actions or deployment.'}
    Write-Cp6EntryJson (Join-Path $output 'build-result.json') $record
    return $record
}
function Get-Cp6CompatibilitySourceInputs {
    [CmdletBinding()]
    param([Parameter(Mandatory)][string]$Worktree,[Parameter(Mandatory)][string[]]$Projects)
    Assert-Cp6Entry ($Projects.Count -gt 0) 'CP6_COMPAT_PROJECT_LIST_INVALID'
    $union=@{}
    foreach($project in $Projects){
        Assert-Cp6Entry ($project -cin $script:projects) 'CP6_COMPAT_PROJECT_INVALID'
        foreach($inputFile in @(Get-Cp6ProjectInputs $Worktree $project)){
            if($union.ContainsKey($inputFile.Path)){Assert-Cp6Entry ($union[$inputFile.Path].Sha256 -ceq $inputFile.Sha256) 'CP6_COMPAT_BUILD_INPUT_CHANGED'}
            else{$union[$inputFile.Path]=$inputFile}
        }
    }
    return @($union.Values|Sort-Object Path)
}
Export-ModuleMember -Function Get-Cp6CompatibilityEntry,Invoke-Cp6CompatibilityEntry,Invoke-Cp6CompatibilityBuild,Get-Cp6CompatibilitySourceInputs
