param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [switch]$AllowTestFixtures
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SpaceGaJson.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$repoFullPath = [System.IO.Path]::GetFullPath($repo).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [System.IO.Path]::DirectorySeparatorChar
$errors = [System.Collections.Generic.List[string]]::new()

function Add-GoldenValidationError {
    param([Parameter(Mandatory)][string]$Message)
    $errors.Add($Message)
}

function Test-GoldenText {
    param($Value)
    return $null -ne $Value -and
        ![string]::IsNullOrWhiteSpace([string]$Value)
}

function Test-GoldenPersonName {
    param($Value)

    if (!(Test-GoldenText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    return ([string]$Value).Trim() -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'architecture|security|admin|administrator|annotator|' +
        'arbitrator|provider|\u5f85\u5b9a|\u672a\u5b9a|' +
        '\u8d1f\u8d23\u4eba|\u56e2\u961f|\u4ea7\u54c1|' +
        '\u6d4b\u8bd5|\u8d28\u91cf|\u67b6\u6784|' +
        '\u5b89\u5168|\u7ba1\u7406\u5458)$')
}

function Test-GoldenSha256 {
    param($Value)
    return (Test-GoldenText $Value) -and
        ([string]$Value) -match '^[a-fA-F0-9]{64}$'
}

function ConvertTo-GoldenInteger {
    param($Value)

    [long]$parsed = 0
    if ($null -eq $Value -or ![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsed) -or $parsed -lt 0) {
        return $null
    }
    return $parsed
}

function ConvertTo-GoldenNumber {
    param($Value)

    [double]$parsed = 0
    if ($null -eq $Value -or ![double]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsed) -or [double]::IsNaN($parsed) -or
        [double]::IsInfinity($parsed)) {
        return $null
    }
    return $parsed
}

function ConvertTo-GoldenUtcTimestamp {
    param($Value)

    if (!(Test-GoldenText $Value) -or ([string]$Value) -notmatch (
        '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$')) {
        return $null
    }
    [DateTimeOffset]$parsed = [DateTimeOffset]::MinValue
    if (![DateTimeOffset]::TryParse(
        [string]$Value,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::RoundtripKind,
        [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) {
        return $null
    }
    return $parsed
}

function Test-GoldenAttestedEvidence {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        $Evidence
    )

    $reference = [string]$Evidence.uri
    $sha256 = [string]$Evidence.sha256
    $acceptedBy = [string]$Evidence.acceptedBy
    $acceptedAtUtc = [string]$Evidence.acceptedAtUtc
    $shaIsValid = Test-GoldenSha256 $sha256

    if (!(Test-GoldenText $reference) -or $reference.Length -gt 2048) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_URI_REQUIRED: $OwnerId has a missing or oversized evidence URI."
    }
    if (!$shaIsValid) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_SHA_INVALID: $OwnerId evidence SHA-256 is invalid."
    }
    if (!(Test-GoldenPersonName $acceptedBy)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_ACCEPTOR_INVALID: $OwnerId must name the real accepting person."
    }
    $acceptedAt = ConvertTo-GoldenUtcTimestamp $acceptedAtUtc
    if ($null -eq $acceptedAt) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_TIME_INVALID: $OwnerId acceptedAtUtc must be an ISO-8601 UTC timestamp."
    }
    elseif ($acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_TIME_FUTURE: $OwnerId acceptedAtUtc cannot be in the future."
    }

    if (!(Test-GoldenText $reference)) {
        return
    }
    if ($reference -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        [Uri]$absoluteUri = $null
        if (![Uri]::TryCreate($reference, [UriKind]::Absolute, [ref]$absoluteUri)) {
            Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_URI_MALFORMED: $OwnerId evidence URI is malformed."
            return
        }
        $isControlledHttps = $absoluteUri.Scheme -eq 'https' -and
            [string]::IsNullOrWhiteSpace($absoluteUri.UserInfo)
        $isControlledUrn = $absoluteUri.Scheme -eq 'urn' -and
            $absoluteUri.AbsoluteUri -match (
                '^urn:cp6-space-ga-evidence:[A-Za-z0-9]' +
                '[A-Za-z0-9:._-]{0,500}$')
        if (!$isControlledHttps -and !$isControlledUrn) {
            Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_URI_UNCONTROLLED: $OwnerId evidence URI must be repository-relative, HTTPS, or a CP6 GA evidence URN."
        }
        elseif (!$AllowTestFixtures -and $isControlledUrn -and
            $absoluteUri.AbsoluteUri -match ':test:') {
            Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_SYNTHETIC: $OwnerId cannot use a test URN as formal golden CAD evidence."
        }
        return
    }

    if ([System.IO.Path]::IsPathRooted($reference)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_PATH_ABSOLUTE: $OwnerId uses an absolute evidence path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $reference))
    $normalizedReference = $reference.Replace('\', '/')
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_PATH_ESCAPE: $OwnerId evidence escapes the repository root."
        return
    }
    if (!$AllowTestFixtures -and
        $normalizedReference -match '(^|/)tools/test-fixtures/') {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_SYNTHETIC: $OwnerId cannot use a test fixture as formal golden CAD evidence."
        return
    }
    if ([System.IO.Path]::GetExtension($fullPath) -match '^(?i:\.dwg|\.dxf)$') {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_RAW_CAD_FORBIDDEN: $OwnerId references raw customer CAD inside the repository."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_PATH_MISSING: $OwnerId evidence path does not exist: $reference"
        return
    }
    if ($shaIsValid) {
        $actualSha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        if (!$actualSha256.Equals(
            $sha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_SHA_MISMATCH: $OwnerId evidence SHA-256 does not match: $reference"
        }
    }
}

