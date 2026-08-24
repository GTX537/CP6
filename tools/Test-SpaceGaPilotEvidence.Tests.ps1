$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaPilotEvidence.ps1'
$fixtureDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-pilot-evidence')
$validFixture = Join-Path $fixtureDirectory 'valid-pilot-evidence.json'
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $fixtureDirectory (
    '.tmp-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory)
$passed = 0

function New-PilotTestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )

    $manifest = Get-Content -LiteralPath $validFixture -Raw |
        ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-PilotValidatorCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [bool]$AllowTestFixtures = $true
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $arguments = @(
            '-NoProfile',
            '-ExecutionPolicy',
            'Bypass',
            '-File',
            $validator,
            '-ManifestPath',
            $ManifestPath)
        if ($AllowTestFixtures) {
            $arguments += '-AllowTestFixtures'
        }
        $output = & $hostExecutable @arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and
        ![string]::IsNullOrWhiteSpace($ExpectedError) -and
        $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    $script:passed++
}

try {
    Invoke-PilotValidatorCase `
        -Name 'valid two-site pilot package' `
        -ManifestPath $validFixture `
        -ShouldPass $true

    Invoke-PilotValidatorCase `
        -Name 'formal validation rejects the synthetic positive fixture' `
        -ManifestPath $validFixture `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_EVIDENCE_SYNTHETIC' `
        -AllowTestFixtures $false

    $oneSitePath = New-PilotTestManifest 'one-site' {
        param($manifest)
        $manifest.sites = @($manifest.sites[0])
    }
    Invoke-PilotValidatorCase `
        -Name 'both pilot site types are mandatory' `
        -ManifestPath $oneSitePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_SITE_SET_INVALID'

    $duplicateSitePath = New-PilotTestManifest 'duplicate-site' {
        param($manifest)
        $manifest.sites[1].siteRef = $manifest.sites[0].siteRef
        $manifest.sites[1].siteType = 'Greenfield'
    }
    Invoke-PilotValidatorCase `
        -Name 'pilot site identity and type are unique' `
        -ManifestPath $duplicateSitePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_SITE_TYPES_INVALID'

    $shortRunPath = New-PilotTestManifest 'short-run' {
        param($manifest)
        $manifest.sites[0].runEndDate = '2026-07-13'
        $manifest.sites[0].continuousRunDays = 13
        $manifest.sites[0].dailyRecordCount = 13
    }
    Invoke-PilotValidatorCase `
        -Name 'thirteen-day pilot cannot pass' `
        -ManifestPath $shortRunPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_CONTINUITY_INVALID'

    $missingDayPath = New-PilotTestManifest 'missing-daily-record' {
        param($manifest)
        $manifest.sites[0].dailyRecordCount = 13
    }
    Invoke-PilotValidatorCase `
        -Name 'every pilot day needs an immutable record' `
        -ManifestPath $missingDayPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_CONTINUITY_INVALID'

    $duplicateDayPath = New-PilotTestManifest 'duplicate-daily-record' {
        param($manifest)
        $manifest.sites[0].dailyRecordDates[7] = (
            $manifest.sites[0].dailyRecordDates[6])
    }
    Invoke-PilotValidatorCase `
        -Name 'daily records cannot duplicate one date and skip another' `
        -ManifestPath $duplicateDayPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_CONTINUITY_INVALID'

    $futureRunPath = New-PilotTestManifest 'future-run' {
        param($manifest)
        $manifest.sites[0].runStartDate = '2099-01-01'
        $manifest.sites[0].runEndDate = '2099-01-14'
    }
    Invoke-PilotValidatorCase `
        -Name 'future calendar time cannot count as a completed pilot' `
        -ManifestPath $futureRunPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_DATES_FUTURE'

    $prematureEvidencePath = New-PilotTestManifest 'premature-evidence' {
        param($manifest)
        $manifest.sites[0].evidence.runLog.acceptedAtUtc = (
            '2026-07-13T12:00:00Z')
    }
    Invoke-PilotValidatorCase `
        -Name 'pilot evidence cannot be accepted before the run ends' `
        -ManifestPath $prematureEvidencePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_EVIDENCE_PREMATURE'

    $severeDefectPath = New-PilotTestManifest 'severe-defect' {
        param($manifest)
        $manifest.sites[0].defects.s2Count = 1
    }
    Invoke-PilotValidatorCase `
        -Name 'any S1 or S2 blocks pilot acceptance' `
        -ManifestPath $severeDefectPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_SEVERE_DEFECTS'

    $openS3Path = New-PilotTestManifest 'open-s3' {
        param($manifest)
        $manifest.sites[1].defects.s3ClosedBeforeSignoff = 0
        $manifest.sites[1].defects.s3OpenAtSignoff = 1
    }
    Invoke-PilotValidatorCase `
        -Name 'open S3 blocks pilot acceptance' `
        -ManifestPath $openS3Path `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_S3_OPEN'

    $missingWorkaroundPath = New-PilotTestManifest 'missing-s3-workaround' {
        param($manifest)
        $manifest.sites[0].defects.s3WithUsableWorkaround = 1
    }
    Invoke-PilotValidatorCase `
        -Name 'every S3 needs a usable workaround during the pilot' `
        -ManifestPath $missingWorkaroundPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_S3_WORKAROUND_MISSING'

    $consistencyPath = New-PilotTestManifest 'consistency-regression' {
        param($manifest)
        $manifest.sites[1].metrics.wmsConsistencyPercent = 99.9
    }
    Invoke-PilotValidatorCase `
        -Name 'two-dimensional three-dimensional and WMS consistency is exact' `
        -ManifestPath $consistencyPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_CONSISTENCY_FAILED'

    $automaticRecoveryPath = New-PilotTestManifest 'automatic-recovery-too-slow' {
        param($manifest)
        $manifest.sites[0].recovery.automaticMaxMinutes = 15.1
    }
    Invoke-PilotValidatorCase `
        -Name 'automatic recovery must complete within fifteen minutes' `
        -ManifestPath $automaticRecoveryPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_RECOVERY_SLO_FAILED'

    $manualRecoveryPath = New-PilotTestManifest 'manual-recovery-too-slow' {
        param($manifest)
        $manifest.sites[1].recovery.manualMaxMinutes = 241
    }
    Invoke-PilotValidatorCase `
        -Name 'manual reconciliation must complete within four hours' `
        -ManifestPath $manualRecoveryPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_RECOVERY_SLO_FAILED'

    $publishedUnavailablePath = New-PilotTestManifest 'published-unavailable' {
        param($manifest)
        $manifest.sites[0].recovery.oldPublishedContinuouslyAvailable = $false
    }
    Invoke-PilotValidatorCase `
        -Name 'old Published must remain available' `
        -ManifestPath $publishedUnavailablePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_PUBLISHED_UNAVAILABLE'

    $boundaryPath = New-PilotTestManifest 'viewer-boundary' {
        param($manifest)
        $manifest.sites[0].boundaries.publishedViewerOnly = $false
    }
    Invoke-PilotValidatorCase `
        -Name 'production viewer must remain Published only' `
        -ManifestPath $boundaryPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_BOUNDARY_FAILED'

    $placeholderConfirmationPath = New-PilotTestManifest 'placeholder-confirmation' {
        param($manifest)
        $confirmation = $manifest.sites[0].confirmations.implementationLead
        $confirmation.name = 'Implementation'
        $confirmation.evidence.acceptedBy = 'Implementation'
    }
    Invoke-PilotValidatorCase `
        -Name 'pilot confirmations require real people' `
        -ManifestPath $placeholderConfirmationPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_CONFIRMATION_NAME_INVALID'

    $mismatchedConfirmationPath = New-PilotTestManifest 'mismatched-confirmation' {
        param($manifest)
        $manifest.sites[1].confirmations.customerWarehouseRepresentative.evidence.acceptedBy = 'Different Person'
    }
    Invoke-PilotValidatorCase `
        -Name 'confirmation evidence must be signed by the named person' `
        -ManifestPath $mismatchedConfirmationPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_CONFIRMATION_MISMATCH'

    $invalidHashPath = New-PilotTestManifest 'invalid-evidence-hash' {
        param($manifest)
        $manifest.sites[0].evidence.runLog.sha256 = '0' * 64
    }
    Invoke-PilotValidatorCase `
        -Name 'pilot evidence hash must match repository content' `
        -ManifestPath $invalidHashPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_EVIDENCE_SHA_MISMATCH'

    $missingEvidencePath = New-PilotTestManifest 'missing-evidence-object' {
        param($manifest)
        $manifest.sites[0].evidence.runLog = $null
    }
    Invoke-PilotValidatorCase `
        -Name 'each mandatory pilot evidence class needs an attestation' `
        -ManifestPath $missingEvidencePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_EVIDENCE_URI_REQUIRED'

    [ordered]@{
        suite = 'CP6_SPACE_GA_PILOT_EVIDENCE'
        passed = $passed
        failed = 0
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
}
