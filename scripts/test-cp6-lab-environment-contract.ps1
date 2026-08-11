[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-lab-contract-$([Guid]::NewGuid().ToString('N'))"

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
    if ($labScript -notmatch 'RELEASE_VERSION=0\.0\.0-lab') {
        throw "Lab image build does not pass a SemVer-compatible release version."
    }
    if ($labScript -notmatch '\$env:CP6_RELEASE_VERSION = "0\.0\.0-lab"') {
        throw "Lab environments do not promote one shared release version."
    }
    if ($labScript -notmatch '-t \$WebImage \$repoRoot') {
        throw "Lab Web image build does not use the repository root context."
    }

    $webDockerfile = [IO.File]::ReadAllText(
        (Join-Path $repoRoot "cp6.web\Dockerfile"),
        [Text.Encoding]::UTF8)
    if ($webDockerfile -notmatch 'COPY sdk/typescript/space-design-v1/' -or
        $webDockerfile -notmatch 'WORKDIR /src/cp6\.web') {
        throw "Web Dockerfile does not preserve the repository-level SDK layout."
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
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith(
        $resolvedSystemTemp,
        [StringComparison]::OrdinalIgnoreCase
    ) -and (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
