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
foreach ($requiredDeploymentProbe in @(
    "health/live",
    "health/ready",
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

$releaseScripts = @(
    "scripts\test-r2-source-gate.ps1",
    "scripts\test-r2-pilot-contract.ps1",
    "scripts\test-r2-pilot-orchestration-contract.ps1",
    "scripts\install-k6-portable.ps1",
    "scripts\prepare-r2-pilot.ps1",
    "scripts\invoke-r2-pilot.ps1",
    "scripts\test-native-client-contract.ps1",
    "scripts\test-r2-artifacts.ps1",
    "scripts\test-r2-deployment.ps1",
    "scripts\test-r2-deployment-contract.ps1",
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
& (Join-Path $repoRoot "scripts\test-r2-pilot-contract.ps1")
& (Join-Path $repoRoot "scripts\test-r2-pilot-orchestration-contract.ps1")
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
        Invoke-CheckedCommand -FilePath "dotnet" `
            -ArgumentList @(
                "tool", "run", "dotnet-ef",
                "migrations", "has-pending-model-changes",
                "--project", "CP6.Core\CP6.Core.csproj",
                "--startup-project", "CP6.WebApi\CP6.WebApi.csproj",
                "--configuration", $Configuration,
                "--no-build"
            ) `
            -Description "EF pending model change check"
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
