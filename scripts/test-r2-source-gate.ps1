[CmdletBinding()]
param(
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$ExpectedVersion = "1.0.0",
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipDependencyAudit,
    [switch]$SkipModelCheck
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Assert-Value {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Actual -ne $Expected) {
        throw "$Description mismatch. Expected '$Expected', found '$Actual'."
    }
}

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$WorkingDirectory = $repoRoot,
        [switch]$CaptureOutput
    )

    Push-Location $WorkingDirectory
    try {
        if ($CaptureOutput) {
            $output = & $FilePath @ArgumentList 2>&1
        }
        else {
            & $FilePath @ArgumentList
        }
        if ($LASTEXITCODE -ne 0) {
            if ($CaptureOutput -and $output) {
                $message = ($output | Out-String).Trim()
                throw "$Description failed with exit code $LASTEXITCODE.`n$message"
            }
            throw "$Description failed with exit code $LASTEXITCODE."
        }
        if ($CaptureOutput) {
            return @($output)
        }
    }
    finally {
        Pop-Location
    }
}

function Read-Utf8Json {
    param([Parameter(Mandatory = $true)][string]$Path)

    $absolutePath = Join-Path $repoRoot $Path
    return [IO.File]::ReadAllText($absolutePath, [Text.Encoding]::UTF8) |
        ConvertFrom-Json
}

function Assert-NoVulnerableNuGetPackages {
    $projects = @(
        "CP6.WebApi\CP6.WebApi.csproj",
        "CP6.Tests\CP6.Tests.csproj",
        "CP6.Desktop\CP6.Desktop.csproj",
        "CP6.Client.Tests\CP6.Client.Tests.csproj"
    )

    foreach ($project in $projects) {
        $output = Invoke-CheckedCommand -FilePath "dotnet" `
            -ArgumentList @("list", $project, "package", "--vulnerable", "--include-transitive") `
            -Description "NuGet vulnerability audit for $project" `
            -CaptureOutput
        $text = ($output | Out-String)
        if ($text -match "has the following vulnerable packages") {
            throw "NuGet vulnerability audit found vulnerable packages in $project.`n$text"
        }
    }
}

$desktopProjectPath = Join-Path $repoRoot "CP6.Desktop\CP6.Desktop.csproj"
$mobileProjectPath = Join-Path $repoRoot "CP6.Mobile\CP6.Mobile.csproj"
$packageManifestPath = Join-Path $repoRoot "CP6.Desktop\Package.appxmanifest"

[xml]$desktopProject = [IO.File]::ReadAllText($desktopProjectPath, [Text.Encoding]::UTF8)
[xml]$mobileProject = [IO.File]::ReadAllText($mobileProjectPath, [Text.Encoding]::UTF8)
[xml]$packageManifest = [IO.File]::ReadAllText($packageManifestPath, [Text.Encoding]::UTF8)
$settings = Read-Utf8Json -Path "CP6.WebApi\appsettings.json"

Assert-Value -Actual ([string]$desktopProject.SelectSingleNode(
    "/Project/PropertyGroup/Version"
).InnerText) `
    -Expected $ExpectedVersion `
    -Description "Desktop application version"
Assert-Value -Actual ([string]$mobileProject.SelectSingleNode(
    "/Project/PropertyGroup/ApplicationDisplayVersion"
).InnerText) `
    -Expected $ExpectedVersion `
    -Description "Android display version"
Assert-Value -Actual ([string]$packageManifest.Package.Identity.Version) `
    -Expected "$ExpectedVersion.0" `
    -Description "MSIX package version"
Assert-Value -Actual ([string]$settings.Security.NativeClient.Windows.LatestVersion) `
    -Expected $ExpectedVersion `
    -Description "Windows bootstrap latest version"
Assert-Value -Actual ([string]$settings.Security.NativeClient.Android.LatestVersion) `
    -Expected $ExpectedVersion `
    -Description "Android bootstrap latest version"