function Get-GoldenSourceSetSha256 {
    param([Parameter(Mandatory)][array]$Samples)

    $lines = $Samples |
        Sort-Object { [string]$_.sampleRef } |
        ForEach-Object {
            ([string]$_.sampleRef) + ':' +
                ([string]$_.sourceSha256).ToLowerInvariant()
        }
    $payload = [string]::Join("`n", $lines)
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

function Get-GoldenP95 {
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Values)

    $numbers = [System.Collections.Generic.List[double]]::new()
    foreach ($value in $Values) {
        $number = ConvertTo-GoldenNumber $value
        if ($null -eq $number -or $number -le 0) {
            return $null
        }
        $numbers.Add($number)
    }
    if ($numbers.Count -lt 5) {
        return $null
    }
    $ordered = @($numbers | Sort-Object)
    $rank = [Math]::Ceiling(0.95 * $ordered.Count)
    return [double]$ordered[$rank - 1]
}

function Test-GoldenMetrics {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        $Metrics
    )

    $coverage = ConvertTo-GoldenNumber $Metrics.targetCoveragePercent
    $accuracy = ConvertTo-GoldenNumber $Metrics.overallAccuracyPercent
    $precision = ConvertTo-GoldenNumber $Metrics.highConfidencePrecisionPercent
    $wilson = ConvertTo-GoldenNumber $Metrics.highConfidenceWilsonLowerBoundPercent
    $reduction = ConvertTo-GoldenNumber $Metrics.manualOperationReductionPercent
    if ($null -eq $coverage -or $coverage -lt 80 -or $coverage -gt 100) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_COVERAGE_FAILED: $OwnerId target coverage must be 80 to 100 percent."
    }
    if ($null -eq $accuracy -or $accuracy -lt 90 -or $accuracy -gt 100) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_ACCURACY_FAILED: $OwnerId overall accuracy must be 90 to 100 percent."
    }
    if ($null -eq $precision -or $precision -lt 95 -or $precision -gt 100) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_PRECISION_FAILED: $OwnerId high-confidence precision must be 95 to 100 percent."
    }
    if ($null -eq $wilson -or $wilson -lt 90 -or $wilson -gt 100) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_WILSON_FAILED: $OwnerId high-confidence Wilson lower bound must be 90 to 100 percent."
    }
    if ($null -eq $reduction -or $reduction -lt 70 -or $reduction -gt 100) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_EFFORT_FAILED: $OwnerId manual operation reduction must be 70 to 100 percent."
    }
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Golden CAD evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Golden CAD evidence manifest must remain inside the repository.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson

