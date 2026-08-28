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

function Fail-ManualModeling([string]$Message) { $errors.Add($Message) }
function Test-ManualModelingText($Value) {
    return $null -ne $Value -and ![string]::IsNullOrWhiteSpace([string]$Value)
}
function Test-ManualModelingSha256($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{64}$'
}
function Test-ManualModelingGitOid($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{40}$'
}
function Test-ManualModelingPerson($Value) {
    if (!(Test-ManualModelingText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    $name = ([string]$Value).Trim()
    if ($name -match '^\d+$') { return $false }
    return $name -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'admin|administrator|test|demo|simulated|\u5f85\u5b9a|' +
        '\u672a\u5b9a|\u8d1f\u8d23\u4eba|\u56e2\u961f)$')
}
function ConvertTo-ManualModelingUtc($Value) {
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
function ConvertTo-ManualModelingInteger($Value) {
    if ($null -eq $Value) { return $null }
    [long]$number = 0
    if (![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}
function Get-ManualModelingGitBlobSha256([string]$BlobOid) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'
    $start.Arguments = "cat-file blob $BlobOid"
    $start.WorkingDirectory = $repoFullPath
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    $memory = [System.IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw $errorText }
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $algorithm.ComputeHash($memory.ToArray()))).Replace('-', '').ToLowerInvariant()
        }
        finally { $algorithm.Dispose() }
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Manual-modeling evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Manual-modeling evidence manifest must remain inside the repository.'
}
$manifestReference = $manifestFullPath.Substring($repoPrefix.Length).Replace('\', '/')
if (!$AllowTestFixtures -and (
    $manifestReference -match '(^|/)tools/test-fixtures/' -or
    $manifestReference.EndsWith(
        '/manual-modeling-evidence-template.json',
        [System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'SPACE_GA_MANUAL_MODELING_SYNTHETIC: a template or test fixture cannot close WP1.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson
if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'WP1_MANUAL_MODELING_FORMAL_EVIDENCE') {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_SCHEMA_INVALID: schema, program, delivery mode or evidence class is invalid.'
}
if ($manifest.conclusion -ne 'Pass') {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_CONCLUSION_INVALID: only a final Pass package can close WP1.'
}
$owner = [string]$manifest.ownerName
if (!(Test-ManualModelingPerson $owner)) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_OWNER_INVALID: one real DeliveryOwner is required.'
}
elseif ((Test-ManualModelingPerson $ExpectedOwnerName) -and
    !$owner.Equals($ExpectedOwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_OWNER_MISMATCH: evidence owner must match the WP1 owner.'
}
$executedAt = ConvertTo-ManualModelingUtc $manifest.executedAtUtc
if ($null -eq $executedAt -or $executedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_TIME_INVALID: executedAtUtc must be a non-future UTC timestamp.'
    $executedAt = [DateTimeOffset]::MinValue
}

$commit = [string]$manifest.applicationCommitSha
if (!(Test-ManualModelingGitOid $commit)) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_COMMIT_INVALID: a full application commit SHA is required.'
}
else {
    & git -C $repoFullPath cat-file -e "$commit`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_COMMIT_MISSING: the tested commit is not available.'
    }
    else {
        & git -C $repoFullPath merge-base --is-ancestor $commit HEAD
        if ($LASTEXITCODE -ne 0) {
            Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_COMMIT_NOT_ANCESTOR: the tested commit must be an ancestor of the evidence commit.'
        }
    }
}

$environment = $manifest.environment
if ($environment.mode -ne 'ControlledSelfReview' -or
    $environment.databaseEngine -ne 'SQLServer' -or
    $environment.testDataClass -ne 'DeterministicControlledTestData' -or
    $environment.productionDataClaimed -ne $false -or
    $environment.productionDeploymentPerformed -ne $false) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_ENVIRONMENT_INVALID: controlled SQL Server self-review must not claim production data or deployment.'
}

$sql = $manifest.sqlServer
$sqlTotal = ConvertTo-ManualModelingInteger $sql.total
$sqlPassed = ConvertTo-ManualModelingInteger $sql.passed
$sqlFailed = ConvertTo-ManualModelingInteger $sql.failed
$sqlSkipped = ConvertTo-ManualModelingInteger $sql.skipped
$engineEdition = ConvertTo-ManualModelingInteger $sql.engineEdition
if (!(Test-ManualModelingText $sql.productVersion) -or
    !(Test-ManualModelingText $sql.edition) -or
    $engineEdition -le 0 -or
    $sql.testProject -ne 'CP6.Space.IntegrationTests' -or
    $sqlTotal -lt 20 -or $sqlPassed -ne $sqlTotal -or
    $sqlFailed -ne 0 -or $sqlSkipped -ne 0) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_SQL_FAILED: at least 20 real SQL Server tests must pass with zero failures and zero skips.'
}
$requiredClasses = @(
    'CP6.Space.IntegrationTests.SpaceVersionCloneSqlServerTests',
    'CP6.Space.IntegrationTests.SpaceDesignSceneSqlServerTests')