$expected = [version]$ExpectedVersion
$windowsMinimum = [version]$settings.Security.NativeClient.Windows.MinimumVersion
$androidMinimum = [version]$settings.Security.NativeClient.Android.MinimumVersion
if ($windowsMinimum -gt $expected) {
    throw "Windows minimum version cannot exceed the latest version."
}
if ($androidMinimum -gt $expected) {
    throw "Android minimum version cannot exceed the latest version."
}
$applicationVersionNode = $mobileProject.SelectSingleNode(
    "/Project/PropertyGroup/ApplicationVersion"
)
if ($null -eq $applicationVersionNode -or [int64]$applicationVersionNode.InnerText -le 0) {
    throw "Android ApplicationVersion must be a positive monotonically increasing integer."
}

$runFullTrust = $packageManifest.Package.Capabilities.ChildNodes |
    Where-Object {
        $_.LocalName -eq "Capability" -and $_.GetAttribute("Name") -eq "runFullTrust"
    }
if (-not $runFullTrust) {
    throw "The Desktop package manifest must declare runFullTrust."
}
$desktopProtocol = $packageManifest.Package.Applications.Application.Extensions.ChildNodes |
    Where-Object {
        $_.LocalName -eq "Extension" -and $_.GetAttribute("Category") -eq "windows.protocol"
    } |
    ForEach-Object { $_.ChildNodes } |
    Where-Object {
        $_.LocalName -eq "Protocol" -and $_.GetAttribute("Name") -eq "cp6-desktop"
    }
if (-not $desktopProtocol) {
    throw "The Desktop package manifest must register the cp6-desktop protocol."
}

$releasePropertyGroup = $mobileProject.Project.PropertyGroup |
    Where-Object { $_.Condition -match "Release" } |
    Select-Object -First 1
if ($null -eq $releasePropertyGroup -or
    [string]$releasePropertyGroup.AndroidManifestPlaceholders -notmatch "usesCleartextTraffic=false") {
    throw "Android Release must set usesCleartextTraffic=false."
}

$desktopPublishScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "CP6.Desktop\scripts\publish-msix.ps1"),
    [Text.Encoding]::UTF8
)
$androidPublishScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "CP6.Mobile\scripts\publish-apk.ps1"),
    [Text.Encoding]::UTF8
)
$artifactGateScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "scripts\test-r2-artifacts.ps1"),
    [Text.Encoding]::UTF8
)
$deploymentGateScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "scripts\test-r2-deployment.ps1"),
    [Text.Encoding]::UTF8
)
$deploymentScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "scripts\deploy-r2.ps1"),
    [Text.Encoding]::UTF8
)
$evidenceScript = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "scripts\publish-r2-evidence.ps1"),
    [Text.Encoding]::UTF8
)
$candidateWorkflow = [IO.File]::ReadAllText(
    (Join-Path $repoRoot ".github\workflows\r2-candidate.yml"),
    [Text.Encoding]::UTF8
)
$freezeWorkflow = [IO.File]::ReadAllText(
    (Join-Path $repoRoot ".github\workflows\r2-freeze.yml"),
    [Text.Encoding]::UTF8
)
$deploymentWorkflow = [IO.File]::ReadAllText(
    (Join-Path $repoRoot ".github\workflows\r2-deploy.yml"),
    [Text.Encoding]::UTF8
)
$developmentCompose = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "docker-compose.yml"),
    [Text.Encoding]::UTF8
)
$developmentKubernetesReadme = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "k8s\README.md"),
    [Text.Encoding]::UTF8
)
$productionCompose = [IO.File]::ReadAllText(
    (Join-Path $repoRoot "deploy\production\compose\compose.yaml"),
    [Text.Encoding]::UTF8
)
$productionKubernetes = (
    Get-ChildItem -LiteralPath (
        Join-Path $repoRoot "deploy\production\kubernetes"
    ) -File -Filter "*.yaml" |
        Sort-Object Name |
        ForEach-Object {
            [IO.File]::ReadAllText($_.FullName, [Text.Encoding]::UTF8)
        }
) -join [Environment]::NewLine
if ($desktopPublishScript -match "updates\.example\.internal") {
    throw "The Desktop publish script contains a placeholder update host."
}
if ($androidPublishScript -notmatch "AndroidSigningStorePass=env:" -or
    $androidPublishScript -notmatch "AndroidSigningKeyPass=env:") {
    throw "Android signing passwords must be passed through env: references."
}
if ($androidPublishScript -match '\[string\]\s*\$(StorePassword|KeyPassword)\b') {
    throw "Android signing passwords must not be accepted as plain command-line parameters."
}
if ($artifactGateScript -notmatch 'windowsDownloadExtension' -or
    $artifactGateScript -notmatch '"\.appinstaller"\s*\{\s*\$appInstallerHash') {
    throw "Artifact gate must match the Windows bootstrap hash to its actual download type."
}
foreach ($manifestV2Contract in @(
    "SchemaVersion\s*=\s*2",
    "EvidenceRootUri",
    "Images\s*=",
    "SupplyChain\s*=",
    "SqlIntegrationReport",
    "Database\s*=",
    "ExecutionSpec\s*=",
    "FreezeSnapshotSha256"
)) {
    if ($artifactGateScript -notmatch $manifestV2Contract) {
        throw "Artifact gate is missing release manifest v2 contract '$manifestV2Contract'."
    }
}
foreach ($requiredDeploymentProbe in @(
    "health/live",
    "health/ready",
    "health/release",
    "release.json",
    "api/client/bootstrap",
    "windows-msix",
    "windows-appinstaller",
    "android-apk"
)) {
    if ($deploymentGateScript -notmatch [regex]::Escape($requiredDeploymentProbe)) {
        throw "Deployment gate is missing '$requiredDeploymentProbe' verification."
    }
}
if ($deploymentGateScript -match
    "DangerousAcceptAnyServerCertificateValidator|ServerCertificateValidationCallback|SkipCertificateCheck") {
    throw "Deployment gate must never bypass TLS certificate validation."
}
foreach ($deploymentContract in @(
    "SchemaVersion.+2",
    "GitSha",
    "Images",
    "LatestMigration",
    "OutputEvidencePath",
    "CandidateResultSha256",
    "ExecutionSpecSha256",
    "FreezeSnapshotSha256"
)) {
    if ($deploymentGateScript -notmatch $deploymentContract) {
        throw "Deployment gate is missing manifest/runtime contract '$deploymentContract'."
    }
}
foreach ($deploymentRunnerContract in @(
    "SchemaVersion.+2",
    "database initialization",
    "repo@digest|Repository\).+Digest",
    "cp6-db-init",
    "rollout.+status"
)) {
    if ($deploymentScript -notmatch $deploymentRunnerContract) {
        throw "Deployment runner is missing '$deploymentRunnerContract'."
    }
}
foreach ($evidenceContract in @(
    "get-bucket-versioning",
    "get-object-lock-configuration",
    "object-lock-mode.+COMPLIANCE",
    "object-lock-retain-until-date",
    "server-side-encryption.+AES256",
    "checksum-algorithm.+SHA256"
)) {
    if ($evidenceScript -notmatch $evidenceContract) {
        throw "Evidence publisher is missing '$evidenceContract'."
    }
}
foreach ($candidateContract in @(
    'tags:\s*\r?\n\s*-\s*"v\*\.\*\.\*"',
    "git rev-parse origin/main",
    "self-hosted, Windows, X64, cp6-release",
    "environment: r2-candidate",
    "dotnet test CP6\.Tests",
    "dotnet test CP6\.Client\.Tests",
    "npm --prefix cp6\.web test",
    "wms-production-console\.spec\.ts",
    "anchore/syft",
    "aquasec/trivy",
    "release-freeze-uri",
    "VerifySnapshot",
    "candidate-result\.json",
    "publish-r2-evidence\.ps1"
)) {
    if ($candidateWorkflow -notmatch $candidateContract) {
        throw "Candidate workflow is missing '$candidateContract'."
    }
}
foreach ($freezeContract in @(
    "environment: r2-release-freeze",
    "self-hosted, Windows, X64, cp6-release",
    "actions/create-github-app-token@v3",
    "permission-contents: write",
    "persist-credentials: false",
    "Mode Freeze",
    "s3api head-object",
    "ObjectLockMode",
    "publish-r2-evidence\.ps1",
    "git tag --annotate",
    "release-freeze-sha256=",
    "execution-spec-sha256="
)) {
    if ($freezeWorkflow -notmatch $freezeContract) {
        throw "Release freeze workflow is missing '$freezeContract'."
    }
}
foreach ($workflowContract in @(
    "type: environment",
    "self-hosted, Windows, X64, cp6-deploy",
    "environment:.+\$\{\{ inputs\.environment \}\}",
    "candidate-result\.json",
    "VerifySnapshot",
    "CP6_CANDIDATE_RESULT",
    "CP6_VAULT_RENDERER",
    "deploy-r2\.ps1",
    "test-r2-deployment\.ps1"
)) {
    if ($deploymentWorkflow -notmatch $workflowContract) {
        throw "Deployment workflow is missing '$workflowContract'."
    }
}
if ($developmentCompose -notmatch "DEVELOPMENT ONLY" -or
    $developmentKubernetesReadme -notmatch "DEVELOPMENT ONLY") {
    throw "Root Compose and k8s assets must be explicitly marked development-only."
}
foreach ($composeContract in @(
    "db-init:",
    "Startup__Mode:\s*DatabaseInit",
    "Startup__SkipDatabaseInitialization:\s*`"false`"",
    "CP6_API_IMAGE.+repository@sha256:digest",
    "cp6-api"
)) {
    if ($productionCompose -notmatch $composeContract) {
        throw "Production Compose is missing '$composeContract'."
    }
}
if ($productionCompose -match
    "(?im)^\s*image:\s*(mcr\.microsoft\.com/mssql|redis:|rabbitmq:|apache/kafka)") {
    throw "Production Compose must not contain local SQL, Redis, or messaging services."
}
foreach ($kubernetesContract in @(
    "kind:\s*Job",
    "name:\s*cp6-db-init",
    "replicas:\s*2",
    "maxUnavailable:\s*0",
    "kind:\s*PodDisruptionBudget",
    "topologySpreadConstraints:",
    "readinessProbe:",
    "livenessProbe:",
    "startupProbe:",
    "resources:",
    "sessionAffinity:\s*ClientIP",
    "ingressClassName:",
    "secretName:",
    "nginx\.ingress\.kubernetes\.io/affinity:\s*cookie"
)) {
    if ($productionKubernetes -notmatch $kubernetesContract) {
        throw "Production Kubernetes templates are missing '$kubernetesContract'."
    }
}

$releaseScripts = @(
    "scripts\test-r2-source-gate.ps1",
    "scripts\test-r2-pilot-contract.ps1",
    "scripts\test-r2-pilot-orchestration-contract.ps1",
    "scripts\install-k6-portable.ps1",
    "scripts\prepare-r2-pilot.ps1",
    "scripts\invoke-r2-pilot.ps1",
    "scripts\test-native-client-contract.ps1",
    "scripts\test-r2-artifacts.ps1",
    "scripts\test-r2-release-readiness.ps1",
    "scripts\test-r2-deployment.ps1",
    "scripts\test-r2-deployment-contract.ps1",
    "scripts\deploy-r2.ps1",
    "scripts\publish-r2-evidence.ps1",
    "scripts\new-r2-reconciliation-evidence.ps1",
    "scripts\test-r2-pilot-exit.ps1",
    "scripts\test-r2b-preflight.ps1",
    "scripts\test-r2-operations-contract.ps1",
    "CP6.Desktop\scripts\publish-msix.ps1",
    "CP6.Mobile\scripts\publish-apk.ps1"
)
foreach ($relativeScript in $releaseScripts) {
    $scriptPath = Join-Path $repoRoot $relativeScript
    $tokens = $null
    $parseErrors = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile(
        $scriptPath,
        [ref]$tokens,
        [ref]$parseErrors
    )
    if ($parseErrors.Count -gt 0) {
        $messages = ($parseErrors | ForEach-Object Message) -join "; "
        throw "$relativeScript contains PowerShell parse errors: $messages"
    }
}

& (Join-Path $repoRoot "scripts\test-r2-deployment-contract.ps1")
& (Join-Path $repoRoot "scripts\test-r2-release-readiness.ps1") `
    -SpecPath (
        Join-Path $repoRoot "docs\client\r2\releases\v$ExpectedVersion\candidate.yaml"
    ) `
    -Mode Structure `
    -ExpectedVersion $ExpectedVersion
Invoke-CheckedCommand -FilePath "npm.cmd" `
    -ArgumentList @("run", "test:r2-readiness") `
    -Description "R2 release readiness contract tests" `
    -WorkingDirectory (Join-Path $repoRoot "cp6.web")
& (Join-Path $repoRoot "scripts\test-r2-pilot-contract.ps1")
& (Join-Path $repoRoot "scripts\test-r2-pilot-orchestration-contract.ps1")
& (Join-Path $repoRoot "scripts\test-r2-operations-contract.ps1")
& (Join-Path $repoRoot "scripts\test-native-client-contract.ps1") `
    -Configuration $Configuration `
    -SkipTests `
    -SkipDesktopBuild

if (-not $SkipDependencyAudit) {
    Assert-NoVulnerableNuGetPackages
    Invoke-CheckedCommand -FilePath "npm.cmd" `
        -ArgumentList @(
            "audit", "--audit-level=low",
            "--registry=https://registry.npmjs.org"
        ) `
        -Description "npm vulnerability audit" `
        -WorkingDirectory (Join-Path $repoRoot "cp6.web")
}

if (-not $SkipModelCheck) {
    $previousAspNetCoreEnvironment = $env:ASPNETCORE_ENVIRONMENT
    $previousDotnetEnvironment = $env:DOTNET_ENVIRONMENT
    try {
        $env:ASPNETCORE_ENVIRONMENT = "Development"
        $env:DOTNET_ENVIRONMENT = "Development"
        $contextChecks = @(
            @{
                Context = "CP6Context"
                Project = "CP6.Core\CP6.Core.csproj"
            },
            @{
                Context = "SpaceContext"
                Project = "CP6.Space.Infrastructure\CP6.Space.Infrastructure.csproj"
            }
        )
        foreach ($contextCheck in $contextChecks) {
            Invoke-CheckedCommand -FilePath "dotnet" `
                -ArgumentList @(
                    "tool", "run", "dotnet-ef",
                    "migrations", "has-pending-model-changes",
                    "--context", $contextCheck.Context,
                    "--project", $contextCheck.Project,
                    "--startup-project", "CP6.WebApi\CP6.WebApi.csproj",
                    "--configuration", $Configuration,
                    "--no-build"
                ) `
                -Description (
                    "EF pending model change check for " +
                    $contextCheck.Context
                )
        }
    }
    finally {
        if ($null -eq $previousAspNetCoreEnvironment) {
            Remove-Item Env:ASPNETCORE_ENVIRONMENT -ErrorAction SilentlyContinue
        }
        else {
            $env:ASPNETCORE_ENVIRONMENT = $previousAspNetCoreEnvironment
        }

        if ($null -eq $previousDotnetEnvironment) {
            Remove-Item Env:DOTNET_ENVIRONMENT -ErrorAction SilentlyContinue
        }
        else {
            $env:DOTNET_ENVIRONMENT = $previousDotnetEnvironment
        }
    }
}

Write-Host "R2 source gate passed for version $ExpectedVersion."
