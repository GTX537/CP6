param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaManualModelingEvidence.ps1'
$repo = Split-Path -Parent $PSScriptRoot
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-manual-modeling\.tmp-' +
    [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function Get-TestGitBlobSha256([string]$BlobOid) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'
    $start.Arguments = "cat-file blob $BlobOid"
    $start.WorkingDirectory = $repo
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

function New-ValidManualModelingManifest {
    $head = (& git -C $repo rev-parse HEAD).Trim()
    $sourcePaths = @(
        'CP6.Space.IntegrationTests/SpaceVersionCloneSqlServerTests.cs',
        'CP6.Space.IntegrationTests/SpaceDesignSceneSqlServerTests.cs',
        'cp6.web/src/views/space/editor/SpaceDesignStartView.spec.ts',
        'cp6.web/src/modules/space-design/layout/DesignLayoutCreatePanel.spec.ts',
        'cp6.web/src/modules/space-design/layout/DesignLayoutPropertiesPanel.spec.ts',
        'cp6.web/src/modules/space-design/layout/layoutCreate.spec.ts',
        'cp6.web/src/modules/space-design/coding/DesignLocationCodingPanel.spec.ts',
        'cp6.web/src/modules/space-design/templates/DesignWarehouseTemplatePanel.spec.ts')
    $sources = @($sourcePaths | ForEach-Object {
        $oid = (& git -C $repo rev-parse "$head`:$($_)").Trim()
        [pscustomobject]@{
            path = $_
            sha256 = Get-TestGitBlobSha256 $oid
            gitBlobOid = $oid
        }
    })
    $executedAt = [DateTimeOffset]::UtcNow.AddMinutes(-10)
    return [pscustomobject]@{
        schemaVersion = 1
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'WP1_MANUAL_MODELING_FORMAL_EVIDENCE'
        conclusion = 'Pass'
        ownerName = 'Zhang Wei'
        executedAtUtc = $executedAt.ToString('yyyy-MM-ddTHH:mm:ssZ')
        applicationCommitSha = $head
        environment = [pscustomobject]@{
            mode = 'ControlledSelfReview'
            databaseEngine = 'SQLServer'
            testDataClass = 'DeterministicControlledTestData'
            productionDataClaimed = $false
            productionDeploymentPerformed = $false
        }
        sqlServer = [pscustomobject]@{
            productVersion = '17.0.4025.3'
            edition = 'Express Edition (64-bit)'
            engineEdition = 4
            testProject = 'CP6.Space.IntegrationTests'
            total = 20
            passed = 20
            failed = 0
            skipped = 0
            testClasses = @(
                'CP6.Space.IntegrationTests.SpaceVersionCloneSqlServerTests',
                'CP6.Space.IntegrationTests.SpaceDesignSceneSqlServerTests')
            requiredCases = @(
                'Blank_mode_creates_an_idempotent_editable_draft_without_published_base',
                'System_template_mode_initializes_every_floor_and_persists_provenance',
                'Tenant_template_mode_uses_only_the_current_tenant_template_scope',
                'Warehouse_template_floor_apply_is_leased_atomic_and_replayable',
                'Layout_commands_create_coded_warehouse_atomically',
                'Location_coding_previews_without_writes_and_applies_with_fences')
        }
        web = [pscustomobject]@{
            runner = 'Vitest'
            testFiles = 6
            total = 25
            passed = 25
            failed = 0
            skipped = 0
            coveredSurfaces = @(
                'BlankAndTemplateStart',
                'LayoutCreate',
                'LayoutProperties',
                'LayoutCommandConstruction',
                'LocationCodePreviewApply',
                'TenantTemplatePreviewCreate')
        }
        result = [pscustomobject]@{
            blankDraftAndExplicitFloorPassed = $true
            completeCodedWarehouseBuilt = $true
            systemAndTenantTemplatePassed = $true
            templateFloorApplyPassed = $true
            locationCodePreviewZeroWritePassed = $true
            locationCodeApplyPassed = $true
            leaseFencePassed = $true
            floorRevisionFencePassed = $true
            contentRevisionFencePassed = $true
            idempotencyFencePassed = $true
            atomicFailureZeroWritePassed = $true
            publishedIsolationPassed = $true
            codedWarehouseCounts = [pscustomobject]@{
                zones = 1
                aisles = 1
                racks = 1
                rackLevels = 2
                locations = 8
            }
        }
        sources = $sources
        selfReview = [pscustomobject]@{
            acceptedBy = 'Zhang Wei'
            acceptedAtUtc = $executedAt.AddMinutes(5).ToString(
                'yyyy-MM-ddTHH:mm:ssZ')
            repeatable = $true
            distinctPersonReviewRequired = $false
        }
    }
}

if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
    $exportPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $exportPath) -Force)
    New-ValidManualModelingManifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $exportPath -Encoding UTF8
    [System.IO.Directory]::Delete($tempDirectory, $true)
    exit 0
}

