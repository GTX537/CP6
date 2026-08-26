[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-lab-contract-$([Guid]::NewGuid().ToString('N'))"
$pipelineSecretNames = @(
    "CP6_DB_MIGRATOR_PASSWORD",
    "CP6_DB_RUNTIME_PASSWORD",
    "CP6_RABBITMQ_PASSWORD",
    "CP6_JWT_SECRET"
)
$originalPipelineSecrets = @{}
foreach ($name in $pipelineSecretNames) {
    $originalPipelineSecrets[$name] = [Environment]::GetEnvironmentVariable($name, "Process")
    [Environment]::SetEnvironmentVariable($name, $null, "Process")
}

function New-FakeCredentialRecord {
    param(
        [Parameter(Mandatory = $true)][string]$Account,
        [Parameter(Mandatory = $true)][string]$Database,
        [Parameter(Mandatory = $true)][string]$Role
    )

    [pscustomobject]@{
        Account = $Account
        Database = $Database
        Role = $Role
        Credential = [PSCredential]::new(
            $Account,
            (ConvertTo-SecureString "Contract-Test-Password-42!" -AsPlainText -Force))
    }
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $dbVaultPath = Join-Path $temporaryRoot "db.dpapi.clixml"
    $labVaultPath = Join-Path $temporaryRoot "lab.dpapi.clixml"

    @(
        New-FakeCredentialRecord -Account "cp6_dev_migrator" -Database "CP6_DEV" -Role "migrator"
        New-FakeCredentialRecord -Account "cp6_dev_runtime" -Database "CP6_DEV" -Role "runtime"
        New-FakeCredentialRecord -Account "cp6_uat_migrator" -Database "CP6_UAT" -Role "migrator"
        New-FakeCredentialRecord -Account "cp6_uat_runtime" -Database "CP6_UAT" -Role "runtime"
        New-FakeCredentialRecord -Account "cp6_prod_lab_migrator" -Database "CP6_PROD_LAB" -Role "migrator"
        New-FakeCredentialRecord -Account "cp6_prod_lab_runtime" -Database "CP6_PROD_LAB" -Role "runtime"
    ) | Export-Clixml -LiteralPath $dbVaultPath

    @(
        foreach ($name in @("dev", "uat", "prod-lab")) {
            [pscustomobject]@{
                Environment = $name
                RabbitMqUser = "cp6_$($name.Replace('-', '_'))"
                RabbitMqPassword = ConvertTo-SecureString "Contract-Rabbit-Password-42!" -AsPlainText -Force
                JwtSecret = ConvertTo-SecureString ("J" * 64) -AsPlainText -Force
            }
        }
    ) | Export-Clixml -LiteralPath $labVaultPath

    $expected = @{
        "dev" = @{ Project = "cp6-dev"; Api = "19991"; Web = "18080"; Rabbit = "16072" }
        "uat" = @{ Project = "cp6-uat"; Api = "29991"; Web = "28080"; Rabbit = "26072" }
        "prod-lab" = @{ Project = "cp6-prod-lab"; Api = "39991"; Web = "38080"; Rabbit = "36072" }
    }

    foreach ($name in @("dev", "uat", "prod-lab")) {
        $output = & (Join-Path $repoRoot "scripts\Invoke-Cp6LabEnvironment.ps1") `
            -Environment $name `
            -Action Config `
            -SqlPort 1433 `
            -DbVaultPath $dbVaultPath `
            -LabVaultPath $labVaultPath 2>&1
        if ($LASTEXITCODE -ne 0) {
            throw "Compose config failed for '$name': $($output | Out-String)"
        }
        if ($env:CP6_API_PORT -ne $expected[$name].Api -or
            $env:CP6_WEB_PORT -ne $expected[$name].Web -or
            $env:CP6_RABBITMQ_MANAGEMENT_PORT -ne $expected[$name].Rabbit) {
            throw "Port mapping is incorrect for '$name'."
        }
        if ($env:CP6_RABBITMQ_VOLUME_NAME -ne "$($expected[$name].Project)_rabbitmq-data") {
            throw "DPAPI mode RabbitMQ volume is incorrect for '$name'."
        }
    }

    foreach ($name in $pipelineSecretNames) {
        [Environment]::SetEnvironmentVariable($name, $null, "Process")
    }
    $partialProbeValue = "synthetic-$([Guid]::NewGuid().ToString('N'))"
    [Environment]::SetEnvironmentVariable(
        "CP6_DB_MIGRATOR_PASSWORD",
        $partialProbeValue,
        "Process")
    $partialSetRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\Invoke-Cp6LabEnvironment.ps1") `
            -Environment dev `
            -Action Config `
            -SqlPort 1433 `
            -DbVaultPath (Join-Path $temporaryRoot "missing-db.clixml") `
            -LabVaultPath (Join-Path $temporaryRoot "missing-lab.clixml") 2>&1 |
            Out-Null
    }
    catch {
        $partialSetRejected = $true
        if ($_ | Out-String | Select-String -SimpleMatch $partialProbeValue -Quiet) {
            throw "Incomplete Pipeline Secret failure exposed a Secret value."
        }
    }
    if (-not $partialSetRejected) {
        throw "An incomplete Pipeline Secret set must be rejected."
    }

    foreach ($name in $pipelineSecretNames) {
        [Environment]::SetEnvironmentVariable(
            $name,
            "synthetic-$([Guid]::NewGuid().ToString('N'))",
            "Process")
    }
    $gitSha = (& git -C $repoRoot rev-parse HEAD).Trim()
    $pipelineOutput = & (Join-Path $repoRoot "scripts\Invoke-Cp6LabEnvironment.ps1") `
        -Environment dev `
        -Action Config `
        -SqlPort 1433 `
        -DbVaultPath (Join-Path $temporaryRoot "missing-db.clixml") `
        -LabVaultPath (Join-Path $temporaryRoot "missing-lab.clixml") `
        -ReleaseVersion "0.0.0-dev.42" `
        -GitSha $gitSha 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Pipeline Secret mode Compose config failed: $($pipelineOutput | Out-String)"
    }
    if ($env:CP6_RABBITMQ_VOLUME_NAME -ne "cp6-dev_rabbitmq-data-azure") {
        throw "Pipeline Secret mode must use the isolated Azure RabbitMQ volume."
    }
    if ($env:CP6_RELEASE_VERSION -ne "0.0.0-dev.42" -or $env:CP6_GIT_SHA -ne $gitSha) {
        throw "Pipeline release identity was not preserved."
    }

    $mismatchedSha = if ($gitSha -eq ("0" * 40)) { "1" * 40 } else { "0" * 40 }
    $mismatchedShaRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\Invoke-Cp6LabEnvironment.ps1") `
            -Environment dev `
            -Action Config `
            -SqlPort 1433 `
            -DbVaultPath (Join-Path $temporaryRoot "missing-db.clixml") `
            -LabVaultPath (Join-Path $temporaryRoot "missing-lab.clixml") `
            -ReleaseVersion "0.0.0-dev.42" `
            -GitSha $mismatchedSha 2>&1 |
            Out-Null
    }
    catch {
        $mismatchedShaRejected = $true
    }
    if (-not $mismatchedShaRejected) {
        throw "A release Git SHA that differs from checkout must be rejected."
    }

    $composeText = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "deploy\lab\compose\compose.yaml"),
        [Text.Encoding]::UTF8)
    if ($composeText -notmatch "CP6_MIGRATION_ENV_FILE" -or
        $composeText -notmatch "CP6_RUNTIME_ENV_FILE" -or
        $composeText -notmatch "CP6_INFRA_ENV_FILE") {
        throw "Compose does not keep migration, runtime, and infrastructure secrets separate."
    }
    if ($composeText -match "cp6_(dev|uat|prod_lab)_(migrator|runtime)" -or
        $composeText -match "Password=") {
        throw "Compose contains an environment-specific account or plaintext password."
    }

    $apiProject = [xml][IO.File]::ReadAllText(
        (Join-Path $repoRoot "CP6.WebApi\CP6.WebApi.csproj"),
        [Text.Encoding]::UTF8)
    $apiDockerfile = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "CP6.WebApi\Dockerfile"),
        [Text.Encoding]::UTF8)
    $projectReferences = @($apiProject.Project.ItemGroup.ProjectReference.Include)
    foreach ($projectReference in $projectReferences) {
        $projectPath = $projectReference.Replace("..\", "").Replace("\", "/")
        if ($apiDockerfile -notmatch [regex]::Escape("COPY $projectPath")) {
            throw "API Dockerfile restore layer omits '$projectPath'."
        }
    }

    $labScript = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "scripts\Invoke-Cp6LabEnvironment.ps1"),
        [Text.Encoding]::UTF8)
    if ($labScript -notmatch '\[string\]\$ReleaseVersion = "0\.0\.0-lab"') {
        throw "Lab script does not expose a SemVer-compatible release version parameter."
    }
    if ($labScript -notmatch '\$env:CP6_RELEASE_VERSION = \$ReleaseVersion') {
        throw "Lab environments do not promote the requested release version."
    }
    if ($labScript -notmatch 'CP6_DB_MIGRATOR_PASSWORD' -or
        $labScript -notmatch 'CP6_DB_RUNTIME_PASSWORD' -or
        $labScript -notmatch 'CP6_RABBITMQ_PASSWORD' -or
        $labScript -notmatch 'CP6_JWT_SECRET') {
        throw "Lab script does not support the complete Pipeline Secret contract."
    }
    if ($labScript -notmatch '\[string\]\$SourceRoot = ""' -or
        $labScript -notmatch '\[string\]\$RuntimeArtifactRoot = ""' -or
        $labScript -notmatch 'Test-Cp6DevRuntimeArtifact\.ps1' -or
        $labScript -notmatch 'Packaging the verified runtime artifact from the selected CI run' -or
        $labScript -notmatch '(?s)& dotnet restore.*?--disable-build-servers.*?--disable-parallel' -or
        $labScript -notmatch '--disable-build-servers' -or
        $labScript -notmatch '-m:1' -or
        $labScript -notmatch '-p:BuildInParallel=false' -or
        $labScript -notmatch '-p:UseSharedCompilation=false' -or
        $labScript -notmatch '& npm\.cmd ci --no-audit --no-fund' -or
        $labScript -notmatch '& npm\.cmd run build-only' -or
        $labScript -notmatch '--max-old-space-size=768' -or
        $labScript -notmatch '\$apiBuildArguments \+= \$apiRuntimeContext' -or
        $labScript -notmatch '\$webBuildArguments \+= \$webRuntimeContext' -or
        $labScript -notmatch '\$apiBuildArguments \+= @\("--iidfile"' -or
        $labScript -notmatch '\$webBuildArguments \+= @\("--iidfile"') {
        throw "Lab image build does not prebuild selected-source artifacts outside Docker."
    }
    if ($labScript -notmatch 'cp6-runtime-build-' -or
        $labScript -notmatch '\$resolvedRuntimeBuildRoot\.StartsWith\(' -or
        $labScript -notmatch 'Remove-Item -LiteralPath \$resolvedRuntimeBuildRoot -Recurse -Force') {
        throw "Lab runtime packaging does not safely clean its dedicated temporary context."
    }
    if ($labScript -notmatch 'Global\\CP6_\$\(\$settings\.ProjectName\)_deploy' -or
        $labScript -notmatch '\$deploymentMutex\.WaitOne\(0\)' -or
        $labScript -notmatch '\$deploymentMutex\.ReleaseMutex\(\)') {
        throw "Lab deployment does not enforce a host-wide deployment mutex."
    }
    $imageAssertionDefinitionIndex = $labScript.IndexOf('function Assert-ComposeServiceImage')
    $apiImageAssertionIndex = $labScript.IndexOf('Assert-ComposeServiceImage -Service "api"')
    $webImageAssertionIndex = $labScript.IndexOf('Assert-ComposeServiceImage -Service "web"')
    if ($imageAssertionDefinitionIndex -lt 0 -or
        $apiImageAssertionIndex -le $imageAssertionDefinitionIndex -or
        $webImageAssertionIndex -le $apiImageAssertionIndex) {
        throw "Lab deployment does not verify the immutable image used by each running container."
    }
    $stopIndex = $labScript.IndexOf('@("stop", "web", "api")')
    $migrationIndex = $labScript.IndexOf('@("--profile", "migration", "run", "--rm", "db-init")')
    $apiStartIndex = $labScript.IndexOf('@("up", "-d", "--wait", "--wait-timeout", "240", "api")')
    $webStartIndex = $labScript.IndexOf('@("up", "-d", "--wait", "--wait-timeout", "240", "web")')
    if ($stopIndex -lt 0 -or
        $migrationIndex -le $stopIndex -or
        $apiStartIndex -le $migrationIndex -or
        $webStartIndex -le $apiStartIndex) {
        throw "Lab deployment does not enforce stop, migrate, API verify, then Web start order."
    }

    $webDockerfile = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "cp6.web\Dockerfile"),
        [Text.Encoding]::UTF8)
    if ($webDockerfile -notmatch 'COPY sdk/typescript/space-design-v1/' -or
        $webDockerfile -notmatch 'WORKDIR /src/cp6\.web') {
        throw "Web Dockerfile does not preserve the repository-level SDK layout."
    }

    $apiRuntimeDockerfile = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "deploy\lab\images\api-runtime.Dockerfile"),
        [Text.Encoding]::UTF8)
    $webRuntimeDockerfile = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "deploy\lab\images\web-runtime.Dockerfile"),
        [Text.Encoding]::UTF8)
    if ($apiRuntimeDockerfile -match 'dotnet/sdk' -or
        $apiRuntimeDockerfile -notmatch 'COPY publish/' -or
        $webRuntimeDockerfile -match 'FROM node:' -or
        $webRuntimeDockerfile -notmatch 'COPY dist/') {
        throw "Lab runtime Dockerfiles must package prebuilt payloads without SDK or Node builds."
    }

    $r2Workflow = [IO.File]::ReadAllText(
        (Join-Path $repoRoot ".github\workflows\r2-candidate.yml"),
        [Text.Encoding]::UTF8)
    if ($r2Workflow -notmatch 'docker buildx build \.\s+\\\s+--file cp6\.web/Dockerfile') {
        throw "R2 candidate workflow still builds Web from the obsolete narrow context."
    }

    Write-Host "CP6 lab environment contract test passed for DEV, UAT, and PROD-LAB."
}
finally {
    foreach ($name in $pipelineSecretNames) {
        [Environment]::SetEnvironmentVariable(
            $name,
            $originalPipelineSecrets[$name],
            "Process")
    }
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith(
        $resolvedSystemTemp,
        [StringComparison]::OrdinalIgnoreCase
    ) -and (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
