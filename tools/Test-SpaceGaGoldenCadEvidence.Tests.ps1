param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaGoldenCadEvidence.ps1'
$fixtureReference = 'tools/test-fixtures/space-ga-evidence/attestation-fixture.txt'
$fixturePath = Join-Path $repo $fixtureReference
$fixtureSha256 = (Get-FileHash -LiteralPath $fixturePath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$hostExecutable = (Get-Process -Id $PID).Path
$fixtureDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-golden-cad-evidence')
$tempDirectory = Join-Path $fixtureDirectory (
    '.tmp-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function New-GoldenAttestation {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$AcceptedBy = 'Zhang Wei',
        [string]$Sha256 = ('1' * 64),
        [string]$Uri,
        [string]$AcceptedAtUtc = '2026-08-14T12:00:00Z'
    )

    if ([string]::IsNullOrWhiteSpace($Uri)) {
        $Uri = "urn:cp6-space-ga-evidence:test:golden:$Id"
    }
    return [pscustomobject]@{
        uri = $Uri
        sha256 = $Sha256
        acceptedBy = $AcceptedBy
        acceptedAtUtc = $AcceptedAtUtc
    }
}

function Get-TestSourceSetSha256 {
    param([Parameter(Mandatory)][array]$Samples)

    $payload = [string]::Join("`n", @($Samples |
        Sort-Object { [string]$_.sampleRef } |
        ForEach-Object {
            ([string]$_.sampleRef) + ':' +
                ([string]$_.sourceSha256).ToLowerInvariant()
        }))
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function New-TestProvider {
    param(
        [Parameter(Mandatory)][string]$Role,
        [Parameter(Mandatory)][string]$ProviderKey,
        [Parameter(Mandatory)][string]$ProviderVersion,
        [Parameter(Mandatory)][int]$QualificationScore,
        [Parameter(Mandatory)][string]$DatasetSha256,
        [Parameter(Mandatory)][string]$SourceSetSha256,
        [Parameter(Mandatory)][string]$EnvironmentSha256,
        [Parameter(Mandatory)][string]$ReportSha256
    )

    $metrics = [pscustomobject]@{
        targetCoveragePercent = 90
        overallAccuracyPercent = 95
        highConfidencePrecisionPercent = 98
        highConfidenceWilsonLowerBoundPercent = 95
        manualOperationReductionPercent = 75
    }
    return [pscustomobject]@{
        role = $Role
        providerKey = $ProviderKey
        providerVersion = $ProviderVersion
        qualificationScore = $QualificationScore
        releaseEligible = $true
        providerConfigSha256 = ('d' * 64)
        evaluationReportSha256 = $ReportSha256
        goldenDatasetSha256 = $DatasetSha256
        evaluatedSourceSetSha256 = $SourceSetSha256
        frozenWorkerEnvironmentSha256 = $EnvironmentSha256
        overallMetrics = $metrics
        outOfSampleMetrics = [pscustomobject]@{
            targetCoveragePercent = 88
            overallAccuracyPercent = 94
            highConfidencePrecisionPercent = 97
            highConfidenceWilsonLowerBoundPercent = 93
            manualOperationReductionPercent = 72
        }
        holdoutUnreportedBlockingOmissions = 0
        performance = [pscustomobject]@{
            standardCadSizeBytes = 52428800
            standardCadSha256 = ('9' * 64)
            frozenWorkerEnvironmentSha256 = $EnvironmentSha256
            reviewReadyDurationsMinutes = @(10, 11, 12, 13, 14)
            trainedUserReadyDurationsMinutes = @(40, 45, 50, 52, 55)
            evidence = New-GoldenAttestation -Id "$Role-performance"
        }
        qualificationEvidence = New-GoldenAttestation -Id "$Role-qualification"
        evaluationEvidence = New-GoldenAttestation `
            -Id "$Role-evaluation" `
            -Sha256 $ReportSha256
    }
}

function New-ValidGoldenManifest {
    $samples = [System.Collections.Generic.List[object]]::new()
    for ($index = 1; $index -le 20; $index++) {
        $split = if ($index -le 10) {
            'Calibration'
        }
        elseif ($index -le 15) {
            'Validation'
        }
        else {
            'ReleaseHoldout'
        }
        $layout = 'L' + ((($index - 1) % 5) + 1)
        $format = if ($index % 2 -eq 0) { 'DWG' } else { 'DXF' }
        $samples.Add([pscustomobject]@{
            sampleRef = ('urn:cp6-space-golden-cad:sample-{0:d2}' -f $index)
            sourceSha256 = $index.ToString('x64')
            sourceSizeBytes = 1000000 + $index
            sourceFormat = $format
            split = $split
            layoutFamily = $layout
            license = 'ApprovedCustomerDerived'
            usedForTuning = $split -eq 'Calibration'
            authorizationEvidence = New-GoldenAttestation -Id "sample-$index-auth"
            deidentificationEvidence = New-GoldenAttestation -Id "sample-$index-deid"
            annotation = [pscustomobject]@{
                reviewedBy = 'Zhang Wei'
                reviewMethod = 'SoloReview'
                evidence = New-GoldenAttestation `
                    -Id "sample-$index-annotation" `
                    -AcceptedBy 'Zhang Wei'
            }
        })
    }
    $sourceSetSha256 = Get-TestSourceSetSha256 -Samples $samples.ToArray()
    $datasetSha256 = 'a' * 64
    $environmentSha256 = 'b' * 64
    return [pscustomobject]@{
        schemaVersion = 3
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'WP7_GOLDEN_CAD_FORMAL_EVIDENCE'
        conclusion = 'Pass'
        dataset = [pscustomobject]@{
            datasetVersion = '1.0.0'
            goldenDatasetSha256 = $datasetSha256
            sourceSetSha256 = $sourceSetSha256
            frozenAtUtc = '2026-07-01T00:00:00Z'
            holdoutFrozenAtUtc = '2026-07-15T00:00:00Z'
            frozenWorkerEnvironmentSha256 = $environmentSha256
            applicationCommitSha = 'c' * 40
            parserVersion = 'parser-1.0.0'
            mappingProfileVersion = 'mapping-1.0.0'
            ruleSetVersion = 'rules-1.0.0'
            expectedAnswerVersion = 'answers-1.0.0'
            isImmutable = $true
            integrityAuditPassed = $true
            integrityAuditEvidence = New-GoldenAttestation `
                -Id 'dataset-integrity' `
                -Uri $fixtureReference `
                -Sha256 $fixtureSha256
            samples = $samples.ToArray()
        }
        providers = @((New-TestProvider `
            -Role 'Primary' `
            -ProviderKey 'provider-primary' `
            -ProviderVersion '1.0.0' `
            -QualificationScore 91 `
            -DatasetSha256 $datasetSha256 `
            -SourceSetSha256 $sourceSetSha256 `
            -EnvironmentSha256 $environmentSha256 `
            -ReportSha256 ('e' * 64)))
    }
}

if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
    $exportPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
    $exportDirectory = Split-Path -Parent $exportPath
    [void](New-Item -ItemType Directory -Path $exportDirectory -Force)
    New-ValidGoldenManifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $exportPath -Encoding UTF8
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
    if ((Test-Path -LiteralPath $fixtureDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $fixtureDirectory -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($fixtureDirectory)
    }
    exit 0
}

function New-GoldenTestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )

    $manifest = New-ValidGoldenManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-GoldenValidatorCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [bool]$AllowTestFixtures = $true
    )

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
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
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

    # The validator exit code has been asserted above. Clear the consumed native
    # process status so a successful suite cannot leak an expected failure to CI.
    $global:LASTEXITCODE = 0
}

try {
    $validPath = New-GoldenTestManifest 'valid' { param($manifest) }
    Invoke-GoldenValidatorCase `
        -Name 'valid formal golden CAD evidence shape' `
        -ManifestPath $validPath `
        -ShouldPass $true

    Invoke-GoldenValidatorCase `
        -Name 'formal mode rejects the synthetic integrity fixture' `
        -ManifestPath $validPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_EVIDENCE_SYNTHETIC' `
        -AllowTestFixtures $false

    Invoke-GoldenValidatorCase `
        -Name 'blank template fails with stable semantic errors' `
        -ManifestPath (Join-Path $repo `
            'docs/space/acceptance/v1.3-ga/golden-cad-evidence-template.json') `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_CONCLUSION_INVALID' `
        -AllowTestFixtures $false

    $sampleCountPath = New-GoldenTestManifest 'sample-count' {
        param($manifest)
        $manifest.dataset.samples = @($manifest.dataset.samples | Select-Object -First 19)
    }
    Invoke-GoldenValidatorCase -Name 'exactly twenty samples' `
        -ManifestPath $sampleCountPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_SAMPLE_COUNT_INVALID'

    $duplicatePath = New-GoldenTestManifest 'duplicate-source' {
        param($manifest)
        $manifest.dataset.samples[1].sourceSha256 = (
            $manifest.dataset.samples[0].sourceSha256)
    }
    Invoke-GoldenValidatorCase -Name 'source hashes are unique' `
        -ManifestPath $duplicatePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_SAMPLE_IDENTITY_DUPLICATE'

    $splitPath = New-GoldenTestManifest 'split-counts' {
        param($manifest)
        $manifest.dataset.samples[9].split = 'Validation'
    }
    Invoke-GoldenValidatorCase -Name 'split is ten five five' `
        -ManifestPath $splitPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_SPLIT_COUNTS_INVALID'

    $layoutPath = New-GoldenTestManifest 'layout-coverage' {
        param($manifest)
        $manifest.dataset.samples[4].layoutFamily = 'L1'
    }
    Invoke-GoldenValidatorCase -Name 'each layout has four samples' `
        -ManifestPath $layoutPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_LAYOUT_COVERAGE_INVALID'

    $formatPath = New-GoldenTestManifest 'format-coverage' {
        param($manifest)
        foreach ($sample in $manifest.dataset.samples) {
            $sample.sourceFormat = 'DXF'
        }
    }
    Invoke-GoldenValidatorCase -Name 'DWG and DXF are both required' `
        -ManifestPath $formatPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_FORMAT_COVERAGE_INVALID'

    $originalWorkPath = New-GoldenTestManifest 'original-work-license' {
        param($manifest)
        foreach ($sample in $manifest.dataset.samples) {
            $sample.license = 'ApprovedOriginalWork'
        }
    }
    Invoke-GoldenValidatorCase -Name 'approved original work can count' `
        -ManifestPath $originalWorkPath -ShouldPass $true

    $licensePath = New-GoldenTestManifest 'license' {
        param($manifest)
        $manifest.dataset.samples[0].license = 'Synthetic'
    }
    Invoke-GoldenValidatorCase -Name 'synthetic sample cannot count' `
        -ManifestPath $licensePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_SAMPLE_INVALID'

    $holdoutLeakPath = New-GoldenTestManifest 'holdout-leak' {
        param($manifest)
        $manifest.dataset.samples[19].usedForTuning = $true
    }
    Invoke-GoldenValidatorCase -Name 'holdout cannot tune' `
        -ManifestPath $holdoutLeakPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_HOLDOUT_LEAK'

    $reviewerPath = New-GoldenTestManifest 'reviewer' {
        param($manifest)
        $manifest.dataset.samples[0].annotation.reviewedBy = 'QA'
    }
    Invoke-GoldenValidatorCase -Name 'one real reviewer is required' `
        -ManifestPath $reviewerPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_REVIEWER_INVALID'

    $reviewerEvidencePath = New-GoldenTestManifest 'reviewer-evidence' {
        param($manifest)
        $manifest.dataset.samples[0].annotation.evidence.acceptedBy = 'Different Person'
    }
    Invoke-GoldenValidatorCase -Name 'reviewer signs annotation evidence' `
        -ManifestPath $reviewerEvidencePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_REVIEWER_MISMATCH'

    $sourceSetPath = New-GoldenTestManifest 'source-set-hash' {
        param($manifest)
        $manifest.dataset.sourceSetSha256 = '0' * 64
    }
    Invoke-GoldenValidatorCase -Name 'source set hash seals samples' `
        -ManifestPath $sourceSetPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_SOURCE_SET_HASH_MISMATCH'

    $providerRolePath = New-GoldenTestManifest 'provider-roles' {
        param($manifest)
        $manifest.providers[0].role = 'Backup'
    }
    Invoke-GoldenValidatorCase -Name 'the single provider is primary' `
        -ManifestPath $providerRolePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PROVIDER_ROLES_INVALID'

    $providerScorePath = New-GoldenTestManifest 'provider-score' {
        param($manifest)
        $manifest.providers[0].qualificationScore = 79
    }
    Invoke-GoldenValidatorCase -Name 'the primary provider scores at least eighty' `
        -ManifestPath $providerScorePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PROVIDER_INVALID'

    $extraProviderPath = New-GoldenTestManifest 'extra-provider' {
        param($manifest)
        $manifest.providers += $manifest.providers[0]
    }
    Invoke-GoldenValidatorCase -Name 'Core GA uses exactly one primary provider' `
        -ManifestPath $extraProviderPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PROVIDER_SET_INVALID'

    $releaseEligiblePath = New-GoldenTestManifest 'release-eligible' {
        param($manifest)
        $manifest.providers[0].releaseEligible = $false
    }
    Invoke-GoldenValidatorCase -Name 'the primary evaluation report is release eligible' `
        -ManifestPath $releaseEligiblePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PROVIDER_INVALID'

    $baselinePath = New-GoldenTestManifest 'provider-baseline' {
        param($manifest)
        $manifest.providers[0].frozenWorkerEnvironmentSha256 = '0' * 64
    }
    Invoke-GoldenValidatorCase -Name 'the primary uses the frozen baseline' `
        -ManifestPath $baselinePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PROVIDER_BASELINE_MISMATCH'

    $reportHashPath = New-GoldenTestManifest 'report-hash' {
        param($manifest)
        $manifest.providers[0].evaluationEvidence.sha256 = '0' * 64
    }
    Invoke-GoldenValidatorCase -Name 'report evidence binds report hash' `
        -ManifestPath $reportHashPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_REPORT_HASH_MISMATCH'

    $metricCases = @(
        @('coverage', 'targetCoveragePercent', 79, 'SPACE_GA_GOLDEN_COVERAGE_FAILED'),
        @('accuracy', 'overallAccuracyPercent', 89, 'SPACE_GA_GOLDEN_ACCURACY_FAILED'),
        @('precision', 'highConfidencePrecisionPercent', 94, 'SPACE_GA_GOLDEN_PRECISION_FAILED'),
        @('wilson', 'highConfidenceWilsonLowerBoundPercent', 89, 'SPACE_GA_GOLDEN_WILSON_FAILED'),
        @('effort', 'manualOperationReductionPercent', 69, 'SPACE_GA_GOLDEN_EFFORT_FAILED'))
    foreach ($case in $metricCases) {
        $caseName = [string]$case[0]
        $property = [string]$case[1]
        $value = $case[2]
        $errorCode = [string]$case[3]
        $metricPath = New-GoldenTestManifest "metric-$caseName" {
            param($manifest)
            $manifest.providers[0].overallMetrics.$property = $value
        }
        Invoke-GoldenValidatorCase -Name "metric gate $caseName" `
            -ManifestPath $metricPath -ShouldPass $false `
            -ExpectedError $errorCode
    }

    $blockingPath = New-GoldenTestManifest 'holdout-blocking' {
        param($manifest)
        $manifest.providers[0].holdoutUnreportedBlockingOmissions = 1
    }
    Invoke-GoldenValidatorCase -Name 'holdout has no unreported blocker' `
        -ManifestPath $blockingPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_HOLDOUT_BLOCKING_OMISSION'

    $sizePath = New-GoldenTestManifest 'standard-size' {
        param($manifest)
        $manifest.providers[0].performance.standardCadSizeBytes = 50000000
    }
    Invoke-GoldenValidatorCase -Name 'performance sample is at least fifty MiB' `
        -ManifestPath $sizePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PERFORMANCE_BASELINE_INVALID'

    $reviewPath = New-GoldenTestManifest 'review-p95' {
        param($manifest)
        $manifest.providers[0].performance.reviewReadyDurationsMinutes = (
            @(10, 11, 12, 13, 15.1))
    }
    Invoke-GoldenValidatorCase -Name 'review P95 is at most fifteen minutes' `
        -ManifestPath $reviewPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_REVIEW_P95_FAILED'

    $readyPath = New-GoldenTestManifest 'ready-p95' {
        param($manifest)
        $manifest.providers[0].performance.trainedUserReadyDurationsMinutes = (
            @(40, 45, 50, 55, 60.1))
    }
    Invoke-GoldenValidatorCase -Name 'trained user P95 is at most sixty minutes' `
        -ManifestPath $readyPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_READY_P95_FAILED'

    $prematurePath = New-GoldenTestManifest 'premature-evidence' {
        param($manifest)
        $manifest.dataset.holdoutFrozenAtUtc = '2026-08-14T13:00:00Z'
    }
    Invoke-GoldenValidatorCase -Name 'provider evidence follows holdout freeze' `
        -ManifestPath $prematurePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_EVIDENCE_PREMATURE'

    $missingEvidencePath = New-GoldenTestManifest 'missing-evidence' {
        param($manifest)
        $manifest.providers[0].performance.evidence = $null
    }
    Invoke-GoldenValidatorCase -Name 'performance evidence is mandatory' `
        -ManifestPath $missingEvidencePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_EVIDENCE_URI_REQUIRED'

    if ($global:LASTEXITCODE -ne 0) {
        throw "Test suite leaked child process exit code $global:LASTEXITCODE."
    }

    [ordered]@{
        suite = 'CP6_SPACE_GA_GOLDEN_CAD_EVIDENCE'
        passed = $passed
        failed = 0
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
    if ((Test-Path -LiteralPath $fixtureDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $fixtureDirectory -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($fixtureDirectory)
    }
}
