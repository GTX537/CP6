[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("dev", "uat", "prod-lab")]
    [string]$Environment,

    [Parameter(Mandatory = $true)]
    [ValidateSet("Initialize", "Build", "Config", "Deploy", "Status", "Stop", "Logs")]
    [string]$Action,

    [string]$ApiImage = "cp6-api:lab-local",
    [string]$WebImage = "cp6-web:lab-local",
    [string]$ReleaseVersion = "0.0.0-lab",
    [string]$GitSha = "",
    [string]$SourceRoot = "",
    [string]$RuntimeArtifactRoot = "",
    [string]$ApiImageIdFile = "",
    [string]$WebImageIdFile = "",
    [switch]$AllowPromotedGitSha,
    [int]$SqlPort = 0,
    [string]$SqlHost = "host.docker.internal",
    [string]$DbVaultPath = (Join-Path `
        ([Environment]::GetFolderPath("UserProfile")) `
        "Documents\CP6-Secrets\sql-lab-accounts.dpapi.clixml"),
    [string]$LabVaultPath = (Join-Path `
        ([Environment]::GetFolderPath("UserProfile")) `
        "Documents\CP6-Secrets\docker-lab-secrets.dpapi.clixml")
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedSourceRoot = if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    $repoRoot
}
else {
    [IO.Path]::GetFullPath($SourceRoot)
}
$composePath = Join-Path $repoRoot "deploy\lab\compose\compose.yaml"

$environmentSettings = @{
    "dev" = [ordered]@{
        ProjectName = "cp6-dev"
        Database = "CP6_DEV"
        MigratorAccount = "cp6_dev_migrator"
        RuntimeAccount = "cp6_dev_runtime"
        RabbitMqUser = "cp6_dev"
        AspNetEnvironment = "Docker"
        ApiPort = 19991
        WebPort = 18080
        RabbitManagementPort = 16072
    }
    "uat" = [ordered]@{
        ProjectName = "cp6-uat"
        Database = "CP6_UAT"
        MigratorAccount = "cp6_uat_migrator"
        RuntimeAccount = "cp6_uat_runtime"
        RabbitMqUser = "cp6_uat"
        AspNetEnvironment = "Staging"
        ApiPort = 29991
        WebPort = 28080
        RabbitManagementPort = 26072
    }
    "prod-lab" = [ordered]@{
        ProjectName = "cp6-prod-lab"
        Database = "CP6_PROD_LAB"
        MigratorAccount = "cp6_prod_lab_migrator"
        RuntimeAccount = "cp6_prod_lab_runtime"
        RabbitMqUser = "cp6_prod_lab"
        AspNetEnvironment = "ProductionLab"
        ApiPort = 39991
        WebPort = 38080
        RabbitManagementPort = 36072
    }
}

if ($ReleaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "ReleaseVersion must be a SemVer-compatible version."
}
if ($AllowPromotedGitSha -and $Action -ne "Deploy") {
    throw "AllowPromotedGitSha is only valid for deployment of an already-built candidate."
}
if ($Action -ne "Build" -and
    (-not [string]::IsNullOrWhiteSpace($ApiImageIdFile) -or
     -not [string]::IsNullOrWhiteSpace($WebImageIdFile))) {
    throw "Image ID output files are only valid for Build."
}
if ($Action -ne "Build" -and
    -not [string]::IsNullOrWhiteSpace($RuntimeArtifactRoot)) {
    throw "RuntimeArtifactRoot is only valid for Build."
}

function New-RandomSecret {
    param([int]$Bytes = 48)

    $buffer = [byte[]]::new($Bytes)
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($buffer)
    }
    finally {
        $generator.Dispose()
    }
    return [Convert]::ToBase64String($buffer).TrimEnd("=").Replace("+", "-").Replace("/", "_")
}

function Protect-SecretDirectory {
    param([Parameter(Mandatory = $true)][string]$Path)

    [IO.Directory]::CreateDirectory($Path) | Out-Null
    if (-not $IsWindows -and $PSVersionTable.PSEdition -eq "Core") {
        return
    }

    $identity = [Security.Principal.WindowsIdentity]::GetCurrent().Name
    & icacls.exe $Path `
        /inheritance:r `
        /grant:r "${identity}:(OI)(CI)F" "NT AUTHORITY\SYSTEM:(OI)(CI)F" |
        Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to restrict the lab secret directory ACL."
    }
}

