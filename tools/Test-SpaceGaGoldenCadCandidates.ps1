param([Parameter(Mandatory)][string]$ManifestPath)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SpaceGaJson.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$repoFullPath = [IO.Path]::GetFullPath($repo).TrimEnd([IO.Path]::DirectorySeparatorChar)
$repoPrefix = $repoFullPath + [IO.Path]::DirectorySeparatorChar
$manifestFullPath = [IO.Path]::GetFullPath($ManifestPath)
$errors = [Collections.Generic.List[string]]::new()

function Add-Error([string]$Message) { $errors.Add($Message) }
function Test-Text($Value) { $null -ne $Value -and ![string]::IsNullOrWhiteSpace([string]$Value) }
function Test-Sha($Value) { (Test-Text $Value) -and ([string]$Value) -match '^[a-fA-F0-9]{64}$' }
function Test-Person($Value) {
    (Test-Text $Value) -and ([string]$Value).Length -le 200 -and
        ([string]$Value).Trim() -notmatch '^(?i:tbd|pending|unknown|n/?a|owner|team|qa|annotator|reviewer|待定|未定|负责人|团队|测试)$'
}
function Convert-Utc($Value) {
    if (!(Test-Text $Value) -or ([string]$Value) -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$') { return $null }
    [DateTimeOffset]$parsed = [DateTimeOffset]::MinValue
    if (![DateTimeOffset]::TryParse([string]$Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$parsed) -or $parsed.Offset -ne [TimeSpan]::Zero) { return $null }
    return $parsed
}
function Test-Evidence([string]$OwnerId, $Evidence, [string]$ExpectedPerson) {
    if ([string]$Evidence.uri -notmatch '^urn:cp6-space-ga-evidence:[A-Za-z0-9][A-Za-z0-9:._-]{0,500}$') { Add-Error "SPACE_GA_CAD_CANDIDATE_EVIDENCE_URI_INVALID: $OwnerId" }
    if (!(Test-Sha $Evidence.sha256)) { Add-Error "SPACE_GA_CAD_CANDIDATE_EVIDENCE_SHA_INVALID: $OwnerId" }
    if (!(Test-Person $Evidence.acceptedBy) -or !([string]$Evidence.acceptedBy).Equals($ExpectedPerson, [StringComparison]::OrdinalIgnoreCase)) { Add-Error "SPACE_GA_CAD_CANDIDATE_EVIDENCE_ACCEPTOR_INVALID: $OwnerId" }
    $acceptedAt = Convert-Utc $Evidence.acceptedAtUtc
    if ($null -eq $acceptedAt -or $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) { Add-Error "SPACE_GA_CAD_CANDIDATE_EVIDENCE_TIME_INVALID: $OwnerId" }
}
function Hash-Utf8([string]$Value) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Value)
    try { return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant() }
    finally { [Array]::Clear($bytes, 0, $bytes.Length) }
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) { throw "Golden CAD candidate manifest was not found: $manifestFullPath" }
if (!$manifestFullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Golden CAD candidate manifest must remain inside the repository.' }
$manifest = Get-Content -Raw -LiteralPath $manifestFullPath | ConvertFrom-SpaceGaJson

if ($manifest.schemaVersion -ne 3 -or $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or $manifest.deliveryMode -ne 'SoloDeveloper' -or $manifest.evidenceClass -ne 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' -or $manifest.conclusion -ne 'Pass') {
    Add-Error 'SPACE_GA_CAD_CANDIDATE_HEADER_INVALID: candidate manifest header is invalid.'
}
$dataset = $manifest.dataset
if ($dataset.eligibilityBasis -notin @('ApprovedOriginalWork','ApprovedCustomerDerived')) { Add-Error 'SPACE_GA_CAD_CANDIDATE_ELIGIBILITY_INVALID: eligibility must be approved original work or approved customer-derived data.' }
if (!(Test-Text $dataset.datasetVersion) -or !(Test-Sha $dataset.goldenDatasetSha256) -or !(Test-Sha $dataset.sourceSetSha256) -or !(Test-Text $dataset.mappingProfileVersion) -or !(Test-Text $dataset.ruleSetVersion) -or !(Test-Text $dataset.expectedAnswerVersion)) {
    Add-Error 'SPACE_GA_CAD_CANDIDATE_BINDING_INVALID: frozen dataset identity is incomplete.'
}
$reviewer = [string]$dataset.reviewer
if (!(Test-Person $reviewer)) { Add-Error 'SPACE_GA_CAD_CANDIDATE_REVIEWER_INVALID: a real solo reviewer is required.' }
$frozenAt = Convert-Utc $dataset.frozenAtUtc
$holdoutAt = Convert-Utc $dataset.holdoutFrozenAtUtc
if ($null -eq $frozenAt -or $null -eq $holdoutAt -or $holdoutAt -lt $frozenAt -or $holdoutAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) { Add-Error 'SPACE_GA_CAD_CANDIDATE_FREEZE_INVALID: dataset and Holdout freeze times are invalid.' }
if ($dataset.isImmutable -ne $true -or $dataset.rawCadCommittedToGit -ne $false -or $dataset.integrityAuditPassed -ne $true -or $dataset.conversionValidationPassed -ne $true) {
    Add-Error 'SPACE_GA_CAD_CANDIDATE_INTEGRITY_INVALID: immutable, repository-external CAD and completed audits are required.'
}
Test-Evidence 'dataset integrity audit' $dataset.integrityAuditEvidence $reviewer
Test-Evidence 'converter contract validation' $dataset.conversionValidationEvidence $reviewer

$samples = @($dataset.samples)
if ($samples.Count -ne 20) { Add-Error 'SPACE_GA_CAD_CANDIDATE_COUNT_INVALID: exactly 20 samples are required.' }
if (@($samples.sampleRef | Sort-Object -Unique).Count -ne $samples.Count -or @($samples.sourceSha256 | Sort-Object -Unique).Count -ne $samples.Count) {
    Add-Error 'SPACE_GA_CAD_CANDIDATE_IDENTITY_DUPLICATE: sample references and source hashes must be unique.'
}
$splitCounts = @{}; $layoutCounts = @{}; $formats = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($sample in $samples) {
    $sampleRef = [string]$sample.sampleRef
    if ($sampleRef -notmatch '^urn:cp6-space-golden-cad:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$' -or !(Test-Sha $sample.sourceSha256) -or [long]$sample.sourceSizeBytes -le 0) { Add-Error "SPACE_GA_CAD_CANDIDATE_SAMPLE_INVALID: $sampleRef" }
    $format = [string]$sample.sourceFormat
    if ($format -notin @('DWG','DXF')) { Add-Error "SPACE_GA_CAD_CANDIDATE_FORMAT_INVALID: $sampleRef" } else { [void]$formats.Add($format) }
    if ([string]$sample.cadVersion -notmatch '^AC\d{4}$') { Add-Error "SPACE_GA_CAD_CANDIDATE_VERSION_INVALID: $sampleRef" }
    $split = [string]$sample.split
    if ($split -notin @('Calibration','Validation','ReleaseHoldout')) { Add-Error "SPACE_GA_CAD_CANDIDATE_SPLIT_INVALID: $sampleRef" } else { $splitCounts[$split] = 1 + [int]$splitCounts[$split] }
    $layout = [string]$sample.layoutFamily
    if ($layout -notin @('L1','L2','L3','L4','L5')) { Add-Error "SPACE_GA_CAD_CANDIDATE_LAYOUT_INVALID: $sampleRef" } else { $layoutCounts[$layout] = 1 + [int]$layoutCounts[$layout] }
    if ($sample.license -ne $dataset.eligibilityBasis -or $sample.unit -ne 'Millimeter' -or $sample.coordinateSystem -ne 'FloorLocal-ZUp') { Add-Error "SPACE_GA_CAD_CANDIDATE_METADATA_INVALID: $sampleRef" }
    if ($sample.mappingProfileVersion -ne $dataset.mappingProfileVersion -or $sample.ruleSetVersion -ne $dataset.ruleSetVersion -or $sample.expectedAnswerVersion -ne $dataset.expectedAnswerVersion) { Add-Error "SPACE_GA_CAD_CANDIDATE_VERSION_BINDING_INVALID: $sampleRef" }
    $expectedTuning = $split -eq 'Calibration'
    if ($sample.usedForTuning -ne $expectedTuning) { Add-Error "SPACE_GA_CAD_CANDIDATE_TUNING_POLICY_INVALID: $sampleRef" }
    Test-Evidence "$sampleRef authorization" $sample.authorizationEvidence $reviewer
    Test-Evidence "$sampleRef deidentification" $sample.deidentificationEvidence $reviewer
    if ($sample.annotation.reviewMethod -ne 'SoloReview' -or !([string]$sample.annotation.reviewedBy).Equals($reviewer, [StringComparison]::OrdinalIgnoreCase)) { Add-Error "SPACE_GA_CAD_CANDIDATE_ANNOTATION_INVALID: $sampleRef" }
    Test-Evidence "$sampleRef annotation" $sample.annotation.evidence $reviewer
    foreach ($hash in @($sample.artifacts.PSObject.Properties.Value)) { if (!(Test-Sha $hash)) { Add-Error "SPACE_GA_CAD_CANDIDATE_ARTIFACT_HASH_INVALID: $sampleRef"; break } }
    if (!([string]$sample.artifacts.sourceSha256).Equals([string]$sample.sourceSha256, [StringComparison]::OrdinalIgnoreCase)) { Add-Error "SPACE_GA_CAD_CANDIDATE_SOURCE_HASH_MISMATCH: $sampleRef" }
}
if ([int]$splitCounts.Calibration -ne 10 -or [int]$splitCounts.Validation -ne 5 -or [int]$splitCounts.ReleaseHoldout -ne 5) { Add-Error 'SPACE_GA_CAD_CANDIDATE_SPLITS_INVALID: split must be 10/5/5.' }
foreach ($layout in 'L1','L2','L3','L4','L5') { if ([int]$layoutCounts[$layout] -lt 4) { Add-Error "SPACE_GA_CAD_CANDIDATE_LAYOUT_COVERAGE_INVALID: $layout" } }
if (!$formats.SetEquals([string[]]@('DWG','DXF'))) { Add-Error 'SPACE_GA_CAD_CANDIDATE_FORMAT_COVERAGE_INVALID: DWG and DXF are both required.' }

if ($samples.Count -eq 20) {
    $sourcePayload = ($samples | Sort-Object sampleRef | ForEach-Object { "$($_.sampleRef):$($_.sourceSha256)" }) -join "`n"
    if ((Hash-Utf8 $sourcePayload) -ne $dataset.sourceSetSha256) { Add-Error 'SPACE_GA_CAD_CANDIDATE_SOURCE_SET_HASH_MISMATCH: source-set seal is invalid.' }
    $datasetPayload = ($samples | Sort-Object sampleRef | ForEach-Object { "$($_.sampleRef):$($_.artifacts.sourceSha256):$($_.artifacts.metadataSha256):$($_.artifacts.expectedElementsSha256):$($_.artifacts.expectedIssuesSha256):$($_.artifacts.mappingProfileSha256):$($_.artifacts.providerIrSha256)" }) -join "`n"
    if ((Hash-Utf8 $datasetPayload) -ne $dataset.goldenDatasetSha256) { Add-Error 'SPACE_GA_CAD_CANDIDATE_DATASET_HASH_MISMATCH: golden-dataset seal is invalid.' }
}

if ($errors.Count -gt 0) { throw ("Golden CAD candidate validation failed:`n" + ($errors -join "`n")) }
[ordered]@{ programId=$manifest.programId; evidenceClass=$manifest.evidenceClass; conclusion=$manifest.conclusion; eligibilityBasis=$dataset.eligibilityBasis; sampleCount=$samples.Count; dwgCount=@($samples | Where-Object sourceFormat -eq 'DWG').Count; dxfCount=@($samples | Where-Object sourceFormat -eq 'DXF').Count; calibrationCount=[int]$splitCounts.Calibration; validationCount=[int]$splitCounts.Validation; releaseHoldoutCount=[int]$splitCounts.ReleaseHoldout } | ConvertTo-Json -Compress
