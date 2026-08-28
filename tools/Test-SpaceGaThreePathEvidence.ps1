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

function Fail-ThreePath([string]$Message) { $errors.Add($Message) }
function Test-ThreePathText($Value) {
    return $null -ne $Value -and
        ![string]::IsNullOrWhiteSpace([string]$Value)
}
function Test-ThreePathSha($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{64}$'
}
function Test-ThreePathPerson($Value) {
    if (!(Test-ThreePathText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    $name = ([string]$Value).Trim()
    if ($name -match '^\d+$') { return $false }
    return $name -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'admin|administrator|test|demo|simulated|\u5f85\u5b9a|' +
        '\u672a\u5b9a|\u8d1f\u8d23\u4eba|\u56e2\u961f)$')
}
function ConvertTo-ThreePathUtc($Value) {
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
function ConvertTo-ThreePathInteger($Value) {
    if ($null -eq $Value) { return $null }
    [long]$number = 0
    if (![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}
function Test-ThreePathEvidence {
    param(
        [Parameter(Mandatory)][string]$Id,
        $Evidence,
        [Parameter(Mandatory)][string]$OwnerName,
        [Parameter(Mandatory)][DateTimeOffset]$ExecutedAt
    )

    $uri = [string]$Evidence.uri
    $sha = [string]$Evidence.sha256
    $acceptedBy = [string]$Evidence.acceptedBy
    $acceptedAt = ConvertTo-ThreePathUtc $Evidence.acceptedAtUtc
    if (!(Test-ThreePathText $uri)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_URI_REQUIRED: $Id requires an evidence URI."
        return
    }
    if (!(Test-ThreePathSha $sha)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_SHA_INVALID: $Id requires a SHA-256."
    }
    if (!(Test-ThreePathPerson $acceptedBy) -or
        !$acceptedBy.Equals($OwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_OWNER_INVALID: $Id must be accepted by the DeliveryOwner."
    }
    if ($null -eq $acceptedAt -or $acceptedAt -lt $ExecutedAt -or
        $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_TIME_INVALID: $Id must be accepted after execution using a non-future UTC timestamp."
    }

    if ($uri -match '^[A-Za-z][A-Za-z0-9+.-]*:') {
        [Uri]$absoluteUri = $null
        if (![Uri]::TryCreate($uri, [UriKind]::Absolute, [ref]$absoluteUri)) {
            Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_URI_INVALID: $Id evidence URI is malformed."
            return
        }
        $isHttps = $absoluteUri.Scheme -eq 'https' -and
            [string]::IsNullOrWhiteSpace($absoluteUri.UserInfo)
        $isUrn = $absoluteUri.Scheme -eq 'urn' -and
            $absoluteUri.AbsoluteUri -match (
                '^urn:cp6-space-ga-evidence:[A-Za-z0-9]' +
                '[A-Za-z0-9:._-]{0,500}$')
        if (!$isHttps -and !$isUrn) {
            Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_URI_INVALID: $Id must use repository-relative, HTTPS or CP6 GA URN evidence."
        }
        elseif (!$AllowTestFixtures -and $isUrn -and
            $absoluteUri.AbsoluteUri -match ':test:') {
            Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_SYNTHETIC: $Id cannot use test evidence in formal mode."
        }
        return
    }

    if ([System.IO.Path]::IsPathRooted($uri)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_PATH_ABSOLUTE: $Id uses an absolute path."
        return
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $uri))
    $normalized = $uri.Replace('\', '/')
    if (!$fullPath.StartsWith(
        $repoPrefix,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_PATH_ESCAPE: $Id escapes the repository."
        return
    }
    if ([System.IO.Path]::GetExtension($fullPath) -match
        '^(?i:\.dwg|\.dxf|\.xlsx|\.pdf|\.png|\.jpg|\.jpeg)$') {
        Fail-ThreePath "SPACE_GA_THREE_PATH_RAW_INPUT_FORBIDDEN: $Id cannot reference raw acceptance inputs in Git."
        return
    }
    if (!$AllowTestFixtures -and $normalized -match '(^|/)tools/test-fixtures/') {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_SYNTHETIC: $Id cannot use a test fixture in formal mode."
        return
    }
    if (!(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_MISSING: $Id evidence does not exist."
        return
    }
    if ((Test-ThreePathSha $sha) -and
        !((Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.Equals(
            $sha,
            [System.StringComparison]::OrdinalIgnoreCase))) {
        Fail-ThreePath "SPACE_GA_THREE_PATH_EVIDENCE_SHA_MISMATCH: $Id evidence hash does not match."
    }
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Three-path evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Three-path evidence manifest must remain inside the repository.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson
if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'WP4_THREE_PATH_FORMAL_EVIDENCE') {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_SCHEMA_INVALID: schema, program, delivery mode or evidence class is invalid.'
}
if ($manifest.conclusion -ne 'Pass') {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_CONCLUSION_INVALID: only a final Pass package can close WP4.'
}
$owner = [string]$manifest.ownerName
if (!(Test-ThreePathPerson $owner)) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_OWNER_INVALID: one real DeliveryOwner is required.'
}
elseif ((Test-ThreePathPerson $ExpectedOwnerName) -and
    !$owner.Equals($ExpectedOwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_OWNER_MISMATCH: evidence owner must match the WP4 owner.'
}
$executedAt = ConvertTo-ThreePathUtc $manifest.executedAtUtc
if ($null -eq $executedAt -or
    $executedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_TIME_INVALID: executedAtUtc must be a non-future UTC timestamp.'
    $executedAt = [DateTimeOffset]::MinValue
}
if ([string]$manifest.applicationCommitSha -notmatch '^[a-fA-F0-9]{40}$' -or
    !(Test-ThreePathSha $manifest.sourceSetSha256) -or
    !(Test-ThreePathSha $manifest.goldenDatasetSha256) -or
    !(Test-ThreePathSha $manifest.workerEnvironmentSha256)) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_BASELINE_INVALID: commit, source set, golden dataset and Worker hashes are required.'
}
if ($manifest.environment.mode -ne 'ControlledAcceptance' -or
    $manifest.environment.databaseEngine -ne 'SQLServer' -or
    $manifest.environment.productionDeploymentPerformed -ne $false -or
    $manifest.environment.productionDataClaimed -ne $false) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_ENVIRONMENT_INVALID: controlled SQL Server acceptance must not claim production data or deployment.'
}

$cadInputs = @($manifest.inputs.cad)
$cadFormats = @($cadInputs | ForEach-Object { [string]$_.sourceFormat })
if ($cadInputs.Count -ne 2 -or
    @($cadFormats | Sort-Object -Unique).Count -ne 2 -or
    'DWG' -notin $cadFormats -or 'DXF' -notin $cadFormats) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_CAD_SET_INVALID: exactly one authorized DWG and one authorized DXF are required.'
}
foreach ($cad in $cadInputs) {
    if ([string]$cad.sampleRef -notmatch
            '^urn:cp6-space-golden-cad:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$' -or
        $cad.license -notin @('ApprovedOriginalWork', 'ApprovedCustomerDerived') -or
        !(Test-ThreePathSha $cad.sourceSha256) -or
        (ConvertTo-ThreePathInteger $cad.sourceSizeBytes) -le 0 -or
        !(Test-ThreePathSha $cad.providerPackageSha256) -or
        !(Test-ThreePathText $cad.providerKey) -or
        !(Test-ThreePathText $cad.providerVersion)) {
        Fail-ThreePath 'SPACE_GA_THREE_PATH_CAD_INPUT_INVALID: each CAD input requires an approved source identity and Primary package binding.'
    }
}
$excel = $manifest.inputs.excel
if ($excel.format -ne 'XLSX' -or
    $excel.dataClass -ne 'ControlledAcceptanceData' -or
    !(Test-ThreePathSha $excel.sha256) -or
    $excel.productionDataClaimed -ne $false) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_EXCEL_INPUT_INVALID: a hash-bound controlled XLSX with no production-data claim is required.'
}
$underlays = @($manifest.inputs.underlays)
$underlayFormats = @($underlays | ForEach-Object { [string]$_.format })
if ($underlays.Count -ne 2 -or
    'PDF' -notin $underlayFormats -or 'PNG' -notin $underlayFormats -or
    $manifest.inputs.blankCanvasIncluded -ne $true) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_MANUAL_INPUT_INVALID: one PDF, one PNG and blank-canvas coverage are required.'
}
foreach ($underlay in $underlays) {
    if ($underlay.dataClass -ne 'ControlledAcceptanceData' -or
        !(Test-ThreePathSha $underlay.sha256) -or
        $underlay.productionDataClaimed -ne $false) {
        Fail-ThreePath 'SPACE_GA_THREE_PATH_UNDERLAY_INPUT_INVALID: underlays require controlled hashes and no production-data claim.'
    }
}

$requiredPaths = @('CAD', 'ExcelCad', 'ManualUnderlayBlankCanvas')
$paths = @($manifest.paths)
$pathNames = @($paths | ForEach-Object { [string]$_.path })
if ($paths.Count -ne 3 -or
    @($pathNames | Sort-Object -Unique).Count -ne 3 -or
    @($requiredPaths | Where-Object { $_ -notin $pathNames }).Count -gt 0) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_SET_INVALID: CAD, ExcelCad and ManualUnderlayBlankCanvas paths are required exactly once.'
}
foreach ($path in $paths) {
    foreach ($result in @(
        'previewPassed',
        'draftUnchangedBeforeApply',
        'explicitApplyPassed',
        'typedChangesetPassed',
        'leaseRevisionIdempotencyPassed')) {
        if ($path.$result -ne $true) {
            Fail-ThreePath "SPACE_GA_THREE_PATH_RESULT_FAILED: $($path.path).$result must pass."
        }
    }
}

$sqlPassed = ConvertTo-ThreePathInteger $manifest.sqlServer.passed
$sqlFailed = ConvertTo-ThreePathInteger $manifest.sqlServer.failed
$sqlSkipped = ConvertTo-ThreePathInteger $manifest.sqlServer.skipped
if (!(Test-ThreePathText $manifest.sqlServer.productVersion) -or
    !(Test-ThreePathText $manifest.sqlServer.edition) -or
    $null -eq $sqlPassed -or $sqlPassed -le 0 -or
    $sqlFailed -ne 0 -or $sqlSkipped -ne 0) {
    Fail-ThreePath 'SPACE_GA_THREE_PATH_SQLSERVER_FAILED: a real SQL Server run must have passed tests with zero failures and zero skips.'
}

foreach ($evidenceName in @(
    'cad', 'excelCad', 'manualUnderlayBlankCanvas', 'sqlServer')) {
    Test-ThreePathEvidence `
        -Id $evidenceName `
        -Evidence $manifest.evidence.$evidenceName `
        -OwnerName $owner `
        -ExecutedAt $executedAt
}

if ($errors.Count -gt 0) {
    throw ("Three-path evidence validation failed:`n" +
        ($errors -join "`n"))
}
[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    cadInputCount = $cadInputs.Count
    pathCount = $paths.Count
    sqlServerPassed = $sqlPassed
} | ConvertTo-Json -Compress
