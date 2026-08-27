param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [string]$InputId,
    [string]$ExpectedOwnerName,
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

function Add-KickoffValidationError {
    param([Parameter(Mandatory)][string]$Message)
    $errors.Add($Message)
}

function Test-KickoffText {
    param($Value)
    return $null -ne $Value -and
        ![string]::IsNullOrWhiteSpace([string]$Value)
}

function Test-KickoffPersonName {
    param($Value)

    if (!(Test-KickoffText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    $personName = ([string]$Value).Trim()
    if ($personName -match '^\d+$' -or
        $personName -match (
            '^(?i:(?:dev(?:elopment)?|test|demo|simulated|' +
            '\u5f00\u53d1\u4eba\u5458|\u6d4b\u8bd5\u4eba\u5458)' +
            '[ _-]?\d+)$')) {
        return $false
    }
    return $personName -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'architecture|security|devops|admin|administrator|' +
        '\u5f85\u5b9a|\u672a\u5b9a|\u8d1f\u8d23\u4eba|\u56e2\u961f|' +
        '\u4ea7\u54c1|\u6d4b\u8bd5|\u8d28\u91cf|\u67b6\u6784|' +
        '\u5b89\u5168|\u8fd0\u7ef4|\u7ba1\u7406\u5458)$')
}

function Test-KickoffSha256 {
    param($Value)
    return (Test-KickoffText $Value) -and
        ([string]$Value) -match '^[a-fA-F0-9]{64}$'
}

function ConvertTo-KickoffInteger {
    param($Value)

    [long]$parsed = 0
    if ($null -eq $Value -or ![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$parsed)) {
        return $null
    }
    return $parsed
}

function ConvertTo-KickoffDate {
    param($Value)

    [DateTime]$parsed = [DateTime]::MinValue
    if (!(Test-KickoffText $Value) -or ![DateTime]::TryParseExact(
        [string]$Value,
        'yyyy-MM-dd',
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None,
        [ref]$parsed)) {
        return $null
    }
    return $parsed.Date
}

function ConvertTo-KickoffUtcTimestamp {
    param($Value)

    if (!(Test-KickoffText $Value) -or ([string]$Value) -notmatch (
        '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$')) {
        return $null
    }
    try {
        $parsed = [DateTimeOffset]::Parse(
            [string]$Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind)
        if ($parsed.Offset -ne [TimeSpan]::Zero) {
            return $null
        }
        return $parsed
    }
    catch {
        return $null
    }
}

function Test-KickoffEvidence {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        $Evidence,
        [string]$ExpectedAcceptor
    )

    $reference = [string]$Evidence.uri
    $sha256 = [string]$Evidence.sha256
    $acceptedBy = [string]$Evidence.acceptedBy
    $acceptedAtUtc = [string]$Evidence.acceptedAtUtc
    $shaIsValid = Test-KickoffSha256 $sha256

    if (!(Test-KickoffText $reference) -or $reference.Length -gt 2048) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_URI_REQUIRED: $OwnerId has a missing or oversized evidence URI."
    }
    if (!$shaIsValid) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_SHA_INVALID: $OwnerId evidence SHA-256 is invalid."
    }
    if (!(Test-KickoffPersonName $acceptedBy)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_ACCEPTOR_INVALID: $OwnerId must name a real accepting person."
    }
    elseif ((Test-KickoffPersonName $ExpectedAcceptor) -and
        !$acceptedBy.Equals(
            $ExpectedAcceptor,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_ACCEPTOR_MISMATCH: $OwnerId evidence acceptor must match the declared owner."
    }

    $acceptedAt = ConvertTo-KickoffUtcTimestamp $acceptedAtUtc
    if ($null -eq $acceptedAt) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_TIME_INVALID: $OwnerId acceptedAtUtc must be an ISO-8601 UTC timestamp."
    }
    elseif ($acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_TIME_FUTURE: $OwnerId evidence cannot be accepted in the future."
    }

    if (!(Test-KickoffText $reference)) {
        return
    }
    $normalizedReference = $reference.Replace('\', '/')
    if (!$AllowTestFixtures -and
        ($normalizedReference -match '(^|/)tools/test-fixtures/' -or
         $normalizedReference -match '(?i):test:')) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_SYNTHETIC: $OwnerId uses a test-only evidence reference."
        return
    }
    if ($reference -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        [Uri]$absoluteUri = $null
        if (![Uri]::TryCreate($reference, [UriKind]::Absolute, [ref]$absoluteUri)) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_URI_MALFORMED: $OwnerId evidence URI is malformed."
            return
        }
        $isControlledHttps = $absoluteUri.Scheme -eq 'https' -and
            [string]::IsNullOrWhiteSpace($absoluteUri.UserInfo)
        $isControlledUrn = $absoluteUri.Scheme -eq 'urn' -and
            $absoluteUri.AbsoluteUri -match (
                '^urn:cp6-space-ga-evidence:[A-Za-z0-9]' +
                '[A-Za-z0-9:._-]{0,500}$')
        if (!$isControlledHttps -and !$isControlledUrn) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_URI_UNCONTROLLED: $OwnerId evidence must be repository-relative, HTTPS or a CP6 GA evidence URN."
        }
        return
    }

    if ([System.IO.Path]::IsPathRooted($reference)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_PATH_ABSOLUTE: $OwnerId uses an absolute evidence path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $reference))
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_PATH_ESCAPE: $OwnerId evidence escapes the repository root."
        return
    }
    if ([System.IO.Path]::GetExtension($fullPath) -match '^(?i:\.dwg|\.dxf)$') {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_RAW_CAD_FORBIDDEN: $OwnerId references raw customer CAD inside the repository."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_PATH_MISSING: $OwnerId evidence path does not exist: $reference"
        return
    }
    if ($shaIsValid) {
        $actualSha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        if (!$actualSha256.Equals(
            $sha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_EVIDENCE_SHA_MISMATCH: $OwnerId evidence hash does not match: $reference"
        }
    }
}

