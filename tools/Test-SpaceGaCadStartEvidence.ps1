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

function Fail-CadStart([string]$Message) { $errors.Add($Message) }
function Test-CadStartText($Value) {
    return $null -ne $Value -and ![string]::IsNullOrWhiteSpace([string]$Value)
}
function Test-CadStartSha256($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{64}$'
}
function Test-CadStartGitOid($Value) {
    return [string]$Value -match '^[a-fA-F0-9]{40}$'
}
function Test-CadStartGuid($Value) {
    [Guid]$parsed = [Guid]::Empty
    return [Guid]::TryParse([string]$Value, [ref]$parsed) -and
        $parsed -ne [Guid]::Empty
}
function Test-CadStartPerson($Value) {
    if (!(Test-CadStartText $Value) -or ([string]$Value).Length -gt 200) {
        return $false
    }
    $name = ([string]$Value).Trim()
    if ($name -match '^\d+$') { return $false }
    return $name -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'admin|administrator|test|demo|simulated|待定|未定|负责人|团队)$')
}
function ConvertTo-CadStartUtc($Value) {
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
function ConvertTo-CadStartInteger($Value) {
    if ($null -eq $Value) { return $null }
    [long]$number = 0
    if (![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}
function Test-CadStartDecimal($Value) {
    if ($null -eq $Value) { return $false }
    [decimal]$number = 0
    return [decimal]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Number,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)
}
function Get-CadStartGitBlobSha256([string]$BlobOid) {
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
    throw "CAD Start evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'CAD Start evidence manifest must remain inside the repository.'
}
$manifestReference = $manifestFullPath.Substring($repoPrefix.Length).Replace('\', '/')
if (!$AllowTestFixtures -and (
    $manifestReference -match '(^|/)tools/test-fixtures/' -or
    $manifestReference.EndsWith(
        '/cad-start-evidence-template.json',
        [System.StringComparison]::OrdinalIgnoreCase))) {
    throw 'SPACE_GA_CAD_START_SYNTHETIC: a template or test fixture cannot close WP2.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw |
    ConvertFrom-SpaceGaJson
if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'WP2_CAD_START_FORMAL_EVIDENCE') {
    Fail-CadStart 'SPACE_GA_CAD_START_SCHEMA_INVALID: schema, program, delivery mode or evidence class is invalid.'
}
if ($manifest.conclusion -ne 'Pass') {
    Fail-CadStart 'SPACE_GA_CAD_START_CONCLUSION_INVALID: only Pass can close WP2.'
}
$owner = [string]$manifest.ownerName
if (!(Test-CadStartPerson $owner)) {
    Fail-CadStart 'SPACE_GA_CAD_START_OWNER_INVALID: a real DeliveryOwner is required.'
}
elseif ((Test-CadStartPerson $ExpectedOwnerName) -and
    !$owner.Equals($ExpectedOwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-CadStart 'SPACE_GA_CAD_START_OWNER_MISMATCH: evidence owner must match WP2.'
}
$executedAt = ConvertTo-CadStartUtc $manifest.executedAtUtc
if ($null -eq $executedAt -or $executedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-CadStart 'SPACE_GA_CAD_START_TIME_INVALID: execution must be a non-future UTC time.'
    $executedAt = [DateTimeOffset]::MinValue
}

$commit = [string]$manifest.applicationCommitSha
if (!(Test-CadStartGitOid $commit)) {
    Fail-CadStart 'SPACE_GA_CAD_START_COMMIT_INVALID: a full application commit SHA is required.'
}
else {
    & git -C $repoFullPath cat-file -e "$commit`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail-CadStart 'SPACE_GA_CAD_START_COMMIT_MISSING: the tested commit is unavailable.'
    }
    else {
        & git -C $repoFullPath merge-base --is-ancestor $commit HEAD
        if ($LASTEXITCODE -ne 0) {
            Fail-CadStart 'SPACE_GA_CAD_START_COMMIT_NOT_ANCESTOR: the tested commit must be an ancestor.'
        }
    }
}

$expectedSourceSet = '7bc708d5a85b1da2e7f35d43c0e94e38deacda72316d9dbbf09db5e97a742955'
$expectedDataset = '2b9438e09e2953b169770d0ee9292d8f9cc9ed697337111bcb61b913484b1f15'
if ($manifest.sourceSetSha256 -ne $expectedSourceSet -or
    $manifest.goldenDatasetSha256 -ne $expectedDataset) {
    Fail-CadStart 'SPACE_GA_CAD_START_DATASET_INVALID: WP2 must use the accepted frozen source set.'
}
$environment = $manifest.environment
if ($environment.mode -ne 'ControlledAcceptance' -or
    $environment.databaseEngine -ne 'SQLServer' -or
    !(Test-CadStartText $environment.productVersion) -or
    !(Test-CadStartText $environment.edition) -or
    $environment.productionDataClaimed -ne $false -or
    $environment.productionDeploymentPerformed -ne $false) {
    Fail-CadStart 'SPACE_GA_CAD_START_ENVIRONMENT_INVALID: real SQL Server controlled acceptance is required without production claims.'
}

$external = $manifest.externalExecution
if ([string]$external.uri -notmatch '^urn:cp6-space-ga-evidence:wp2-cad-start:' -or
    !(Test-CadStartSha256 $external.sha256) -or
    $external.evidenceClass -ne 'WP2_CAD_START_CONTROLLED_EXECUTION' -or
    $external.conclusion -ne 'Pass') {
    Fail-CadStart 'SPACE_GA_CAD_START_EXECUTION_INVALID: the controlled external execution must be content-hash attested.'
}
$provider = $manifest.provider
if ($provider.providerKey -ne 'cp6-autocad-worker' -or
    $provider.providerVersion -ne '1.0.0+worker.c794e9c0ebbb.autocad.25.0.58.0.0.dxf.1.1.0' -or
    $provider.workerReleaseSha256 -ne 'c794e9c0ebbb2c736866827e07e6682347992dd5a672218efddfe6ff5c0f202e' -or
    $provider.sourceCommit -ne 'd2d0a0d1b0978a4283bd9387f4120eefe10a135d' -or
    $provider.autoCadCoreConsoleVersion -ne '25.0.58.0.0' -or
    $provider.autoCadCoreConsoleSha256 -ne 'd1fd7232893094234f31c65445d0ec9259ffc1df17fb15aad99373e31545cefb' -or
    $provider.managedDxfConverterVersion -ne '1.1.0') {
    Fail-CadStart 'SPACE_GA_CAD_START_PROVIDER_INVALID: the exact accepted Primary Worker release is required.'
}

$expectedSamples = [ordered]@{
    'L1-C01' = [pscustomobject]@{
        sampleRef = 'urn:cp6-space-golden-cad:v1.0.0:l1-c01'
        sourceFormat = 'DWG'
        sourceSha256 = 'c9338360df1df80a46bf83c7ca3bd0e4dd05bc4b4cefb34e9130272e8b4a2ca4'
        sourceSizeBytes = 43128
        authorizationSha256 = '3403ae0c1b1109b990db2aacaeb35dd5e099d54c9db992e15a2cca7b1988f625'
        deidentificationSha256 = 'cdf37825008f3f1cb0781a890f102ab00132a5ebc768fbc3e52e70cc95c9adcf'
        providerPackageSha256 = 'e7d5a673ad933956b18f929e046617111ed6bdad58c4b1a1b436f9f3708cc48a'
    }
    'L1-C02' = [pscustomobject]@{
        sampleRef = 'urn:cp6-space-golden-cad:v1.0.0:l1-c02'
        sourceFormat = 'DXF'
        sourceSha256 = 'a426a21806cddffdc6b400b61b23a5330e6ec52bb2be0de8af8ca326b3e6b3ba'
        sourceSizeBytes = 258714
        authorizationSha256 = '16102fad4d33466c3d2b2c432b56f36347bc91a150fc864ef7b0b2ee83513b89'
        deidentificationSha256 = '14a7266bde4b6b6874363035e711184bf63421ebfa7a435edda9ef48d7923ecf'
        providerPackageSha256 = '4ffc211c3200bfcdcd8d79df0c38c43ee924b1fd53ad1ea07674b1747bb56d57'
    }
}
$samples = @($manifest.samples)
$sampleIds = @($samples | ForEach-Object { [string]$_.sampleId })
if ($samples.Count -ne 2 -or
    @($sampleIds | Sort-Object -Unique).Count -ne 2 -or
    @($expectedSamples.Keys | Where-Object { $_ -notin $sampleIds }).Count -gt 0) {
    Fail-CadStart 'SPACE_GA_CAD_START_SAMPLE_SET_INVALID: exactly the accepted DWG and DXF samples are required.'
}
$floorIds = [System.Collections.Generic.List[string]]::new()
$preparationIds = [System.Collections.Generic.List[string]]::new()
$jobIds = [System.Collections.Generic.List[string]]::new()
foreach ($sample in $samples) {
    $id = [string]$sample.sampleId
    $expected = $expectedSamples[$id]
    if ($null -eq $expected -or
        $sample.sampleRef -ne $expected.sampleRef -or
        $sample.sourceFormat -ne $expected.sourceFormat -or
        $sample.license -ne 'ApprovedOriginalWork' -or
        $sample.sourceSha256 -ne $expected.sourceSha256 -or
        (ConvertTo-CadStartInteger $sample.sourceSizeBytes) -ne $expected.sourceSizeBytes -or
        $sample.authorizationSha256 -ne $expected.authorizationSha256 -or
        $sample.deidentificationSha256 -ne $expected.deidentificationSha256 -or
        $sample.providerPackageSha256 -ne $expected.providerPackageSha256) {
        Fail-CadStart "SPACE_GA_CAD_START_SAMPLE_IDENTITY_INVALID: frozen sample identity changed: $id"
        continue
    }
    $selection = $sample.selection
    if (!(Test-CadStartGuid $selection.floorLogicalId) -or
        !(Test-CadStartText $selection.floorCode) -or
        $selection.confirmedUnit -ne 'Millimeter' -or
        !(Test-CadStartGuid $selection.mappingProfileId) -or
        (ConvertTo-CadStartInteger $selection.mappingProfileVersion) -lt 1 -or
        !(Test-CadStartSha256 $selection.mappingDefinitionSha256)) {
        Fail-CadStart "SPACE_GA_CAD_START_SELECTION_INVALID: explicit Floor, Unit and Mapping Profile are required: $id"
    }
    else { $floorIds.Add([string]$selection.floorLogicalId) }
    foreach ($field in @(
        'sourceOriginX', 'sourceOriginY',
        'floorOriginMillimetersX', 'floorOriginMillimetersY',
        'rotationZDegrees')) {
        if (!(Test-CadStartDecimal $selection.transform.$field)) {
            Fail-CadStart "SPACE_GA_CAD_START_TRANSFORM_INVALID: transform.$field is required: $id"
        }
    }
    $audit = $sample.audit
    if (!(Test-CadStartGuid $audit.preparationId) -or
        !(Test-CadStartGuid $audit.jobId) -or
        (ConvertTo-CadStartInteger $audit.baseContentRevision) -lt 0 -or
        !(Test-CadStartSha256 $audit.coordinateTransformSha256) -or
        !(Test-CadStartSha256 $audit.mappingPreviewSha256) -or
        !(Test-CadStartSha256 $audit.semanticPreviewSha256) -or
        $audit.idempotentReplay -ne $true -or
        $audit.draftUnchangedDuringPreview -ne $true -or
        $audit.readyForParsing -ne $true -or
        $null -eq (ConvertTo-CadStartUtc $audit.expiresAtUtc)) {
        Fail-CadStart "SPACE_GA_CAD_START_AUDIT_INVALID: sealed Preparation and Parse Start audit must pass: $id"
    }
    else {
        $preparationIds.Add([string]$audit.preparationId)
        $jobIds.Add([string]$audit.jobId)
    }
    $baseRevision = ConvertTo-CadStartInteger $audit.baseContentRevision
    if ($baseRevision -gt 0 -and !(Test-CadStartSha256 $audit.baseContentHash)) {
        Fail-CadStart "SPACE_GA_CAD_START_BASE_HASH_INVALID: a revised Draft requires a base hash: $id"
    }
    if ($baseRevision -eq 0 -and $null -ne $audit.baseContentHash -and
        !(Test-CadStartSha256 $audit.baseContentHash)) {
        Fail-CadStart "SPACE_GA_CAD_START_BASE_HASH_INVALID: base hash must be null or SHA-256: $id"
    }
}
if (@($floorIds | Sort-Object -Unique).Count -ne 1 -or
    @($preparationIds | Sort-Object -Unique).Count -ne 2 -or
    @($jobIds | Sort-Object -Unique).Count -ne 2) {
    Fail-CadStart 'SPACE_GA_CAD_START_AUDIT_IDENTITY_INVALID: one selected Floor and unique Preparation/Job identities are required.'
}

$runner = $manifest.verification.controlledRunner
$sql = $manifest.verification.sqlServerRegression
$web = $manifest.verification.web
if ($runner.conclusion -ne 'Pass' -or
    (ConvertTo-CadStartInteger $runner.sampleCount) -ne 2 -or
    (ConvertTo-CadStartInteger $runner.dwgPassed) -ne 1 -or
    (ConvertTo-CadStartInteger $runner.dxfPassed) -ne 1 -or
    (ConvertTo-CadStartInteger $runner.residualAttemptDirectoryCount) -ne 0 -or
    (ConvertTo-CadStartInteger $runner.residualRawCadFileCount) -ne 0) {
    Fail-CadStart 'SPACE_GA_CAD_START_RUNNER_FAILED: both formats must pass with zero raw CAD residuals.'
}
$sqlTotal = ConvertTo-CadStartInteger $sql.total
if ($sql.testProject -ne 'CP6.Space.IntegrationTests' -or
    $sqlTotal -lt 21 -or
    (ConvertTo-CadStartInteger $sql.passed) -ne $sqlTotal -or
    (ConvertTo-CadStartInteger $sql.failed) -ne 0 -or
    (ConvertTo-CadStartInteger $sql.skipped) -ne 0) {
    Fail-CadStart 'SPACE_GA_CAD_START_SQL_FAILED: at least 21 SQL/product tests must pass with zero failures or skips.'
}
$webTotal = ConvertTo-CadStartInteger $web.total
if ($web.runner -ne 'Vitest' -or
    (ConvertTo-CadStartInteger $web.testFiles) -ne 2 -or
    $webTotal -lt 14 -or
    (ConvertTo-CadStartInteger $web.passed) -ne $webTotal -or
    (ConvertTo-CadStartInteger $web.failed) -ne 0 -or
    (ConvertTo-CadStartInteger $web.skipped) -ne 0 -or
    $web.typeCheckPassed -ne $true) {
    Fail-CadStart 'SPACE_GA_CAD_START_WEB_FAILED: Wizard/API tests and strict type-check must pass.'
}
$tamper = $manifest.tamperTest
if ($tamper.rejected -ne $true -or
    $tamper.errorCode -ne 'SPACE_CAD_PREPARATION_INVALID' -or
    (ConvertTo-CadStartInteger $tamper.jobsBefore) -ne 2 -or
    (ConvertTo-CadStartInteger $tamper.jobsAfter) -ne 2) {
    Fail-CadStart 'SPACE_GA_CAD_START_TAMPER_FAILED: tampered requests must fail with zero job writes.'
}
$boundaries = $manifest.boundaries
foreach ($property in @(
    'rawCadStoredInRepository', 'productionDataClaimed',
    'productionDeploymentPerformed', 'productionWmsClaimed')) {
    if ($boundaries.$property -ne $false) {
        Fail-CadStart "SPACE_GA_CAD_START_BOUNDARY_INVALID: boundaries.$property must remain false."
    }
}

$requiredSourcePaths = @(
    'tools/CP6.Space.CadStartAcceptance/Program.cs',
    'CP6.Space.Infrastructure/SpaceCadPreparationService.cs',
    'CP6.Space.Infrastructure/SpaceCadParseService.cs',
    'CP6.Space.Infrastructure/SpaceCadRemoteWorkerProvider.cs',
    'CP6.Space.Domain/SpaceCadParsePreparation.cs',
    'cp6.web/src/modules/space-design/cad-start/DesignCadStartWizard.spec.ts',
    'cp6.web/src/api/space/designCadParse.spec.ts')
$sources = @($manifest.sources)
$sourcePaths = @($sources | ForEach-Object { ([string]$_.path).Replace('\', '/') })
if ($sources.Count -ne $requiredSourcePaths.Count -or
    @($sourcePaths | Sort-Object -Unique).Count -ne $sourcePaths.Count -or
    @($requiredSourcePaths | Where-Object { $_ -notin $sourcePaths }).Count -gt 0) {
    Fail-CadStart 'SPACE_GA_CAD_START_SOURCE_SET_INVALID: the frozen runner, product and Web sources are required exactly once.'
}
foreach ($source in $sources) {
    $path = ([string]$source.path).Replace('\', '/')
    if ($path -notin $requiredSourcePaths -or
        !(Test-CadStartSha256 $source.sha256) -or
        !(Test-CadStartGitOid $source.gitBlobOid)) {
        Fail-CadStart 'SPACE_GA_CAD_START_SOURCE_IDENTITY_INVALID: every source needs path, SHA-256 and Git blob OID.'
        continue
    }
    $actualOid = (& git -C $repoFullPath rev-parse "$commit`:$path" 2>$null | Out-String).Trim()
    if ($LASTEXITCODE -ne 0 -or !(Test-CadStartGitOid $actualOid)) {
        Fail-CadStart "SPACE_GA_CAD_START_SOURCE_MISSING: source is unavailable at the tested commit: $path"
        continue
    }
    if (!$actualOid.Equals(
        [string]$source.gitBlobOid,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-CadStart "SPACE_GA_CAD_START_SOURCE_BLOB_MISMATCH: source Git identity changed: $path"
        continue
    }
    try { $actualSha = Get-CadStartGitBlobSha256 $actualOid }
    catch {
        Fail-CadStart "SPACE_GA_CAD_START_SOURCE_HASH_FAILED: source could not be hashed: $path"
        continue
    }
    if (!$actualSha.Equals(
        [string]$source.sha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        Fail-CadStart "SPACE_GA_CAD_START_SOURCE_SHA_MISMATCH: source SHA-256 changed: $path"
    }
}

$review = $manifest.selfReview
$acceptedAt = ConvertTo-CadStartUtc $review.acceptedAtUtc
if (!(Test-CadStartPerson $review.acceptedBy) -or
    !([string]$review.acceptedBy).Equals(
        $owner,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-CadStart 'SPACE_GA_CAD_START_REVIEW_OWNER_INVALID: the DeliveryOwner must accept WP2.'
}
if ($null -eq $acceptedAt -or $acceptedAt -lt $executedAt -or
    $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-CadStart 'SPACE_GA_CAD_START_REVIEW_TIME_INVALID: acceptance must follow execution in UTC.'
}
if ($review.repeatable -ne $true -or $review.distinctPersonReviewRequired -ne $false) {
    Fail-CadStart 'SPACE_GA_CAD_START_REVIEW_INVALID: repeatable single-owner review is required.'
}

if ($errors.Count -gt 0) {
    throw ("CAD Start evidence validation failed:`n" + ($errors -join "`n"))
}

[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    applicationCommitSha = $commit
    sampleCount = $samples.Count
    sqlPassed = $sql.passed
    webPassed = $web.passed
    sourceCount = $sources.Count
} | ConvertTo-Json -Compress