if ($manifest.schemaVersion -ne 3) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_SCHEMA_INVALID: schemaVersion must be 3.'
}
if ($manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA') {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_PROGRAM_INVALID: programId is not the frozen Core GA program.'
}
if ($manifest.deliveryMode -ne 'SoloDeveloper') {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_DELIVERY_MODE_INVALID: deliveryMode must remain SoloDeveloper.'
}
if ($manifest.evidenceClass -ne 'WP7_GOLDEN_CAD_FORMAL_EVIDENCE') {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_CLASS_INVALID: evidenceClass must be WP7_GOLDEN_CAD_FORMAL_EVIDENCE.'
}
if ($manifest.conclusion -ne 'Pass') {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_CONCLUSION_INVALID: only a final Pass package can close WP7.'
}

$dataset = $manifest.dataset
$frozenAt = ConvertTo-GoldenUtcTimestamp $dataset.frozenAtUtc
$holdoutFrozenAt = ConvertTo-GoldenUtcTimestamp $dataset.holdoutFrozenAtUtc
if ($null -eq $frozenAt -or $null -eq $holdoutFrozenAt -or
    $holdoutFrozenAt -lt $frozenAt -or
    $holdoutFrozenAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_FREEZE_TIME_INVALID: dataset and Holdout freeze times must be ordered, non-future UTC timestamps.'
}
if (!(Test-GoldenText $dataset.datasetVersion) -or
    !(Test-GoldenSha256 $dataset.goldenDatasetSha256) -or
    !(Test-GoldenSha256 $dataset.sourceSetSha256) -or
    !(Test-GoldenSha256 $dataset.frozenWorkerEnvironmentSha256) -or
    ([string]$dataset.applicationCommitSha) -notmatch '^[a-fA-F0-9]{40}$' -or
    !(Test-GoldenText $dataset.parserVersion) -or
    !(Test-GoldenText $dataset.mappingProfileVersion) -or
    !(Test-GoldenText $dataset.ruleSetVersion) -or
    !(Test-GoldenText $dataset.expectedAnswerVersion)) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_DATASET_BINDING_INVALID: dataset version, hashes, commit and frozen parser/mapping/rule/answer versions are required.'
}
if ($dataset.isImmutable -ne $true -or $dataset.integrityAuditPassed -ne $true) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_DATASET_MUTABLE: the formal dataset must be immutable and pass its integrity audit.'
}
Test-GoldenAttestedEvidence -OwnerId 'Golden dataset integrity audit' -Evidence $dataset.integrityAuditEvidence

$samples = @($dataset.samples)
if ($samples.Count -ne 20) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_SAMPLE_COUNT_INVALID: exactly 20 authorized golden CAD samples are required.'
}
$sampleRefs = @($samples | ForEach-Object { [string]$_.sampleRef })
$sourceHashes = @($samples | ForEach-Object { [string]$_.sourceSha256 })
if (@($sampleRefs | Sort-Object -Unique).Count -ne $samples.Count -or
    @($sourceHashes | Sort-Object -Unique).Count -ne $samples.Count) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_SAMPLE_IDENTITY_DUPLICATE: sample references and source hashes must be unique.'
}

