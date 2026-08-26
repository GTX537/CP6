[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    [Parameter(Mandatory)]
    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$GitSha,

    [Parameter(Mandatory)]
    [string]$OutputRoot,

    [ValidateRange(0, 7200)]
    [int]$MaxWaitSeconds = 1800,

    [ValidateRange(5, 300)]
    [int]$PollIntervalSeconds = 20
)

$ErrorActionPreference = "Stop"
$authorization = [Environment]::GetEnvironmentVariable(
    "CP6_GITHUB_AUTHORIZATION",
    "Process")
if ([string]::IsNullOrWhiteSpace($authorization) -or
    $authorization -notmatch '^(?i)AUTHORIZATION:\s+\S+') {
    throw "An authorized GitHub checkout credential is required to download the runtime artifact."
}

$normalizedGitSha = $GitSha.ToLowerInvariant()
$artifactName = "cp6-dev-runtime-$normalizedGitSha"
$headers = @{
    Accept = "application/vnd.github+json"
    Authorization = ($authorization -replace '^(?i)AUTHORIZATION:\s*', '')
    "User-Agent" = "CP6-Azure-Artifact-Bridge"
    "X-GitHub-Api-Version" = "2022-11-28"
}
$apiRoot = "https://api.github.com/repos/$Repository"
$encodedArtifactName = [Uri]::EscapeDataString($artifactName)
$maximumPolls = [math]::Ceiling($MaxWaitSeconds / [double]$PollIntervalSeconds)
$selectedArtifact = $null
$selectedRun = $null

for ($poll = 0; $poll -le $maximumPolls; $poll++) {
    $artifactResponse = Invoke-RestMethod `
        -Uri "$apiRoot/actions/artifacts?name=$encodedArtifactName&per_page=100" `
        -Headers $headers `
        -Method Get
    $candidates = @($artifactResponse.artifacts) |
        Where-Object {
            -not $_.expired -and
            $_.name -eq $artifactName -and
            ([string]$_.workflow_run.head_sha).Equals(
                $normalizedGitSha,
                [StringComparison]::OrdinalIgnoreCase)
        } |
        Sort-Object created_at -Descending

    foreach ($candidate in $candidates) {
        $workflowRun = Invoke-RestMethod `
            -Uri "$apiRoot/actions/runs/$($candidate.workflow_run.id)" `
            -Headers $headers `
            -Method Get
        if (-not ([string]$workflowRun.head_sha).Equals(
            $normalizedGitSha,
            [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }
        if ($workflowRun.path -ne '.github/workflows/client-contract.yml' -or
            $workflowRun.event -notin @('push', 'workflow_dispatch')) {
            continue
        }
        if ($workflowRun.status -eq 'completed' -and $workflowRun.conclusion -eq 'success') {
            $selectedArtifact = $candidate
            $selectedRun = $workflowRun
            break
        }
    }

    if ($null -ne $selectedArtifact) {
        break
    }
    if ($poll -eq $maximumPolls) {
        throw "No successful GitHub client-contract runtime artifact was found for '$normalizedGitSha' after waiting up to $MaxWaitSeconds seconds."
    }
    Write-Host "Waiting for GitHub client-contract to publish '$artifactName' (poll $($poll + 1)/$($maximumPolls + 1))."
    Start-Sleep -Seconds $PollIntervalSeconds
}

if ([string]$selectedArtifact.digest -notmatch '^sha256:[0-9a-fA-F]{64}$') {
    throw "GitHub did not provide a SHA-256 digest for artifact '$artifactName'."
}
if ([long]$selectedArtifact.size_in_bytes -le 0 -or
    [long]$selectedArtifact.size_in_bytes -gt 536870912) {
    throw "GitHub artifact '$artifactName' has an invalid or excessive size."
}

$resolvedOutputRoot = [IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $resolvedOutputRoot) {
    if (@(Get-ChildItem -LiteralPath $resolvedOutputRoot -Force).Count -gt 0) {
        throw "Runtime artifact output root '$resolvedOutputRoot' must be empty."
    }
}
else {
    [IO.Directory]::CreateDirectory($resolvedOutputRoot) | Out-Null
}

$archivePath = Join-Path ([IO.Path]::GetTempPath()) (
    "cp6-github-runtime-{0}-{1}.zip" -f
    $normalizedGitSha,
    [Guid]::NewGuid().ToString('N'))
try {
    Invoke-WebRequest `
        -Uri $selectedArtifact.archive_download_url `
        -Headers $headers `
        -Method Get `
        -OutFile $archivePath `
        -UseBasicParsing

    $expectedArchiveHash = ([string]$selectedArtifact.digest).Substring(7).ToLowerInvariant()
    $actualArchiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actualArchiveHash -ne $expectedArchiveHash) {
        throw "GitHub artifact archive digest does not match its API metadata."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
    try {
        $outputPrefix = $resolvedOutputRoot.TrimEnd(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        foreach ($entry in $archive.Entries) {
            $destinationPath = [IO.Path]::GetFullPath(
                (Join-Path $resolvedOutputRoot $entry.FullName))
            if (-not $destinationPath.StartsWith(
                $outputPrefix,
                [StringComparison]::OrdinalIgnoreCase)) {
                throw "GitHub artifact archive contains a path outside the output root."
            }
            if ([string]::IsNullOrEmpty($entry.Name)) {
                [IO.Directory]::CreateDirectory($destinationPath) | Out-Null
                continue
            }
            [IO.Directory]::CreateDirectory(
                (Split-Path -Parent $destinationPath)) | Out-Null
            [IO.Compression.ZipFileExtensions]::ExtractToFile(
                $entry,
                $destinationPath,
                $false)
        }
    }
    finally {
        $archive.Dispose()
    }

    $releaseVersion = "0.0.0-dev.$normalizedGitSha"
    & (Join-Path $PSScriptRoot "Test-Cp6DevRuntimeArtifact.ps1") `
        -ArtifactRoot $resolvedOutputRoot `
        -ExpectedReleaseVersion $releaseVersion `
        -ExpectedGitSha $normalizedGitSha

    Write-Host "Bridged GitHub Actions Run $($selectedRun.id) artifact '$artifactName' for $normalizedGitSha."
    return [pscustomobject]@{
        ArtifactId = [long]$selectedArtifact.id
        ArtifactName = $artifactName
        GitHubRunId = [long]$selectedRun.id
        GitSha = $normalizedGitSha
        ReleaseVersion = $releaseVersion
        ArchiveSha256 = $actualArchiveHash
        OutputRoot = $resolvedOutputRoot
    }
}
finally {
    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        Remove-Item -LiteralPath $archivePath -Force
    }
    [Environment]::SetEnvironmentVariable(
        "CP6_GITHUB_AUTHORIZATION",
        $null,
        "Process")
}
