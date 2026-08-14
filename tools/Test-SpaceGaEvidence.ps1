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

function Test-PersonName {
    param($Value)

    if (!(Test-Text $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    return ([string]$Value).Trim() -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'architecture|security|admin|administrator|\u5f85\u5b9a|' +
        '\u672a\u5b9a|\u8d1f\u8d23\u4eba|\u56e2\u961f|\u4ea7\u54c1|' +
        '\u6d4b\u8bd5|\u8d28\u91cf|\u67b6\u6784|\u5b89\u5168|' +
        '\u7ba1\u7406\u5458)$')
}

function Test-AttestedEvidence {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        [Parameter(Mandatory)]$Evidence
    )

    $reference = [string]$Evidence.uri
    $sha256 = [string]$Evidence.sha256
    $acceptedBy = [string]$Evidence.acceptedBy
    $acceptedAtUtc = [string]$Evidence.acceptedAtUtc
    $shaIsValid = $sha256 -match '^[a-fA-F0-9]{64}$'

    if (!(Test-Text $reference) -or $reference.Length -gt 2048) {
        Add-ValidationError "SPACE_GA_EVIDENCE_URI_REQUIRED: $OwnerId has a missing or oversized evidence URI."
    }
    if (!$shaIsValid) {
        Add-ValidationError "SPACE_GA_EVIDENCE_SHA_INVALID: $OwnerId evidence SHA-256 is invalid."
    }
    if (!(Test-PersonName $acceptedBy)) {
        Add-ValidationError "SPACE_GA_EVIDENCE_ACCEPTOR_INVALID: $OwnerId evidence must name the real accepting person."
    }

    [DateTimeOffset]$acceptedAt = [DateTimeOffset]::MinValue
    $acceptedAtIsValid = $acceptedAtUtc -match (
        '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$')
    if ($acceptedAtIsValid) {
        try {
            $acceptedAt = [DateTimeOffset]::Parse(
                $acceptedAtUtc,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::RoundtripKind)
        }
        catch {
            $acceptedAtIsValid = $false
        }
    }
    if (!$acceptedAtIsValid -or $acceptedAt.Offset -ne [TimeSpan]::Zero) {
        Add-ValidationError "SPACE_GA_EVIDENCE_TIME_INVALID: $OwnerId evidence acceptedAtUtc must be an ISO-8601 UTC timestamp."
    }
    elseif ($acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        Add-ValidationError "SPACE_GA_EVIDENCE_TIME_FUTURE: $OwnerId evidence acceptedAtUtc cannot be in the future."
    }

    if (!(Test-Text $reference)) {
        return
    }
    if ($reference -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        [Uri]$absoluteUri = $null
        if (![Uri]::TryCreate(
            $reference,
            [UriKind]::Absolute,
            [ref]$absoluteUri)) {
            Add-ValidationError "SPACE_GA_EVIDENCE_URI_MALFORMED: $OwnerId evidence URI is malformed."
            return
        }
        $isControlledHttps = $absoluteUri.Scheme -eq 'https' -and
            [string]::IsNullOrWhiteSpace($absoluteUri.UserInfo)
        $isControlledUrn = $absoluteUri.Scheme -eq 'urn' -and
            $absoluteUri.AbsoluteUri -match (
                '^urn:cp6-space-ga-evidence:[A-Za-z0-9]' +
                '[A-Za-z0-9:._-]{0,500}$')
        if (!$isControlledHttps -and !$isControlledUrn) {
            Add-ValidationError (
                "SPACE_GA_EVIDENCE_URI_UNCONTROLLED: $OwnerId evidence URI " +
                "must be repository-relative, HTTPS, " +
                "or a CP6 GA evidence URN.")
        }
        return
    }

    if ([System.IO.Path]::IsPathRooted($reference)) {
        Add-ValidationError "SPACE_GA_EVIDENCE_PATH_ABSOLUTE: $OwnerId uses an absolute evidence path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $reference))
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-ValidationError "SPACE_GA_EVIDENCE_PATH_ESCAPE: $OwnerId evidence escapes the repository root."
        return
    }
    if ([System.IO.Path]::GetExtension($fullPath) -match '^(?i:\.dwg|\.dxf)$') {
        Add-ValidationError "SPACE_GA_EVIDENCE_RAW_CAD_FORBIDDEN: $OwnerId references raw customer CAD inside the repository."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-ValidationError "SPACE_GA_EVIDENCE_PATH_MISSING: $OwnerId evidence path does not exist: $reference"
        return
    }
    if ($shaIsValid) {
        $actualSha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        if (!$actualSha256.Equals($sha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-ValidationError "SPACE_GA_EVIDENCE_SHA_MISMATCH: $OwnerId evidence SHA-256 does not match: $reference"
        }
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
    if ($signer.status -eq 'Signed') {
        if (!(Test-PersonName $signer.name)) {
            Add-ValidationError (
                "SPACE_GA_SIGNER_NAME_INVALID: Signer $($signer.role) " +
                "is Signed without a real person name.")
        }
        if (@($signer.evidence).Count -eq 0) {
            Add-ValidationError (
                "SPACE_GA_SIGNER_EVIDENCE_REQUIRED: Signer $($signer.role) " +
                "is Signed without evidence.")
        }
        foreach ($evidence in @($signer.evidence)) {
            Test-AttestedEvidence -OwnerId "Signer $($signer.role)" -Evidence $evidence
        }
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
    if ($input.status -eq 'Complete') {
        if (!(Test-PersonName $input.ownerName)) {
            Add-ValidationError (
                "SPACE_GA_INPUT_OWNER_INVALID: $($input.id) is Complete " +
                "without a real person owner.")
        }
        if (@($input.evidence).Count -eq 0) {
            Add-ValidationError (
                "SPACE_GA_INPUT_EVIDENCE_REQUIRED: $($input.id) is Complete " +
                "without evidence.")
        }
        foreach ($evidence in @($input.evidence)) {
            Test-AttestedEvidence -OwnerId $input.id -Evidence $evidence
        }
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
        if (!(Test-PersonName $gate.ownerName)) {
            Add-ValidationError (
                "SPACE_GA_GATE_OWNER_INVALID: $($gate.id) is Accepted " +
                "without a real person owner.")
        }
        if (@($gate.acceptedEvidence).Count -eq 0) {
            Add-ValidationError (
                "SPACE_GA_GATE_EVIDENCE_REQUIRED: $($gate.id) is Accepted " +
                "without evidence.")
        }
        foreach ($evidence in @($gate.acceptedEvidence)) {
            Test-AttestedEvidence -OwnerId $gate.id -Evidence $evidence
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
        [Console]::Error.WriteLine($validationError)
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
