[CmdletBinding()]
param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [switch]$SkipTests,
    [switch]$SkipDesktopBuild,
    [switch]$IncludeAndroidBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-CheckedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Push-Location $repoRoot
    try {
        & $FilePath @ArgumentList
        if ($LASTEXITCODE -ne 0) {
            throw "$Description failed with exit code $LASTEXITCODE."
        }
    }
    finally {
        Pop-Location
    }
}

function Read-RepoText {
    param([Parameter(Mandatory = $true)][string]$RelativePath)

    $path = Join-Path $repoRoot $RelativePath
    return [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
}

$settings = Read-RepoText "CP6.WebApi\appsettings.json" |
    ConvertFrom-Json
$allowedRedirects = @(
    $settings.Security.NativeClient.AllowedRedirectUris
)
$expectedRedirects = @(
    "cp6-desktop://auth/callback",
    "cp6-mobile://auth/callback"
)
foreach ($redirect in $expectedRedirects) {
    if ($allowedRedirects -notcontains $redirect) {
        throw "Native SSO allowlist is missing '$redirect'."
    }
}
if ($allowedRedirects.Count -ne $expectedRedirects.Count) {
    throw "Native SSO allowlist contains an unowned redirect URI."
}

[xml]$desktopManifest = Read-RepoText "CP6.Desktop\Package.appxmanifest"
$desktopProtocol = $desktopManifest.SelectSingleNode(
    "//*[local-name()='Protocol' and @Name='cp6-desktop']"
)
if ($null -eq $desktopProtocol) {
    throw "Desktop package does not own the cp6-desktop protocol."
}

$mainActivity = Read-RepoText `
    "CP6.Mobile\Platforms\Android\MainActivity.cs"
foreach ($requiredAndroidDeclaration in @(
    'Exported\s*=\s*true',
    'LaunchMode\s*=\s*LaunchMode\.SingleTask',
    'DataScheme\s*=\s*"cp6-mobile"',
    'DataHost\s*=\s*"auth"',
    'DataPathPrefix\s*=\s*"/callback"'
)) {
    if ($mainActivity -notmatch $requiredAndroidDeclaration) {
        throw "Android callback declaration is missing '$requiredAndroidDeclaration'."
    }
}

$nativeSsoService = Read-RepoText `
    "CP6.Client.Core\NativeSsoService.cs"
if ($nativeSsoService -notmatch
    'StartAsync\(\s*string tenantCode,\s*CancellationToken') {
    throw "Native SSO start must derive the redirect URI from the platform."
}
if ($nativeSsoService -match
    'StartAsync\(\s*string tenantCode,\s*string redirectUri') {
    throw "Native SSO start accepts a caller-controlled redirect URI."
}
foreach ($requiredCallbackGuard in @(
    'IsExpectedCallback\(callback\)',
    'query\.ContainsKey\("error"\)',
    'E-CLIENT-SSO-CALLBACK'
)) {
    if ($nativeSsoService -notmatch $requiredCallbackGuard) {
        throw "Native SSO callback guard is missing '$requiredCallbackGuard'."
    }
}

$grantCache = Read-RepoText `
    "CP6.WebApi\Services\NativeSsoGrantCache.cs"
if ($grantCache -notmatch
    'Condition\.StringEqual\(Key\(key\), expectedValue\)' -or
    $grantCache -notmatch
    'transaction\.KeyDeleteAsync\(Key\(key\)\)') {
    throw "Redis native SSO grants are not compare-and-delete atomic."
}

if (-not $SkipTests) {
    Invoke-CheckedCommand -FilePath "dotnet" `
        -ArgumentList @(
            "test",
            "CP6.Client.Tests\CP6.Client.Tests.csproj",
            "-c", $Configuration,
            "--no-restore"
        ) `
        -Description "Native client contract tests"

    Invoke-CheckedCommand -FilePath "dotnet" `
        -ArgumentList @(
            "test",
            "CP6.Tests\CP6.Tests.csproj",
            "-c", $Configuration,
            "--no-restore",
            "--filter",
            "FullyQualifiedName~NativeSsoGrantStoreTests|FullyQualifiedName~ClientBootstrapVersionTests"
        ) `
        -Description "Native server security contract tests"
}

if (-not $SkipDesktopBuild) {
    Invoke-CheckedCommand -FilePath "dotnet" `
        -ArgumentList @(
            "build",
            "CP6.Desktop\CP6.Desktop.csproj",
            "-c", $Configuration,
            "--no-restore",
            "-m:1"
        ) `
        -Description "Desktop client build"
}

if ($IncludeAndroidBuild) {
    Invoke-CheckedCommand -FilePath "dotnet" `
        -ArgumentList @(
            "build",
            "CP6.Mobile\CP6.Mobile.csproj",
            "-f", "net10.0-android",
            "-c", $Configuration,
            "--no-restore",
            "-m:1"
        ) `
        -Description "Android client build"
}

Write-Host "Native client startup and SSO contract passed."
