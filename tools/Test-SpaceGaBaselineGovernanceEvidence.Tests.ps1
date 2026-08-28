param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaBaselineGovernanceEvidence.ps1'
$repo = Split-Path -Parent $PSScriptRoot
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-baseline-governance\.tmp-' +
    [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function New-ValidBaselineManifest {
    $head = (& git -C $repo rev-parse HEAD).Trim()
    $candidate = 'docs/space/acceptance/v1.3-ga/authorized-golden-cad-candidates-v1.0.0.json'
    $primary = 'docs/space/acceptance/v1.3-ga/autocad-primary-input-v1.0.0.json'
    return [pscustomobject]@{
        schemaVersion = 1
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'WP0_BASELINE_GOVERNANCE_FORMAL_EVIDENCE'
        conclusion = 'Pass'
        ownerName = 'Zhang Wei'
        acceptedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-5).ToString(
            'yyyy-MM-ddTHH:mm:ssZ',
            [System.Globalization.CultureInfo]::InvariantCulture)
        kickoffDate = '2026-08-27'
        targetGaDate = '2026-09-27'
        baseline = [pscustomobject]@{
            mainCommitSha = $head
            branch = 'main'
            remote = 'origin'
            commitPresent = $true
            postMergeSmokePassed = $true
            workspaceClean = $true
            productionDeploymentPerformed = $false
        }
        governance = [pscustomobject]@{
            singleDeliveryOwner = $true
            distinctPersonQuorumRequired = $false
            allGateOwnersAssigned = $true
            externalInputOwnersAssigned = $true
        }
        externalInputs = @(
            [pscustomobject]@{
                id = 'AUTHORIZED_GOLDEN_CAD_CANDIDATES'
                ownerName = 'Zhang Wei'
                status = 'Complete'
                verificationManifest = $candidate
                evidenceSha256 = (Get-FileHash -LiteralPath (Join-Path $repo $candidate) -Algorithm SHA256).Hash.ToLowerInvariant()
            },
            [pscustomobject]@{
                id = 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER'
                ownerName = 'Zhang Wei'
                status = 'Complete'
                verificationManifest = $primary
                evidenceSha256 = (Get-FileHash -LiteralPath (Join-Path $repo $primary) -Algorithm SHA256).Hash.ToLowerInvariant()
            })
        acceptedDependencies = @(
            'WP3_PRIMARY_PROVIDER_AND_ISOLATED_WORKER',
            'WP4_THREE_PATH_END_TO_END',
            'WP7_GOLDEN_CAD_FORMAL_EVIDENCE')
        smoke = [pscustomobject]@{
            gaValidator = [pscustomobject]@{
                passed = $true
                pendingInputs = 0
                pendingGatesBeforeWp0Acceptance = 6
                pendingSigners = 1
            }
            threePathEvidenceTests = [pscustomobject]@{ passed = 11; failed = 0 }
            gaEvidenceAttestationTests = [pscustomobject]@{ passed = 42; failed = 0 }
        }
    }
}

if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
    $exportPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $exportPath) -Force)
    New-ValidBaselineManifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $exportPath -Encoding UTF8
    [System.IO.Directory]::Delete($tempDirectory, $true)
    exit 0
}

function New-BaselineTestManifest {
    param([Parameter(Mandatory)][string]$Name, [Parameter(Mandatory)][scriptblock]$Mutation)
    $manifest = New-ValidBaselineManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}
function Invoke-BaselineCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [string]$ExpectedOwnerName,
        [string]$ExpectedKickoffDate,
        [string]$ExpectedTargetGaDate
    )
    $args = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$validator,'-ManifestPath',$ManifestPath)
    if ($ExpectedOwnerName) { $args += @('-ExpectedOwnerName',$ExpectedOwnerName) }
    if ($ExpectedKickoffDate) { $args += @('-ExpectedKickoffDate',$ExpectedKickoffDate) }
    if ($ExpectedTargetGaDate) { $args += @('-ExpectedTargetGaDate',$ExpectedTargetGaDate) }
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $output = & $hostExecutable @args 2>&1 | Out-String; $exitCode = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    if ($ShouldPass -and $exitCode -ne 0) { throw "$Name should pass but exited $exitCode.`n$output" }
    if (!$ShouldPass -and $exitCode -eq 0) { throw "$Name should fail but exited 0.`n$output" }
    if (!$ShouldPass -and $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    $script:passed++
    $global:LASTEXITCODE = 0
}

try {
    $valid = New-BaselineTestManifest 'valid' { param($manifest) }
    Invoke-BaselineCase -Name 'valid baseline governance evidence' -ManifestPath $valid -ShouldPass $true `
        -ExpectedOwnerName 'Zhang Wei' -ExpectedKickoffDate '2026-08-27' -ExpectedTargetGaDate '2026-09-27'

    $owner = New-BaselineTestManifest 'owner' { param($manifest); $manifest.ownerName = '00001' }
    Invoke-BaselineCase -Name 'owner is real' -ManifestPath $owner -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_OWNER_INVALID'

    Invoke-BaselineCase -Name 'target date matches index' -ManifestPath $valid -ShouldPass $false `
        -ExpectedTargetGaDate '2026-10-01' -ExpectedError 'SPACE_GA_BASELINE_TARGET_MISMATCH'

    $commit = New-BaselineTestManifest 'commit' { param($manifest); $manifest.baseline.mainCommitSha = '0' * 40 }
    Invoke-BaselineCase -Name 'commit exists' -ManifestPath $commit -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_COMMIT_MISSING'

    $production = New-BaselineTestManifest 'production' { param($manifest); $manifest.baseline.productionDeploymentPerformed = $true }
    Invoke-BaselineCase -Name 'production is not claimed' -ManifestPath $production -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_MAIN_INVALID'

    $inputSet = New-BaselineTestManifest 'input-set' { param($manifest); $manifest.externalInputs = @($manifest.externalInputs[0]) }
    Invoke-BaselineCase -Name 'two inputs are required' -ManifestPath $inputSet -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_INPUT_SET_INVALID'

    $inputHash = New-BaselineTestManifest 'input-hash' { param($manifest); $manifest.externalInputs[0].evidenceSha256 = '0' * 64 }
    Invoke-BaselineCase -Name 'input hash matches' -ManifestPath $inputHash -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_INPUT_HASH_MISMATCH'

    $dependency = New-BaselineTestManifest 'dependency' { param($manifest); $manifest.acceptedDependencies = @($manifest.acceptedDependencies[0..1]) }
    Invoke-BaselineCase -Name 'accepted dependencies are complete' -ManifestPath $dependency -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_DEPENDENCIES_INVALID'

    $smoke = New-BaselineTestManifest 'smoke' { param($manifest); $manifest.smoke.gaEvidenceAttestationTests.failed = 1 }
    Invoke-BaselineCase -Name 'post-merge smoke passes' -ManifestPath $smoke -ShouldPass $false `
        -ExpectedError 'SPACE_GA_BASELINE_SMOKE_FAILED'

    [ordered]@{ suite = 'CP6_SPACE_GA_BASELINE_GOVERNANCE_EVIDENCE'; passed = $passed; failed = 0 } |
        ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) { [System.IO.Directory]::Delete($tempDirectory, $true) }
    $parent = Split-Path -Parent $tempDirectory
    if ((Test-Path -LiteralPath $parent -PathType Container) -and
        @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($parent)
    }
}
