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
$composePath = Join-Path $repoRoot "deploy\lab\compose\compose.yaml"

$environmentSettings = @{
    "dev" = [ordered]@{
        ProjectName = "cp6-dev"
        Database = "CP6_DEV"
        MigratorAccount = "cp6_dev_migrator"
        RuntimeAccount = "cp6_dev_runtime"
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
        AspNetEnvironment = "ProductionLab"
        ApiPort = 39991
        WebPort = 38080
        RabbitManagementPort = 36072
    }
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

function Get-DbRecord {
    param([Parameter(Mandatory = $true)][string]$Account)

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

    $dbMigrator = Get-DbRecord -Account $Settings.MigratorAccount
    $dbRuntime = Get-DbRecord -Account $Settings.RuntimeAccount
    $labRecord = Import-Clixml -LiteralPath $LabVaultPath |
        Where-Object { $_.Environment -eq $Environment } |
        Select-Object -First 1
    if ($null -eq $labRecord) {
        throw "Environment '$Environment' was not found in the encrypted lab vault."
    }

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

    $gitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
    $env:CP6_INFRA_ENV_FILE = $infraPath
    $env:CP6_MIGRATION_ENV_FILE = $migrationPath
    $env:CP6_RUNTIME_ENV_FILE = $runtimePath
    $env:CP6_API_IMAGE = $ApiImage
    $env:CP6_WEB_IMAGE = $WebImage
    $env:CP6_ASPNETCORE_ENVIRONMENT = $Settings.AspNetEnvironment
    $env:CP6_API_PORT = [string]$Settings.ApiPort
    $env:CP6_WEB_PORT = [string]$Settings.WebPort
    $env:CP6_RABBITMQ_MANAGEMENT_PORT = [string]$Settings.RabbitManagementPort
    $env:CP6_KAFKA_TOPIC = "cp6.$Environment.operlog"
    $env:CP6_KAFKA_CONSUMER_GROUP = "cp6-$Environment-operlog-consumer"
    $env:CP6_RELEASE_VERSION = "0.0.0-lab"
    $env:CP6_GIT_SHA = $gitSha
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

function Assert-DockerDaemon {
    & docker info --format "{{.ServerVersion}}" 1>$null 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Docker Desktop Linux engine is not running. Start Docker Desktop and retry."
    }
}

$settings = $environmentSettings[$Environment]

if ($Action -eq "Initialize") {
    Initialize-LabVault
    $resolvedPort = Get-SqlPort
    Get-DbRecord -Account $settings.MigratorAccount | Out-Null
    Get-DbRecord -Account $settings.RuntimeAccount | Out-Null
    [pscustomobject]@{
        Environment = $Environment
        ProjectName = $settings.ProjectName
        Database = $settings.Database
        SqlEndpoint = "$SqlHost,$resolvedPort"
        ApiUrl = "http://127.0.0.1:$($settings.ApiPort)"
        WebUrl = "http://127.0.0.1:$($settings.WebPort)"
        Secrets = "DPAPI encrypted"
    }
    exit 0
}

if ($Action -eq "Build") {
    Assert-DockerDaemon
    $gitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
    & docker build -f (Join-Path $repoRoot "CP6.WebApi\Dockerfile") `
        --build-arg "RELEASE_VERSION=0.0.0-lab" `
        --build-arg "GIT_SHA=$gitSha" `
        -t $ApiImage $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "API image build failed." }
    & docker build -f (Join-Path $repoRoot "cp6.web\Dockerfile") `
        --build-arg "RELEASE_VERSION=0.0.0-lab" `
        --build-arg "GIT_SHA=$gitSha" `
        -t $WebImage $repoRoot
    if ($LASTEXITCODE -ne 0) { throw "Web image build failed." }
    Write-Host "Built '$ApiImage' and '$WebImage' from $gitSha."
    exit 0
}

Initialize-LabVault
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-lab-$([Guid]::NewGuid().ToString('N'))"

try {
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
            Invoke-Compose -Arguments @("--profile", "migration", "run", "--rm", "db-init")
            Invoke-Compose -Arguments @("up", "-d", "--wait", "--wait-timeout", "240", "api", "web")

            $live = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.ApiPort)/health/live"
            $ready = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.ApiPort)/health/ready"
            $release = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.ApiPort)/health/release"
            $webRelease = Invoke-RestMethod -Uri "http://127.0.0.1:$($settings.WebPort)/release.json"
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
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith(
        $resolvedSystemTemp,
        [StringComparison]::OrdinalIgnoreCase
    ) -and (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
