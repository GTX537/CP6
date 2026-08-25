[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ApiPublishPath,

    [Parameter(Mandatory = $true)]
    [string]$WebDistPath,

    [Parameter(Mandatory = $true)]
    [string]$WebNginxConfigPath,

    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,

    [Parameter(Mandatory = $true)]
    [string]$ReleaseVersion,

    [Parameter(Mandatory = $true)]
    [string]$GitSha
)

$ErrorActionPreference = "Stop"

if ($ReleaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$') {
    throw "ReleaseVersion must be a SemVer-compatible version."
}
if ($GitSha -notmatch '^[0-9a-fA-F]{40}$') {
    throw "GitSha must be a complete 40-character commit SHA."
}

$resolvedApiPublishPath = [IO.Path]::GetFullPath($ApiPublishPath)
$resolvedWebDistPath = [IO.Path]::GetFullPath($WebDistPath)
$resolvedWebNginxConfigPath = [IO.Path]::GetFullPath($WebNginxConfigPath)
$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)

if (-not (Test-Path -LiteralPath $resolvedApiPublishPath -PathType Container)) {
    throw "API publish directory '$resolvedApiPublishPath' does not exist."
}
if (-not (Test-Path -LiteralPath $resolvedWebDistPath -PathType Container)) {
    throw "Web dist directory '$resolvedWebDistPath' does not exist."
}
if (-not (Test-Path -LiteralPath $resolvedWebNginxConfigPath -PathType Leaf)) {
    throw "Web nginx configuration '$resolvedWebNginxConfigPath' does not exist."
}
if (-not (Test-Path -LiteralPath (Join-Path $resolvedApiPublishPath "CP6.WebApi.dll") -PathType Leaf)) {
    throw "API publish payload does not contain CP6.WebApi.dll."
}
if (Test-Path -LiteralPath $resolvedOutputRoot) {
    throw "Runtime artifact output '$resolvedOutputRoot' already exists."
}

$webReleasePath = Join-Path $resolvedWebDistPath "release.json"
if (-not (Test-Path -LiteralPath $webReleasePath -PathType Leaf)) {
    throw "Web dist payload does not contain release.json."
}
$webRelease = Get-Content -LiteralPath $webReleasePath -Raw -Encoding utf8 |
    ConvertFrom-Json
if ($webRelease.version -ne $ReleaseVersion -or
    -not ([string]$webRelease.gitSha).Equals(
        $GitSha,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Web release identity does not match the requested runtime artifact."
}

$apiDestination = Join-Path $resolvedOutputRoot "api\publish"
$webDistDestination = Join-Path $resolvedOutputRoot "web\dist"
$webNginxDestination = Join-Path $resolvedOutputRoot "web\nginx.conf"
[IO.Directory]::CreateDirectory($apiDestination) | Out-Null
[IO.Directory]::CreateDirectory($webDistDestination) | Out-Null

Get-ChildItem -LiteralPath $resolvedApiPublishPath -Force |
    Copy-Item -Destination $apiDestination -Recurse -Force
Get-ChildItem -LiteralPath $resolvedWebDistPath -Force |
    Copy-Item -Destination $webDistDestination -Recurse -Force
Copy-Item `
    -LiteralPath $resolvedWebNginxConfigPath `
    -Destination $webNginxDestination `
    -Force

$rootPrefix = $resolvedOutputRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
$files = @(
    Get-ChildItem -LiteralPath $resolvedOutputRoot -Recurse -File |
        Sort-Object FullName |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($rootPrefix.Length).Replace('\', '/')
            [ordered]@{
                path = $relativePath
                length = [long]$_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        }
)

$manifest = [ordered]@{
    schemaVersion = 1
    artifactType = "cp6-dev-runtime"
    releaseVersion = $ReleaseVersion
    gitSha = $GitSha.ToLowerInvariant()
    createdAtUtc = [DateTime]::UtcNow.ToString("o")
    payloads = [ordered]@{
        api = [ordered]@{
            root = "api/publish"
            entryPoint = "api/publish/CP6.WebApi.dll"
        }
        web = [ordered]@{
            root = "web/dist"
            releaseIdentity = "web/dist/release.json"
            nginxConfig = "web/nginx.conf"
        }
    }
    files = $files
}
$manifestPath = Join-Path $resolvedOutputRoot "manifest.json"
$manifest | ConvertTo-Json -Depth 8 |
    Set-Content -LiteralPath $manifestPath -Encoding utf8

Write-Host "Created CP6 DEV runtime artifact for $ReleaseVersion / $($GitSha.ToLowerInvariant()) with $($files.Count) hashed files."
$manifest
