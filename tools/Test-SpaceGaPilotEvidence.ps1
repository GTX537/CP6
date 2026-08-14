param(
    [Parameter(Mandatory)]
    [string]$ManifestPath,
    [switch]$AllowTestFixtures
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$repoFullPath = [System.IO.Path]::GetFullPath($repo).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [System.IO.Path]::DirectorySeparatorChar
$errors = [System.Collections.Generic.List[string]]::new()

function Add-PilotValidationError {
    param([Parameter(Mandatory)][string]$Message)
    $errors.Add($Message)
}

function Test-PilotText {
    param($Value)
    return $null -ne $Value -and
        ![string]::IsNullOrWhiteSpace([string]$Value)
}

function Test-PilotPersonName {
    param($Value)

    if (!(Test-PilotText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    return ([string]$Value).Trim() -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'architecture|security|admin|administrator|customer|' +
        'implementation|\u5f85\u5b9a|\u672a\u5b9a|\u8d1f\u8d23\u4eba|' +
        '\u56e2\u961f|\u4ea7\u54c1|\u6d4b\u8bd5|\u8d28\u91cf|' +
        '\u67b6\u6784|\u5b89\u5168|\u7ba1\u7406\u5458)$')
}

function ConvertTo-NonNegativeInteger {
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

function ConvertTo-PilotNumber {
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

function ConvertTo-PilotDate {
    param($Value)

    [DateTime]$parsed = [DateTime]::MinValue
    if (!(Test-PilotText $Value) -or ![DateTime]::TryParseExact(
        [string]$Value,
        'yyyy-MM-dd',
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None,
        [ref]$parsed)) {
        return $null
    }
    return $parsed
}

function Test-PilotAttestedEvidence {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        $Evidence
    )

    $reference = [string]$Evidence.uri
    $sha256 = [string]$Evidence.sha256
    $acceptedBy = [string]$Evidence.acceptedBy
    $acceptedAtUtc = [string]$Evidence.acceptedAtUtc
    $shaIsValid = $sha256 -match '^[a-fA-F0-9]{64}$'

    if (!(Test-PilotText $reference) -or $reference.Length -gt 2048) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_URI_REQUIRED: $OwnerId has a missing or oversized evidence URI."
    }
    if (!$shaIsValid) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_SHA_INVALID: $OwnerId evidence SHA-256 is invalid."
    }
    if (!(Test-PilotPersonName $acceptedBy)) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_ACCEPTOR_INVALID: $OwnerId must name the real accepting person."
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
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_TIME_INVALID: $OwnerId acceptedAtUtc must be an ISO-8601 UTC timestamp."
    }
    elseif ($acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_TIME_FUTURE: $OwnerId acceptedAtUtc cannot be in the future."
    }

    if (!(Test-PilotText $reference)) {
        return
    }
    if ($reference -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        [Uri]$absoluteUri = $null
        if (![Uri]::TryCreate($reference, [UriKind]::Absolute, [ref]$absoluteUri)) {
            Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_URI_MALFORMED: $OwnerId evidence URI is malformed."
            return
        }
        $isControlledHttps = $absoluteUri.Scheme -eq 'https' -and
            [string]::IsNullOrWhiteSpace($absoluteUri.UserInfo)
        $isControlledUrn = $absoluteUri.Scheme -eq 'urn' -and
            $absoluteUri.AbsoluteUri -match (
                '^urn:cp6-space-ga-evidence:[A-Za-z0-9]' +
                '[A-Za-z0-9:._-]{0,500}$')
        if (!$isControlledHttps -and !$isControlledUrn) {
            Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_URI_UNCONTROLLED: $OwnerId evidence URI must be repository-relative, HTTPS, or a CP6 GA evidence URN."
        }
        return
    }

    if ([System.IO.Path]::IsPathRooted($reference)) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_PATH_ABSOLUTE: $OwnerId uses an absolute evidence path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $reference))
    $normalizedReference = $reference.Replace('\', '/')
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_PATH_ESCAPE: $OwnerId evidence escapes the repository root."
        return
    }
    if (!$AllowTestFixtures -and
        $normalizedReference -match '(^|/)tools/test-fixtures/') {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_SYNTHETIC: $OwnerId cannot use a test fixture as formal Pilot evidence."
        return
    }
    if ([System.IO.Path]::GetExtension($fullPath) -match '^(?i:\.dwg|\.dxf)$') {
        Add-PilotValidationError "SPACE_GA_PILOT_RAW_CAD_FORBIDDEN: $OwnerId references raw customer CAD inside the repository."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_PATH_MISSING: $OwnerId evidence path does not exist: $reference"
        return
    }
    if ($shaIsValid) {
        $actualSha256 = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        if (!$actualSha256.Equals(
            $sha256,
            [System.StringComparison]::OrdinalIgnoreCase)) {
            Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_SHA_MISMATCH: $OwnerId evidence SHA-256 does not match: $reference"
        }
    }
}

