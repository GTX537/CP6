Set-StrictMode -Version Latest

function Get-Cp6ApplicationArtifactFiles([string]$ArtifactDirectory) {
    if(!(Test-Path -LiteralPath (Join-Path $ArtifactDirectory 'CP6.WebApi.dll') -PathType Leaf)){throw 'CP6_COMPAT_API_ARTIFACT_CHANGED'}
    return @(Get-ChildItem -LiteralPath $ArtifactDirectory -File -Recurse -Force -ErrorAction Stop|Sort-Object FullName|ForEach-Object {
        [pscustomobject]@{Path=[IO.Path]::GetRelativePath($ArtifactDirectory,$_.FullName).Replace('\','/');Length=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName -ErrorAction Stop).Hash}
    })
}

function Assert-Cp6ApplicationArtifact($Context,$Settings) {
    try {
        if($Settings.RunId -cne $Context.RunId -or
            $Settings.SettingsPath -cne (Join-Path $Context.PrivateDirectory 'application-settings.private.json') -or
            $Settings.ArtifactManifestPath -cne (Join-Path $Context.RunDirectory 'application-artifact.json')){throw 'CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
        $private=Get-Content -LiteralPath $Settings.SettingsPath -Raw -ErrorAction Stop|ConvertFrom-Json -ErrorAction Stop
        if($private.RunId -cne $Context.RunId -or $private.SourceSha -cne $Settings.SourceSha -or
            $private.ArtifactDirectory -cne $Settings.ArtifactDirectory){throw 'CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
        if((Get-FileHash -LiteralPath $Settings.ArtifactManifestPath -ErrorAction Stop).Hash -cne $Settings.ArtifactManifestSha256){throw 'CP6_COMPAT_API_ARTIFACT_CHANGED'}
        $manifest=Get-Content -LiteralPath $Settings.ArtifactManifestPath -Raw -ErrorAction Stop|ConvertFrom-Json -ErrorAction Stop
        if($manifest.Task -cne $Context.Task -or $manifest.RunId -cne $Context.RunId -or
            $manifest.SourceSha -cne $Settings.SourceSha -or $manifest.ArtifactDirectory -cne $Settings.ArtifactDirectory){throw 'CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
        $actual=@(Get-Cp6ApplicationArtifactFiles $Settings.ArtifactDirectory)
        if(($manifest.Files|ConvertTo-Json -Depth 6 -Compress) -cne ($actual|ConvertTo-Json -Depth 6 -Compress)){throw 'CP6_COMPAT_API_ARTIFACT_CHANGED'}
    }catch{
        if($_.Exception.Message -cmatch '^CP6_COMPAT_[A-Z0-9_]+$'){throw}
        throw 'CP6_COMPAT_API_ARTIFACT_INVALID'
    }
}

function New-Cp6ApplicationSettings {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)][string]$ArtifactDirectory,[Parameter(Mandatory)][string]$SourceSha)
    if($SourceSha -cnotmatch '^[a-f0-9]{40}$'){throw 'CP6_COMPAT_SOURCE_SHA_INVALID'}
    $artifact=[IO.Path]::GetFullPath($ArtifactDirectory)
    if(!(Test-Path -LiteralPath (Join-Path $artifact 'CP6.WebApi.dll'))){throw 'CP6_COMPAT_API_ARTIFACT_MISSING'}
    $path=Join-Path $Context.PrivateDirectory 'application-settings.private.json'
    if(Test-Path -LiteralPath $path){
        $settings=Get-Content -LiteralPath $path -Raw|ConvertFrom-Json
        if($settings.RunId -cne $Context.RunId -or $settings.SourceSha -cne $SourceSha -or $settings.ArtifactDirectory -cne $artifact){throw 'CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
    }else{
        $settings=[pscustomobject]@{
            RunId=$Context.RunId;SourceSha=$SourceSha;ArtifactDirectory=$artifact
            JwtSecret=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(64))
            JwtIssuer='CP6.WP6.'+$Context.RunId;JwtAudience='CP6.WP6.LocalVerification'
        }
        [IO.File]::WriteAllText($path,($settings|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
    }
    $files=@(Get-Cp6ApplicationArtifactFiles $artifact)
    $manifest=Join-Path $Context.RunDirectory 'application-artifact.json'
    $data=[pscustomobject]@{Task=$Context.Task;RunId=$Context.RunId;SourceSha=$SourceSha;ArtifactDirectory=$artifact;Files=$files;ProductionCandidate=$false}
    if(Test-Path -LiteralPath $manifest){
        $prior=Get-Content -LiteralPath $manifest -Raw|ConvertFrom-Json
        if(($prior.Files|ConvertTo-Json -Depth 6 -Compress) -cne ($files|ConvertTo-Json -Depth 6 -Compress)){throw 'CP6_COMPAT_API_ARTIFACT_CHANGED'}
    }else{[IO.File]::WriteAllText($manifest,($data|ConvertTo-Json -Depth 8),[Text.UTF8Encoding]::new($false))}
    # Private settings/credentials stay on disk and are loaded only when constructing a child environment.
    return [pscustomobject]@{RunId=$Context.RunId;SourceSha=$SourceSha;ArtifactDirectory=$artifact;ArtifactManifestPath=$manifest;ArtifactManifestSha256=(Get-FileHash -LiteralPath $manifest).Hash;SettingsPath=$path}
}

function Get-Cp6ApplicationEnvironment {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt,[Parameter(Mandatory)]$Settings,[ValidateSet('DatabaseInit','Api')][string]$Mode,[int]$Port=0)
    if($Settings.RunId -cne $Context.RunId -or $Settings.SettingsPath -cne (Join-Path $Context.PrivateDirectory 'application-settings.private.json')){throw 'CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
    $private=Get-Content -LiteralPath $Settings.SettingsPath -Raw|ConvertFrom-Json
    if($private.RunId -cne $Context.RunId -or $private.SourceSha -cne $Settings.SourceSha){throw 'CP6_COMPAT_APPLICATION_SETTINGS_MISMATCH'}
    if($Mode -ceq 'Api' -and ($Port -lt 1024 -or $Port -gt 65535)){throw 'CP6_COMPAT_PORT_INVALID'}
    return @{
        DOTNET_ENVIRONMENT='Development';ASPNETCORE_ENVIRONMENT='Development'
        Database__Provider=$Context.Provider
        ConnectionStrings__DefaultConnection=(Get-Cp6OwnedDatabaseConnection -Context $Context -Receipt $Receipt)
        ConnectionStrings__Redis='';Kafka__BootstrapServers='';RabbitMQ__HostName=''
        Startup__Mode=$Mode;Startup__SkipDatabaseInitialization=$(if($Mode -ceq 'Api'){'true'}else{'false'})
        Startup__SkipHostedServices=$(if($Mode -ceq 'Api'){'false'}else{'true'})
        ASPNETCORE_URLS=$(if($Mode -ceq 'Api'){'http://127.0.0.1:'+$Port}else{'http://127.0.0.1:0'})
        JWT__Secret=$private.JwtSecret;JWT__Issuer=$private.JwtIssuer;JWT__Audience=$private.JwtAudience
        Security__Cookie__Secure='false';Security__Csrf__Enabled='true'
        CrmOidc__Enabled='false';CrmIdentity__Enabled='false';ErpIntegration__Enabled='false'
        Space__Cad__RemoteWorker__Enabled='false'
        SpaceObservability__LegacyIntegrationEventTimeZoneId='UTC'
        Space__Files__RootPath=(Join-Path $Context.PrivateDirectory 'space-files')
        Storage__Provider='Local';Storage__LocalRoot=(Join-Path $Context.PrivateDirectory 'uploads')
        Release__GitSha=$Settings.SourceSha;Release__Version='WP6-local-verification'
        Logging__LogLevel__Default='Information';'Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command'='Warning'
    }
}

function Invoke-Cp6ApplicationInitialization {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt,[Parameter(Mandatory)]$Settings,[Parameter(Mandatory)][string]$Label)
    if($Label -cnotmatch '^[a-z0-9-]{1,80}$'){throw 'CP6_COMPAT_LABEL_INVALID'}
    Assert-Cp6ApplicationArtifact $Context $Settings
    $null=Test-Cp6OwnedDatabase -Context $Context -Receipt $Receipt -RequireNoSessions
    $logs=Join-Path $Context.PrivateDirectory $Label
    $environment=Get-Cp6ApplicationEnvironment -Context $Context -Receipt $Receipt -Settings $Settings -Mode DatabaseInit
    $result=Invoke-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @((Join-Path $Settings.ArtifactDirectory 'CP6.WebApi.dll')) -WorkingDirectory $Settings.ArtifactDirectory -LogDirectory $logs -ChildEnvironment $environment -TimeoutSeconds 600
    $stdout=[IO.File]::ReadAllText((Join-Path $logs 'stdout.log'))
    $completed=$stdout.Contains('Database initialization completed; the one-shot process will exit.')
    $report=[pscustomobject]@{Task=$Context.Task;Provider=$Context.Provider;DatabaseName=$Receipt.DatabaseName;Label=$Label;ArtifactManifestSha256=$Settings.ArtifactManifestSha256;Process=$result;InitializerCompletionObserved=$completed;Success=($result.ExitCode -eq 0 -and !$result.TimedOut -and $completed)}
    [IO.File]::WriteAllText((Join-Path $Context.RunDirectory ($Label+'.json')),($report|ConvertTo-Json -Depth 8))
    return $report
}

function Start-Cp6ApplicationHost {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Context,[Parameter(Mandatory)]$Receipt,[Parameter(Mandatory)]$Settings,[Parameter(Mandatory)][string]$Label)
    if($Label -cnotmatch '^[a-z0-9-]{1,80}$'){throw 'CP6_COMPAT_LABEL_INVALID'}
    Assert-Cp6ApplicationArtifact $Context $Settings
    $null=Test-Cp6OwnedDatabase -Context $Context -Receipt $Receipt
    $reservation=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,0)
    try{$reservation.Start();$port=$reservation.LocalEndpoint.Port}finally{$reservation.Stop()}
    $environment=Get-Cp6ApplicationEnvironment -Context $Context -Receipt $Receipt -Settings $Settings -Mode Api -Port $port
    $handle=Start-Cp6CompatibilityProcess -Executable (Get-Command dotnet).Source -ArgumentList @((Join-Path $Settings.ArtifactDirectory 'CP6.WebApi.dll')) -WorkingDirectory $Settings.ArtifactDirectory -LogDirectory (Join-Path $Context.PrivateDirectory $Label) -ChildEnvironment $environment
    return [pscustomobject]@{Handle=$handle;Port=$port;BaseUri='http://127.0.0.1:'+$port;DatabaseName=$Receipt.DatabaseName;ArtifactManifestSha256=$Settings.ArtifactManifestSha256;SourceSha=$Settings.SourceSha;Label=$Label}
}

function Wait-Cp6ApplicationReady {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$HostInstance,[ValidateRange(1,180)][int]$TimeoutSeconds=120)
    $deadline=[DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $handler=[Net.Http.HttpClientHandler]::new();$handler.AllowAutoRedirect=$false;$handler.UseProxy=$false
    $client=[Net.Http.HttpClient]::new($handler);$client.Timeout=[timespan]::FromSeconds(10)
    try{
        do{
            if(Assert-Cp6CompatibilityListener -Handle $HostInstance.Handle -Port $HostInstance.Port){
                $observations=@();$ready=$true
                foreach($path in @('/health/live','/health/ready','/health/release')){
                    $response=$client.GetAsync($HostInstance.BaseUri+$path).GetAwaiter().GetResult()
                    try{
                        $body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
                        $observations+=@([pscustomobject]@{Path=$path;StatusCode=[int]$response.StatusCode;BodySha256=[Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($body)))})
                        if([int]$response.StatusCode -ne 200){$ready=$false}
                        if($path -ceq '/health/release' -and [int]$response.StatusCode -eq 200){
                            $release=$body|ConvertFrom-Json
                            if($release.gitSha -cne $HostInstance.SourceSha -or [string]::IsNullOrWhiteSpace($release.latestMigration)){throw 'CP6_COMPAT_RELEASE_IDENTITY_MISMATCH'}
                        }
                    }finally{$response.Dispose()}
                }
                if($ready){return [pscustomobject]@{Success=$true;ProcessId=$HostInstance.Handle.ProcessId;Port=$HostInstance.Port;DatabaseName=$HostInstance.DatabaseName;SourceSha=$HostInstance.SourceSha;ArtifactManifestSha256=$HostInstance.ArtifactManifestSha256;LatestCoreMigration=$release.latestMigration;Http=$observations;CheckedUtc=[datetime]::UtcNow.ToString('o')}}
            }
            Start-Sleep -Milliseconds 500
        }while([DateTime]::UtcNow -lt $deadline)
        throw 'CP6_COMPAT_API_READY_TIMEOUT'
    }finally{$client.Dispose();$handler.Dispose()}
}

Export-ModuleMember -Function New-Cp6ApplicationSettings,Get-Cp6ApplicationEnvironment,Invoke-Cp6ApplicationInitialization,Start-Cp6ApplicationHost,Wait-Cp6ApplicationReady