$testClasses = @($sql.testClasses | ForEach-Object { [string]$_ })
if (@($testClasses | Sort-Object -Unique).Count -ne $testClasses.Count -or
    @($requiredClasses | Where-Object { $_ -notin $testClasses }).Count -gt 0) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_SQL_CLASSES_INVALID: Version Clone and Design Scene SQL classes are required.'
}
$requiredCases = @(
    'Blank_mode_creates_an_idempotent_editable_draft_without_published_base',
    'System_template_mode_initializes_every_floor_and_persists_provenance',
    'Tenant_template_mode_uses_only_the_current_tenant_template_scope',
    'Warehouse_template_floor_apply_is_leased_atomic_and_replayable',
    'Layout_commands_create_coded_warehouse_atomically',
    'Location_coding_previews_without_writes_and_applies_with_fences')
$actualCases = @($sql.requiredCases | ForEach-Object { [string]$_ })
if (@($actualCases | Sort-Object -Unique).Count -ne $actualCases.Count -or
    @($requiredCases | Where-Object { $_ -notin $actualCases }).Count -gt 0) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_SQL_CASES_INVALID: all required blank, template, layout and coding cases are required.'
}

$web = $manifest.web
$webFiles = ConvertTo-ManualModelingInteger $web.testFiles
$webTotal = ConvertTo-ManualModelingInteger $web.total
$webPassed = ConvertTo-ManualModelingInteger $web.passed
$webFailed = ConvertTo-ManualModelingInteger $web.failed
$webSkipped = ConvertTo-ManualModelingInteger $web.skipped
if ($web.runner -ne 'Vitest' -or $webFiles -ne 6 -or
    $webTotal -lt 25 -or $webPassed -ne $webTotal -or
    $webFailed -ne 0 -or $webSkipped -ne 0) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_WEB_FAILED: six focused Web files and at least 25 tests must pass without failures or skips.'
}
$requiredSurfaces = @(
    'BlankAndTemplateStart',
    'LayoutCreate',
    'LayoutProperties',
    'LayoutCommandConstruction',
    'LocationCodePreviewApply',
    'TenantTemplatePreviewCreate')
$surfaces = @($web.coveredSurfaces | ForEach-Object { [string]$_ })
if (@($surfaces | Sort-Object -Unique).Count -ne $surfaces.Count -or
    @($requiredSurfaces | Where-Object { $_ -notin $surfaces }).Count -gt 0) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_WEB_SURFACES_INVALID: all six authoring surfaces are required.'
}

foreach ($property in @(
    'blankDraftAndExplicitFloorPassed',
    'completeCodedWarehouseBuilt',
    'systemAndTenantTemplatePassed',
    'templateFloorApplyPassed',
    'locationCodePreviewZeroWritePassed',
    'locationCodeApplyPassed',
    'leaseFencePassed',
    'floorRevisionFencePassed',
    'contentRevisionFencePassed',
    'idempotencyFencePassed',
    'atomicFailureZeroWritePassed',
    'publishedIsolationPassed')) {
    if ($manifest.result.$property -ne $true) {
        Fail-ManualModeling "SPACE_GA_MANUAL_MODELING_RESULT_FAILED: result.$property must pass."
    }
}
$counts = $manifest.result.codedWarehouseCounts
if ((ConvertTo-ManualModelingInteger $counts.zones) -lt 1 -or
    (ConvertTo-ManualModelingInteger $counts.aisles) -lt 1 -or
    (ConvertTo-ManualModelingInteger $counts.racks) -lt 1 -or
    (ConvertTo-ManualModelingInteger $counts.rackLevels) -lt 1 -or
    (ConvertTo-ManualModelingInteger $counts.locations) -lt 1) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_WAREHOUSE_EMPTY: a complete coded warehouse hierarchy is required.'
}

