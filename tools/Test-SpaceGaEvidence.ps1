param(
    [string]$ManifestPath,
    [switch]$RequireGaReady
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repo (
        'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
}
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$repoFullPath = [System.IO.Path]::GetFullPath($repo).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [System.IO.Path]::DirectorySeparatorChar

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "GA evidence manifest was not found: $manifestFullPath"
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-Json
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

function Test-RelativeEvidencePath {
    param(
        [Parameter(Mandatory)][string]$GateId,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        Add-ValidationError "$GateId uses an absolute evidence path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $RelativePath))
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-ValidationError "$GateId evidence escapes the repository root."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-ValidationError "$GateId evidence path does not exist: $RelativePath"
    }
}

if ($manifest.schemaVersion -ne 1) {
    Add-ValidationError 'schemaVersion must be 1.'
}
if ($manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA') {
    Add-ValidationError 'programId is not the frozen Core GA program.'
}
if ($manifest.baselinePercent -ne 72 -or $manifest.gaPercent -ne 100) {
    Add-ValidationError 'The frozen 72-to-100 progress policy changed.'
}

$requiredSignerRoles = @('Product', 'QA', 'WMS', 'Architecture', 'Security')
$signerRoles = @($manifest.signers | ForEach-Object { $_.role })
if (@($signerRoles | Sort-Object -Unique).Count -ne $signerRoles.Count) {
    Add-ValidationError 'Signer roles must be unique.'
}
if ($signerRoles.Count -ne $requiredSignerRoles.Count) {
    Add-ValidationError 'The five-role signer set cannot be expanded or reduced.'
}
foreach ($role in $requiredSignerRoles) {
    if ($role -notin $signerRoles) {
        Add-ValidationError "Missing required signer role: $role"
    }
}
foreach ($signer in @($manifest.signers)) {
    if ($signer.status -notin @('Pending', 'Signed')) {
        Add-ValidationError "Signer $($signer.role) has an invalid status."
    }
    if ($signer.status -eq 'Signed' -and
        (!(Test-Text $signer.name) -or @($signer.evidence).Count -eq 0)) {
        Add-ValidationError (
            "Signer $($signer.role) is Signed without a real name and evidence.")
    }
}

$requiredInputIds = @(
    'NAMED_GA_SIGNERS',
    'CORE_TEAM_ALLOCATION',
    'AUTHORIZED_GOLDEN_CAD_CANDIDATES',
    'PROVIDER_APPROVALS_AND_ISOLATED_WORKER',
    'TWO_PILOT_SITES_AND_WMS_WINDOWS'
)
$inputIds = @($manifest.externalInputs | ForEach-Object { $_.id })
if (@($inputIds | Sort-Object -Unique).Count -ne $inputIds.Count) {
    Add-ValidationError 'External input ids must be unique.'
}
if ($inputIds.Count -ne $requiredInputIds.Count) {
    Add-ValidationError 'The frozen external input set cannot be expanded or reduced.'
}
foreach ($inputId in $requiredInputIds) {
    if ($inputId -notin $inputIds) {
        Add-ValidationError "Missing required external input: $inputId"
    }
}
foreach ($input in @($manifest.externalInputs)) {
    if (!(Test-Text $input.ownerRole) -or
        !(Test-Text $input.deadlineMilestone) -or
        @($input.evidenceFormat).Count -eq 0) {
        Add-ValidationError "$($input.id) is missing ownership, deadline or format."
    }
    if ($input.status -notin @('Pending', 'Complete')) {
        Add-ValidationError "$($input.id) has an invalid status."
    }
    if ($input.status -eq 'Complete' -and
        (!(Test-Text $input.ownerName) -or @($input.evidence).Count -eq 0)) {
        Add-ValidationError (
            "$($input.id) is Complete without a named owner and evidence.")
    }
}

$requiredGateIds = @(
    'WP0_BASELINE_AND_GOVERNANCE',
    'WP1_DESIGN_V1_MANUAL_MODELING',
    'WP2_CAD_START_WIZARD',
    'WP3_SITE_PRIMARY_BACKUP_PROVIDERS',
    'WP4_THREE_PATH_END_TO_END',
    'WP5_VIEWER_ACCESSIBILITY_AND_PERFORMANCE',
    'WP6_PUBLISH_WMS_SECURITY_AND_RECOVERY',
    'WP7_GOLDEN_CAD_FORMAL_EVIDENCE',
    'WP8_TWO_SITE_PILOT_AND_SIGNOFF'
)
$gateIds = @($manifest.gates | ForEach-Object { $_.id })
if (@($gateIds | Sort-Object -Unique).Count -ne $gateIds.Count) {
    Add-ValidationError 'Gate ids must be unique.'
}
if ($gateIds.Count -ne $requiredGateIds.Count) {
    Add-ValidationError 'The frozen WP0-WP8 gate set cannot be expanded or reduced.'
}
foreach ($gateId in $requiredGateIds) {
    if ($gateId -notin $gateIds) {
        Add-ValidationError "Missing required blocking gate: $gateId"
    }
}
foreach ($gate in @($manifest.gates)) {
    if ($gate.blocking -ne $true) {
        Add-ValidationError "$($gate.id) must remain blocking."
    }
    if (!(Test-Text $gate.ownerRole) -or
        !(Test-Text $gate.deadlineMilestone) -or
        @($gate.evidenceFormat).Count -eq 0 -or
        @($gate.acceptanceCriteria).Count -eq 0) {
        Add-ValidationError "$($gate.id) is missing governance metadata."
    }
    if ($gate.implementationStatus -notin
        @('Pending', 'Partial', 'Complete', 'ExternalExecution')) {
        Add-ValidationError "$($gate.id) has an invalid implementation status."
    }
    if ($gate.acceptanceStatus -notin @('Pending', 'Accepted')) {
        Add-ValidationError "$($gate.id) has an invalid acceptance status."
    }
    foreach ($path in @($gate.evidencePaths)) {
        Test-RelativeEvidencePath -GateId $gate.id -RelativePath $path
    }
    if ($gate.acceptanceStatus -eq 'Accepted') {
        if (!(Test-Text $gate.ownerName) -or
            @($gate.acceptedEvidence).Count -eq 0) {
            Add-ValidationError (
                "$($gate.id) is Accepted without a named owner and evidence.")
        }
        foreach ($evidence in @($gate.acceptedEvidence)) {
            if (!(Test-Text $evidence.uri) -or
                [string]$evidence.sha256 -notmatch '^[a-fA-F0-9]{64}$' -or
                !(Test-Text $evidence.acceptedBy) -or
                !(Test-Text $evidence.acceptedAtUtc)) {
                Add-ValidationError (
                    "$($gate.id) has incomplete accepted evidence metadata.")
            }
        }
    }
}

$allInputsComplete = @(
    $manifest.externalInputs |
        Where-Object { $_.status -ne 'Complete' }).Count -eq 0
$allGatesAccepted = @(
    $manifest.gates |
        Where-Object { $_.blocking -and $_.acceptanceStatus -ne 'Accepted' }
).Count -eq 0
$allSignersSigned = @(
    $manifest.signers |
        Where-Object { $_.status -ne 'Signed' }).Count -eq 0
$datesRecorded = (Test-Text $manifest.kickoffDate) -and
    (Test-Text $manifest.targetGaDate)
$gaReady = $allInputsComplete -and $allGatesAccepted -and
    $allSignersSigned -and $datesRecorded

if ($manifest.declaredStatus -notin @('NoGo', 'GaReady')) {
    Add-ValidationError 'declaredStatus must be NoGo or GaReady.'
}
if (($manifest.declaredStatus -eq 'GaReady') -ne $gaReady) {
    Add-ValidationError 'declaredStatus does not match the derived GA state.'
}

if ($errors.Count -gt 0) {
    foreach ($validationError in $errors) {
        Write-Error $validationError
    }
    exit 1
}

$summary = [ordered]@{
    programId = $manifest.programId
    declaredStatus = $manifest.declaredStatus
    gaReady = $gaReady
    pendingInputs = @(
        $manifest.externalInputs |
            Where-Object { $_.status -ne 'Complete' }).Count
    pendingGates = @(
        $manifest.gates |
            Where-Object { $_.acceptanceStatus -ne 'Accepted' }).Count
    pendingSigners = @(
        $manifest.signers |
            Where-Object { $_.status -ne 'Signed' }).Count
}
$summary | ConvertTo-Json -Compress

if ($RequireGaReady -and !$gaReady) {
    exit 2
}
