param(
    [Parameter(Mandatory)]
    [string]$ControlledManifestPath,
    [Parameter(Mandatory)]
    [string]$BusinessEvidencePath,
    [Parameter(Mandatory)]
    [string]$EvaluationReportPath,
    [Parameter(Mandatory)]
    [string]$PerformanceEvidencePath,
    [Parameter(Mandatory)]
    [string]$RulesPath,
    [Parameter(Mandatory)]
    [string]$QualificationEvidencePath,
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-fA-F0-9]{40}$')]
    [string]$ApplicationCommitSha,
    [Parameter(Mandatory)]
    [string]$ProviderVersion,
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$FrozenWorkerEnvironmentSha256,
    [Parameter(Mandatory)]
    [string]$OutputPath,
    [string]$AcceptedBy = 'BUBAO.GAO'
)

$ErrorActionPreference = 'Stop'

function Resolve-RequiredFile {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (!(Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Required file was not found: $resolved"
    }
    return $resolved
}

function Read-JsonFile {
    param([Parameter(Mandatory)][string]$Path)

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
}

function Get-LowerSha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Convert-ToPercent {
    param([Parameter(Mandatory)]$Value)

    return [Math]::Round(100 * [double]$Value, 6)
}

function Convert-ToUtcTimestamp {
    param([Parameter(Mandatory)]$Value)

    if ($Value -is [DateTime]) {
        return $Value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    if ($Value -is [DateTimeOffset]) {
        return $Value.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    }
    return [DateTimeOffset]::Parse(
        [string]$Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind
    ).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
}

$controlledPath = Resolve-RequiredFile $ControlledManifestPath
$businessPath = Resolve-RequiredFile $BusinessEvidencePath
$reportPath = Resolve-RequiredFile $EvaluationReportPath
$performancePath = Resolve-RequiredFile $PerformanceEvidencePath
$rulesFullPath = Resolve-RequiredFile $RulesPath
$qualificationPath = Resolve-RequiredFile $QualificationEvidencePath
$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)

$controlled = Read-JsonFile $controlledPath
$business = Read-JsonFile $businessPath
$performance = Read-JsonFile $performancePath
$rules = Read-JsonFile $rulesFullPath
$qualification = Read-JsonFile $qualificationPath

if ($controlled.conclusion -ne 'Pass' -or
    $controlled.dataset.samples.Count -ne 20 -or
    $business.gate.releaseEligible -ne $true -or
    $business.holdoutUnreportedBlockingOmissions -ne 0 -or
    $performance.conclusion -ne 'Pass' -or
    $performance.failureCount -ne 0 -or
    $qualification.cadGaReady -ne $true) {
    throw 'One or more source evidence packages are not final Pass evidence.'
}
if (!$business.goldenDatasetSha256.Equals(
        [string]$controlled.dataset.goldenDatasetSha256,
        [System.StringComparison]::OrdinalIgnoreCase) -or
    !$business.sourceSetSha256.Equals(
        [string]$controlled.dataset.sourceSetSha256,
        [System.StringComparison]::OrdinalIgnoreCase) -or
    !$performance.applicationCommitSha.Equals(
        $ApplicationCommitSha,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Dataset, source set or application commit evidence is not consistently bound.'
}

$samples = foreach ($sample in $controlled.dataset.samples) {
    [ordered]@{
        sampleRef = $sample.sampleRef
        sourceSha256 = $sample.sourceSha256
        sourceSizeBytes = [long]$sample.sourceSizeBytes
        sourceFormat = $sample.sourceFormat
        split = $sample.split
        layoutFamily = $sample.layoutFamily
        license = $sample.license
        usedForTuning = [bool]$sample.usedForTuning
        authorizationEvidence = $sample.authorizationEvidence
        deidentificationEvidence = $sample.deidentificationEvidence
        annotation = $sample.annotation
    }
}

$metric = {
    param($source)
    return [ordered]@{
        targetCoveragePercent = Convert-ToPercent $source.targetCoverage
        overallAccuracyPercent = Convert-ToPercent $source.overallSemanticAccuracy
        highConfidencePrecisionPercent = Convert-ToPercent $source.highConfidencePrecision
        highConfidenceWilsonLowerBoundPercent = Convert-ToPercent $source.highConfidenceWilsonLowerBound
        manualOperationReductionPercent = Convert-ToPercent $source.manualOperationReduction
    }
}
$reportSha256 = Get-LowerSha256 $reportPath
$performanceSha256 = Get-LowerSha256 $performancePath
$qualificationSha256 = Get-LowerSha256 $qualificationPath
$rulesSha256 = Get-LowerSha256 $rulesFullPath
$evaluationAcceptedAt = Convert-ToUtcTimestamp $business.evaluatedAtUtc
$qualificationAcceptedAt = Convert-ToUtcTimestamp $qualification.evaluatedAtUtc
$performanceAcceptedAt = Convert-ToUtcTimestamp $performance.attestation.acceptedAtUtc

$manifest = [ordered]@{
    schemaVersion = 3
    programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
    deliveryMode = 'SoloDeveloper'
    evidenceClass = 'WP7_GOLDEN_CAD_FORMAL_EVIDENCE'
    conclusion = 'Pass'
    dataset = [ordered]@{
        datasetVersion = $controlled.dataset.datasetVersion
        goldenDatasetSha256 = $controlled.dataset.goldenDatasetSha256
        sourceSetSha256 = $controlled.dataset.sourceSetSha256
        frozenAtUtc = $controlled.dataset.frozenAtUtc
        holdoutFrozenAtUtc = $controlled.dataset.holdoutFrozenAtUtc
        frozenWorkerEnvironmentSha256 = $FrozenWorkerEnvironmentSha256.ToLowerInvariant()
        applicationCommitSha = $ApplicationCommitSha.ToLowerInvariant()
        parserVersion = $rules.parserVersion
        mappingProfileVersion = $controlled.dataset.mappingProfileVersion
        ruleSetVersion = $controlled.dataset.ruleSetVersion
        expectedAnswerVersion = $controlled.dataset.expectedAnswerVersion
        isImmutable = [bool]$controlled.dataset.isImmutable
        integrityAuditPassed = [bool]$controlled.dataset.integrityAuditPassed
        integrityAuditEvidence = $controlled.dataset.integrityAuditEvidence
        samples = @($samples)
    }
    providers = @(
        [ordered]@{
            role = 'Primary'
            providerKey = 'cp6-autocad-worker'
            providerVersion = $ProviderVersion
            qualificationScore = [int]$qualification.primary.qualificationScore
            releaseEligible = [bool]$business.gate.releaseEligible
            providerConfigSha256 = $rulesSha256
            evaluationReportSha256 = $reportSha256
            goldenDatasetSha256 = $controlled.dataset.goldenDatasetSha256
            evaluatedSourceSetSha256 = $controlled.dataset.sourceSetSha256
            frozenWorkerEnvironmentSha256 = $FrozenWorkerEnvironmentSha256.ToLowerInvariant()
            overallMetrics = & $metric $business.overallMetrics
            outOfSampleMetrics = & $metric $business.outOfSampleMetrics
            holdoutUnreportedBlockingOmissions = [int]$business.holdoutUnreportedBlockingOmissions
            performance = [ordered]@{
                standardCadSizeBytes = [long]$performance.standardCad.standardCadSizeBytes
                standardCadSha256 = $performance.standardCad.standardCadSha256
                frozenWorkerEnvironmentSha256 = $FrozenWorkerEnvironmentSha256.ToLowerInvariant()
                reviewReadyDurationsMinutes = @($performance.reviewReadyDurationsMinutes)
                trainedUserReadyDurationsMinutes = @($performance.trainedUserReadyDurationsMinutes)
                evidence = [ordered]@{
                    uri = 'urn:cp6-space-ga-evidence:golden-cad:v1.0.0:primary:performance'
                    sha256 = $performanceSha256
                    acceptedBy = $AcceptedBy
                    acceptedAtUtc = $performanceAcceptedAt
                }
            }
            qualificationEvidence = [ordered]@{
                uri = 'docs/space/acceptance/v1.3-ga/autocad-primary-qualification-v1.0.0.json'
                sha256 = $qualificationSha256
                acceptedBy = $AcceptedBy
                acceptedAtUtc = $qualificationAcceptedAt
            }
            evaluationEvidence = [ordered]@{
                uri = 'urn:cp6-space-ga-evidence:golden-cad:v1.0.0:primary:business-evaluation'
                sha256 = $reportSha256
                acceptedBy = $AcceptedBy
                acceptedAtUtc = $evaluationAcceptedAt
            }
        }
    )
}

[System.IO.Directory]::CreateDirectory(
    [System.IO.Path]::GetDirectoryName($outputFullPath)) | Out-Null
$json = $manifest | ConvertTo-Json -Depth 30
[System.IO.File]::WriteAllText(
    $outputFullPath,
    $json + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))

[ordered]@{
    outputPath = $outputFullPath
    sha256 = Get-LowerSha256 $outputFullPath
    sampleCount = $samples.Count
    providerCount = $manifest.providers.Count
    conclusion = $manifest.conclusion
} | ConvertTo-Json -Compress
