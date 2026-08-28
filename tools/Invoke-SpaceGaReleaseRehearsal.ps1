param(
    [Parameter(Mandatory)][string]$EvidenceDirectory,
    [Parameter(Mandatory)][string]$OwnerName,
    [string]$SqlServerInstance = '(localdb)\MSSQLLocalDB'
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$repoFullPath = [IO.Path]::GetFullPath($repo).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [IO.Path]::DirectorySeparatorChar
$evidenceFullPath = [IO.Path]::GetFullPath($EvidenceDirectory)
$utf8 = [Text.UTF8Encoding]::new($false)
$startedAt = [DateTimeOffset]::UtcNow

function Write-RehearsalJson {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)]$Value
    )
    $json = $Value | ConvertTo-Json -Depth 100
    [IO.File]::WriteAllText($Path, $json + [Environment]::NewLine, $utf8)
}

function Get-RehearsalSha256([string]$Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Invoke-RehearsalValidator {
    param(
        [Parameter(Mandatory)][string]$Script,
        [Parameter(Mandatory)][string]$Manifest,
        [switch]$PassOwner
    )
    $arguments = @{
        ManifestPath = Join-Path $repo $Manifest
    }
    if ($PassOwner) { $arguments.ExpectedOwnerName = $OwnerName }
    $output = & (Join-Path $PSScriptRoot $Script) @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "$Script failed with exit code $LASTEXITCODE.`n$($output -join [Environment]::NewLine)"
    }
    return [ordered]@{
        script = $Script
        manifest = $Manifest.Replace('\', '/')
        output = ($output -join [Environment]::NewLine).Trim()
        manifestSha256 = Get-RehearsalSha256 (Join-Path $repo $Manifest)
    }
}

function Invoke-RehearsalTest {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Filter,
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][int]$ExpectedTotal
    )
    $trxPath = Join-Path $evidenceFullPath "$Name.trx"
    $logPath = Join-Path $evidenceFullPath "$Name.log"
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $output = & dotnet test (Join-Path $repo $Project) `
        --configuration Release `
        --nologo `
        --filter $Filter `
        --results-directory $evidenceFullPath `
        --logger "trx;LogFileName=$Name.trx" 2>&1
    $exitCode = $LASTEXITCODE
    $watch.Stop()
    [IO.File]::WriteAllLines(
        $logPath,
        @($output | ForEach-Object { [string]$_ }),
        $utf8)
    if ($exitCode -ne 0) {
        throw "$Name failed with exit code $exitCode. See $logPath"
    }
    if (!(Test-Path -LiteralPath $trxPath -PathType Leaf)) {
        throw "$Name did not produce its TRX evidence."
    }

    [xml]$trx = Get-Content -LiteralPath $trxPath -Raw
    $counters = $trx.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $passed = [int]$counters.passed
    $failed = [int]$counters.failed
    $notExecuted = [int]$counters.notExecuted
    if ($total -ne $ExpectedTotal -or $executed -ne $ExpectedTotal -or
        $passed -ne $ExpectedTotal -or $failed -ne 0 -or
        $notExecuted -ne 0) {
        throw (
            "$Name expected $ExpectedTotal/$ExpectedTotal with no skip; " +
            "actual total=$total executed=$executed passed=$passed " +
            "failed=$failed notExecuted=$notExecuted.")
    }

    return [ordered]@{
        name = $Name
        project = $Project.Replace('\', '/')
        filter = $Filter
        total = $total
        executed = $executed
        passed = $passed
        failed = $failed
        notExecuted = $notExecuted
        durationSeconds = [Math]::Round($watch.Elapsed.TotalSeconds, 3)
        trx = [ordered]@{
            fileName = [IO.Path]::GetFileName($trxPath)
            sha256 = Get-RehearsalSha256 $trxPath
        }
        log = [ordered]@{
            fileName = [IO.Path]::GetFileName($logPath)
            sha256 = Get-RehearsalSha256 $logPath
        }
        text = $trx.DocumentElement.InnerText
    }
}

function Get-RehearsalMetric {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Name
    )
    $match = [regex]::Match(
        $Text,
        [regex]::Escape($Name) + '=([0-9]+(?:\.[0-9]+)?)')
    if (!$match.Success) { throw "Required test metric is missing: $Name" }
    return [double]::Parse(
        $match.Groups[1].Value,
        [Globalization.CultureInfo]::InvariantCulture)
}

if ($evidenceFullPath.StartsWith(
    $repoPrefix,
    [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Formal release rehearsal evidence must remain outside the repository.'
}
if (Test-Path -LiteralPath $evidenceFullPath) {
    if (!(Test-Path -LiteralPath $evidenceFullPath -PathType Container) -or
        @(Get-ChildItem -LiteralPath $evidenceFullPath -Force).Count -ne 0) {
        throw 'EvidenceDirectory must be a new or empty directory.'
    }
}
else {
    [void](New-Item -ItemType Directory -Path $evidenceFullPath)
}
if ([string]::IsNullOrWhiteSpace($OwnerName) -or $OwnerName -match '^\d+$') {
    throw 'A real DeliveryOwner name is required.'
}
if ((& git -C $repoFullPath status --porcelain | Out-String).Trim().Length -ne 0) {
    throw 'Formal release rehearsal requires a clean tracked worktree.'
}
$commit = (& git -C $repoFullPath rev-parse HEAD | Out-String).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[a-fA-F0-9]{40}$') {
    throw 'The application commit could not be resolved.'
}
if ($null -eq (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd is required for the controlled SQL Server identity check.'
}

$sqlIdentity = & sqlcmd -S $SqlServerInstance -E -b -V 16 -W -h -1 `
    -s '|' -Q (
        "SET NOCOUNT ON; SELECT " +
        "CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(128))," +
        "CAST(SERVERPROPERTY('ProductLevel') AS nvarchar(128))," +
        "CAST(SERVERPROPERTY('Edition') AS nvarchar(128));") 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "SQL Server identity check failed.`n$($sqlIdentity -join [Environment]::NewLine)"
}
$sqlIdentityLine = @($sqlIdentity | ForEach-Object { ([string]$_).Trim() } |
    Where-Object { $_ -and $_ -notmatch '^Changed database context' })[-1]
