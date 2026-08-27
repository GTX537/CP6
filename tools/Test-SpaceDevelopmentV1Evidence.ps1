param(
    [string]$ManifestPath,
    [string]$FormalGaIndexPath,
    [switch]$RequireDevelopmentComplete
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SpaceGaJson.ps1')
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repo (
        'docs\space\acceptance\development-v1\development-evidence-index.json')
}
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$repoFullPath = [System.IO.Path]::GetFullPath($repo).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [System.IO.Path]::DirectorySeparatorChar
$errors = [System.Collections.Generic.List[string]]::new()

function Add-ValidationError {
    param([Parameter(Mandatory)][string]$Message)
    $errors.Add($Message)
}

function Test-Text {
    param($Value)
    return $null -ne $Value -and
        ![string]::IsNullOrWhiteSpace([string]$Value)
}

function Resolve-RepositoryFile {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        Add-ValidationError "SPACE_DEV_V1_PATH_ABSOLUTE: $OwnerId uses an absolute path."
        return $null
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $RelativePath))
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-ValidationError "SPACE_DEV_V1_PATH_ESCAPE: $OwnerId escapes the repository root."
        return $null
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-ValidationError "SPACE_DEV_V1_PATH_MISSING: $OwnerId evidence does not exist: $RelativePath"
        return $null
    }
    return $fullPath
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Development V1 evidence manifest was not found: $manifestFullPath"
}
$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson

if ($manifest.schemaVersion -ne 1) {
    Add-ValidationError 'SPACE_DEV_V1_SCHEMA_INVALID: schemaVersion must be 1.'
}
if ($manifest.programId -ne 'CP6_SPACE_STUDIO_DEVELOPMENT_V1') {
    Add-ValidationError 'SPACE_DEV_V1_PROGRAM_INVALID: programId changed.'
}
if ($manifest.formalGaEligible -ne $false -or
    $manifest.countsTowardProductionGa -ne $false) {
    Add-ValidationError 'SPACE_DEV_V1_FORMAL_GA_FORBIDDEN: development acceptance cannot count toward production GA.'
}
if ($manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.deliveryOwner -ne 'BUBAO.GAO' -or
    $manifest.evaluatedAt -ne '2026-08-27' -or
    [string]$manifest.sourceBaseline -notmatch '^main@[a-f0-9]{40}$') {
    Add-ValidationError 'SPACE_DEV_V1_OWNER_INVALID: one real DeliveryOwner is required.'
}

$scope = $manifest.scope
$requiredFalseScope = @(
    'requiresAuthorizedCustomerCad',
    'requiresProductionProviderApproval',
    'requiresBackupProvider',
    'requiresProductionWmsWindow',
    'requiresProductionPilot',
    'requiresFormalHumanSignoff'
)
if ($scope.environment -ne 'RepositoryAndDevelopment' -or
    $scope.usesSyntheticCad -ne $true) {
    Add-ValidationError 'SPACE_DEV_V1_SCOPE_INVALID: the development-only environment and synthetic-data scope must stay explicit.'
}
foreach ($property in $requiredFalseScope) {
    if ($scope.$property -ne $false) {
        Add-ValidationError "SPACE_DEV_V1_SCOPE_EXPANSION: $property must remain false."
    }
}