function Get-KickoffCandidateSetSha256 {
    param([Parameter(Mandatory)][AllowEmptyCollection()][array]$Candidates)

    $payload = [string]::Join("`n", @($Candidates |
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

function Test-KickoffSectionHeader {
    param(
        [Parameter(Mandatory)][string]$RequiredInputId,
        $Section
    )

    if ($null -eq $Section -or $Section.inputId -ne $RequiredInputId) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_SECTION_INVALID: $RequiredInputId section is missing or misidentified."
        return $false
    }
    if ($Section.status -ne 'Complete') {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_SECTION_INCOMPLETE: $RequiredInputId must be Complete before the external input can close."
    }
    if (!(Test-KickoffPersonName $Section.ownerName)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_OWNER_INVALID: $RequiredInputId must name a real owner."
    }
    elseif ((Test-KickoffPersonName $ExpectedOwnerName) -and
        !([string]$Section.ownerName).Equals(
            $ExpectedOwnerName,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-KickoffValidationError "SPACE_GA_KICKOFF_OWNER_MISMATCH: $RequiredInputId owner does not match the GA evidence index."
    }
    Test-KickoffEvidence `
        -OwnerId "$RequiredInputId completion" `
        -Evidence $Section.completionEvidence `
        -ExpectedAcceptor ([string]$Section.ownerName)
    return $true
}

function Test-AuthorizedGoldenCadCandidates {
    param($Section)

    if (!(Test-KickoffSectionHeader `
        -RequiredInputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -Section $Section)) {
        return
    }
    if (!(Test-KickoffText $Section.candidateSetVersion) -or
        !(Test-KickoffSha256 $Section.candidateSetSha256)) {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_CAD_SET_INVALID: candidate set version and SHA-256 are required.'
    }
    $candidates = @($Section.candidates)
    if ($candidates.Count -ne 20) {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_CAD_COUNT_INVALID: exactly 20 authorized candidate CAD files are required.'
    }
    $sampleRefs = @($candidates | ForEach-Object { [string]$_.sampleRef })
    $sourceHashes = @($candidates | ForEach-Object { [string]$_.sourceSha256 })
    if (@($sampleRefs | Sort-Object -Unique).Count -ne $candidates.Count -or
        @($sourceHashes | Sort-Object -Unique).Count -ne $candidates.Count) {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_CAD_IDENTITY_DUPLICATE: candidate references and source hashes must be unique.'
    }
    $formats = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $layouts = @{}
    foreach ($candidate in $candidates) {
        $sampleRef = [string]$candidate.sampleRef
        $ownerId = if (Test-KickoffText $sampleRef) { $sampleRef } else { 'CAD candidate' }
        $size = ConvertTo-KickoffInteger $candidate.sourceSizeBytes
        $format = [string]$candidate.sourceFormat
        $layout = [string]$candidate.layoutFamily
        if ($sampleRef -notmatch '^urn:cp6-space-golden-cad:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$' -or
            !(Test-KickoffSha256 $candidate.sourceSha256) -or
            $null -eq $size -or $size -le 0 -or
            $candidate.license -notin @(
                'ApprovedCustomerDerived', 'ApprovedOriginalWork') -or
            $candidate.authorizedForGoldenEvaluation -ne $true) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_CAD_CANDIDATE_INVALID: $ownerId must be an authorized original-work or customer-derived candidate with opaque identity, hash and byte length."
        }
        if ($format -notin @('DWG', 'DXF')) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_CAD_FORMAT_INVALID: $ownerId must be DWG or DXF."
        }
        else {
            [void]$formats.Add($format)
        }
        if ($layout -notin @('L1', 'L2', 'L3', 'L4', 'L5')) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_CAD_LAYOUT_INVALID: $ownerId must use L1 through L5."
        }
        else {
            $layouts[$layout] = 1 + [int]$layouts[$layout]
        }
        Test-KickoffEvidence -OwnerId "$ownerId authorization" -Evidence $candidate.authorizationEvidence
        Test-KickoffEvidence -OwnerId "$ownerId deidentification" -Evidence $candidate.deidentificationEvidence
    }
    if (!$formats.SetEquals([string[]]@('DWG', 'DXF'))) {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_CAD_FORMAT_COVERAGE_INVALID: the candidate set must contain both DWG and DXF.'
    }
    foreach ($layout in @('L1', 'L2', 'L3', 'L4', 'L5')) {
        if ([int]$layouts[$layout] -lt 4) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_CAD_LAYOUT_COVERAGE_INVALID: $layout requires at least four candidates."
        }
    }
    if ($candidates.Count -eq 20 -and
        (Test-KickoffSha256 $Section.candidateSetSha256)) {
        $actualSetSha256 = Get-KickoffCandidateSetSha256 -Candidates $candidates
        if (!$actualSetSha256.Equals(
            [string]$Section.candidateSetSha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-KickoffValidationError 'SPACE_GA_KICKOFF_CAD_SET_HASH_MISMATCH: candidateSetSha256 does not seal the listed candidate references and source hashes.'
        }
    }
}