function Initialize-LabVault {
    if (Test-Path -LiteralPath $LabVaultPath -PathType Leaf) {
        return
    }

    $vaultDirectory = Split-Path -Parent $LabVaultPath
    Protect-SecretDirectory -Path $vaultDirectory
    $records = foreach ($name in @("dev", "uat", "prod-lab")) {
        [pscustomobject]@{
            Environment = $name
            RabbitMqUser = "cp6_$($name.Replace('-', '_'))"
            RabbitMqPassword = ConvertTo-SecureString (New-RandomSecret) -AsPlainText -Force
            JwtSecret = ConvertTo-SecureString (New-RandomSecret -Bytes 64) -AsPlainText -Force
        }
    }
    $records | Export-Clixml -LiteralPath $LabVaultPath
}

function Get-SqlPort {
    if ($SqlPort -gt 0) {
        return $SqlPort
    }

    $instanceMapPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"
    if (-not (Test-Path -LiteralPath $instanceMapPath)) {
        throw "SQL Server instance registry was not found. Supply -SqlPort explicitly."
    }
    $instanceId = (Get-ItemProperty -LiteralPath $instanceMapPath).KOUSQLSERVER
    if ([string]::IsNullOrWhiteSpace($instanceId)) {
        throw "SQL instance KOUSQLSERVER was not found. Supply -SqlPort explicitly."
    }
    $ipAllPath = "HKLM:\SOFTWARE\Microsoft\Microsoft SQL Server\$instanceId\MSSQLServer\SuperSocketNetLib\Tcp\IPAll"
    $ipAll = Get-ItemProperty -LiteralPath $ipAllPath
    $portText = if (-not [string]::IsNullOrWhiteSpace($ipAll.TcpPort)) {
        $ipAll.TcpPort
    }
    else {
        $ipAll.TcpDynamicPorts
    }
    $detectedPort = 0
    if (-not [int]::TryParse($portText, [ref]$detectedPort) -or $detectedPort -le 0) {
        throw "KOUSQLSERVER does not expose a usable TCP port. Supply -SqlPort explicitly."
    }
    return $detectedPort
}

function Get-PlainSecret {
    param([Parameter(Mandatory = $true)][Security.SecureString]$SecureString)

    return [Net.NetworkCredential]::new("", $SecureString).Password
}

function Get-RequiredPipelineSecret {
    param([Parameter(Mandatory = $true)][string]$Name)

    $value = [Environment]::GetEnvironmentVariable($Name, "Process")
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "Required deployment Secret '$Name' is missing."
    }
    return $value
}

function Test-PipelineSecretSource {
    $names = @(
        "CP6_DB_MIGRATOR_PASSWORD",
        "CP6_DB_RUNTIME_PASSWORD",
        "CP6_RABBITMQ_PASSWORD",
        "CP6_JWT_SECRET"
    )
    $present = @($names | Where-Object {
        -not [string]::IsNullOrWhiteSpace(
            [Environment]::GetEnvironmentVariable($_, "Process"))
    })
    if ($present.Count -eq 0) {
        return $false
    }
    if ($present.Count -ne $names.Count) {
        $missing = @($names | Where-Object { $_ -notin $present })
        throw "Deployment Secret set is incomplete. Missing: $($missing -join ', ')."
    }
    return $true
}

function New-PipelineCredentialRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Account,
        [Parameter(Mandatory = $true)][string]$PasswordEnvironmentVariable
    )

    $password = Get-RequiredPipelineSecret -Name $PasswordEnvironmentVariable
    return [pscustomobject]@{
        Account = $Account
        Credential = [PSCredential]::new(
            $Account,
            (ConvertTo-SecureString $password -AsPlainText -Force))
    }
}