function New-ManualModelingTestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation)
    $manifest = New-ValidManualModelingManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}
function Invoke-ManualModelingCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [string]$ExpectedOwnerName
    )
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy',
        'Bypass',
        '-File',
        $validator,
        '-ManifestPath',
        $ManifestPath,
        '-AllowTestFixtures')
    if ($ExpectedOwnerName) { $args += @('-ExpectedOwnerName', $ExpectedOwnerName) }
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable @args 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $old }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    $script:passed++
    $global:LASTEXITCODE = 0
}

try {
    $valid = New-ManualModelingTestManifest 'valid' { param($manifest) }
    Invoke-ManualModelingCase -Name 'valid manual-modeling evidence' `
        -ManifestPath $valid -ShouldPass $true -ExpectedOwnerName 'Zhang Wei'

    $owner = New-ManualModelingTestManifest 'owner' {
        param($manifest); $manifest.ownerName = '00001'
    }
    Invoke-ManualModelingCase -Name 'owner is real' -ManifestPath $owner `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_OWNER_INVALID'

    Invoke-ManualModelingCase -Name 'owner matches index' -ManifestPath $valid `
        -ShouldPass $false -ExpectedOwnerName 'Li Ming' `
        -ExpectedError 'SPACE_GA_MANUAL_MODELING_OWNER_MISMATCH'

    $commit = New-ManualModelingTestManifest 'commit' {
        param($manifest); $manifest.applicationCommitSha = '0' * 40
    }
    Invoke-ManualModelingCase -Name 'tested commit exists' -ManifestPath $commit `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_COMMIT_MISSING'

    $production = New-ManualModelingTestManifest 'production' {
        param($manifest); $manifest.environment.productionDeploymentPerformed = $true
    }
    Invoke-ManualModelingCase -Name 'production is not claimed' `
        -ManifestPath $production -ShouldPass $false `
        -ExpectedError 'SPACE_GA_MANUAL_MODELING_ENVIRONMENT_INVALID'

    $sqlSkip = New-ManualModelingTestManifest 'sql-skip' {
        param($manifest); $manifest.sqlServer.skipped = 1
    }
    Invoke-ManualModelingCase -Name 'SQL skips are rejected' -ManifestPath $sqlSkip `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_SQL_FAILED'

    $sqlCases = New-ManualModelingTestManifest 'sql-cases' {
        param($manifest); $manifest.sqlServer.requiredCases = @(
            $manifest.sqlServer.requiredCases[0..4])
    }
    Invoke-ManualModelingCase -Name 'SQL coverage cannot shrink' `
        -ManifestPath $sqlCases -ShouldPass $false `
        -ExpectedError 'SPACE_GA_MANUAL_MODELING_SQL_CASES_INVALID'

    $web = New-ManualModelingTestManifest 'web' {
        param($manifest); $manifest.web.failed = 1
    }
    Invoke-ManualModelingCase -Name 'Web failures are rejected' -ManifestPath $web `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_WEB_FAILED'

    $result = New-ManualModelingTestManifest 'result' {
        param($manifest); $manifest.result.idempotencyFencePassed = $false
    }
    Invoke-ManualModelingCase -Name 'write fences must pass' -ManifestPath $result `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_RESULT_FAILED'

    $empty = New-ManualModelingTestManifest 'empty' {
        param($manifest); $manifest.result.codedWarehouseCounts.locations = 0
    }
    Invoke-ManualModelingCase -Name 'warehouse must be complete' -ManifestPath $empty `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_WAREHOUSE_EMPTY'

    $source = New-ManualModelingTestManifest 'source' {
        param($manifest); $manifest.sources[0].sha256 = '0' * 64
    }
    Invoke-ManualModelingCase -Name 'source content is attested' -ManifestPath $source `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_SOURCE_SHA_MISMATCH'

    $review = New-ManualModelingTestManifest 'review' {
        param($manifest); $manifest.selfReview.acceptedAtUtc = '2020-01-01T00:00:00Z'
    }
    Invoke-ManualModelingCase -Name 'review follows execution' -ManifestPath $review `
        -ShouldPass $false -ExpectedError 'SPACE_GA_MANUAL_MODELING_REVIEW_TIME_INVALID'

    [ordered]@{
        suite = 'CP6_SPACE_GA_MANUAL_MODELING_EVIDENCE'
        passed = $passed
        failed = 0
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
    $parent = Split-Path -Parent $tempDirectory
    if ((Test-Path -LiteralPath $parent -PathType Container) -and
        @(Get-ChildItem -LiteralPath $parent -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($parent)
    }
}
