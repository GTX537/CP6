[CmdletBinding()]
param(
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$Version = "2.1.0",
    [ValidatePattern("^[A-Fa-f0-9]{64}$")]
    [string]$ExpectedSha256 = "185ca503ead8f0348daa79c002469e5eb324473c39452f29b5f70b1c1b4c8503"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $repoRoot ".tools\k6"
$versionRoot = Join-Path $toolRoot "v$Version"
$existing = Get-ChildItem -LiteralPath $versionRoot `
    -Recurse -File -Filter "k6.exe" -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -ne $existing) {
    Write-Host "k6 is already installed: $($existing.FullName)"
    & $existing.FullName version
    exit $LASTEXITCODE
}
if (Test-Path -LiteralPath $versionRoot) {
    throw "The target '$versionRoot' already exists but does not contain k6.exe. Inspect it before retrying."
}

$assetName = "k6-v$Version-windows-amd64.zip"
$downloadUri = "https://github.com/grafana/k6/releases/download/v$Version/$assetName"
$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "cp6-k6-$([Guid]::NewGuid().ToString('N'))"
$archivePath = Join-Path $temporaryRoot $assetName
$extractRoot = Join-Path $temporaryRoot "extracted"

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    Write-Host "Downloading $downloadUri"
    Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath

    $actualSha256 = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash
    if ($actualSha256 -ne $ExpectedSha256) {
        throw "k6 archive SHA-256 mismatch. Expected $ExpectedSha256, received $actualSha256."
    }

    Expand-Archive -LiteralPath $archivePath -DestinationPath $extractRoot
    $extracted = Get-ChildItem -LiteralPath $extractRoot `
        -Recurse -File -Filter "k6.exe" |
        Select-Object -First 1
    if ($null -eq $extracted) {
        throw "The verified k6 archive did not contain k6.exe."
    }
    & $extracted.FullName version
    if ($LASTEXITCODE -ne 0) {
        throw "The extracted k6 binary failed its version check."
    }

    [IO.Directory]::CreateDirectory($toolRoot) | Out-Null
    Move-Item -LiteralPath $extractRoot -Destination $versionRoot
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if (
        (Test-Path -LiteralPath $resolvedTemporaryRoot) -and
        $resolvedTemporaryRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        ([IO.Path]::GetFileName($resolvedTemporaryRoot)).StartsWith("cp6-k6-", [StringComparison]::Ordinal)
    ) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}

$installed = Get-ChildItem -LiteralPath $versionRoot -Recurse -File -Filter "k6.exe" |
    Select-Object -First 1
if ($null -eq $installed) {
    throw "k6 installation did not produce an executable."
}
Write-Host "Installed verified k6 v${Version}: $($installed.FullName)"