function Get-LabRecord {
    param([Parameter(Mandatory = $true)]$Settings)

    if ($script:usePipelineSecrets) {
        return [pscustomobject]@{
            Environment = $Environment
            RabbitMqUser = $Settings.RabbitMqUser
            RabbitMqPassword = ConvertTo-SecureString `
                (Get-RequiredPipelineSecret -Name "CP6_RABBITMQ_PASSWORD") `
                -AsPlainText -Force
            JwtSecret = ConvertTo-SecureString `
                (Get-RequiredPipelineSecret -Name "CP6_JWT_SECRET") `
                -AsPlainText -Force
        }
    }

    $record = Import-Clixml -LiteralPath $LabVaultPath |
        Where-Object { $_.Environment -eq $Environment } |
        Select-Object -First 1
    if ($null -eq $record) {
        throw "Environment '$Environment' was not found in the encrypted lab vault."
    }
    return $record
}

function Get-ReleaseGitSha {
    $repositorySha = (& git -C $resolvedSourceRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $repositorySha -notmatch '^[A-Fa-f0-9]{40}$') {
        throw "Unable to resolve the source Git SHA at '$resolvedSourceRoot'."
    }
    if ([string]::IsNullOrWhiteSpace($GitSha)) {
        return $repositorySha.ToLowerInvariant()
    }
    if ($GitSha -notmatch '^[A-Fa-f0-9]{40}$') {
        throw "GitSha must be a complete 40-character commit SHA."
    }
    if (-not $AllowPromotedGitSha -and
        -not $repositorySha.Equals($GitSha, [StringComparison]::OrdinalIgnoreCase)) {
        throw "GitSha '$GitSha' does not match checked-out commit '$repositorySha'."
    }
    return $GitSha.ToLowerInvariant()
}

$usePipelineSecrets = Test-PipelineSecretSource

function Get-DbRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Account,
        [string]$PasswordEnvironmentVariable = ""
    )

    if ($script:usePipelineSecrets) {
        return New-PipelineCredentialRecord `
            -Account $Account `
            -PasswordEnvironmentVariable $PasswordEnvironmentVariable
    }

    if (-not (Test-Path -LiteralPath $DbVaultPath -PathType Leaf)) {
        throw "Encrypted SQL account note was not found at '$DbVaultPath'."
    }
    $record = Import-Clixml -LiteralPath $DbVaultPath |
        Where-Object { $_.Account -eq $Account } |
        Select-Object -First 1
    if ($null -eq $record) {
        throw "SQL account '$Account' was not found in the encrypted note."
    }
    return $record
}

function New-SqlConnectionString {
    param(
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)]$Record,
        [Parameter(Mandatory = $true)][int]$Port
    )

    $builder = [Data.SqlClient.SqlConnectionStringBuilder]::new()
    # Windows PowerShell 5.1 exposes the strongly typed properties but routes
    # several setters through unsupported keyword names. Canonical SQL keys
    # work consistently in both Windows PowerShell and PowerShell 7.
    $builder["Data Source"] = "$SqlHost,$Port"
    $builder["Initial Catalog"] = $Database
    $builder["User ID"] = $Record.Account
    $builder["Password"] = $Record.Credential.GetNetworkCredential().Password
    $builder.Encrypt = $true
    $builder.TrustServerCertificate = $true
    $builder.MultipleActiveResultSets = $true
    return $builder.ConnectionString
}

function Write-RawEnvFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][hashtable]$Values
    )

    $lines = foreach ($key in $Values.Keys) {
        $value = [string]$Values[$key]
        if ($value.Contains("`r") -or $value.Contains("`n")) {
            throw "Environment value '$key' contains a newline."
        }
        "$key=$value"
    }
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

function Get-ImageDigestOrPlaceholder {
    param([Parameter(Mandatory = $true)][string]$Image)

    $ErrorActionPreference = "Continue"
    $output = & docker image inspect --format "{{.Id}}" $Image 2>&1
    if ($LASTEXITCODE -eq 0 -and $output -match "^sha256:[0-9a-f]{64}$") {
        return $output.Trim()
    }
    return "sha256:" + ("0" * 64)
}

