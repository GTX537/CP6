param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [string]$ExpectedOwnerName,
    [switch]$AllowTestFixtures
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SpaceGaJson.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$repoFullPath = [System.IO.Path]::GetFullPath($repo).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [System.IO.Path]::DirectorySeparatorChar
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$errors = [System.Collections.Generic.List[string]]::new()

function Fail-Rehearsal([string]$Message) { $errors.Add($Message) }
function Test-RehearsalText($Value) {
    return $null -ne $Value -and
        ![string]::IsNullOrWhiteSpace([string]$Value)
}
function Test-RehearsalSha($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{64}$'
}
function Test-RehearsalPerson($Value) {
    if (!(Test-RehearsalText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    $name = ([string]$Value).Trim()
    if ($name -match '^\d+$') { return $false }
    return $name -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'admin|administrator|test|demo|simulated|\u5f85\u5b9a|' +
        '\u672a\u5b9a|\u8d1f\u8d23\u4eba|\u56e2\u961f)$')
}
function ConvertTo-RehearsalUtc($Value) {
    if ([string]$Value -notmatch (
        '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$')) {
        return $null
    }
    try {
        $parsed = [DateTimeOffset]::Parse(
            [string]$Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind)
        if ($parsed.Offset -ne [TimeSpan]::Zero) { return $null }
        return $parsed
    }
    catch { return $null }
}
function ConvertTo-RehearsalNumber($Value) {
    if ($null -eq $Value) { return $null }
    [double]$number = 0
    if (![double]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}
function ConvertTo-RehearsalInteger($Value) {
    if ($null -eq $Value) { return $null }
    [long]$number = 0
    if (![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}
function Test-RehearsalEvidence {
    param(
        [Parameter(Mandatory)][string]$Id,
        $Evidence,
        [Parameter(Mandatory)][string]$OwnerName,
        [Parameter(Mandatory)][DateTimeOffset]$ExecutedAt
    )

    $uri = [string]$Evidence.uri
    $sha = [string]$Evidence.sha256
    $acceptedBy = [string]$Evidence.acceptedBy
    $acceptedAt = ConvertTo-RehearsalUtc $Evidence.acceptedAtUtc
    if (!(Test-RehearsalText $uri)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_URI_REQUIRED: $Id requires an evidence URI."
        return
    }
    if (!(Test-RehearsalSha $sha)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_SHA_INVALID: $Id requires a SHA-256."
    }
    if (!(Test-RehearsalPerson $acceptedBy) -or
        !$acceptedBy.Equals($OwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_OWNER_INVALID: $Id must be accepted by the DeliveryOwner."
    }
    if ($null -eq $acceptedAt -or $acceptedAt -lt $ExecutedAt -or
        $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_TIME_INVALID: $Id must be accepted after execution using a non-future UTC timestamp."
    }

    if ($uri -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        [Uri]$absoluteUri = $null
        if (![Uri]::TryCreate($uri, [UriKind]::Absolute, [ref]$absoluteUri)) {
            Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_URI_INVALID: $Id evidence URI is malformed."
            return
        }
        $isHttps = $absoluteUri.Scheme -eq 'https' -and
            [string]::IsNullOrWhiteSpace($absoluteUri.UserInfo)
        $isUrn = $absoluteUri.Scheme -eq 'urn' -and
            $absoluteUri.AbsoluteUri -match (
                '^urn:cp6-space-ga-evidence:[A-Za-z0-9]' +
                '[A-Za-z0-9:._-]{0,500}$')
        if (!$isHttps -and !$isUrn) {
            Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_URI_INVALID: $Id must use repository-relative, HTTPS or CP6 GA URN evidence."
        }
        elseif (!$AllowTestFixtures -and $isUrn -and
            $absoluteUri.AbsoluteUri -match ':test:') {
            Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_SYNTHETIC: $Id cannot use test evidence in formal mode."
        }
        return
    }

    if ([System.IO.Path]::IsPathRooted($uri)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_PATH_ABSOLUTE: $Id uses an absolute path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $uri))
    $normalized = $uri.Replace('\', '/')
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_PATH_ESCAPE: $Id escapes the repository."
        return
    }
    if ([System.IO.Path]::GetExtension($fullPath) -match '^(?i:\.dwg|\.dxf)$') {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_RAW_CAD_FORBIDDEN: $Id cannot reference raw CAD in Git."
        return
    }
    if (!$AllowTestFixtures -and $normalized -match '(^|/)tools/test-fixtures/') {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_SYNTHETIC: $Id cannot use a test fixture in formal mode."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_MISSING: $Id evidence does not exist."
        return
    }
    if ((Test-RehearsalSha $sha) -and
        !((Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.Equals(
            $sha,
            [System.StringComparison]::OrdinalIgnoreCase))) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_EVIDENCE_SHA_MISMATCH: $Id evidence hash does not match."
    }
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Release rehearsal evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Release rehearsal manifest must remain inside the repository.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson
if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'WP8_RELEASE_REHEARSAL') {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_SCHEMA_INVALID: schema, program, delivery mode or evidence class is invalid.'
}
if ($manifest.conclusion -ne 'Pass') {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_CONCLUSION_INVALID: only a final Pass package can close WP8.'
}
$owner = [string]$manifest.ownerName
if (!(Test-RehearsalPerson $owner)) {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_OWNER_INVALID: one real DeliveryOwner is required.'
}
elseif ((Test-RehearsalPerson $ExpectedOwnerName) -and
    !$owner.Equals(
        $ExpectedOwnerName,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_OWNER_MISMATCH: rehearsal owner must match the WP8 owner.'
}
$executedAt = ConvertTo-RehearsalUtc $manifest.executedAtUtc
if ($null -eq $executedAt -or
    $executedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_TIME_INVALID: executedAtUtc must be a non-future UTC timestamp.'
    $executedAt = [DateTimeOffset]::MinValue
}
if ([string]$manifest.applicationCommitSha -notmatch '^[a-fA-F0-9]{40}$' -or
    !(Test-RehearsalSha $manifest.sourceSetSha256) -or
    !(Test-RehearsalSha $manifest.goldenDatasetSha256) -or
    !(Test-RehearsalSha $manifest.workerEnvironmentSha256)) {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_BASELINE_INVALID: commit, source set, golden dataset and Worker hashes are required.'
}
if ($manifest.environment.mode -ne 'ControlledReleaseRehearsal' -or
    $manifest.environment.databaseEngine -ne 'SQLServer' -or
    $manifest.environment.wmsSystem -ne 'CP6_WMS' -or
    $manifest.environment.publishedViewerOnly -ne $true -or
    $manifest.environment.secretsByReferenceOnly -ne $true) {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_ENVIRONMENT_INVALID: controlled SQL Server, CP6 WMS, Published-only Viewer and secret references are required.'
}
$resultNames = @(
    'cadDwgDxfEndToEndPassed',
    'threeAuthoringPathsPassed',
    'publishAndWmsPassed',
    'publishedDraftIsolationPassed',
    'recoveryPassed',
    'securityNegativePassed',
    'noDuplicateWrites')
foreach ($resultName in $resultNames) {
    if ($manifest.results.$resultName -ne $true) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_RESULT_FAILED: $resultName must pass."
    }
}
$automaticMinutes = ConvertTo-RehearsalNumber (
    $manifest.recovery.automaticRecoveryMaxMinutes)
$manualMinutes = ConvertTo-RehearsalNumber (
    $manifest.recovery.manualRecoveryMaxMinutes)
if ($null -eq $automaticMinutes -or $automaticMinutes -lt 0 -or
    $automaticMinutes -gt 15 -or $null -eq $manualMinutes -or
    $manualMinutes -lt 0 -or $manualMinutes -gt 240 -or
    $manifest.recovery.oldPublishedRemainedAvailable -ne $true) {
    Fail-Rehearsal 'SPACE_GA_REHEARSAL_RECOVERY_FAILED: recovery must stay within 15/240 minutes and old Published must remain available.'
}
foreach ($severity in @('s1Open', 's2Open', 'blockingS3Open')) {
    if ((ConvertTo-RehearsalInteger $manifest.defects.$severity) -ne 0) {
        Fail-Rehearsal "SPACE_GA_REHEARSAL_DEFECTS_OPEN: $severity must be zero."
    }
}
foreach ($evidenceName in @('execution', 'publishWms', 'viewer', 'recovery', 'security')) {
    Test-RehearsalEvidence `
        -Id $evidenceName `
        -Evidence $manifest.evidence.$evidenceName `
        -OwnerName $owner `
        -ExecutedAt $executedAt
}

if ($errors.Count -gt 0) {
    throw ("Release rehearsal evidence validation failed:`n" +
        ($errors -join "`n"))
}
[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    ownerName = $owner
    resultCount = $resultNames.Count
} | ConvertTo-Json -Compress