function Test-ProviderApprovalsAndWorker {
    param($Section)

    if (!(Test-KickoffSectionHeader `
        -RequiredInputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -Section $Section)) {
        return
    }
    $providers = @($Section.candidateProviders)
    if ($providers.Count -ne 1 -or $providers[0].role -ne 'Primary') {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_PROVIDER_COUNT_INVALID: Core GA requires exactly one approved Primary Provider chain.'
    }
    foreach ($provider in $providers) {
        $providerId = ([string]$provider.providerKey) + '@' +
            ([string]$provider.providerVersion)
        if (!(Test-KickoffText $provider.providerKey) -or
            !(Test-KickoffText $provider.providerVersion) -or
            $provider.adapterContract -ne 'ICadConverter' -or
            $provider.dataBoundary -notin @('ControlledIsolatedWorker', 'ApprovedCloud')) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_PROVIDER_INVALID: $providerId must bind a version, ICadConverter and an approved data boundary."
        }
        if ($provider.licensingApproved -ne $true -or
            $provider.securityApproved -ne $true -or
            $provider.retentionDeletionApproved -ne $true) {
            Add-KickoffValidationError "SPACE_GA_KICKOFF_PROVIDER_APPROVALS_INCOMPLETE: $providerId is missing licensing, security or retention/deletion approval."
        }
        Test-KickoffEvidence -OwnerId "$providerId licensing" -Evidence $provider.licensingEvidence
        Test-KickoffEvidence -OwnerId "$providerId security" -Evidence $provider.securityEvidence
        Test-KickoffEvidence -OwnerId "$providerId retention and deletion" -Evidence $provider.retentionDeletionEvidence
        if ($provider.dataBoundary -eq 'ApprovedCloud') {
            if ($provider.cloudApprovals.tenantApproved -ne $true -or
                $provider.cloudApprovals.customerApproved -ne $true -or
                $provider.cloudApprovals.securityApproved -ne $true) {
                Add-KickoffValidationError "SPACE_GA_KICKOFF_CLOUD_APPROVALS_INCOMPLETE: $providerId cloud processing requires tenant, customer and security approval."
            }
            Test-KickoffEvidence -OwnerId "$providerId tenant cloud approval" -Evidence $provider.cloudApprovals.tenantEvidence
            Test-KickoffEvidence -OwnerId "$providerId customer cloud approval" -Evidence $provider.cloudApprovals.customerEvidence
            Test-KickoffEvidence -OwnerId "$providerId security cloud approval" -Evidence $provider.cloudApprovals.securityEvidence
        }
    }

    $worker = $Section.worker
    if ([string]$worker.workerRef -notmatch '^urn:cp6-space-ga-worker:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$' -or
        !(Test-KickoffSha256 $worker.environmentSha256) -or
        $worker.isolated -ne $true -or
        $worker.secretsByReferenceOnly -ne $true -or
        $worker.rawCadRetentionMode -ne 'Ephemeral' -or
        $worker.outboundNetworkPolicy -ne 'DenyByDefault') {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_WORKER_INVALID: the controlled Worker must be opaque, environment-sealed, isolated, ephemeral, deny-by-default and use secret references only.'
    }
    Test-KickoffEvidence -OwnerId 'Isolated Worker readiness' -Evidence $worker.readinessEvidence
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Kickoff evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Kickoff evidence manifest must remain inside the repository.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson
$requiredInputIds = @(
    'AUTHORIZED_GOLDEN_CAD_CANDIDATES',
    'PRIMARY_PROVIDER_AND_ISOLATED_WORKER')
if ((Test-KickoffText $InputId) -and $InputId -notin $requiredInputIds) {
    Add-KickoffValidationError "SPACE_GA_KICKOFF_INPUT_INVALID: unsupported external input id $InputId."
}
if ($manifest.schemaVersion -ne 3 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'M0_EXTERNAL_INPUT_READINESS') {
    Add-KickoffValidationError 'SPACE_GA_KICKOFF_SCHEMA_INVALID: schema, program or evidence class does not match the frozen M0 contract.'
}
if (Test-KickoffText $InputId) {
    if ($manifest.conclusion -notin @('InProgress', 'Pass')) {
        Add-KickoffValidationError 'SPACE_GA_KICKOFF_CONCLUSION_INVALID: an input can close only from an InProgress or Pass package.'
    }
}
elseif ($manifest.conclusion -ne 'Pass') {
    Add-KickoffValidationError 'SPACE_GA_KICKOFF_CONCLUSION_INVALID: the complete kickoff package must conclude Pass.'
}
$kickoffDate = ConvertTo-KickoffDate $manifest.kickoffDate
$targetGaDate = ConvertTo-KickoffDate $manifest.targetGaDate
if ($null -eq $kickoffDate -or $null -eq $targetGaDate -or
    $targetGaDate -le $kickoffDate -or
    $kickoffDate -gt [DateTime]::UtcNow.Date) {
    Add-KickoffValidationError 'SPACE_GA_KICKOFF_DATES_INVALID: kickoff and target GA dates must be ordered yyyy-MM-dd values and kickoff cannot be in the future.'
}

$sections = [ordered]@{
    AUTHORIZED_GOLDEN_CAD_CANDIDATES = $manifest.authorizedGoldenCadCandidates
    PRIMARY_PROVIDER_AND_ISOLATED_WORKER = $manifest.primaryProviderAndIsolatedWorker
}
$inputsToValidate = if (Test-KickoffText $InputId) {
    @($InputId)
}
else {
    $requiredInputIds
}
foreach ($currentInputId in $inputsToValidate) {
    switch ($currentInputId) {
        'AUTHORIZED_GOLDEN_CAD_CANDIDATES' {
            Test-AuthorizedGoldenCadCandidates $sections[$currentInputId]
        }
        'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' {
            Test-ProviderApprovalsAndWorker $sections[$currentInputId]
        }
    }
}

if ($errors.Count -gt 0) {
    throw ("Kickoff evidence validation failed:`n" + ($errors -join "`n"))
}

[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    validatedInputs = @($inputsToValidate)
} | ConvertTo-Json -Compress