$splitCounts = @{}
$layoutCounts = @{}
$formats = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($sample in $samples) {
    $sampleRef = [string]$sample.sampleRef
    $ownerId = if (Test-GoldenText $sampleRef) { $sampleRef } else { 'Golden sample' }
    if ($sampleRef -notmatch '^urn:cp6-space-golden-cad:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$' -or
        !(Test-GoldenSha256 $sample.sourceSha256) -or
        $sample.license -notin @('ApprovedCustomerDerived', 'ApprovedOriginalWork')) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_SAMPLE_INVALID: $ownerId must use an opaque reference, unique source hash and an approved customer-derived or original-work license."
    }
    $sourceSize = ConvertTo-GoldenInteger $sample.sourceSizeBytes
    if ($null -eq $sourceSize -or $sourceSize -le 0) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_SOURCE_SIZE_INVALID: $ownerId must record the real source byte length."
    }
    $format = [string]$sample.sourceFormat
    if ($format -notin @('DWG', 'DXF')) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_FORMAT_INVALID: $ownerId must be DWG or DXF."
    }
    else {
        [void]$formats.Add($format)
    }
    $split = [string]$sample.split
    if ($split -notin @('Calibration', 'Validation', 'ReleaseHoldout')) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_SPLIT_INVALID: $ownerId has an invalid split."
    }
    else {
        $splitCounts[$split] = 1 + [int]$splitCounts[$split]
    }
    $layout = [string]$sample.layoutFamily
    if ($layout -notin @('L1', 'L2', 'L3', 'L4', 'L5')) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_LAYOUT_INVALID: $ownerId has an invalid layout family."
    }
    else {
        $layoutCounts[$layout] = 1 + [int]$layoutCounts[$layout]
    }
    if ($split -eq 'ReleaseHoldout' -and $sample.usedForTuning -ne $false) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_HOLDOUT_LEAK: $ownerId Release Holdout cannot be used for tuning."
    }

    $annotation = $sample.annotation
    $reviewedBy = [string]$annotation.reviewedBy
    if (!(Test-GoldenPersonName $reviewedBy) -or
        [string]$annotation.reviewMethod -ne 'SoloReview') {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_REVIEWER_INVALID: $ownerId requires one real reviewer and reviewMethod=SoloReview."
    }
    Test-GoldenAttestedEvidence -OwnerId "$ownerId authorization" -Evidence $sample.authorizationEvidence
    Test-GoldenAttestedEvidence -OwnerId "$ownerId deidentification" -Evidence $sample.deidentificationEvidence
    Test-GoldenAttestedEvidence -OwnerId "$ownerId annotation" -Evidence $annotation.evidence
    if ((Test-GoldenPersonName $reviewedBy) -and
        !([string]$annotation.evidence.acceptedBy).Equals(
            $reviewedBy,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_REVIEWER_MISMATCH: $ownerId annotation evidence must be accepted by its reviewer."
    }
}

