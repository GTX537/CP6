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

function Fail-Viewer([string]$Message) { $errors.Add($Message) }
function Test-ViewerText($Value) {
    return $null -ne $Value -and ![string]::IsNullOrWhiteSpace([string]$Value)
}
function Test-ViewerSha256($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{64}$'
}
function Test-ViewerGitOid($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{40}$'
}
function Test-ViewerPerson($Value) {
    if (!(Test-ViewerText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    $name = ([string]$Value).Trim()
    if ($name -match '^\d+$') { return $false }
    return $name -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'admin|administrator|test|demo|simulated|待定|未定|负责人|团队)$')
}
function ConvertTo-ViewerUtc($Value) {
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
function ConvertTo-ViewerInteger($Value) {
    if ($null -eq $Value) { return $null }
    [long]$number = 0
    if (![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}
function ConvertTo-ViewerDouble($Value) {
    if ($null -eq $Value) { return $null }
    [double]$number = 0
    if (![double]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Float,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number) -or
        [double]::IsNaN($number) -or [double]::IsInfinity($number)) {
        return $null
    }
    return $number
}
function Get-ViewerGitBlobSha256([string]$BlobOid) {
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
    throw "Viewer evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Viewer evidence manifest must remain inside the repository.'
}
$manifestReference = $manifestFullPath.Substring($repoPrefix.Length).Replace('\', '/')
if (!$AllowTestFixtures -and (
    $manifestReference -match '(^|/)tools/test-fixtures/' -or
    $manifestReference.EndsWith(
        '/viewer-evidence-template.json',
        [System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'SPACE_GA_VIEWER_SYNTHETIC: a template or test fixture cannot close WP5.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson
if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'WP5_VIEWER_FORMAL_EVIDENCE') {
    Fail-Viewer 'SPACE_GA_VIEWER_SCHEMA_INVALID: schema, program, delivery mode or evidence class is invalid.'
}
if ($manifest.conclusion -ne 'Pass') {
    Fail-Viewer 'SPACE_GA_VIEWER_CONCLUSION_INVALID: only Pass can close WP5.'
}
$owner = [string]$manifest.ownerName
if (!(Test-ViewerPerson $owner)) {
    Fail-Viewer 'SPACE_GA_VIEWER_OWNER_INVALID: a real DeliveryOwner is required.'
}
elseif ((Test-ViewerPerson $ExpectedOwnerName) -and
    !$owner.Equals($ExpectedOwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-Viewer 'SPACE_GA_VIEWER_OWNER_MISMATCH: evidence owner must match WP5.'
}
$executedAt = ConvertTo-ViewerUtc $manifest.executedAtUtc
if ($null -eq $executedAt -or $executedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-Viewer 'SPACE_GA_VIEWER_TIME_INVALID: execution must be a non-future UTC time.'
    $executedAt = [DateTimeOffset]::MinValue
}

$commit = [string]$manifest.applicationCommitSha
if (!(Test-ViewerGitOid $commit)) {
    Fail-Viewer 'SPACE_GA_VIEWER_COMMIT_INVALID: a full application commit SHA is required.'
}
else {
    & git -C $repoFullPath cat-file -e "$commit`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail-Viewer 'SPACE_GA_VIEWER_COMMIT_MISSING: the tested commit is unavailable.'
    }
    else {
        & git -C $repoFullPath merge-base --is-ancestor $commit HEAD
        if ($LASTEXITCODE -ne 0) {
            Fail-Viewer 'SPACE_GA_VIEWER_COMMIT_NOT_ANCESTOR: the tested commit must be an ancestor.'
        }
    }
}

$environment = $manifest.environment
if ($environment.mode -ne 'ControlledAcceptance' -or
    !(Test-ViewerText $environment.operatingSystem) -or
    !(Test-ViewerText $environment.browser) -or
    !(Test-ViewerText $environment.nodeVersion) -or
    !(Test-ViewerText $environment.gpuRenderer) -or
    [string]$environment.webglVersion -notmatch 'WebGL\s*2' -or
    $environment.trackedWorktreeCleanAtExecution -ne $true) {
    Fail-Viewer 'SPACE_GA_VIEWER_ENVIRONMENT_INVALID: clean controlled hardware WebGL2 execution is required.'
}

$evidence = @($manifest.rawEvidence)
$evidenceUris = @($evidence | ForEach-Object { [string]$_.uri })
$requiredEvidenceClasses = @(
    'PUBLISHED_BOUNDARY_FORMAL',
    'PERFORMANCE_FORMAL_GA',
    'PERFORMANCE_SCREENSHOT',
    'ACCESSIBILITY_FORMAL',
    'VIEWPORT_SCREENSHOT',
    'VIEWPORT_SCREENSHOT')
$actualEvidenceClasses = @($evidence | ForEach-Object { [string]$_.evidenceClass })
if ($evidence.Count -ne 6 -or
    @($evidenceUris | Sort-Object -Unique).Count -ne 6 -or
    (Compare-Object ($requiredEvidenceClasses | Sort-Object) `
        ($actualEvidenceClasses | Sort-Object)).Count -ne 0) {
    Fail-Viewer 'SPACE_GA_VIEWER_RAW_EVIDENCE_SET_INVALID: all six formal evidence objects are required exactly once.'
}
foreach ($item in $evidence) {
    if ([string]$item.uri -notmatch '^urn:cp6-space-ga-evidence:wp5:' -or
        !(Test-ViewerSha256 $item.sha256) -or
        [string]$item.mediaType -notin @('application/json', 'image/png')) {
        Fail-Viewer 'SPACE_GA_VIEWER_RAW_EVIDENCE_INVALID: every raw evidence object needs a controlled URN, SHA-256 and supported media type.'
    }
}

$published = $manifest.verification.publishedBoundary
$publishedTotal = ConvertTo-ViewerInteger $published.total
if ($published.conclusion -ne 'Pass' -or
    $published.productionEntryPoint -ne 'cp6.web/src/views/space/viewer/FloorViewer.vue' -or
    $published.authority -ne 'DesignRevision' -or
    $published.versionStatus -ne 'Published' -or
    $published.runtimeOverlayIncluded -ne $false -or
    (ConvertTo-ViewerInteger $published.testFiles) -lt 3 -or
    $publishedTotal -lt 12 -or
    (ConvertTo-ViewerInteger $published.passed) -ne $publishedTotal -or
    (ConvertTo-ViewerInteger $published.failed) -ne 0 -or
    (ConvertTo-ViewerInteger $published.skipped) -ne 0) {
    Fail-Viewer 'SPACE_GA_VIEWER_PUBLISHED_BOUNDARY_FAILED: production Viewer must fail closed to the Current Published Design Revision.'
}

$performance = $manifest.verification.performance
$budgets = $performance.budgets
$observed = $performance.observed
$performanceValid =
    $performance.conclusion -eq 'Pass' -and
    $performance.classification -eq 'FORMAL_GA' -and
    $performance.datasetVersion -eq 'E08-S05-STANDARD' -and
    (ConvertTo-ViewerInteger $performance.locationCount) -eq 10000 -and
    (ConvertTo-ViewerInteger $performance.rackCount) -eq 500 -and
    (ConvertTo-ViewerInteger $performance.coldRuns) -ge 30 -and
    (ConvertTo-ViewerInteger $performance.failedRuns) -eq 0 -and
    (ConvertTo-ViewerInteger $performance.pickHits) -eq
        (ConvertTo-ViewerInteger $performance.expectedPickHits) -and
    (ConvertTo-ViewerInteger $performance.pickHits) -ge 3000 -and
    $null -eq $performance.requiredGpuPattern -and
    $performance.hardwareRenderer -eq $true -and
    $performance.webgl2 -eq $true -and
    $performance.consistentRenderer -eq $true -and
    $performance.consoleClean -eq $true -and
    (ConvertTo-ViewerInteger $budgets.maxDrawCalls) -eq 100 -and
    (ConvertTo-ViewerInteger $budgets.maxInteractiveP95Milliseconds) -eq 3000 -and
    (ConvertTo-ViewerInteger $budgets.maxFrameP95Milliseconds) -eq 20 -and
    (ConvertTo-ViewerInteger $budgets.maxLabelUpdateP95Milliseconds) -eq 16 -and
    (ConvertTo-ViewerInteger $budgets.maxPickP95Milliseconds) -eq 150 -and
    (ConvertTo-ViewerInteger $budgets.maxStockApplyP95Milliseconds) -eq 3000 -and
    (ConvertTo-ViewerInteger $budgets.maxVisibleLabels) -eq 200
if (!$performanceValid) {
    Fail-Viewer 'SPACE_GA_VIEWER_PERFORMANCE_CONTRACT_INVALID: the frozen hardware WebGL2 performance contract changed or did not pass.'
}
$metricPairs = @(
    @('drawCallsMax', 'maxDrawCalls'),
    @('interactiveP95Milliseconds', 'maxInteractiveP95Milliseconds'),
    @('frameP95Milliseconds', 'maxFrameP95Milliseconds'),
    @('labelUpdateP95Milliseconds', 'maxLabelUpdateP95Milliseconds'),
    @('pickP95Milliseconds', 'maxPickP95Milliseconds'),
    @('stockApplyP95Milliseconds', 'maxStockApplyP95Milliseconds'),
    @('visibleLabelsMax', 'maxVisibleLabels'))
foreach ($pair in $metricPairs) {
    $actual = ConvertTo-ViewerDouble $observed.($pair[0])
    $budget = ConvertTo-ViewerDouble $budgets.($pair[1])
    if ($null -eq $actual -or $null -eq $budget -or $actual -gt $budget) {
        Fail-Viewer "SPACE_GA_VIEWER_PERFORMANCE_BUDGET_FAILED: $($pair[0]) exceeds the frozen budget."
    }
}

$accessibility = $manifest.verification.accessibility
$viewports = @($accessibility.viewports)
if ($accessibility.conclusion -ne 'Pass' -or
    $accessibility.runner -ne 'Playwright' -or
    $accessibility.project -ne 'space-viewer-ga-mocked' -or
    $accessibility.fixtureClassification -ne 'Simulated' -or
    (ConvertTo-ViewerInteger $accessibility.total) -lt 4 -or
    (ConvertTo-ViewerInteger $accessibility.passed) -ne
        (ConvertTo-ViewerInteger $accessibility.total) -or
    (ConvertTo-ViewerInteger $accessibility.failed) -ne 0 -or
    (ConvertTo-ViewerInteger $accessibility.skipped) -ne 0 -or
    $viewports.Count -ne 2 -or
    '1440x900' -notin $viewports -or '1280x720' -notin $viewports -or
    $accessibility.publishedOnlyRequestBoundaryPassed -ne $true -or
    $accessibility.keyboardPassed -ne $true -or
    $accessibility.chromiumAccessibilityTreePassed -ne $true -or
    (ConvertTo-ViewerDouble $accessibility.minimumContrastRatio) -lt 4.5 -or
    $accessibility.contrastPassed -ne $true -or
    $accessibility.consoleClean -ne $true) {
    Fail-Viewer 'SPACE_GA_VIEWER_ACCESSIBILITY_FAILED: both viewports, keyboard, accessibility tree, contrast and console checks must pass without skips.'
}

$regression = $manifest.verification.repositoryRegression
if ($regression.typeCheckPassed -ne $true -or
    $regression.productionBuildPassed -ne $true -or
    (ConvertTo-ViewerInteger $regression.unitTestFiles) -lt 176 -or
    (ConvertTo-ViewerInteger $regression.unitTestsPassed) -lt 906 -or
    (ConvertTo-ViewerInteger $regression.unitTestsFailed) -ne 0) {
    Fail-Viewer 'SPACE_GA_VIEWER_REGRESSION_FAILED: type-check, production build and repository tests must pass.'
}

$boundaries = $manifest.boundaries
if ($boundaries.exactGpuBrandRequired -ne $false -or
    $boundaries.hardwareAccelerationRequired -ne $true -or
    $boundaries.softwareRendererAllowed -ne $false -or
    $boundaries.uiFixtureUsedForProductionDataClaim -ne $false -or
    $boundaries.productionDataClaimed -ne $false -or
    $boundaries.productionWmsClaimed -ne $false -or
    $boundaries.productionDeploymentPerformed -ne $false -or
    $boundaries.distinctPersonReviewRequired -ne $false) {
    Fail-Viewer 'SPACE_GA_VIEWER_BOUNDARY_INVALID: hardware and non-production claim boundaries changed.'
}

$requiredSourcePaths = @(
    'cp6.web/src/api/space/designPublishedScene.ts',
    'cp6.web/src/space-viewer/publishedBoundary.spec.ts',
    'cp6.web/src/space-viewer/build/SceneBuilder.ts',
    'cp6.web/src/space-viewer/build/SceneBuilder.published.spec.ts',
    'cp6.web/src/views/space/viewer/FloorViewer.vue',
    'cp6.web/src/views/space/viewer/FloorList.vue',
    'cp6.web/src/space-viewer/performance/standardWarehouse.ts',
    'cp6.web/src/space-viewer/performance/budgets.ts',
    'cp6.web/src/space-viewer/performance/browserBenchmark.ts',
    'cp6.web/scripts/space-performance-evidence.mjs',
    'cp6.web/scripts/space-performance-browser.mjs',
    'cp6.web/e2e/space-viewer-ga.spec.ts',
    'cp6.web/playwright.config.ts')
$sources = @($manifest.sources)
$sourcePaths = @($sources | ForEach-Object { ([string]$_.path).Replace('\', '/') })
if ($sources.Count -ne $requiredSourcePaths.Count -or
    @($sourcePaths | Sort-Object -Unique).Count -ne $sourcePaths.Count -or
    @($requiredSourcePaths | Where-Object { $_ -notin $sourcePaths }).Count -gt 0) {
    Fail-Viewer 'SPACE_GA_VIEWER_SOURCE_SET_INVALID: the production, performance and browser sources are required exactly once.'
}
foreach ($source in $sources) {
    $path = ([string]$source.path).Replace('\', '/')
    if ($path -notin $requiredSourcePaths -or
        !(Test-ViewerSha256 $source.sha256) -or
        !(Test-ViewerGitOid $source.gitBlobOid)) {
        Fail-Viewer 'SPACE_GA_VIEWER_SOURCE_IDENTITY_INVALID: every source needs path, SHA-256 and Git blob OID.'
        continue
    }
    $actualOid = (& git -C $repoFullPath rev-parse "$commit`:$path" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or !(Test-ViewerGitOid $actualOid)) {
        Fail-Viewer "SPACE_GA_VIEWER_SOURCE_MISSING: source is unavailable at the tested commit: $path"
        continue
    }
    if (!$actualOid.Equals(
        [string]$source.gitBlobOid,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Viewer "SPACE_GA_VIEWER_SOURCE_BLOB_MISMATCH: source Git identity changed: $path"
        continue
    }
    $headOid = (& git -C $repoFullPath rev-parse "HEAD`:$path" 2>$null |
        Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or
        !$headOid.Equals(
            [string]$source.gitBlobOid,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Viewer "SPACE_GA_VIEWER_SOURCE_HEAD_DRIFT: accepted WP5 source changed after formal execution: $path"
        continue
    }
    try { $actualSha = Get-ViewerGitBlobSha256 $actualOid }
    catch {
        Fail-Viewer "SPACE_GA_VIEWER_SOURCE_HASH_FAILED: source could not be hashed: $path"
        continue
    }
    if (!$actualSha.Equals(
        [string]$source.sha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-Viewer "SPACE_GA_VIEWER_SOURCE_SHA_MISMATCH: source SHA-256 changed: $path"
    }
}

$review = $manifest.selfReview
$acceptedAt = ConvertTo-ViewerUtc $review.acceptedAtUtc
if (!(Test-ViewerPerson $review.acceptedBy) -or
    !([string]$review.acceptedBy).Equals(
        $owner,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-Viewer 'SPACE_GA_VIEWER_REVIEW_OWNER_INVALID: the DeliveryOwner must accept WP5.'
}
if ($null -eq $acceptedAt -or $acceptedAt -lt $executedAt -or
    $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-Viewer 'SPACE_GA_VIEWER_REVIEW_TIME_INVALID: acceptance must follow execution in UTC.'
}
if ($review.repeatable -ne $true -or $review.distinctPersonReviewRequired -ne $false) {
    Fail-Viewer 'SPACE_GA_VIEWER_REVIEW_INVALID: repeatable single-owner review is required.'
}

if ($errors.Count -gt 0) {
    throw ("Viewer evidence validation failed:`n" + ($errors -join "`n"))
}

[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    applicationCommitSha = $commit
    performanceRuns = $performance.coldRuns
    browserTests = $accessibility.total
    sourceCount = $sources.Count
} | ConvertTo-Json -Compress
