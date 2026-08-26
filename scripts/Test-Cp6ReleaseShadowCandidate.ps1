[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [string]$CandidateResultUri,

    [Parameter(Mandatory = $true)]
    [string]$AllowedEvidenceRootUri,

    [Parameter(Mandatory = $true)]
    [string]$FixtureDirectory,

    [string]$OutputReportPath,

    [string]$AzureRunId = '0',

    [string]$AzurePipelineName = 'CP6 Release Shadow S0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Assert-Condition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Assert-ExactProperties {
    param(
        [Parameter(Mandatory = $true)]$Object,
        [Parameter(Mandatory = $true)][string[]]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-Condition -Condition ($null -ne $Object) -Message "$Description is required."
    $actual = @($Object.PSObject.Properties.Name)
    $missing = @($Expected | Where-Object { $actual -cnotcontains $_ })
    $unknown = @($actual | Where-Object { $Expected -cnotcontains $_ })
    if ($missing.Count -gt 0 -or $unknown.Count -gt 0) {
        throw (
            "$Description schema mismatch. Missing=[$($missing -join ', ')]; " +
            "Unknown=[$($unknown -join ', ')]."
        )
    }
}

function Read-JsonObject {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    Assert-Condition -Condition ($item.Length -gt 0) -Message "$Description must not be empty."
    Assert-Condition -Condition ($item.Length -le 1MB) -Message "$Description exceeds the 1 MiB S0 limit."
    try {
        return [IO.File]::ReadAllText($item.FullName, [Text.Encoding]::UTF8) |
            ConvertFrom-Json
    }
    catch {
        throw "$Description is not valid JSON: $($_.Exception.Message)"
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-Condition -Condition ($Value -cmatch '^[A-Fa-f0-9]{64}$') `
        -Message "$Description must be a complete SHA-256 value."
}

function Assert-IsoTimestamp {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $parsed = [DateTimeOffset]::MinValue
    Assert-Condition -Condition (
        [DateTimeOffset]::TryParse(
            $Value,
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref]$parsed
        )
    ) -Message "$Description must be an ISO-8601 timestamp."
}

function Get-NormalizedEvidenceRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $uri = $Value -as [Uri]
    Assert-Condition -Condition (
        $null -ne $uri -and
        $uri.IsAbsoluteUri -and
        @('s3', 'https') -contains $uri.Scheme -and
        -not [string]::IsNullOrWhiteSpace($uri.Host) -and
        [string]::IsNullOrWhiteSpace($uri.UserInfo) -and
        [string]::IsNullOrWhiteSpace($uri.Query) -and
        [string]::IsNullOrWhiteSpace($uri.Fragment)
    ) -Message "$Description must be an absolute s3:// or HTTPS URI without credentials, query, or fragment."

    return $Value.TrimEnd('/')
}

function Assert-EvidenceReference {
    param(
        [Parameter(Mandatory = $true)]$Reference,
        [Parameter(Mandatory = $true)][string]$Description
    )

    Assert-ExactProperties -Object $Reference `
        -Expected @('FileName', 'Bytes', 'Sha256') `
        -Description $Description
    Assert-Condition -Condition (
        -not [string]::IsNullOrWhiteSpace([string]$Reference.FileName) -and
        [string]$Reference.FileName -notmatch '[\\/]' -and
        [string]$Reference.FileName -notmatch '^\.{1,2}$'
    ) -Message "$Description FileName must be a leaf file name."
    Assert-Condition -Condition ([int64]$Reference.Bytes -gt 0) `
        -Message "$Description Bytes must be positive."
    Assert-Sha256 -Value ([string]$Reference.Sha256) `
        -Description "$Description Sha256"
}

function Assert-ReleaseArtifact {
    param(
        [Parameter(Mandatory = $true)]$Artifact,
        [Parameter(Mandatory = $true)][string]$ExpectedKind
    )

    Assert-ExactProperties -Object $Artifact `
        -Expected @('Kind', 'FileName', 'Bytes', 'Sha256', 'DownloadUrl', 'Signer') `
        -Description "Artifact '$ExpectedKind'"
    Assert-Condition -Condition ([string]$Artifact.Kind -ceq $ExpectedKind) `
        -Message "Artifact kind must be '$ExpectedKind'."
    Assert-Condition -Condition (
        -not [string]::IsNullOrWhiteSpace([string]$Artifact.FileName) -and
        [string]$Artifact.FileName -notmatch '[\\/]'
    ) -Message "Artifact '$ExpectedKind' FileName must be a leaf file name."
    Assert-Condition -Condition ([int64]$Artifact.Bytes -gt 0) `
        -Message "Artifact '$ExpectedKind' Bytes must be positive."
    Assert-Sha256 -Value ([string]$Artifact.Sha256) `
        -Description "Artifact '$ExpectedKind' Sha256"

    $downloadUri = [string]$Artifact.DownloadUrl -as [Uri]
    Assert-Condition -Condition (
        $null -ne $downloadUri -and
        $downloadUri.IsAbsoluteUri -and
        $downloadUri.Scheme -eq 'https' -and
        [string]::IsNullOrWhiteSpace($downloadUri.UserInfo)
    ) -Message "Artifact '$ExpectedKind' DownloadUrl must be credential-free HTTPS."

    if ($ExpectedKind -eq 'android-apk') {
        Assert-ExactProperties -Object $Artifact.Signer `
            -Expected @('Type', 'Identity', 'CertificateSha256') `
            -Description "Artifact '$ExpectedKind' signer"
        Assert-Condition -Condition ([string]$Artifact.Signer.Type -ceq 'APK') `
            -Message "Artifact '$ExpectedKind' signer type must be APK."
        Assert-Sha256 -Value ([string]$Artifact.Signer.CertificateSha256) `
            -Description "Artifact '$ExpectedKind' signer certificate"
    }
    else {
        Assert-ExactProperties -Object $Artifact.Signer `
            -Expected @('Type', 'Identity', 'CertificateThumbprint') `
            -Description "Artifact '$ExpectedKind' signer"
        Assert-Condition -Condition (
            [string]$Artifact.Signer.Type -in @('Authenticode', 'MSIXReference')
        ) -Message "Artifact '$ExpectedKind' signer type is invalid."
        Assert-Condition -Condition (
            [string]$Artifact.Signer.CertificateThumbprint -cmatch '^[A-Fa-f0-9]{40}$'
        ) -Message "Artifact '$ExpectedKind' signer thumbprint must be complete."
    }
    Assert-Condition -Condition (
        -not [string]::IsNullOrWhiteSpace([string]$Artifact.Signer.Identity)
    ) -Message "Artifact '$ExpectedKind' signer identity is required."
}

$fixtureRoot = (Resolve-Path -LiteralPath $FixtureDirectory -ErrorAction Stop).Path
$paths = [ordered]@{
    Candidate = Join-Path $fixtureRoot 'candidate-result.json'
    Manifest = Join-Path $fixtureRoot 'release-manifest.json'
    Freeze = Join-Path $fixtureRoot 'release-freeze.json'
    Spec = Join-Path $fixtureRoot 'candidate.yaml'
}
foreach ($entry in $paths.GetEnumerator()) {
    Assert-Condition -Condition (Test-Path -LiteralPath $entry.Value -PathType Leaf) `
        -Message "S0 fixture '$($entry.Key)' is missing."
}

$allowedRoot = Get-NormalizedEvidenceRoot `
    -Value $AllowedEvidenceRootUri `
    -Description 'AllowedEvidenceRootUri'
$releaseRoot = "$allowedRoot/v$ExpectedVersion"
$expectedCandidateUri = "$releaseRoot/candidate-result.json"
Assert-Condition -Condition ($CandidateResultUri -ceq $expectedCandidateUri) `
    -Message "CandidateResultUri must equal '$expectedCandidateUri'."

$candidate = Read-JsonObject -Path $paths.Candidate -Description 'candidate-result.json'
Assert-ExactProperties -Object $candidate -Expected @(
    'SchemaVersion',
    'ReleaseVersion',
    'Tag',
    'GitSha',
    'GeneratedAtUtc',
    'ManifestUri',
    'ManifestSha256',
    'FreezeSnapshotUri',
    'FreezeSnapshotSha256',
    'ExecutionSpecPath',
    'ExecutionSpecSha256'
) -Description 'candidate-result.json'
Assert-Condition -Condition ([int]$candidate.SchemaVersion -eq 1) `
    -Message 'candidate-result.json SchemaVersion must be 1.'
Assert-Condition -Condition ([string]$candidate.ReleaseVersion -ceq $ExpectedVersion) `
    -Message 'candidate-result.json ReleaseVersion does not match the requested version.'
Assert-Condition -Condition ([string]$candidate.Tag -ceq "v$ExpectedVersion") `
    -Message 'candidate-result.json Tag must be the immutable SemVer tag.'
Assert-Condition -Condition ([string]$candidate.GitSha -cmatch '^[a-f0-9]{40}$') `
    -Message 'candidate-result.json GitSha must be a complete lowercase commit SHA.'
Assert-IsoTimestamp -Value ([string]$candidate.GeneratedAtUtc) `
    -Description 'candidate-result.json GeneratedAtUtc'

$expectedSpecPath = "docs/client/r2/releases/v$ExpectedVersion/candidate.yaml"
Assert-Condition -Condition ([string]$candidate.ManifestUri -ceq "$releaseRoot/release-manifest.json") `
    -Message 'candidate-result.json ManifestUri is outside the approved release root.'
Assert-Condition -Condition ([string]$candidate.FreezeSnapshotUri -ceq "$releaseRoot/release-freeze.json") `
    -Message 'candidate-result.json FreezeSnapshotUri is outside the approved release root.'
Assert-Condition -Condition ([string]$candidate.ExecutionSpecPath -ceq $expectedSpecPath) `
    -Message 'candidate-result.json ExecutionSpecPath is not the versioned candidate spec.'
foreach ($hashProperty in @(
    'ManifestSha256',
    'FreezeSnapshotSha256',
    'ExecutionSpecSha256'
)) {
    Assert-Sha256 -Value ([string]$candidate.$hashProperty) `
        -Description "candidate-result.json $hashProperty"
}

$actualManifestHash = Get-Sha256 -Path $paths.Manifest
$actualFreezeHash = Get-Sha256 -Path $paths.Freeze
$actualSpecHash = Get-Sha256 -Path $paths.Spec
Assert-Condition -Condition (
    $actualManifestHash -ceq ([string]$candidate.ManifestSha256).ToUpperInvariant()
) -Message 'release-manifest.json SHA-256 does not match candidate-result.json.'
Assert-Condition -Condition (
    $actualFreezeHash -ceq ([string]$candidate.FreezeSnapshotSha256).ToUpperInvariant()
) -Message 'release-freeze.json SHA-256 does not match candidate-result.json.'
Assert-Condition -Condition (
    $actualSpecHash -ceq ([string]$candidate.ExecutionSpecSha256).ToUpperInvariant()
) -Message 'candidate.yaml SHA-256 does not match candidate-result.json.'

$manifest = Read-JsonObject -Path $paths.Manifest -Description 'release-manifest.json'
Assert-ExactProperties -Object $manifest -Expected @(
    'SchemaVersion',
    'ReleaseVersion',
    'GitSha',
    'GeneratedAtUtc',
    'EvidenceRootUri',
    'ExecutionSpec',
    'Artifacts',
    'Images',
    'SupplyChain',
    'Database'
) -Description 'release-manifest.json'
Assert-Condition -Condition ([int]$manifest.SchemaVersion -eq 2) `
    -Message 'release-manifest.json SchemaVersion must be 2.'
Assert-Condition -Condition ([string]$manifest.ReleaseVersion -ceq $ExpectedVersion) `
    -Message 'release-manifest.json ReleaseVersion does not match.'
Assert-Condition -Condition ([string]$manifest.GitSha -ceq [string]$candidate.GitSha) `
    -Message 'release-manifest.json GitSha does not match candidate-result.json.'
Assert-IsoTimestamp -Value ([string]$manifest.GeneratedAtUtc) `
    -Description 'release-manifest.json GeneratedAtUtc'
Assert-Condition -Condition ([string]$manifest.EvidenceRootUri -ceq $releaseRoot) `
    -Message 'release-manifest.json EvidenceRootUri does not match the approved release root.'

Assert-ExactProperties -Object $manifest.ExecutionSpec -Expected @(
    'Version',
    'RepositoryPath',
    'SpecSha256',
    'FreezeSnapshotUri',
    'FreezeSnapshotSha256',
    'ChangeTicket',
    'ApprovedAt'
) -Description 'release-manifest.json ExecutionSpec'
Assert-Condition -Condition ([int]$manifest.ExecutionSpec.Version -eq 1) `
    -Message 'release-manifest.json ExecutionSpec.Version must be 1.'
Assert-Condition -Condition ([string]$manifest.ExecutionSpec.RepositoryPath -ceq $expectedSpecPath) `
    -Message 'release-manifest.json ExecutionSpec.RepositoryPath does not match.'
Assert-Condition -Condition (
    ([string]$manifest.ExecutionSpec.SpecSha256).ToUpperInvariant() -ceq $actualSpecHash
) -Message 'release-manifest.json ExecutionSpec.SpecSha256 does not match candidate.yaml.'
Assert-Condition -Condition (
    [string]$manifest.ExecutionSpec.FreezeSnapshotUri -ceq [string]$candidate.FreezeSnapshotUri
) -Message 'release-manifest.json FreezeSnapshotUri does not match candidate-result.json.'
Assert-Condition -Condition (
    ([string]$manifest.ExecutionSpec.FreezeSnapshotSha256).ToUpperInvariant() -ceq $actualFreezeHash
) -Message 'release-manifest.json FreezeSnapshotSha256 does not match release-freeze.json.'
Assert-Condition -Condition (
    -not [string]::IsNullOrWhiteSpace([string]$manifest.ExecutionSpec.ChangeTicket)
) -Message 'release-manifest.json ExecutionSpec.ChangeTicket is required.'
Assert-IsoTimestamp -Value ([string]$manifest.ExecutionSpec.ApprovedAt) `
    -Description 'release-manifest.json ExecutionSpec.ApprovedAt'

Assert-Condition -Condition (@($manifest.Artifacts).Count -eq 3) `
    -Message 'release-manifest.json must contain exactly three signed native artifacts.'
foreach ($kind in @('windows-msix', 'windows-appinstaller', 'android-apk')) {
    $matches = @($manifest.Artifacts | Where-Object { [string]$_.Kind -ceq $kind })
    Assert-Condition -Condition ($matches.Count -eq 1) `
        -Message "release-manifest.json must contain exactly one '$kind' artifact."
    Assert-ReleaseArtifact -Artifact $matches[0] -ExpectedKind $kind
}

Assert-ExactProperties -Object $manifest.Images -Expected @('Api', 'Web') `
    -Description 'release-manifest.json Images'
$allowedImages = [ordered]@{
    Api = 'ghcr.io/gtx537/cp6-api'
    Web = 'ghcr.io/gtx537/cp6-web'
}
foreach ($imageName in $allowedImages.Keys) {
    $image = $manifest.Images.$imageName
    Assert-ExactProperties -Object $image -Expected @('Repository', 'Digest') `
        -Description "release-manifest.json Images.$imageName"
    Assert-Condition -Condition (
        [string]$image.Repository -ceq [string]$allowedImages[$imageName]
    ) -Message "release-manifest.json Images.$imageName Repository is outside the allowlist."
    Assert-Condition -Condition (
        [string]$image.Digest -cmatch '^sha256:[a-f0-9]{64}$'
    ) -Message "release-manifest.json Images.$imageName Digest must be a complete immutable digest."
}

Assert-ExactProperties -Object $manifest.SupplyChain -Expected @(
    'Sbom',
    'VulnerabilityReport',
    'SourceGateReport',
    'SqlIntegrationReport'
) -Description 'release-manifest.json SupplyChain'
foreach ($property in @(
    'Sbom',
    'VulnerabilityReport',
    'SourceGateReport',
    'SqlIntegrationReport'
)) {
    Assert-EvidenceReference -Reference $manifest.SupplyChain.$property `
        -Description "release-manifest.json SupplyChain.$property"
}

Assert-ExactProperties -Object $manifest.Database -Expected @(
    'LatestMigration',
    'InitializationArtifact',
    'MigrationPolicy'
) -Description 'release-manifest.json Database'
Assert-Condition -Condition (
    -not [string]::IsNullOrWhiteSpace([string]$manifest.Database.LatestMigration)
) -Message 'release-manifest.json Database.LatestMigration is required.'
Assert-Condition -Condition ([string]$manifest.Database.MigrationPolicy -ceq 'ForwardOnly') `
    -Message 'release-manifest.json Database.MigrationPolicy must be ForwardOnly.'
Assert-EvidenceReference -Reference $manifest.Database.InitializationArtifact `
    -Description 'release-manifest.json Database.InitializationArtifact'

$freeze = Read-JsonObject -Path $paths.Freeze -Description 'release-freeze.json'
Assert-ExactProperties -Object $freeze -Expected @(
    'schemaVersion',
    'status',
    'releaseVersion',
    'tag',
    'gitSha',
    'repositoryPath',
    'specSha256',
    'changeTicket',
    'approvedAt',
    'approvalExpiresAt',
    'generatedAtUtc',
    'generatedBy',
    'workflowRunUri',
    'evidenceRootUri',
    'environments',
    'runners',
    'deployment',
    'inputs'
) -Description 'release-freeze.json'
Assert-Condition -Condition ([int]$freeze.schemaVersion -eq 1) `
    -Message 'release-freeze.json schemaVersion must be 1.'
Assert-Condition -Condition ([string]$freeze.status -ceq 'Approved') `
    -Message 'release-freeze.json status must be Approved.'
Assert-Condition -Condition ([string]$freeze.releaseVersion -ceq $ExpectedVersion) `
    -Message 'release-freeze.json releaseVersion does not match.'
Assert-Condition -Condition ([string]$freeze.tag -ceq "v$ExpectedVersion") `
    -Message 'release-freeze.json tag does not match.'
Assert-Condition -Condition ([string]$freeze.gitSha -ceq [string]$candidate.GitSha) `
    -Message 'release-freeze.json gitSha does not match candidate-result.json.'
Assert-Condition -Condition ([string]$freeze.repositoryPath -ceq $expectedSpecPath) `
    -Message 'release-freeze.json repositoryPath does not match.'
Assert-Condition -Condition (
    ([string]$freeze.specSha256).ToUpperInvariant() -ceq $actualSpecHash
) -Message 'release-freeze.json specSha256 does not match candidate.yaml.'
Assert-Condition -Condition ([string]$freeze.evidenceRootUri -ceq $releaseRoot) `
    -Message 'release-freeze.json evidenceRootUri does not match.'
Assert-Condition -Condition (
    [string]$freeze.changeTicket -ceq [string]$manifest.ExecutionSpec.ChangeTicket
) -Message 'release-freeze.json changeTicket does not match release-manifest.json.'
Assert-Condition -Condition (
    [string]$freeze.approvedAt -ceq [string]$manifest.ExecutionSpec.ApprovedAt
) -Message 'release-freeze.json approvedAt does not match release-manifest.json.'
foreach ($dateProperty in @('approvedAt', 'approvalExpiresAt', 'generatedAtUtc')) {
    Assert-IsoTimestamp -Value ([string]$freeze.$dateProperty) `
        -Description "release-freeze.json $dateProperty"
}
Assert-Condition -Condition (
    -not [string]::IsNullOrWhiteSpace([string]$freeze.generatedBy)
) -Message 'release-freeze.json generatedBy is required.'
$workflowRunUri = [string]$freeze.workflowRunUri -as [Uri]
Assert-Condition -Condition (
    $null -ne $workflowRunUri -and $workflowRunUri.Scheme -eq 'https'
) -Message 'release-freeze.json workflowRunUri must be HTTPS.'
Assert-Condition -Condition (
    $null -ne $freeze.environments -and
    $null -ne $freeze.runners -and
    $null -ne $freeze.deployment -and
    @($freeze.inputs).Count -gt 0
) -Message 'release-freeze.json must retain approved environment, runner, deployment, and input bindings.'

$report = [ordered]@{
    SchemaVersion = 1
    Authority = 'Shadow'
    Deployable = $false
    Mode = 'S0OfflineFixture'
    ReleaseVersion = $ExpectedVersion
    GitSha = [string]$candidate.GitSha
    CandidateResult = [ordered]@{
        Uri = $CandidateResultUri
        Sha256 = Get-Sha256 -Path $paths.Candidate
    }
    Manifest = [ordered]@{
        Uri = [string]$candidate.ManifestUri
        Sha256 = $actualManifestHash
    }
    FreezeSnapshot = [ordered]@{
        Uri = [string]$candidate.FreezeSnapshotUri
        Sha256 = $actualFreezeHash
    }
    ExecutionSpec = [ordered]@{
        RepositoryPath = $expectedSpecPath
        Sha256 = $actualSpecHash
    }
    Images = [ordered]@{
        Api = [ordered]@{
            Repository = [string]$manifest.Images.Api.Repository
            Digest = [string]$manifest.Images.Api.Digest
        }
        Web = [ordered]@{
            Repository = [string]$manifest.Images.Web.Repository
            Digest = [string]$manifest.Images.Web.Digest
        }
    }
    Checks = @(
        'CandidateResultSchema1',
        'ReleaseManifestSchema2',
        'FreezeSnapshotSchema1',
        'ObjectSha256Bindings',
        'GhcrDigestMetadata',
        'SupplyChainMetadata',
        'ForwardOnlyDatabaseMetadata',
        'ShadowNonDeployableSemantic'
    )
    Azure = [ordered]@{
        RunId = $AzureRunId
        Pipeline = $AzurePipelineName
    }
    VerifiedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
}

if (-not [string]::IsNullOrWhiteSpace($OutputReportPath)) {
    $reportParent = Split-Path -Parent $OutputReportPath
    if (-not [string]::IsNullOrWhiteSpace($reportParent)) {
        [IO.Directory]::CreateDirectory($reportParent) | Out-Null
    }
    [IO.File]::WriteAllText(
        $OutputReportPath,
        ($report | ConvertTo-Json -Depth 10) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
}

Write-Host "CP6 Release Shadow S0 candidate contract passed for v$ExpectedVersion."
[pscustomobject]$report