if ([int]$splitCounts['Calibration'] -ne 10 -or
    [int]$splitCounts['Validation'] -ne 5 -or
    [int]$splitCounts['ReleaseHoldout'] -ne 5) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_SPLIT_COUNTS_INVALID: formal split must be exactly 10 Calibration, 5 Validation and 5 ReleaseHoldout.'
}
foreach ($layout in @('L1', 'L2', 'L3', 'L4', 'L5')) {
    if ([int]$layoutCounts[$layout] -lt 4) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_LAYOUT_COVERAGE_INVALID: $layout requires at least four samples."
    }
}
if (!$formats.SetEquals([string[]]@('DWG', 'DXF'))) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_FORMAT_COVERAGE_INVALID: the formal set must contain real DWG and DXF samples.'
}
if ($samples.Count -eq 20 -and (Test-GoldenSha256 $dataset.sourceSetSha256)) {
    $actualSourceSetSha256 = Get-GoldenSourceSetSha256 -Samples $samples
    if (!$actualSourceSetSha256.Equals(
        [string]$dataset.sourceSetSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-GoldenValidationError 'SPACE_GA_GOLDEN_SOURCE_SET_HASH_MISMATCH: sourceSetSha256 does not seal the listed sample references and hashes.'
    }
}

$providers = @($manifest.providers)
if ($providers.Count -ne 1) {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_PROVIDER_SET_INVALID: exactly one evaluated Primary Provider is required for Core GA.'
}
$providerRoles = @($providers | ForEach-Object { [string]$_.role })
if ($providerRoles.Count -ne 1 -or $providerRoles[0] -ne 'Primary') {
    Add-GoldenValidationError 'SPACE_GA_GOLDEN_PROVIDER_ROLES_INVALID: the Core GA Provider must have role Primary.'
}

foreach ($provider in $providers) {
    $providerId = ([string]$provider.providerKey) + '@' +
        ([string]$provider.providerVersion)
    if (!(Test-GoldenText $provider.providerKey) -or
        !(Test-GoldenText $provider.providerVersion) -or
        (ConvertTo-GoldenInteger $provider.qualificationScore) -lt 80 -or
        $provider.releaseEligible -ne $true -or
        !(Test-GoldenSha256 $provider.providerConfigSha256) -or
        !(Test-GoldenSha256 $provider.evaluationReportSha256)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_PROVIDER_INVALID: $providerId must bind a qualified version, config, release-eligible report and score at least 80."
    }
    if (!([string]$provider.goldenDatasetSha256).Equals(
            [string]$dataset.goldenDatasetSha256,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        !([string]$provider.evaluatedSourceSetSha256).Equals(
            [string]$dataset.sourceSetSha256,
            [System.StringComparison]::OrdinalIgnoreCase) -or
        !([string]$provider.frozenWorkerEnvironmentSha256).Equals(
            [string]$dataset.frozenWorkerEnvironmentSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_PROVIDER_BASELINE_MISMATCH: $providerId must use the frozen dataset, source set and Worker environment."
    }
    if (!([string]$provider.evaluationEvidence.sha256).Equals(
        [string]$provider.evaluationReportSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_REPORT_HASH_MISMATCH: $providerId evaluation evidence must attest the declared report hash."
    }
    Test-GoldenMetrics -OwnerId "$providerId overall" -Metrics $provider.overallMetrics
    Test-GoldenMetrics -OwnerId "$providerId out-of-sample" -Metrics $provider.outOfSampleMetrics
    $holdoutOmissions = ConvertTo-GoldenInteger $provider.holdoutUnreportedBlockingOmissions
    if ($holdoutOmissions -ne 0) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_HOLDOUT_BLOCKING_OMISSION: $providerId Holdout has an unreported Blocking omission."
    }

    $standardCadSize = ConvertTo-GoldenInteger $provider.performance.standardCadSizeBytes
    $reviewP95 = Get-GoldenP95 -Values @(
        $provider.performance.reviewReadyDurationsMinutes)
    $readyP95 = Get-GoldenP95 -Values @(
        $provider.performance.trainedUserReadyDurationsMinutes)
    if ($null -eq $standardCadSize -or $standardCadSize -lt 52428800 -or
        !(Test-GoldenSha256 $provider.performance.standardCadSha256) -or
        !([string]$provider.performance.frozenWorkerEnvironmentSha256).Equals(
            [string]$dataset.frozenWorkerEnvironmentSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_PERFORMANCE_BASELINE_INVALID: $providerId requires a 50 MiB standard CAD and the frozen Worker environment."
    }
    if ($null -eq $reviewP95 -or $reviewP95 -gt 15) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_REVIEW_P95_FAILED: $providerId 50 MiB to review P95 must be at most 15 minutes across at least five runs."
    }
    if ($null -eq $readyP95 -or $readyP95 -gt 60) {
        Add-GoldenValidationError "SPACE_GA_GOLDEN_READY_P95_FAILED: $providerId trained-user first Ready P95 must be at most 60 minutes across at least five observations."
    }
    Test-GoldenAttestedEvidence -OwnerId "$providerId qualification" -Evidence $provider.qualificationEvidence
    Test-GoldenAttestedEvidence -OwnerId "$providerId evaluation" -Evidence $provider.evaluationEvidence
    Test-GoldenAttestedEvidence -OwnerId "$providerId performance" -Evidence $provider.performance.evidence

    if ($null -ne $holdoutFrozenAt) {
        foreach ($evidence in @(
            $provider.qualificationEvidence,
            $provider.evaluationEvidence,
            $provider.performance.evidence)) {
            $acceptedAt = ConvertTo-GoldenUtcTimestamp $evidence.acceptedAtUtc
            if ($null -ne $acceptedAt -and $acceptedAt -lt $holdoutFrozenAt) {
                Add-GoldenValidationError "SPACE_GA_GOLDEN_EVIDENCE_PREMATURE: $providerId evidence cannot be accepted before Holdout is frozen."
                break
            }
        }
    }
}

if ($errors.Count -gt 0) {
    throw ("Golden CAD evidence validation failed:`n" + ($errors -join "`n"))
}

[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    sampleCount = $samples.Count
    calibrationCount = [int]$splitCounts['Calibration']
    validationCount = [int]$splitCounts['Validation']
    releaseHoldoutCount = [int]$splitCounts['ReleaseHoldout']
    providerCount = $providers.Count
} | ConvertTo-Json -Compress