$sqlParts = @($sqlIdentityLine -split '\|')
if ($sqlParts.Count -ne 3) { throw 'SQL Server identity output was invalid.' }
$sqlConnection = "Server=$SqlServerInstance;Integrated Security=true;" +
    'TrustServerCertificate=true;Connect Timeout=30'
$previousSqlConnection = [Environment]::GetEnvironmentVariable(
    'CP6_TEST_SQLSERVER', 'Process')
[Environment]::SetEnvironmentVariable(
    'CP6_TEST_SQLSERVER', $sqlConnection, 'Process')

try {
    $baseline = [Collections.Generic.List[object]]::new()
    $baseline.Add((Invoke-RehearsalValidator `
        -Script 'Test-SpaceGaCadStartEvidence.ps1' `
        -Manifest 'docs/space/acceptance/v1.3-ga/cad-start-formal-evidence-v1.0.0.json' `
        -PassOwner))
    $baseline.Add((Invoke-RehearsalValidator `
        -Script 'Test-SpaceGaThreePathEvidence.ps1' `
        -Manifest 'docs/space/acceptance/v1.3-ga/three-path-formal-evidence-v1.0.0.json' `
        -PassOwner))
    $baseline.Add((Invoke-RehearsalValidator `
        -Script 'Test-SpaceGaGoldenCadEvidence.ps1' `
        -Manifest 'docs/space/acceptance/v1.3-ga/golden-cad-formal-evidence-v1.0.0.json'))
    $baseline.Add((Invoke-RehearsalValidator `
        -Script 'Test-SpaceGaViewerEvidence.ps1' `
        -Manifest 'docs/space/acceptance/v1.3-ga/viewer-formal-evidence-v1.0.0.json' `
        -PassOwner))

    $integrationFilter = @(
        'FullyQualifiedName~SpacePublishOrchestratorSqlServerTests',
        'FullyQualifiedName~Cp6SpaceWmsAdapterSqlServerTests',
        ('FullyQualifiedName~SpaceDesignSceneSqlServerTests.' +
            'Published_viewer_scene_uses_only_current_published_pointer'),
        'FullyQualifiedName~SpaceReleaseRehearsalRecoverySqlServerTests'
    ) -join '|'
    $integration = Invoke-RehearsalTest `
        -Project 'CP6.Space.IntegrationTests/CP6.Space.IntegrationTests.csproj' `
        -Filter $integrationFilter `
        -Name 'space-release-rehearsal-integration' `
        -ExpectedTotal 8
    $security = Invoke-RehearsalTest `
        -Project 'CP6.Tests/CP6.Tests.csproj' `
        -Filter 'FullyQualifiedName~SpaceReleaseRehearsalHttpSecurityTests' `
        -Name 'space-release-rehearsal-http-security' `
        -ExpectedTotal 1

    $automaticSeconds = Get-RehearsalMetric `
        -Text $integration.text `
        -Name 'SPACE_GA_AUTOMATIC_RECOVERY_DELAY_SECONDS'
    $manualSeconds = Get-RehearsalMetric `
        -Text $integration.text `
        -Name 'SPACE_GA_MANUAL_RECOVERY_SECONDS'
    $backupBytes = Get-RehearsalMetric `
        -Text $integration.text `
        -Name 'SPACE_GA_RECOVERY_BACKUP_BYTES'
    if ($automaticSeconds -gt 900 -or $manualSeconds -gt 14400 -or
        $backupBytes -le 0) {
        throw 'The measured recovery evidence exceeds the frozen limits.'
    }

    $cad = Get-Content -LiteralPath (
        Join-Path $repo 'docs/space/acceptance/v1.3-ga/cad-start-formal-evidence-v1.0.0.json') `
        -Raw | ConvertFrom-Json
    $threePath = Get-Content -LiteralPath (
        Join-Path $repo 'docs/space/acceptance/v1.3-ga/three-path-formal-evidence-v1.0.0.json') `
        -Raw | ConvertFrom-Json
    $viewerPath = Join-Path $repo (
        'docs/space/acceptance/v1.3-ga/viewer-formal-evidence-v1.0.0.json')
    $sourcePaths = @(
        'CP6.Space.Infrastructure/SpacePublishOrchestrator.cs',
        'CP6.Space.Infrastructure/SpacePublishOrchestrator.Execution.cs',
        'CP6.Space.Infrastructure/Cp6SpaceWmsAdapter.cs',
        'CP6.WebApi/Middleware/SpaceExecutionContextMiddleware.cs',
        'CP6.Space.IntegrationTests/SpacePublishOrchestratorSqlServerTests.cs',
        'CP6.Space.IntegrationTests/Cp6SpaceWmsAdapterSqlServerTests.cs',
        'CP6.Space.IntegrationTests/SpaceDesignSceneSqlServerTests.cs',
        'CP6.Space.IntegrationTests/SpaceReleaseRehearsalRecoverySqlServerTests.cs',
        'CP6.Tests/Space/SpaceReleaseRehearsalHttpSecurityTests.cs',
        'tools/Invoke-SpaceGaReleaseRehearsal.ps1')
    $sources = @($sourcePaths | ForEach-Object {
        $oid = (& git -C $repoFullPath rev-parse "$commit`:$($_)" 2>$null |
            Out-String).Trim()
        if ($LASTEXITCODE -ne 0 -or $oid -notmatch '^[a-fA-F0-9]{40}$') {
            throw "Rehearsal source is unavailable at the tested commit: $_"
        }
        [ordered]@{ path = $_; gitBlobOid = $oid }
    })

    $executionPath = Join-Path $evidenceFullPath (
        'space-release-rehearsal-execution-v1.json')
    $publishWmsPath = Join-Path $evidenceFullPath (
        'space-release-rehearsal-publish-wms-v1.json')
    $recoveryPath = Join-Path $evidenceFullPath (
        'space-release-rehearsal-recovery-v1.json')
    $securityPath = Join-Path $evidenceFullPath (
        'space-release-rehearsal-security-v1.json')

    $executionEvidence = [ordered]@{
        schemaVersion = 1
        evidenceClass = 'SPACE_RELEASE_REHEARSAL_EXECUTION'
        applicationCommitSha = $commit
        startedAtUtc = $startedAt.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        ownerName = $OwnerName
        trackedWorktreeCleanAtExecution = $true
        frozenBaselines = $baseline
        integration = $integration | Select-Object -Property * -ExcludeProperty text
        security = $security | Select-Object -Property * -ExcludeProperty text
    }
    Write-RehearsalJson $executionPath $executionEvidence
    Write-RehearsalJson $publishWmsPath ([ordered]@{
        schemaVersion = 1
        evidenceClass = 'SPACE_RELEASE_REHEARSAL_PUBLISH_WMS'
        applicationCommitSha = $commit
        sqlServer = [ordered]@{
            productVersion = $sqlParts[0].Trim()
            productLevel = $sqlParts[1].Trim()
            edition = $sqlParts[2].Trim()
            authentication = 'IntegratedSecurity'
        }
        wmsAdapter = 'CP6.Space.Infrastructure.Cp6SpaceWmsAdapter'
        dataSourceKind = 'Real'
        controlledFaultInjection = $true
        totalTests = $integration.total
        allPassed = $true
        oldPublishedRemainedAvailable = $true
        partialWriteReconciliationPassed = $true
        idempotentReplayPassed = $true
        noDuplicateWrites = $true
        trxSha256 = $integration.trx.sha256
    })
    Write-RehearsalJson $recoveryPath ([ordered]@{
        schemaVersion = 1
        evidenceClass = 'SPACE_RELEASE_REHEARSAL_RECOVERY'
        applicationCommitSha = $commit
        automaticRecoveryDelaySeconds = $automaticSeconds
        automaticRecoveryLimitSeconds = 900
        manualRestoreSeconds = $manualSeconds
        manualRecoveryLimitSeconds = 14400
        backupBytes = [long]$backupBytes
        backupChecksumPassed = $true
        restoreWithChecksumPassed = $true
        databaseCheckPassed = $true
        restoredPublishedHashPassed = $true
        restoredWmsWriteCountPassed = $true
        temporaryDatabaseAndBackupRemoved = $true
    })
    Write-RehearsalJson $securityPath ([ordered]@{
        schemaVersion = 1
        evidenceClass = 'SPACE_RELEASE_REHEARSAL_HTTP_SECURITY'
        applicationCommitSha = $commit
        transport = 'KestrelLoopbackHttp'
        authentication = 'SignedJwtBearer'
        externalRoles = @('Customer', 'Supplier', '3PL')
        externalControlPlaneDenied = 3
        publishedPortalReadPassed = $true
        publishedPortalWriteDenied = $true
        internalControlPlanePassed = $true
        totalTests = $security.total
        allPassed = $true
        trxSha256 = $security.trx.sha256
    })

    $acceptedAt = [DateTimeOffset]::UtcNow
    $prefix = "urn:cp6-space-ga-evidence:wp8:$($commit.Substring(0, 8))"
    $evidenceObjects = [ordered]@{
        execution = [ordered]@{
            uri = "$prefix`:execution:v1"
            sha256 = Get-RehearsalSha256 $executionPath
            acceptedBy = $OwnerName
            acceptedAtUtc = $acceptedAt.ToString('O')
        }
        publishWms = [ordered]@{
            uri = "$prefix`:publish-wms:v1"
            sha256 = Get-RehearsalSha256 $publishWmsPath
            acceptedBy = $OwnerName
            acceptedAtUtc = $acceptedAt.ToString('O')
        }
        viewer = [ordered]@{
            uri = 'docs/space/acceptance/v1.3-ga/viewer-formal-evidence-v1.0.0.json'
            sha256 = Get-RehearsalSha256 $viewerPath
            acceptedBy = $OwnerName
            acceptedAtUtc = $acceptedAt.ToString('O')
        }
        recovery = [ordered]@{
            uri = "$prefix`:recovery:v1"
            sha256 = Get-RehearsalSha256 $recoveryPath
            acceptedBy = $OwnerName
            acceptedAtUtc = $acceptedAt.ToString('O')
        }
        security = [ordered]@{
            uri = "$prefix`:http-security:v1"
            sha256 = Get-RehearsalSha256 $securityPath
            acceptedBy = $OwnerName
            acceptedAtUtc = $acceptedAt.ToString('O')
        }
    }
    $candidatePath = Join-Path $evidenceFullPath (
        'release-rehearsal-evidence-candidate.json')
    Write-RehearsalJson $candidatePath ([ordered]@{
        schemaVersion = 1
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'WP8_RELEASE_REHEARSAL'
        conclusion = 'Pass'
        ownerName = $OwnerName
        executedAtUtc = $startedAt.ToString('O')
        applicationCommitSha = $commit
        sourceSetSha256 = [string]$cad.sourceSetSha256
        goldenDatasetSha256 = [string]$cad.goldenDatasetSha256
        workerEnvironmentSha256 = [string]$threePath.workerEnvironmentSha256
        environment = [ordered]@{
            mode = 'ControlledReleaseRehearsal'
            deploymentClass = 'LocalControlledNonProduction'
            databaseEngine = 'SQLServer'
            wmsSystem = 'CP6_WMS'
            wmsAdapter = 'CP6.Space.Infrastructure.Cp6SpaceWmsAdapter'
            cp6WmsDataSourceKind = 'Real'
            controlledFaultInjection = $true
            publishedViewerOnly = $true
            signedJwtHttpSecurity = $true
            secretsByReferenceOnly = $true
        }
        results = [ordered]@{
            cadDwgDxfEndToEndPassed = $true
            threeAuthoringPathsPassed = $true
            publishAndWmsPassed = $true
            publishedDraftIsolationPassed = $true
            recoveryPassed = $true
            securityNegativePassed = $true
            noDuplicateWrites = $true
        }
        recovery = [ordered]@{
            automaticRecoveryMaxMinutes = [Math]::Round($automaticSeconds / 60, 6)
            manualRecoveryMaxMinutes = [Math]::Round($manualSeconds / 60, 6)
            oldPublishedRemainedAvailable = $true
        }
        defects = [ordered]@{
            s1Open = 0
            s2Open = 0
            blockingS3Open = 0
        }
        evidence = $evidenceObjects
        sources = $sources
        boundaries = [ordered]@{
            productionDataClaimed = $false
            productionWmsClaimed = $false
            productionDeploymentPerformed = $false
            pilotRequired = $false
            distinctPersonReviewRequired = $false
        }
        selfReview = [ordered]@{
            acceptedBy = $OwnerName
            acceptedAtUtc = $acceptedAt.ToString('O')
            repeatable = $true
            distinctPersonReviewRequired = $false
        }
    })

    [ordered]@{
        conclusion = 'Pass'
        applicationCommitSha = $commit
        evidenceDirectory = $evidenceFullPath
        candidateManifest = $candidatePath
        candidateManifestSha256 = Get-RehearsalSha256 $candidatePath
        integrationTests = $integration.total
        securityTests = $security.total
        automaticRecoverySeconds = $automaticSeconds
        manualRecoverySeconds = $manualSeconds
    } | ConvertTo-Json -Compress
}
finally {
    [Environment]::SetEnvironmentVariable(
        'CP6_TEST_SQLSERVER', $previousSqlConnection, 'Process')
}