function Set-ComposeEnvironment {
    param(
        [Parameter(Mandatory = $true)]$Settings,
        [Parameter(Mandatory = $true)][string]$TemporaryRoot,
        [Parameter(Mandatory = $true)][int]$Port
    )

    $dbMigrator = Get-DbRecord `
        -Account $Settings.MigratorAccount `
        -PasswordEnvironmentVariable "CP6_DB_MIGRATOR_PASSWORD"
    $dbRuntime = Get-DbRecord `
        -Account $Settings.RuntimeAccount `
        -PasswordEnvironmentVariable "CP6_DB_RUNTIME_PASSWORD"
    $labRecord = Get-LabRecord -Settings $Settings

    $migrationPath = Join-Path $TemporaryRoot "migration.env"
    $runtimePath = Join-Path $TemporaryRoot "runtime.env"
    $infraPath = Join-Path $TemporaryRoot "infrastructure.env"
    $rabbitPassword = Get-PlainSecret -SecureString $labRecord.RabbitMqPassword

    Write-RawEnvFile -Path $migrationPath -Values @{
        ConnectionStrings__DefaultConnection = New-SqlConnectionString `
            -Database $Settings.Database -Record $dbMigrator -Port $Port
    }
    Write-RawEnvFile -Path $runtimePath -Values @{
        ConnectionStrings__DefaultConnection = New-SqlConnectionString `
            -Database $Settings.Database -Record $dbRuntime -Port $Port
        RabbitMQ__UserName = $labRecord.RabbitMqUser
        RabbitMQ__Password = $rabbitPassword
        JWT__Secret = Get-PlainSecret -SecureString $labRecord.JwtSecret
    }
    Write-RawEnvFile -Path $infraPath -Values @{
        RABBITMQ_DEFAULT_USER = $labRecord.RabbitMqUser
        RABBITMQ_DEFAULT_PASS = $rabbitPassword
    }

    $releaseGitSha = Get-ReleaseGitSha
    $env:CP6_INFRA_ENV_FILE = $infraPath
    $env:CP6_MIGRATION_ENV_FILE = $migrationPath
    $env:CP6_RUNTIME_ENV_FILE = $runtimePath
    $env:CP6_API_IMAGE = $ApiImage
    $env:CP6_WEB_IMAGE = $WebImage
    $env:CP6_ASPNETCORE_ENVIRONMENT = $Settings.AspNetEnvironment
    $env:CP6_API_PORT = [string]$Settings.ApiPort
    $env:CP6_WEB_PORT = [string]$Settings.WebPort
    $env:CP6_RABBITMQ_MANAGEMENT_PORT = [string]$Settings.RabbitManagementPort
    $env:CP6_RABBITMQ_VOLUME_NAME = if ($script:usePipelineSecrets) {
        "$($Settings.ProjectName)_rabbitmq-data-azure"
    }
    else {
        "$($Settings.ProjectName)_rabbitmq-data"
    }
    $env:CP6_KAFKA_TOPIC = "cp6.$Environment.operlog"
    $env:CP6_KAFKA_CONSUMER_GROUP = "cp6-$Environment-operlog-consumer"
    $env:CP6_RELEASE_VERSION = $ReleaseVersion
    $env:CP6_GIT_SHA = $releaseGitSha
    $env:CP6_API_DIGEST = Get-ImageDigestOrPlaceholder -Image $ApiImage
    $env:CP6_WEB_DIGEST = Get-ImageDigestOrPlaceholder -Image $WebImage
}

function Invoke-Compose {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    & docker compose -f $composePath -p $settings.ProjectName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose failed with exit code $LASTEXITCODE."
    }
}