$requiredSourcePaths = @(
    'CP6.Space.IntegrationTests/SpaceVersionCloneSqlServerTests.cs',
    'CP6.Space.IntegrationTests/SpaceDesignSceneSqlServerTests.cs',
    'cp6.web/src/views/space/editor/SpaceDesignStartView.spec.ts',
    'cp6.web/src/modules/space-design/layout/DesignLayoutCreatePanel.spec.ts',
    'cp6.web/src/modules/space-design/layout/DesignLayoutPropertiesPanel.spec.ts',
    'cp6.web/src/modules/space-design/layout/layoutCreate.spec.ts',
    'cp6.web/src/modules/space-design/coding/DesignLocationCodingPanel.spec.ts',
    'cp6.web/src/modules/space-design/templates/DesignWarehouseTemplatePanel.spec.ts')
$sources = @($manifest.sources)
$sourcePaths = @($sources | ForEach-Object { ([string]$_.path).Replace('\', '/') })
if ($sources.Count -ne $requiredSourcePaths.Count -or
    @($sourcePaths | Sort-Object -Unique).Count -ne $sourcePaths.Count -or
    @($requiredSourcePaths | Where-Object { $_ -notin $sourcePaths }).Count -gt 0) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_SOURCE_SET_INVALID: the frozen two SQL and six Web test sources are required exactly once.'
}
foreach ($source in $sources) {
    $path = ([string]$source.path).Replace('\', '/')
    if ($path -notin $requiredSourcePaths -or
        !(Test-ManualModelingSha256 $source.sha256) -or
        !(Test-ManualModelingGitOid $source.gitBlobOid)) {
        Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_SOURCE_IDENTITY_INVALID: each source requires an approved path, SHA-256 and Git blob OID.'
        continue
    }
    $actualOid = (& git -C $repoFullPath rev-parse "$commit`:$path" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or !(Test-ManualModelingGitOid $actualOid)) {
        Fail-ManualModeling "SPACE_GA_MANUAL_MODELING_SOURCE_MISSING: source is unavailable at the tested commit: $path"
        continue
    }
    if (!$actualOid.Equals(
        [string]$source.gitBlobOid,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-ManualModeling "SPACE_GA_MANUAL_MODELING_SOURCE_BLOB_MISMATCH: source Git identity does not match: $path"
        continue
    }
    try { $actualSha = Get-ManualModelingGitBlobSha256 $actualOid }
    catch {
        Fail-ManualModeling "SPACE_GA_MANUAL_MODELING_SOURCE_HASH_FAILED: source could not be hashed: $path"
        continue
    }
    if (!$actualSha.Equals(
        [string]$source.sha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-ManualModeling "SPACE_GA_MANUAL_MODELING_SOURCE_SHA_MISMATCH: source SHA-256 does not match: $path"
    }
}

$review = $manifest.selfReview
$acceptedAt = ConvertTo-ManualModelingUtc $review.acceptedAtUtc
if (!(Test-ManualModelingPerson $review.acceptedBy) -or
    !([string]$review.acceptedBy).Equals(
        $owner,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_REVIEW_OWNER_INVALID: self-review must be accepted by the DeliveryOwner.'
}
if ($null -eq $acceptedAt -or $acceptedAt -lt $executedAt -or
    $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_REVIEW_TIME_INVALID: acceptance must be a non-future UTC timestamp after execution.'
}
if ($review.repeatable -ne $true -or $review.distinctPersonReviewRequired -ne $false) {
    Fail-ManualModeling 'SPACE_GA_MANUAL_MODELING_REVIEW_INVALID: repeatable single-owner self-review is required without a second-person gate.'
}

if ($errors.Count -gt 0) {
    throw ("Manual-modeling evidence validation failed:`n" +
        ($errors -join "`n"))
}

[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    applicationCommitSha = $commit
    sqlPassed = $sqlPassed
    webPassed = $webPassed
    sourceCount = $sources.Count
} | ConvertTo-Json -Compress
