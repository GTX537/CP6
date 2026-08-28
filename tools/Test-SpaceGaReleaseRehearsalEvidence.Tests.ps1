param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaReleaseRehearsalEvidence.ps1'
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-release-rehearsal\.tmp-' +
    [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function New-RehearsalEvidence([string]$Id) {
    return [pscustomobject]@{
        uri = "urn:cp6-space-ga-evidence:test:rehearsal:$Id"
        sha256 = '1' * 64
        acceptedBy = 'Zhang Wei'
        acceptedAtUtc = '2026-08-27T15:05:00Z'
    }
}

function New-ValidRehearsalManifest {
    return [pscustomobject]@{
        schemaVersion = 1
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'WP8_RELEASE_REHEARSAL'
        conclusion = 'Pass'
        ownerName = 'Zhang Wei'
        executedAtUtc = '2026-08-27T15:00:00Z'
        applicationCommitSha = 'a' * 40
        sourceSetSha256 = 'b' * 64
        goldenDatasetSha256 = 'c' * 64
        workerEnvironmentSha256 = 'd' * 64
        environment = [pscustomobject]@{
            mode = 'ControlledReleaseRehearsal'
            deploymentClass = 'LocalControlledNonProduction'
            databaseEngine = 'SQLServer'
            wmsSystem = 'CP6_WMS'
            wmsAdapter = 'CP6.Space.Infrastructure.Cp6SpaceWmsAdapter'
            cp6WmsDataSourceKind = 'Real'
            controlledFaultInjection = $true
            publishedViewerOnly = $true
            signedJwtHttpSecurity = $true
            secretsByReferenceOnly = $true
        }
        results = [pscustomobject]@{
            cadDwgDxfEndToEndPassed = $true
            threeAuthoringPathsPassed = $true
            publishAndWmsPassed = $true
            publishedDraftIsolationPassed = $true
            recoveryPassed = $true
            securityNegativePassed = $true
            noDuplicateWrites = $true
        }
        recovery = [pscustomobject]@{
            automaticRecoveryMaxMinutes = 12
            manualRecoveryMaxMinutes = 180
            oldPublishedRemainedAvailable = $true
        }
        defects = [pscustomobject]@{
            s1Open = 0
            s2Open = 0
            blockingS3Open = 0
        }
        evidence = [pscustomobject]@{
            execution = New-RehearsalEvidence 'execution'
            publishWms = New-RehearsalEvidence 'publish-wms'
            viewer = New-RehearsalEvidence 'viewer'
            recovery = New-RehearsalEvidence 'recovery'
            security = New-RehearsalEvidence 'security'
        }
        boundaries = [pscustomobject]@{
            productionDataClaimed = $false
            productionWmsClaimed = $false
            productionDeploymentPerformed = $false
            pilotRequired = $false
            distinctPersonReviewRequired = $false
        }
        selfReview = [pscustomobject]@{
            acceptedBy = 'Zhang Wei'
            acceptedAtUtc = '2026-08-27T15:05:00Z'
            repeatable = $true
            distinctPersonReviewRequired = $false
        }
    }
}

if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
    $exportPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $exportPath) -Force)
    New-ValidRehearsalManifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $exportPath -Encoding UTF8
    [System.IO.Directory]::Delete($tempDirectory, $true)
    exit 0
}

function New-RehearsalTestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )
    $manifest = New-ValidRehearsalManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-RehearsalCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [string]$ExpectedOwnerName,
        [bool]$AllowTestFixtures = $true
    )
    $args = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $validator,
        '-ManifestPath', $ManifestPath)
    if ($AllowTestFixtures) { $args += '-AllowTestFixtures' }
    if (![string]::IsNullOrWhiteSpace($ExpectedOwnerName)) {
        $args += @('-ExpectedOwnerName', $ExpectedOwnerName)
    }
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable @args 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $old }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    $script:passed++
    $global:LASTEXITCODE = 0
}

try {
    $validPath = New-RehearsalTestManifest 'valid' { param($manifest) }
    Invoke-RehearsalCase -Name 'valid controlled rehearsal' `
        -ManifestPath $validPath -ShouldPass $true

    Invoke-RehearsalCase -Name 'formal mode rejects test evidence' `
        -ManifestPath $validPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_EVIDENCE_SYNTHETIC' `
        -AllowTestFixtures $false

    $ownerPath = New-RehearsalTestManifest 'owner' {
        param($manifest); $manifest.ownerName = '00001'
    }
    Invoke-RehearsalCase -Name 'owner must be real' `
        -ManifestPath $ownerPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_OWNER_INVALID'

    Invoke-RehearsalCase -Name 'owner matches WP8 owner' `
        -ManifestPath $validPath -ShouldPass $false `
        -ExpectedOwnerName 'Different Person' `
        -ExpectedError 'SPACE_GA_REHEARSAL_OWNER_MISMATCH'

    $environmentPath = New-RehearsalTestManifest 'environment' {
        param($manifest); $manifest.environment.publishedViewerOnly = $false
    }
    Invoke-RehearsalCase -Name 'Published-only environment is required' `
        -ManifestPath $environmentPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_ENVIRONMENT_INVALID'

    $resultPath = New-RehearsalTestManifest 'result' {
        param($manifest); $manifest.results.publishAndWmsPassed = $false
    }
    Invoke-RehearsalCase -Name 'all release results pass' `
        -ManifestPath $resultPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_RESULT_FAILED'

    $recoveryPath = New-RehearsalTestManifest 'recovery' {
        param($manifest); $manifest.recovery.automaticRecoveryMaxMinutes = 16
    }
    Invoke-RehearsalCase -Name 'recovery threshold is enforced' `
        -ManifestPath $recoveryPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_RECOVERY_FAILED'

    $defectPath = New-RehearsalTestManifest 'defect' {
        param($manifest); $manifest.defects.s1Open = 1
    }
    Invoke-RehearsalCase -Name 'blocking defects are closed' `
        -ManifestPath $defectPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_DEFECTS_OPEN'

    $boundaryPath = New-RehearsalTestManifest 'production-boundary' {
        param($manifest)
        $manifest.boundaries.productionDeploymentPerformed = $true
    }
    Invoke-RehearsalCase -Name 'controlled rehearsal is not deployment' `
        -ManifestPath $boundaryPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_BOUNDARY_INVALID'

    $evidenceOwnerPath = New-RehearsalTestManifest 'evidence-owner' {
        param($manifest); $manifest.evidence.viewer.acceptedBy = 'Different Person'
    }
    Invoke-RehearsalCase -Name 'Owner accepts evidence' `
        -ManifestPath $evidenceOwnerPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_EVIDENCE_OWNER_INVALID'

    $timePath = New-RehearsalTestManifest 'evidence-time' {
        param($manifest); $manifest.evidence.security.acceptedAtUtc = '2026-08-27T14:00:00Z'
    }
    Invoke-RehearsalCase -Name 'evidence follows execution' `
        -ManifestPath $timePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_EVIDENCE_TIME_INVALID'

    [ordered]@{
        suite = 'CP6_SPACE_GA_RELEASE_REHEARSAL_EVIDENCE'
        passed = $passed
        failed = 0
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
    $parent = Split-Path -Parent $tempDirectory
    if ((Test-Path -LiteralPath $parent -PathType Container) -and
        @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($parent)
    }
}
