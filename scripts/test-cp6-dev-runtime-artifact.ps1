[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-dev-runtime-artifact-contract-$([Guid]::NewGuid().ToString('N'))"
$version = "0.0.0-dev.42"
$gitSha = "0123456789abcdef0123456789abcdef01234567"

function New-SyntheticPayload {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$ReleaseVersion,
        [Parameter(Mandatory = $true)][string]$ReleaseGitSha
    )

    $api = Join-Path $Root "source\api"
    $web = Join-Path $Root "source\web"
    [IO.Directory]::CreateDirectory($api) | Out-Null
    [IO.Directory]::CreateDirectory($web) | Out-Null
    Set-Content -LiteralPath (Join-Path $api "CP6.WebApi.dll") -Value "synthetic-api" -Encoding utf8
    Set-Content -LiteralPath (Join-Path $api "CP6.WebApi.deps.json") -Value "{}" -Encoding utf8
    Set-Content -LiteralPath (Join-Path $web "index.html") -Value "<html></html>" -Encoding utf8
    [ordered]@{
        version = $ReleaseVersion
        gitSha = $ReleaseGitSha
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $web "release.json") -Encoding utf8
    $nginx = Join-Path $Root "source\nginx.conf"
    Set-Content -LiteralPath $nginx -Value "server { listen 80; }" -Encoding utf8
    [pscustomobject]@{ Api = $api; Web = $web; Nginx = $nginx }
}

function New-SyntheticArtifact {
    param([Parameter(Mandatory = $true)][string]$Name)

    $caseRoot = Join-Path $temporaryRoot $Name
    $payload = New-SyntheticPayload `
        -Root $caseRoot `
        -ReleaseVersion $version `
        -ReleaseGitSha $gitSha
    $artifact = Join-Path $caseRoot "artifact"
    & (Join-Path $repoRoot "scripts\New-Cp6DevRuntimeArtifact.ps1") `
        -ApiPublishPath $payload.Api `
        -WebDistPath $payload.Web `
        -WebNginxConfigPath $payload.Nginx `
        -OutputRoot $artifact `
        -ReleaseVersion $version `
        -GitSha $gitSha |
        Out-Null
    $artifact
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

    $validArtifact = New-SyntheticArtifact -Name "valid"
    & (Join-Path $repoRoot "scripts\Test-Cp6DevRuntimeArtifact.ps1") `
        -ArtifactRoot $validArtifact `
        -ExpectedReleaseVersion $version `
        -ExpectedGitSha $gitSha

    $tamperedArtifact = New-SyntheticArtifact -Name "tampered"
    Add-Content `
        -LiteralPath (Join-Path $tamperedArtifact "api\publish\CP6.WebApi.dll") `
        -Value "tampered"
    $tamperRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\Test-Cp6DevRuntimeArtifact.ps1") `
            -ArtifactRoot $tamperedArtifact `
            -ExpectedReleaseVersion $version `
            -ExpectedGitSha $gitSha 2>&1 |
            Out-Null
    }
    catch {
        $tamperRejected = $true
    }
    if (-not $tamperRejected) {
        throw "A tampered runtime payload must be rejected."
    }

    $extraFileArtifact = New-SyntheticArtifact -Name "extra-file"
    Set-Content `
        -LiteralPath (Join-Path $extraFileArtifact "web\dist\unexpected.txt") `
        -Value "unexpected" `
        -Encoding utf8
    $extraFileRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\Test-Cp6DevRuntimeArtifact.ps1") `
            -ArtifactRoot $extraFileArtifact `
            -ExpectedReleaseVersion $version `
            -ExpectedGitSha $gitSha 2>&1 |
            Out-Null
    }
    catch {
        $extraFileRejected = $true
    }
    if (-not $extraFileRejected) {
        throw "An unlisted runtime payload file must be rejected."
    }

    $identityRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\Test-Cp6DevRuntimeArtifact.ps1") `
            -ArtifactRoot $validArtifact `
            -ExpectedReleaseVersion "0.0.0-dev.43" `
            -ExpectedGitSha $gitSha 2>&1 |
            Out-Null
    }
    catch {
        $identityRejected = $true
    }
    if (-not $identityRejected) {
        throw "A runtime artifact from another CI identity must be rejected."
    }

    Write-Host "CP6 DEV runtime artifact contract test passed."
}
finally {
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith(
        $resolvedSystemTemp,
        [StringComparison]::OrdinalIgnoreCase
    ) -and (Split-Path -Leaf $resolvedTemporaryRoot).StartsWith(
        "cp6-dev-runtime-artifact-contract-",
        [StringComparison]::OrdinalIgnoreCase
    ) -and (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