$requiredGateIds = @(
    'DV1_AUTHORING_AND_TEMPLATES',
    'DV1_THREE_INPUT_PATHS_AND_EDITING',
    'DV1_CAD_WORKER_AND_SYNTHETIC_DATASET',
    'DV1_VIEWER_ACCESSIBILITY_AND_PERFORMANCE',
    'DV1_PUBLISH_WMS_SECURITY_AND_RECOVERY',
    'DV1_ACCEPTANCE_AUTOMATION_AND_BOUNDARY'
)
$gateIds = @($manifest.gates | ForEach-Object { [string]$_.id })
if ($gateIds.Count -ne $requiredGateIds.Count -or
    @($gateIds | Sort-Object -Unique).Count -ne $requiredGateIds.Count) {
    Add-ValidationError 'SPACE_DEV_V1_GATE_SET_INVALID: the six development gates must be unique and complete.'
}
foreach ($gateId in $requiredGateIds) {
    if ($gateId -notin $gateIds) {
        Add-ValidationError "SPACE_DEV_V1_GATE_MISSING: $gateId"
    }
}
foreach ($gate in @($manifest.gates)) {
    if ($gate.status -notin @('Pending', 'Passed')) {
        Add-ValidationError "SPACE_DEV_V1_GATE_STATUS_INVALID: $($gate.id) has an invalid status."
    }
    if (@($gate.acceptanceCriteria).Count -eq 0 -or
        @($gate.evidencePaths).Count -eq 0) {
        Add-ValidationError "SPACE_DEV_V1_GATE_EVIDENCE_REQUIRED: $($gate.id) lacks criteria or evidence."
    }
    $uniquePaths = @($gate.evidencePaths | Sort-Object -Unique)
    if ($uniquePaths.Count -ne @($gate.evidencePaths).Count) {
        Add-ValidationError "SPACE_DEV_V1_GATE_EVIDENCE_DUPLICATE: $($gate.id) repeats evidence paths."
    }
    foreach ($path in @($gate.evidencePaths)) {
        [void](Resolve-RepositoryFile -OwnerId ([string]$gate.id) `
            -RelativePath ([string]$path))
    }
}

$datasetConfig = $manifest.developmentDataset
$datasetManifestPath = Resolve-RepositoryFile `
    -OwnerId 'developmentDataset' `
    -RelativePath ([string]$datasetConfig.manifestPath)
$datasetSampleCount = 0
if ($null -ne $datasetManifestPath) {
    $dataset = Get-Content -LiteralPath $datasetManifestPath -Raw |
        ConvertFrom-SpaceGaJson
    $samples = @($dataset.samples)
    $datasetSampleCount = $samples.Count
    if ($dataset.datasetVersion -ne $datasetConfig.expectedVersion -or
        $dataset.purpose -ne $datasetConfig.expectedPurpose -or
        $dataset.purpose -ne 'DevelopmentSeed' -or
        $dataset.countsTowardReleaseGate -ne $false -or
        $datasetConfig.countsTowardReleaseGate -ne $false) {
        Add-ValidationError 'SPACE_DEV_V1_DATASET_BOUNDARY_INVALID: the dataset must remain a non-release DevelopmentSeed.'
    }
    if ($samples.Count -ne $datasetConfig.expectedSampleCount -or
        $samples.Count -ne 20) {
        Add-ValidationError 'SPACE_DEV_V1_DATASET_COUNT_INVALID: exactly 20 development samples are required.'
    }
    $expectedFamilies = @($datasetConfig.expectedLayoutFamilies)
    $actualFamilies = @($samples | ForEach-Object { $_.layoutFamily } |
        Sort-Object -Unique)
    if ((Compare-Object $expectedFamilies $actualFamilies).Count -ne 0) {
        Add-ValidationError 'SPACE_DEV_V1_LAYOUT_FAMILIES_INVALID: L1-L5 coverage changed.'
    }
    foreach ($family in $expectedFamilies) {
        $familyCount = @($samples | Where-Object {
            $_.layoutFamily -eq $family }).Count
        if ($familyCount -ne $datasetConfig.expectedSamplesPerLayoutFamily -or
            $familyCount -ne 4) {
            Add-ValidationError "SPACE_DEV_V1_LAYOUT_COUNT_INVALID: $family must contain four samples."
        }
    }
    $caseIndexPath = Join-Path (Split-Path -Parent $datasetManifestPath) `
        'case-index.json'
    if (!(Test-Path -LiteralPath $caseIndexPath -PathType Leaf)) {
        Add-ValidationError 'SPACE_DEV_V1_CASE_INDEX_MISSING: case-index.json is required.'
    }
    else {
        $caseIndex = Get-Content -LiteralPath $caseIndexPath -Raw |
            ConvertFrom-SpaceGaJson
        $actualCadVersions = @($caseIndex.samples | ForEach-Object {
            $_.cadVersion } | Sort-Object -Unique)
        $expectedCadVersions = @($datasetConfig.expectedCadVersions)
        if ((Compare-Object $expectedCadVersions $actualCadVersions).Count -ne 0) {
            Add-ValidationError 'SPACE_DEV_V1_CAD_VERSION_MATRIX_INVALID: the DXF version matrix changed.'
        }
    }
    $datasetRoot = Split-Path -Parent $datasetManifestPath
    $datasetPrefix = [System.IO.Path]::GetFullPath($datasetRoot).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) +
        [System.IO.Path]::DirectorySeparatorChar
    foreach ($sample in $samples) {
        if ($sample.split -ne 'DevelopmentSeed' -or
            [string]$sample.sourceFile -notmatch '(?i)\.dxf$') {
            Add-ValidationError "SPACE_DEV_V1_SAMPLE_BOUNDARY_INVALID: $($sample.sampleId) is not a development DXF sample."
            continue
        }
        $sourcePath = [System.IO.Path]::GetFullPath((Join-Path $datasetRoot `
            ([string]$sample.sourceFile)))
        if (!$sourcePath.StartsWith(
            $datasetPrefix,
            [System.StringComparison]::OrdinalIgnoreCase) -or
            !(Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
            Add-ValidationError "SPACE_DEV_V1_SAMPLE_MISSING: $($sample.sampleId) source is missing or escapes the dataset."
            continue
        }
        $actualHash = (Get-FileHash -LiteralPath $sourcePath `
            -Algorithm SHA256).Hash
        if (!$actualHash.Equals(
            [string]$sample.sourceSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-ValidationError "SPACE_DEV_V1_SAMPLE_HASH_MISMATCH: $($sample.sampleId) hash changed."
        }
    }
}

$formalConfig = $manifest.formalBoundary
$formalSnapshot = $formalConfig.snapshotAtAcceptance
if ($formalSnapshot.declaredStatus -ne 'NoGo' -or
    $formalSnapshot.baselinePercent -ne 72 -or
    $formalSnapshot.pendingExternalInputs -ne 3 -or
    $formalSnapshot.pendingGates -ne 9 -or
    $formalSnapshot.pendingSigners -ne 1) {
    Add-ValidationError 'SPACE_DEV_V1_FORMAL_SNAPSHOT_INVALID: the acceptance snapshot must preserve Core GA 72 percent / NoGo / 3-9-1.'
}
if ([string]::IsNullOrWhiteSpace($FormalGaIndexPath)) {
    $FormalGaIndexPath = Resolve-RepositoryFile `
        -OwnerId 'formalBoundary' `
        -RelativePath ([string]$formalConfig.formalGaIndexPath)
}
if ($null -ne $FormalGaIndexPath -and
    (Test-Path -LiteralPath $FormalGaIndexPath -PathType Leaf)) {
    $formalGa = Get-Content -LiteralPath $FormalGaIndexPath -Raw |
        ConvertFrom-SpaceGaJson
    if ($formalGa.programId -ne $formalConfig.formalProgramId -or
        $formalGa.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA') {
        Add-ValidationError 'SPACE_DEV_V1_FORMAL_PROGRAM_INVALID: the production GA boundary changed.'
    }
    $forbiddenPrefixes = @($formalConfig.forbiddenFormalAcceptancePrefixes)
    $formalAcceptedUris = @(
        @($formalGa.externalInputs).evidence.uri
        @($formalGa.gates).acceptedEvidence.uri
        @($formalGa.signers).evidence.uri
    ) | Where-Object { Test-Text $_ }
    foreach ($uri in $formalAcceptedUris) {
        $normalizedUri = ([string]$uri).Replace('\', '/')
        foreach ($prefix in $forbiddenPrefixes) {
            if ($normalizedUri.StartsWith(
                [string]$prefix,
                [System.StringComparison]::OrdinalIgnoreCase)) {
                Add-ValidationError "SPACE_DEV_V1_FORMAL_EVIDENCE_LEAK: formal GA accepted development evidence: $normalizedUri"
            }
        }
    }
}
else {
    Add-ValidationError 'SPACE_DEV_V1_FORMAL_INDEX_MISSING: the formal GA index is required.'
    $formalGa = $null
}

$passedGateCount = @($manifest.gates | Where-Object {
    $_.status -eq 'Passed' }).Count
$gateCount = @($manifest.gates).Count
$derivedPercent = if ($gateCount -eq 0) { 0 } else {
    [int][Math]::Floor(($passedGateCount * 100) / $gateCount)
}
$developmentReady = $gateCount -eq $requiredGateIds.Count -and
    $passedGateCount -eq $requiredGateIds.Count -and
    $datasetSampleCount -eq 20
if ($manifest.completionPercent -ne $derivedPercent -or
    ($manifest.declaredStatus -eq 'DevelopmentComplete') -ne $developmentReady) {
    Add-ValidationError 'SPACE_DEV_V1_DERIVED_STATUS_MISMATCH: declared status or percentage does not match the gates.'
}

if ($errors.Count -gt 0) {
    foreach ($validationError in $errors) {
        [Console]::Error.WriteLine($validationError)
    }
    exit 1
}

$summary = [ordered]@{
    programId = $manifest.programId
    declaredStatus = $manifest.declaredStatus
    developmentReady = $developmentReady
    completionPercent = $derivedPercent
    passedGates = $passedGateCount
    totalGates = $gateCount
    corpusSampleCount = $datasetSampleCount
    formalGaEligible = $false
    formalGaStatus = if ($null -eq $formalGa) { $null } else {
        $formalGa.declaredStatus
    }
    formalPendingInputs = if ($null -eq $formalGa) { $null } else {
        @($formalGa.externalInputs | Where-Object {
            $_.status -ne 'Complete' }).Count
    }
    formalPendingGates = if ($null -eq $formalGa) { $null } else {
        @($formalGa.gates | Where-Object {
            $_.acceptanceStatus -ne 'Accepted' }).Count
    }
    formalPendingSigners = if ($null -eq $formalGa) { $null } else {
        @($formalGa.signers | Where-Object {
            $_.status -ne 'Signed' }).Count
    }
}
$summary | ConvertTo-Json -Compress

if ($RequireDevelopmentComplete -and !$developmentReady) {
    exit 2
}
