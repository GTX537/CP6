[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ArtifactRoot,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedReleaseVersion,

    [Parameter(Mandatory = $true)]
    [string]$ExpectedGitSha,

    [switch]$PassThru
)

$ErrorActionPreference = "Stop"

if ($ExpectedReleaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "ExpectedReleaseVersion must be a SemVer-compatible version."
}
if ($ExpectedGitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "ExpectedGitSha must be a complete 40-character commit SHA."
}

$resolvedArtifactRoot = [IO.Path]::GetFullPath($ArtifactRoot)
$manifestPath = Join-Path $resolvedArtifactRoot "manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Runtime artifact manifest '$manifestPath' does not exist."
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding utf8 |
    ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.artifactType -ne "cp6-dev-runtime") {
    throw "Runtime artifact schema or type is not supported."
}
if ($manifest.releaseVersion -ne $ExpectedReleaseVersion -or
    -not ([string]$manifest.gitSha).Equals(
        $ExpectedGitSha,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Runtime artifact identity does not match the selected CI run."
}

$requiredManifestValues = [ordered]@{
    "API root" = [string]$manifest.payloads.api.root
    "API entry point" = [string]$manifest.payloads.api.entryPoint
    "Web root" = [string]$manifest.payloads.web.root
    "Web release identity" = [string]$manifest.payloads.web.releaseIdentity
    "Web nginx config" = [string]$manifest.payloads.web.nginxConfig
}
$expectedManifestValues = @(
    "api/publish",
    "api/publish/CP6.WebApi.dll",
    "web/dist",
    "web/dist/release.json",
    "web/nginx.conf"
)
$manifestValueIndex = 0
foreach ($entry in $requiredManifestValues.GetEnumerator()) {
    if ($entry.Value -ne $expectedManifestValues[$manifestValueIndex]) {
        throw "Runtime artifact $($entry.Key) is invalid."
    }
    $manifestValueIndex++
}

$manifestFiles = @($manifest.files)
if ($manifestFiles.Count -eq 0) {
    throw "Runtime artifact manifest does not contain file hashes."
}

$manifestByPath = @{}
foreach ($entry in $manifestFiles) {
    $relativePath = [string]$entry.path
    if ([string]::IsNullOrWhiteSpace($relativePath) -or
        $relativePath.Contains('\') -or
        [IO.Path]::IsPathRooted($relativePath) -or
        $relativePath.Split('/') -contains '..') {
        throw "Runtime artifact manifest contains an unsafe relative path."
    }
    if ($manifestByPath.ContainsKey($relativePath)) {
        throw "Runtime artifact manifest contains duplicate path '$relativePath'."
    }
    $manifestByPath[$relativePath] = $entry
}

$rootPrefix = $resolvedArtifactRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$actualFiles = @(
    Get-ChildItem -LiteralPath $resolvedArtifactRoot -Recurse -File |
        Where-Object { $_.FullName -ne $manifestPath } |
        Sort-Object FullName
)
if ($actualFiles.Count -ne $manifestFiles.Count) {
    throw "Runtime artifact file count does not match its manifest."
}
foreach ($file in $actualFiles) {
    $relativePath = $file.FullName.Substring($rootPrefix.Length).Replace('\', '/')
    if (-not $manifestByPath.ContainsKey($relativePath)) {
        throw "Runtime artifact contains unlisted file '$relativePath'."
    }
    $entry = $manifestByPath[$relativePath]
    $actualHash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ([long]$entry.length -ne [long]$file.Length -or
        -not $actualHash.Equals(
            [string]$entry.sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime artifact hash or length mismatch for '$relativePath'."
    }
}

$apiEntryPoint = Join-Path $resolvedArtifactRoot "api\publish\CP6.WebApi.dll"
$webReleasePath = Join-Path $resolvedArtifactRoot "web\dist\release.json"
$webNginxPath = Join-Path $resolvedArtifactRoot "web\nginx.conf"
foreach ($requiredPath in @($apiEntryPoint, $webReleasePath, $webNginxPath)) {
    if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
        throw "Runtime artifact required file '$requiredPath' does not exist."
    }
}

$webRelease = Get-Content -LiteralPath $webReleasePath -Raw -Encoding utf8 |
    ConvertFrom-Json
if ($webRelease.version -ne $ExpectedReleaseVersion -or
    -not ([string]$webRelease.gitSha).Equals(
        $ExpectedGitSha,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Web release identity does not match the selected CI run."
}

Write-Host "Verified CP6 DEV runtime artifact for $ExpectedReleaseVersion / $($ExpectedGitSha.ToLowerInvariant()) with $($actualFiles.Count) hashed files."
if ($PassThru) {
    $manifest
}
