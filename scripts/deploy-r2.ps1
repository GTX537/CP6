[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Compose", "Kubernetes")]
    [string]$Target,
    [Parameter(Mandatory = $true)][string]$ReleaseManifestPath,
    [Parameter(Mandatory = $true)][string]$SecretEnvFile,
    [string]$Namespace = "cp6-production",
    [string]$IngressHost,
    [string]$IngressClass = "nginx",
    [string]$TlsSecret = "cp6-tls"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$Description
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Assert-Image {
    param([Parameter(Mandatory = $true)]$Image, [string]$Name)
    if ([string]::IsNullOrWhiteSpace([string]$Image.Repository) -or
        [string]$Image.Digest -notmatch "^sha256:[A-Fa-f0-9]{64}$") {
        throw "$Name image repository or digest is invalid."
    }
    return "$($Image.Repository)@$(([string]$Image.Digest).ToLowerInvariant())"
}

$manifestPath = (Resolve-Path -LiteralPath $ReleaseManifestPath -ErrorAction Stop).Path
$secretPath = (Resolve-Path -LiteralPath $SecretEnvFile -ErrorAction Stop).Path
$manifest = [IO.File]::ReadAllText($manifestPath, [Text.Encoding]::UTF8) |
    ConvertFrom-Json
if ([int]$manifest.SchemaVersion -ne 2) {
    throw "Only release manifest SchemaVersion 2 can be deployed."
}
if ([string]$manifest.ReleaseVersion -notmatch "^\d+\.\d+\.\d+$" -or
    [string]$manifest.GitSha -notmatch "^[A-Fa-f0-9]{40}$") {
    throw "Release version or Git SHA is invalid."
}
if ((Get-Item -LiteralPath $secretPath).Length -le 0) {
    throw "The runner-rendered secret file is empty."
}

$apiImage = Assert-Image -Image $manifest.Images.Api -Name "API"
$webImage = Assert-Image -Image $manifest.Images.Web -Name "Web"
$apiDigest = ([string]$manifest.Images.Api.Digest).ToLowerInvariant()
$webDigest = ([string]$manifest.Images.Web.Digest).ToLowerInvariant()

if ($Target -eq "Compose") {
    $env:CP6_API_IMAGE = $apiImage
    $env:CP6_WEB_IMAGE = $webImage
    $env:CP6_API_DIGEST = $apiDigest
    $env:CP6_WEB_DIGEST = $webDigest
    $env:CP6_RELEASE_VERSION = [string]$manifest.ReleaseVersion
    $env:CP6_GIT_SHA = [string]$manifest.GitSha
    $env:CP6_ENV_FILE = $secretPath
    $compose = Join-Path $repoRoot "deploy\production\compose\compose.yaml"

    Invoke-Checked -FilePath "docker" -Description "Compose database initialization" `
        -Arguments @(
            "compose", "--env-file", $secretPath, "-f", $compose,
            "up", "--no-deps", "--abort-on-container-exit",
            "--exit-code-from", "db-init", "db-init"
        )
    Invoke-Checked -FilePath "docker" -Description "Compose API/Web rollout" `
        -Arguments @(
            "compose", "--env-file", $secretPath, "-f", $compose,
            "up", "-d", "--no-deps", "api", "web"
        )

    foreach ($service in @("api", "web")) {
        $containerId = (& docker compose --env-file $secretPath `
            -f $compose ps -q $service).Trim()
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($containerId)) {
            throw "Compose service '$service' is not running."
        }
        $actualImage = (& docker inspect --format "{{.Config.Image}}" $containerId).Trim()
        $expectedImage = if ($service -eq "api") { $apiImage } else { $webImage }
        if ($actualImage -ne $expectedImage) {
            throw "Compose service '$service' is not running the manifest image digest."
        }
    }
    Write-Host "R2 Compose rollout completed with digest-pinned images."
    return
}

if ([string]::IsNullOrWhiteSpace($IngressHost)) {
    throw "IngressHost is required for Kubernetes."
}
if ($Namespace -notmatch "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$" -or
    $IngressClass -notmatch "^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$" -or
    $TlsSecret -notmatch "^[a-z0-9]([-a-z0-9.]*[a-z0-9])?$") {
    throw "Kubernetes namespace, ingress class, or TLS secret is invalid."
}

$templateRoot = Join-Path $repoRoot "deploy\production\kubernetes"
$renderRoot = Join-Path (
    [IO.Path]::GetTempPath()
) "cp6-r2-k8s-$([Guid]::NewGuid().ToString('N'))"
[IO.Directory]::CreateDirectory($renderRoot) | Out-Null
try {
    $replacements = [ordered]@{
        "CP6_NAMESPACE" = $Namespace
        "CP6_RELEASE_VERSION" = [string]$manifest.ReleaseVersion
        "CP6_GIT_SHA" = [string]$manifest.GitSha
        "CP6_API_IMAGE" = $apiImage
        "CP6_WEB_IMAGE" = $webImage
        "CP6_API_DIGEST" = $apiDigest
        "CP6_WEB_DIGEST" = $webDigest
        "CP6_HOST" = $IngressHost
        "CP6_INGRESS_CLASS" = $IngressClass
        "CP6_TLS_SECRET" = $TlsSecret
    }
    foreach ($template in Get-ChildItem -LiteralPath $templateRoot -Filter "*.yaml" -File) {
        $content = [IO.File]::ReadAllText($template.FullName, [Text.Encoding]::UTF8)
        foreach ($entry in $replacements.GetEnumerator()) {
            $content = $content.Replace($entry.Key, $entry.Value)
        }
        [IO.File]::WriteAllText(
            (Join-Path $renderRoot $template.Name),
            $content,
            [Text.UTF8Encoding]::new($false))
    }

    & kubectl create namespace $Namespace --dry-run=client -o yaml |
        kubectl apply -f -
    if ($LASTEXITCODE -ne 0) { throw "Kubernetes namespace apply failed." }

    & kubectl -n $Namespace create secret generic cp6-runtime-secrets `
        --from-env-file=$secretPath --dry-run=client -o yaml |
        kubectl apply -f -
    if ($LASTEXITCODE -ne 0) { throw "Kubernetes runtime secret apply failed." }

    Invoke-Checked -FilePath "kubectl" -Description "Kubernetes runtime config apply" `
        -Arguments @("apply", "-f", (Join-Path $renderRoot "configmap.yaml"))
    Invoke-Checked -FilePath "kubectl" -Description "Remove prior initialization job" `
        -Arguments @(
            "-n", $Namespace, "delete", "job", "cp6-db-init",
            "--ignore-not-found=true", "--wait=true"
        )
    Invoke-Checked -FilePath "kubectl" -Description "Kubernetes database initialization apply" `
        -Arguments @("apply", "-f", (Join-Path $renderRoot "db-init-job.yaml"))
    Invoke-Checked -FilePath "kubectl" -Description "Kubernetes database initialization wait" `
        -Arguments @(
            "-n", $Namespace, "wait", "--for=condition=complete",
            "job/cp6-db-init", "--timeout=900s"
        )

    foreach ($file in @("api.yaml", "web.yaml", "ingress.yaml")) {
        Invoke-Checked -FilePath "kubectl" -Description "Kubernetes $file apply" `
            -Arguments @("apply", "-f", (Join-Path $renderRoot $file))
    }
    foreach ($deployment in @("cp6-api", "cp6-web")) {
        Invoke-Checked -FilePath "kubectl" -Description "$deployment rollout" `
            -Arguments @(
                "-n", $Namespace, "rollout", "status",
                "deployment/$deployment", "--timeout=600s"
            )
    }

    $actualApi = (& kubectl -n $Namespace get deployment cp6-api `
        -o "jsonpath={.spec.template.spec.containers[0].image}").Trim()
    $actualWeb = (& kubectl -n $Namespace get deployment cp6-web `
        -o "jsonpath={.spec.template.spec.containers[0].image}").Trim()
    if ($actualApi -ne $apiImage -or $actualWeb -ne $webImage) {
        throw "Kubernetes deployments do not match release manifest image digests."
    }
    Write-Host "R2 Kubernetes rollout completed with digest-pinned images."
}
finally {
    $resolvedRenderRoot = [IO.Path]::GetFullPath($renderRoot)
    $resolvedTempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedRenderRoot.StartsWith(
            $resolvedTempRoot,
            [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedRenderRoot)) {
        Remove-Item -LiteralPath $resolvedRenderRoot -Recurse -Force
    }
}