function Test-PilotConfirmation {
    param(
        [Parameter(Mandatory)][string]$OwnerId,
        $Confirmation
    )

    $name = [string]$Confirmation.name
    if (!(Test-PilotPersonName $name)) {
        Add-PilotValidationError "SPACE_GA_PILOT_CONFIRMATION_NAME_INVALID: $OwnerId must name a real person."
    }
    Test-PilotAttestedEvidence -OwnerId $OwnerId -Evidence $Confirmation.evidence
    if ((Test-PilotPersonName $name) -and
        !([string]$Confirmation.evidence.acceptedBy).Equals(
            $name,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-PilotValidationError "SPACE_GA_PILOT_CONFIRMATION_MISMATCH: $OwnerId evidence acceptor must match the named confirmer."
    }
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Pilot evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Pilot evidence manifest must remain inside the repository.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-Json

if ($manifest.schemaVersion -ne 1) {
    Add-PilotValidationError 'SPACE_GA_PILOT_SCHEMA_INVALID: schemaVersion must be 1.'
}
if ($manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA') {
    Add-PilotValidationError 'SPACE_GA_PILOT_PROGRAM_INVALID: programId is not the frozen Core GA program.'
}
if ($manifest.evidenceClass -ne 'WP8_TWO_SITE_PILOT') {
    Add-PilotValidationError 'SPACE_GA_PILOT_CLASS_INVALID: evidenceClass must be WP8_TWO_SITE_PILOT.'
}
if ($manifest.conclusion -ne 'Pass') {
    Add-PilotValidationError 'SPACE_GA_PILOT_CONCLUSION_INVALID: only a final Pass package can close WP8.'
}

$sites = @($manifest.sites)
if ($sites.Count -ne 2) {
    Add-PilotValidationError 'SPACE_GA_PILOT_SITE_SET_INVALID: exactly two Pilot Sites are required.'
}
$siteTypes = @($sites | ForEach-Object { [string]$_.siteType })
$siteRefs = @($sites | ForEach-Object { [string]$_.siteRef })
if (@($siteTypes | Sort-Object -Unique).Count -ne 2 -or
    'Greenfield' -notin $siteTypes -or 'Retrofit' -notin $siteTypes) {
    Add-PilotValidationError 'SPACE_GA_PILOT_SITE_TYPES_INVALID: one Greenfield and one Retrofit Site are required.'
}
if (@($siteRefs | Sort-Object -Unique).Count -ne $siteRefs.Count) {
    Add-PilotValidationError 'SPACE_GA_PILOT_SITE_REFS_DUPLICATE: Pilot Site references must be unique.'
}

foreach ($site in $sites) {
    $siteRef = [string]$site.siteRef
    $ownerId = if (Test-PilotText $siteRef) { $siteRef } else { 'Pilot Site' }
    if ($siteRef -notmatch '^urn:cp6-space-site:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$') {
        Add-PilotValidationError "SPACE_GA_PILOT_SITE_REF_INVALID: $ownerId must use an opaque CP6 Site URN, not a customer name."
    }

    $start = ConvertTo-PilotDate $site.runStartDate
    $end = ConvertTo-PilotDate $site.runEndDate
    if ($null -eq $start -or $null -eq $end -or $end -lt $start) {
        Add-PilotValidationError "SPACE_GA_PILOT_DATES_INVALID: $ownerId must provide ordered ISO run dates."
        $calendarDays = $null
    }
    else {
        $calendarDays = ($end - $start).Days + 1
        if ($end.Date -gt [DateTime]::UtcNow.Date) {
            Add-PilotValidationError "SPACE_GA_PILOT_DATES_FUTURE: $ownerId cannot close a Pilot window in the future."
        }
    }
    $continuousDays = ConvertTo-NonNegativeInteger $site.continuousRunDays
    $dailyRecordCount = ConvertTo-NonNegativeInteger $site.dailyRecordCount
    $dailyRecordDates = @($site.dailyRecordDates)
    $dailyDatesValid = $null -ne $calendarDays -and
        $dailyRecordDates.Count -eq $calendarDays
    if ($dailyDatesValid) {
        for ($dayIndex = 0; $dayIndex -lt $dailyRecordDates.Count; $dayIndex++) {
            $expectedDate = $start.AddDays($dayIndex).ToString(
                'yyyy-MM-dd',
                [System.Globalization.CultureInfo]::InvariantCulture)
            if ([string]$dailyRecordDates[$dayIndex] -ne $expectedDate) {
                $dailyDatesValid = $false
                break
            }
        }
    }
    if ($null -eq $calendarDays -or $calendarDays -lt 14 -or
        $null -eq $continuousDays -or $continuousDays -ne $calendarDays -or
        $null -eq $dailyRecordCount -or
        $dailyRecordCount -ne $dailyRecordDates.Count -or
        !$dailyDatesValid) {
        Add-PilotValidationError "SPACE_GA_PILOT_CONTINUITY_INVALID: $ownerId needs at least 14 consecutive calendar days and one immutable daily record per day."
    }

    $s1Count = ConvertTo-NonNegativeInteger $site.defects.s1Count
    $s2Count = ConvertTo-NonNegativeInteger $site.defects.s2Count
    $s3Opened = ConvertTo-NonNegativeInteger $site.defects.s3Opened
    $s3WithWorkaround = ConvertTo-NonNegativeInteger $site.defects.s3WithUsableWorkaround
    $s3Closed = ConvertTo-NonNegativeInteger $site.defects.s3ClosedBeforeSignoff
    $s3Open = ConvertTo-NonNegativeInteger $site.defects.s3OpenAtSignoff
    if ($s1Count -ne 0 -or $s2Count -ne 0) {
        Add-PilotValidationError "SPACE_GA_PILOT_SEVERE_DEFECTS: $ownerId must have zero S1 and S2 defects."
    }
    if ($null -eq $s3Opened -or $null -eq $s3Closed -or
        $null -eq $s3Open -or $s3Open -ne 0 -or $s3Closed -ne $s3Opened) {
        Add-PilotValidationError "SPACE_GA_PILOT_S3_OPEN: $ownerId must close every S3 before signoff."
    }
    if ($null -eq $s3WithWorkaround -or $s3WithWorkaround -ne $s3Opened) {
        Add-PilotValidationError "SPACE_GA_PILOT_S3_WORKAROUND_MISSING: $ownerId must record a usable workaround for every S3."
    }

    $modelingMinutes = ConvertTo-PilotNumber $site.metrics.modelingDurationMinutes
    $manualChanges = ConvertTo-NonNegativeInteger $site.metrics.manualModificationCount
    $objectConsistency = ConvertTo-PilotNumber $site.metrics.twoDThreeDObjectConsistencyPercent
    $wmsConsistency = ConvertTo-PilotNumber $site.metrics.wmsConsistencyPercent
    if ($null -eq $modelingMinutes -or $modelingMinutes -le 0 -or
        $null -eq $manualChanges) {
        Add-PilotValidationError "SPACE_GA_PILOT_MODELING_METRICS_INVALID: $ownerId must record real modeling duration and manual modification count."
    }
    if ($objectConsistency -ne 100 -or $wmsConsistency -ne 100) {
        Add-PilotValidationError "SPACE_GA_PILOT_CONSISTENCY_FAILED: $ownerId must prove 100 percent 2D/3D/object-list and WMS consistency."
    }

    $automaticIncidents = ConvertTo-NonNegativeInteger $site.recovery.automaticIncidentCount
    $automaticMax = ConvertTo-PilotNumber $site.recovery.automaticMaxMinutes
    $manualIncidents = ConvertTo-NonNegativeInteger $site.recovery.manualIncidentCount
    $manualMax = ConvertTo-PilotNumber $site.recovery.manualMaxMinutes
    $automaticInvalid = $null -eq $automaticIncidents -or $null -eq $automaticMax -or
        ($automaticIncidents -eq 0 -and $automaticMax -ne 0) -or
        ($automaticIncidents -gt 0 -and ($automaticMax -le 0 -or $automaticMax -gt 15))
    $manualInvalid = $null -eq $manualIncidents -or $null -eq $manualMax -or
        ($manualIncidents -eq 0 -and $manualMax -ne 0) -or
        ($manualIncidents -gt 0 -and ($manualMax -le 0 -or $manualMax -gt 240))
    if ($automaticInvalid -or $manualInvalid) {
        Add-PilotValidationError "SPACE_GA_PILOT_RECOVERY_SLO_FAILED: $ownerId exceeds the 15-minute automatic or 240-minute manual recovery limit."
    }
    if ($site.recovery.oldPublishedContinuouslyAvailable -ne $true) {
        Add-PilotValidationError "SPACE_GA_PILOT_PUBLISHED_UNAVAILABLE: $ownerId did not prove old Published remained available during failures."
    }
    if ($site.boundaries.publishedViewerOnly -ne $true -or
        $site.boundaries.noLongTermDualWrite -ne $true) {
        Add-PilotValidationError "SPACE_GA_PILOT_BOUNDARY_FAILED: $ownerId must keep Viewer Published-only and avoid long-term dual write."
    }

    Test-PilotAttestedEvidence -OwnerId "$ownerId run log" -Evidence $site.evidence.runLog
    Test-PilotAttestedEvidence -OwnerId "$ownerId metrics" -Evidence $site.evidence.metrics
    Test-PilotAttestedEvidence -OwnerId "$ownerId defect closure" -Evidence $site.evidence.defectClosure
    Test-PilotAttestedEvidence -OwnerId "$ownerId business outcome" -Evidence $site.evidence.businessOutcome
    Test-PilotAttestedEvidence -OwnerId "$ownerId open issues appendix" -Evidence $site.evidence.openIssuesAppendix
    Test-PilotConfirmation -OwnerId "$ownerId customer warehouse representative" -Confirmation $site.confirmations.customerWarehouseRepresentative
    Test-PilotConfirmation -OwnerId "$ownerId implementation lead" -Confirmation $site.confirmations.implementationLead

    if ($null -ne $end) {
        $attestations = @(
            $site.evidence.runLog,
            $site.evidence.metrics,
            $site.evidence.defectClosure,
            $site.evidence.businessOutcome,
            $site.evidence.openIssuesAppendix,
            $site.confirmations.customerWarehouseRepresentative.evidence,
            $site.confirmations.implementationLead.evidence)
        foreach ($attestation in $attestations) {
            [DateTimeOffset]$acceptedAt = [DateTimeOffset]::MinValue
            if ([DateTimeOffset]::TryParse(
                [string]$attestation.acceptedAtUtc,
                [System.Globalization.CultureInfo]::InvariantCulture,
                [System.Globalization.DateTimeStyles]::RoundtripKind,
                [ref]$acceptedAt) -and
                $acceptedAt.UtcDateTime.Date -lt $end.Date) {
                Add-PilotValidationError "SPACE_GA_PILOT_EVIDENCE_PREMATURE: $ownerId evidence cannot be accepted before the Pilot window ends."
                break
            }
        }
    }
}

if ($errors.Count -gt 0) {
    throw ("Pilot evidence validation failed:`n" + ($errors -join "`n"))
}

[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    siteCount = $sites.Count
    siteTypes = @($siteTypes | Sort-Object)
    minimumContinuousRunDays = @(
        $sites | ForEach-Object { [int]$_.continuousRunDays } |
            Measure-Object -Minimum).Minimum
} | ConvertTo-Json -Compress