function Assert-ComposeServiceImage {
    param(
        [Parameter(Mandatory = $true)][string]$Service,
        [Parameter(Mandatory = $true)][string]$ExpectedImage
    )

    $expectedImageId = @(& docker image inspect --format "{{.Id}}" $ExpectedImage 2>$null) |
        Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or $expectedImageId -notmatch '^sha256:[0-9a-f]{64}$') {
        throw "Unable to resolve the immutable image ID for '$ExpectedImage'."
    }

    $containerId = @(& docker compose `
        -f $composePath `
        -p $settings.ProjectName `
        ps -q $Service 2>$null) | Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
        throw "Compose service '$Service' does not have a running container."
    }

    $runningImageId = @(& docker inspect --format "{{.Image}}" $containerId 2>$null) |
        Select-Object -First 1
    if ($LASTEXITCODE -ne 0 -or
        -not ([string]$runningImageId).Equals(
            [string]$expectedImageId,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Compose service '$Service' is not running the promoted immutable image."
    }
}

function Assert-DockerDaemon {
    & docker info --format "{{.ServerVersion}}" 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Desktop Linux engine is not running. Start Docker Desktop and retry."
    }
}

$settings = $environmentSettings[$Environment]

if ($Action -eq "Initialize") {
    if (-not $usePipelineSecrets) {
        Initialize-LabVault
    }
    $resolvedPort = Get-SqlPort
    Get-DbRecord `
        -Account $settings.MigratorAccount `
        -PasswordEnvironmentVariable "CP6_DB_MIGRATOR_PASSWORD" |
        Out-Null
    Get-DbRecord `
        -Account $settings.RuntimeAccount `
        -PasswordEnvironmentVariable "CP6_DB_RUNTIME_PASSWORD" |
        Out-Null
    [pscustomobject]@{
        Environment = $Environment
        ProjectName = $settings.ProjectName
        Database = $settings.Database
        SqlEndpoint = "$SqlHost,$resolvedPort"
        ApiUrl = "http://127.0.0.1:$($settings.ApiPort)"
        WebUrl = "http://127.0.0.1:$($settings.WebPort)"
        Secrets = if ($usePipelineSecrets) { "Process environment" } else { "DPAPI encrypted" }
    }
    exit 0
}

if ($Action -eq "Build") {
    Assert-DockerDaemon
    $releaseGitSha = Get-ReleaseGitSha
    $useRuntimeArtifact = -not [string]::IsNullOrWhiteSpace($RuntimeArtifactRoot)
    $runtimeBuildRoot = Join-Path `
        ([IO.Path]::GetTempPath()) `
        "cp6-runtime-build-$([Guid]::NewGuid().ToString('N'))"
    $apiRuntimeContext = Join-Path $runtimeBuildRoot "api"
    $apiPublishRoot = Join-Path $apiRuntimeContext "publish"
    $webRuntimeContext = Join-Path $runtimeBuildRoot "web"
    $webDistPayloadRoot = Join-Path $webRuntimeContext "dist"
    $apiProjectPath = Join-Path $resolvedSourceRoot "CP6.WebApi\CP6.WebApi.csproj"
    $webSourceRoot = Join-Path $resolvedSourceRoot "cp6.web"
    $webSourceDist = Join-Path $webSourceRoot "dist"
    $apiRuntimeDockerfile = Join-Path `
        $resolvedSourceRoot `
        "deploy\lab\images\api-runtime.Dockerfile"
    $webRuntimeDockerfile = Join-Path `
        $resolvedSourceRoot `
        "deploy\lab\images\web-runtime.Dockerfile"
    $previousReleaseVersion = [Environment]::GetEnvironmentVariable(
        "CP6_RELEASE_VERSION",
        "Process")
    $previousGitSha = [Environment]::GetEnvironmentVariable("CP6_GIT_SHA", "Process")
    $previousNodeOptions = [Environment]::GetEnvironmentVariable("NODE_OPTIONS", "Process")
    $previousNpmJobs = [Environment]::GetEnvironmentVariable("npm_config_jobs", "Process")

    try {
        [IO.Directory]::CreateDirectory($apiPublishRoot) | Out-Null
        [IO.Directory]::CreateDirectory($webDistPayloadRoot) | Out-Null
        if ($useRuntimeArtifact) {
            $resolvedRuntimeArtifactRoot = [IO.Path]::GetFullPath($RuntimeArtifactRoot)
            & (Join-Path $repoRoot "scripts\Test-Cp6DevRuntimeArtifact.ps1") `
                -ArtifactRoot $resolvedRuntimeArtifactRoot `
                -ExpectedReleaseVersion $ReleaseVersion `
                -ExpectedGitSha $releaseGitSha |
                Out-Null
            Get-ChildItem `
                -LiteralPath (Join-Path $resolvedRuntimeArtifactRoot "api\publish") `
                -Force |
                Copy-Item -Destination $apiPublishRoot -Recurse -Force
            Get-ChildItem `
                -LiteralPath (Join-Path $resolvedRuntimeArtifactRoot "web\dist") `
                -Force |
                Copy-Item -Destination $webDistPayloadRoot -Recurse -Force
            Copy-Item `
                -LiteralPath (Join-Path $resolvedRuntimeArtifactRoot "web\nginx.conf") `
                -Destination (Join-Path $webRuntimeContext "nginx.conf") `
                -Force
            Write-Host "Packaging the verified runtime artifact from the selected CI run."
        }
        else {
            & dotnet restore `
                $apiProjectPath `
                --disable-build-servers `
                --disable-parallel
            if ($LASTEXITCODE -ne 0) { throw "API host restore failed." }
            & dotnet publish `
                $apiProjectPath `
                -c Release `
                -o $apiPublishRoot `
                --no-restore `
                --disable-build-servers `
                -m:1 `
                -p:BuildInParallel=false `
                -p:UseSharedCompilation=false `
                "-p:Version=$ReleaseVersion" `
                "-p:InformationalVersion=$ReleaseVersion+$releaseGitSha"
            if ($LASTEXITCODE -ne 0) { throw "API host publish failed." }

            [Environment]::SetEnvironmentVariable(
                "CP6_RELEASE_VERSION",
                $ReleaseVersion,
                "Process")
            [Environment]::SetEnvironmentVariable("CP6_GIT_SHA", $releaseGitSha, "Process")
            [Environment]::SetEnvironmentVariable(
                "NODE_OPTIONS",
                "--max-old-space-size=768",
                "Process")
            [Environment]::SetEnvironmentVariable("npm_config_jobs", "1", "Process")
            Push-Location $webSourceRoot
            try {
                & npm.cmd ci --no-audit --no-fund
                if ($LASTEXITCODE -ne 0) { throw "Web host dependency restore failed." }
                & npm.cmd run build-only
                if ($LASTEXITCODE -ne 0) { throw "Web host build failed." }
            }
            finally {
                Pop-Location
            }

            Get-ChildItem -LiteralPath $webSourceDist -Force |
                Copy-Item -Destination $webDistPayloadRoot -Recurse -Force
            Copy-Item `
                -LiteralPath (Join-Path $webSourceRoot "nginx.conf") `
                -Destination (Join-Path $webRuntimeContext "nginx.conf") `
                -Force
        }

        $apiBuildArguments = @(
            "build",
            "-f", $apiRuntimeDockerfile,
            "-t", $ApiImage
        )
        if (-not [string]::IsNullOrWhiteSpace($ApiImageIdFile)) {
            $apiBuildArguments += @("--iidfile", [IO.Path]::GetFullPath($ApiImageIdFile))
        }
        $apiBuildArguments += $apiRuntimeContext
        & docker @apiBuildArguments
        if ($LASTEXITCODE -ne 0) { throw "API runtime image packaging failed." }

        $webBuildArguments = @(
            "build",
            "-f", $webRuntimeDockerfile,
            "-t", $WebImage
        )
        if (-not [string]::IsNullOrWhiteSpace($WebImageIdFile)) {
            $webBuildArguments += @("--iidfile", [IO.Path]::GetFullPath($WebImageIdFile))
        }
        $webBuildArguments += $webRuntimeContext
        & docker @webBuildArguments
        if ($LASTEXITCODE -ne 0) { throw "Web runtime image packaging failed." }
        Write-Host "Built '$ApiImage' and '$WebImage' from $releaseGitSha."
        exit 0
    }
    finally {
        [Environment]::SetEnvironmentVariable(
            "CP6_RELEASE_VERSION",
            $previousReleaseVersion,
            "Process")
        [Environment]::SetEnvironmentVariable("CP6_GIT_SHA", $previousGitSha, "Process")
        [Environment]::SetEnvironmentVariable("NODE_OPTIONS", $previousNodeOptions, "Process")
        [Environment]::SetEnvironmentVariable("npm_config_jobs", $previousNpmJobs, "Process")

        $resolvedRuntimeBuildRoot = [IO.Path]::GetFullPath($runtimeBuildRoot)
        $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        $runtimeBuildLeaf = Split-Path -Leaf $resolvedRuntimeBuildRoot
        if ($resolvedRuntimeBuildRoot.StartsWith(
            $resolvedSystemTemp,
            [StringComparison]::OrdinalIgnoreCase
        ) -and $runtimeBuildLeaf.StartsWith(
            "cp6-runtime-build-",
            [StringComparison]::OrdinalIgnoreCase
        ) -and (Test-Path -LiteralPath $resolvedRuntimeBuildRoot)) {
            Remove-Item -LiteralPath $resolvedRuntimeBuildRoot -Recurse -Force
        }
    }
}

if (-not $usePipelineSecrets) {
    Initialize-LabVault
}
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-lab-$([Guid]::NewGuid().ToString('N'))"
$deploymentMutex = $null
$deploymentMutexAcquired = $false

try {
    if ($Action -eq "Deploy") {
        $mutexName = "Global\CP6_$($settings.ProjectName)_deploy"
        $deploymentMutex = [Threading.Mutex]::new($false, $mutexName)
        try {
            $deploymentMutexAcquired = $deploymentMutex.WaitOne(0)
        }
        catch [Threading.AbandonedMutexException] {
            $deploymentMutexAcquired = $true
        }
        if (-not $deploymentMutexAcquired) {
            throw "Another '$($settings.ProjectName)' deployment already holds the host lock."
        }
    }

    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $resolvedPort = Get-SqlPort
    Set-ComposeEnvironment -Settings $settings -TemporaryRoot $temporaryRoot -Port $resolvedPort

    switch ($Action) {
        "Config" {
            Invoke-Compose -Arguments @("config", "--quiet")
            Write-Host "Compose contract is valid for '$Environment'."
        }
        "Deploy" {
            Assert-DockerDaemon
            Invoke-Compose -Arguments @("up", "-d", "--wait", "--wait-timeout", "240", "redis", "rabbitmq", "kafka")
            # Keep the schema transition inside an explicit maintenance window.
            # Never run a forward-only migration while the old API/Web are serving traffic.
            Invoke-Compose -Arguments @("stop", "web", "api")
            Invoke-Compose -Arguments @("--profile", "migration", "run", "--rm", "db-init")
            Invoke-Compose -Arguments @("up", "-d", "--wait", "--wait-timeout", "240", "api")
            Assert-ComposeServiceImage -Service "api" -ExpectedImage $ApiImage

            $live = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.ApiPort)/health/live"
            $ready = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.ApiPort)/health/ready"
            $release = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.ApiPort)/health/release"
            if ($live.status -ne "Healthy" -or $ready.status -ne "Healthy") {
                throw "API did not become healthy after the database migration."
            }
            if ($release.version -ne $ReleaseVersion -or
                $release.gitSha -ne $env:CP6_GIT_SHA) {
                throw "API release identity does not match the promoted candidate."
            }

            Invoke-Compose -Arguments @("up", "-d", "--wait", "--wait-timeout", "240", "web")
            Assert-ComposeServiceImage -Service "web" -ExpectedImage $WebImage
            $webRelease = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.WebPort)/release.json"
            if ($webRelease.version -ne $ReleaseVersion -or
                $webRelease.gitSha -ne $env:CP6_GIT_SHA) {
                throw "Web release identity does not match the promoted candidate."
            }
            [pscustomobject]@{
                Environment = $Environment
                Live = $live.status
                Ready = $ready.status
                ApiVersion = $release.version
                ApiGitSha = $release.gitSha
                WebVersion = $webRelease.version
                WebGitSha = $webRelease.gitSha
                WebUrl = "http://127.0.0.1:$($settings.WebPort)"
            }
        }
        "Status" { Invoke-Compose -Arguments @("ps") }
        "Stop" { Invoke-Compose -Arguments @("stop") }
        "Logs" { Invoke-Compose -Arguments @("logs", "--tail", "200") }
    }
}
finally {
    try {
        $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
        $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
        if ($resolvedTemporaryRoot.StartsWith(
            $resolvedSystemTemp,
            [StringComparison]::OrdinalIgnoreCase
        ) -and (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
    }
    finally {
        if ($deploymentMutexAcquired) {
            $deploymentMutex.ReleaseMutex()
        }
        if ($null -ne $deploymentMutex) {
            $deploymentMutex.Dispose()
        }
    }
}
