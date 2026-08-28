param(
    [Parameter(Mandatory)]
    [string]$SourceDxfPath,
    [Parameter(Mandatory)]
    [string]$ExpectedSourceSha256,
    [Parameter(Mandatory)]
    [string]$ReleasedCadExperimentPath,
    [Parameter(Mandatory)]
    [string]$CurrentCadExperimentDllPath,
    [Parameter(Mandatory)]
    [string]$OutputRoot,
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-fA-F0-9]{40}$')]
    [string]$ApplicationCommitSha,
    [Parameter(Mandatory)]
    [string]$ProviderVersion,
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-fA-F0-9]{64}$')]
    [string]$FrozenWorkerEnvironmentSha256,
    [ValidateRange(5, 100)]
    [int]$ObservationCount = 20,
    [ValidateRange(0, 10)]
    [int]$WarmupCount = 1,
    [string]$AcceptedBy = 'BUBAO.GAO'
)

$ErrorActionPreference = 'Stop'
$targetBytes = 50L * 1024L * 1024L
$tenantId = '11111111-1111-1111-1111-111111111111'
$floorId = '44444444-4444-4444-4444-444444444444'
$profileId = '88888888-8888-8888-8888-888888888888'
$modelVersionId = '99999999-9999-9999-9999-999999999999'

function Resolve-RequiredFile {
    param([Parameter(Mandatory)][string]$Path)

    $resolved = [System.IO.Path]::GetFullPath($Path)
    if (!(Test-Path -LiteralPath $resolved -PathType Leaf)) {
        throw "Required file was not found: $resolved"
    }
    return $resolved
}

function Get-LowerSha256 {
    param([Parameter(Mandatory)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Utf8Json {
    param(
        [Parameter(Mandatory)]$Value,
        [Parameter(Mandatory)][string]$Path,
        [int]$Depth = 20
    )

    $json = $Value | ConvertTo-Json -Depth $Depth
    [System.IO.File]::WriteAllText(
        $Path,
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
}

function Write-PaddedAuthorizedDxf {
    param(
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$DestinationPath
    )

    $sourceBytes = [System.IO.File]::ReadAllBytes($SourcePath)
    $marker = [System.Text.Encoding]::ASCII.GetBytes("0`r`nEOF`r`n")
    $markerIndex = -1
    for ($index = $sourceBytes.Length - $marker.Length; $index -ge 0; $index--) {
        $matches = $true
        for ($offset = 0; $offset -lt $marker.Length; $offset++) {
            if ($sourceBytes[$index + $offset] -ne $marker[$offset]) {
                $matches = $false
                break
            }
        }
        if ($matches) {
            $markerIndex = $index
            break
        }
    }
    if ($markerIndex -lt 0) {
        $marker = [System.Text.Encoding]::ASCII.GetBytes("0`nEOF`n")
        for ($index = $sourceBytes.Length - $marker.Length; $index -ge 0; $index--) {
            $matches = $true
            for ($offset = 0; $offset -lt $marker.Length; $offset++) {
                if ($sourceBytes[$index + $offset] -ne $marker[$offset]) {
                    $matches = $false
                    break
                }
            }
            if ($matches) {
                $markerIndex = $index
                break
            }
        }
    }
    if ($markerIndex -lt 0) {
        throw 'The source DXF does not contain a terminal 0/EOF record.'
    }

    $commentPrefix = [System.Text.Encoding]::ASCII.GetBytes("999`r`n")
    $commentSuffix = [System.Text.Encoding]::ASCII.GetBytes("`r`n0`r`nEOF`r`n")
    $paddingLength = $targetBytes - $markerIndex -
        $commentPrefix.Length - $commentSuffix.Length
    if ($paddingLength -lt 1) {
        throw 'The source DXF is too large for the 50 MiB performance envelope.'
    }

    $buffer = [byte[]]::new(64 * 1024)
    [Array]::Fill[byte]($buffer, [byte][char]'X')
    $stream = [System.IO.FileStream]::new(
        $DestinationPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None,
        64 * 1024,
        [System.IO.FileOptions]::SequentialScan)
    try {
        $stream.Write($sourceBytes, 0, $markerIndex)
        $stream.Write($commentPrefix, 0, $commentPrefix.Length)
        while ($paddingLength -gt 0) {
            $count = [int][Math]::Min($buffer.Length, $paddingLength)
            $stream.Write($buffer, 0, $count)
            $paddingLength -= $count
        }
        $stream.Write($commentSuffix, 0, $commentSuffix.Length)
    }
    finally {
        $stream.Dispose()
        [Array]::Clear($sourceBytes, 0, $sourceBytes.Length)
        [Array]::Clear($buffer, 0, $buffer.Length)
    }

    $actualLength = (Get-Item -LiteralPath $DestinationPath).Length
    if ($actualLength -ne $targetBytes) {
        throw "The derived DXF is $actualLength bytes; expected $targetBytes."
    }
}

function ConvertTo-QuotedArgument {
    param([Parameter(Mandatory)][string]$Value)

    return '"' + $Value.Replace('"', '\"') + '"'
}

function Invoke-MeasuredProcess {
    param(
        [Parameter(Mandatory)][string]$FilePath,
        [Parameter(Mandatory)][string[]]$Arguments,
        [Parameter(Mandatory)][string]$LogStem
    )

    $stdout = $LogStem + '.stdout.log'
    $stderr = $LogStem + '.stderr.log'
    $argumentText = ($Arguments | ForEach-Object {
            ConvertTo-QuotedArgument ([string]$_)
        }) -join ' '
    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process `
        -FilePath $FilePath `
        -ArgumentList $argumentText `
        -NoNewWindow `
        -PassThru `
        -RedirectStandardOutput $stdout `
        -RedirectStandardError $stderr
    $process.WaitForExit()
    $stopwatch.Stop()
    $process.Refresh()
    $measurement = [pscustomobject][ordered]@{
        elapsedSeconds = [Math]::Round($stopwatch.Elapsed.TotalSeconds, 6)
        cpuSeconds = [Math]::Round($process.TotalProcessorTime.TotalSeconds, 6)
        peakWorkingSetBytes = [long]$process.PeakWorkingSet64
        exitCode = $process.ExitCode
    }
    if ($process.ExitCode -ne 0) {
        $errorText = Get-Content -LiteralPath $stderr -Raw
        throw "Command failed with exit code $($process.ExitCode): $errorText"
    }
    return $measurement
}

function Invoke-WorkflowRun {
    param(
        [Parameter(Mandatory)][int]$Ordinal,
        [Parameter(Mandatory)][bool]$IsWarmup,
        [Parameter(Mandatory)][string]$StandardCadPath,
        [Parameter(Mandatory)][string]$ConfirmationPath,
        [Parameter(Mandatory)][string]$ProfileDraftPath,
        [Parameter(Mandatory)][string]$RunRoot
    )

    [System.IO.Directory]::CreateDirectory($RunRoot) | Out-Null
    $cadIr = Join-Path $RunRoot 'cad-ir.json'
    $prepared = Join-Path $RunRoot 'prepared-cad-ir.json'
    $inventory = Join-Path $RunRoot 'inventory.json'
    $profile = Join-Path $RunRoot 'mapping-profile.json'
    $mapping = Join-Path $RunRoot 'mapping-preview.json'
    $semantic = Join-Path $RunRoot 'semantic-preview.json'
    $proposals = Join-Path $RunRoot 'rule-only-proposals.json'
    $steps = [System.Collections.Generic.List[object]]::new()
    $workflowStopwatch = [System.Diagnostics.Stopwatch]::StartNew()

    $steps.Add((Invoke-MeasuredProcess $ReleasedCadExperimentPath @(
                'convert-dev-ir', '--input', $StandardCadPath,
                '--output', $cadIr) (Join-Path $RunRoot '01-convert')))
    $steps.Add((Invoke-MeasuredProcess $ReleasedCadExperimentPath @(
                'prepare-dev-coordinate', '--input', $cadIr,
                '--confirmation', $ConfirmationPath,
                '--output', $prepared) (Join-Path $RunRoot '02-prepare')))
    $steps.Add((Invoke-MeasuredProcess $ReleasedCadExperimentPath @(
                'build-dev-inventory', '--input', $prepared,
                '--output', $inventory) (Join-Path $RunRoot '03-inventory')))
    $steps.Add((Invoke-MeasuredProcess 'dotnet' @(
                $CurrentCadExperimentDllPath,
                'seal-dev-mapping-profile', '--input', $ProfileDraftPath,
                '--output', $profile) (Join-Path $RunRoot '04-seal-mapping')))
    $steps.Add((Invoke-MeasuredProcess 'dotnet' @(
                $CurrentCadExperimentDllPath,
                'preview-dev-mapping', '--inventory', $inventory,
                '--profile', $profile, '--tenant-id', $tenantId,
                '--output', $mapping) (Join-Path $RunRoot '05-preview-mapping')))
    $steps.Add((Invoke-MeasuredProcess 'dotnet' @(
                $CurrentCadExperimentDllPath,
                'parse-dev-semantic', '--prepared', $prepared,
                '--inventory', $inventory, '--profile', $profile,
                '--mapping', $mapping, '--output', $semantic) (
                Join-Path $RunRoot '06-parse-semantic')))
    $trainedUserReadySeconds = $workflowStopwatch.Elapsed.TotalSeconds
    $semanticDocument = Get-Content -LiteralPath $semantic -Raw | ConvertFrom-Json
    if ($semanticDocument.readyForConfirmation -ne $true) {
        throw "Run $Ordinal did not reach readyForConfirmation."
    }

    $runId = [Guid]::NewGuid().ToString()
    $steps.Add((Invoke-MeasuredProcess 'dotnet' @(
                $CurrentCadExperimentDllPath,
                'synthesize-dev-rule-only-proposals', '--semantic', $semantic,
                '--model-version-id', $modelVersionId, '--run-id', $runId,
                '--rule-version', 'warehouse-rule-only-v2',
                '--output', $proposals) (Join-Path $RunRoot '07-synthesize')))
    $workflowStopwatch.Stop()
    $proposalDocument = Get-Content -LiteralPath $proposals -Raw | ConvertFrom-Json
    if ($proposalDocument.summary.canEnterReview -ne $true) {
        throw "Run $Ordinal did not produce reviewable proposals."
    }

    $cpuSeconds = ($steps | Measure-Object -Property cpuSeconds -Sum).Sum
    $peakBytes = ($steps | Measure-Object -Property peakWorkingSetBytes -Maximum).Maximum
    return [pscustomobject][ordered]@{
        ordinal = $Ordinal
        warmup = $IsWarmup
        reviewReadyDurationSeconds = [Math]::Round(
            $workflowStopwatch.Elapsed.TotalSeconds,
            6)
        reviewReadyDurationMinutes = [Math]::Round(
            $workflowStopwatch.Elapsed.TotalMinutes,
            9)
        trainedUserReadyDurationSeconds = [Math]::Round(
            $trainedUserReadySeconds,
            6)
        trainedUserReadyDurationMinutes = [Math]::Round(
            $trainedUserReadySeconds / 60,
            9)
        cpuSeconds = [Math]::Round([double]$cpuSeconds, 6)
        peakWorkingSetBytes = [long]$peakBytes
        readyForConfirmation = $true
        canEnterReview = $true
        failureCount = 0
    }
}

$sourceDxf = Resolve-RequiredFile $SourceDxfPath
$ReleasedCadExperimentPath = Resolve-RequiredFile $ReleasedCadExperimentPath
$CurrentCadExperimentDllPath = Resolve-RequiredFile $CurrentCadExperimentDllPath
$actualSourceSha256 = Get-LowerSha256 $sourceDxf
if (!$actualSourceSha256.Equals(
        $ExpectedSourceSha256,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Source DXF hash mismatch: $actualSourceSha256"
}

$output = [System.IO.Path]::GetFullPath($OutputRoot)
if (Test-Path -LiteralPath $output) {
    throw "OutputRoot already exists; use a new evidence directory: $output"
}
[System.IO.Directory]::CreateDirectory($output) | Out-Null
$standardCad = Join-Path $output 'authorized-original-derived-50mib.dxf'
Write-PaddedAuthorizedDxf $sourceDxf $standardCad
$standardCadSha256 = Get-LowerSha256 $standardCad

$confirmationPath = Join-Path $output 'coordinate-confirmation.json'
Write-Utf8Json ([ordered]@{
        sourceSha256 = $standardCadSha256
        unitConfirmed = $true
        confirmedUnit = 'Millimeter'
        sourceOriginInSourceUnits = [ordered]@{ x = 0; y = 0; z = 0 }
        floorOriginMillimeters = [ordered]@{ x = 0; y = 0; z = 0 }
        rotationZDegrees = 0
        targetFloor = [ordered]@{
            floorLogicalId = $floorId
            floorCode = 'F01'
            level = 1
            elevationMillimeters = 0
            coordinateSystem = 'LOCAL_MM_Z_UP'
            boundaryBounds = [ordered]@{
                minX = -1000000
                minY = -1000000
                maxX = 1000000
                maxY = 1000000
            }
        }
    }) $confirmationPath

$ruleDefinitions = @(
    @('LAYER-A-WALL', 1000, 'A-WALL', 'Wall', 'ClosedBoundary', 0.99),
    @('LAYER-A-DOOR', 990, 'A-DOOR', 'Door', 'DirectGeometry', 0.99),
    @('LAYER-A-DOCK', 980, 'A-DOCK', 'Dock', 'ClosedBoundary', 0.99),
    @('LAYER-A-ZONE', 970, 'A-ZONE', 'Zone', 'ClosedBoundary', 0.99),
    @('LAYER-A-AISLE', 960, 'A-AISLE', 'Aisle', 'ClosedBoundary', 0.99),
    @('LAYER-S-COLUMN', 950, 'S-COLUMN', 'Column', 'DirectGeometry', 0.99),
    @('LAYER-S-RACK', 940, 'S-RACK', 'Rack', 'ClosedBoundary', 0.99),
    @('LAYER-S-PALLET', 930, 'S-PALLET', 'Equipment', 'ClosedBoundary', 0.97),
    @('LAYER-M-EQUIP', 920, 'M-EQUIP', 'Equipment', 'ClosedBoundary', 0.95),
    @('LAYER-A-ANNO', 910, 'A-ANNO', 'Annotation', 'DirectGeometry', 0.99)
)
$mappingRules = foreach ($definition in $ruleDefinitions) {
    [ordered]@{
        ruleId = $definition[0]
        priority = $definition[1]
        sourceKind = 'Layer'
        matchKind = 'Exact'
        pattern = $definition[2]
        attributeName = $null
        attributeMatchKind = $null
        attributePattern = $null
        target = $definition[3]
        targetSubtype = $null
        geometryRule = $definition[4]
        defaultHeightMillimeters = $null
        defaultThicknessMillimeters = $null
        confidenceWeight = $definition[5]
        isRequired = $false
    }
}
$mappingRules += [ordered]@{
    ruleId = 'LAYER-OTHER-GUIDE'
    priority = 0
    sourceKind = 'Layer'
    matchKind = 'Regex'
    pattern = '.*'
    attributeName = $null
    attributeMatchKind = $null
    attributePattern = $null
    target = 'Guide'
    targetSubtype = $null
    geometryRule = 'DirectGeometry'
    defaultHeightMillimeters = $null
    defaultThicknessMillimeters = $null
    confidenceWeight = 0.90
    isRequired = $false
}
$mappingRules += [ordered]@{
    ruleId = 'BLOCK-OTHER-GUIDE'
    priority = 0
    sourceKind = 'Block'
    matchKind = 'Regex'
    pattern = '.*'
    attributeName = $null
    attributeMatchKind = $null
    attributePattern = $null
    target = 'Guide'
    targetSubtype = $null
    geometryRule = 'InsertionPoint'
    defaultHeightMillimeters = $null
    defaultThicknessMillimeters = $null
    confidenceWeight = 0.90
    isRequired = $false
}
$profileDraftPath = Join-Path $output 'mapping-profile-draft.json'
Write-Utf8Json ([ordered]@{
        schemaVersion = 1
        profileId = $profileId
        version = 1
        name = 'WP7 authorized original 50 MiB performance mapping'
        scope = 'System'
        tenantId = $null
        isEnabled = $true
        basedOnProfileId = $null
        basedOnVersion = $null
        rules = @($mappingRules)
    }) $profileDraftPath

$observations = [System.Collections.Generic.List[object]]::new()
$totalRuns = $WarmupCount + $ObservationCount
for ($run = 1; $run -le $totalRuns; $run++) {
    $isWarmup = $run -le $WarmupCount
    $runRoot = Join-Path $output ('run-{0:D2}' -f $run)
    $observation = Invoke-WorkflowRun `
        -Ordinal $run `
        -IsWarmup $isWarmup `
        -StandardCadPath $standardCad `
        -ConfirmationPath $confirmationPath `
        -ProfileDraftPath $profileDraftPath `
        -RunRoot $runRoot
    $observations.Add($observation)
    Write-Host (
        'Run {0}/{1}: review={2:N3}s ready={3:N3}s warmup={4}' -f
        $run,
        $totalRuns,
        $observation.reviewReadyDurationSeconds,
        $observation.trainedUserReadyDurationSeconds,
        $isWarmup)
}

$accepted = @($observations | Where-Object { !$_.warmup })
$reviewDurations = @($accepted | ForEach-Object {
        $_.reviewReadyDurationMinutes
    })
$readyDurations = @($accepted | ForEach-Object {
        $_.trainedUserReadyDurationMinutes
    })
$reviewOrdered = @($reviewDurations | Sort-Object)
$readyOrdered = @($readyDurations | Sort-Object)
$p95Index = [Math]::Ceiling(0.95 * $ObservationCount) - 1
$acceptedAtUtc = [DateTimeOffset]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
$evidence = [ordered]@{
    schemaVersion = 1
    programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
    evidenceClass = 'CP6_SPACE_WP7_PRIMARY_PERFORMANCE'
    conclusion = 'Pass'
    applicationCommitSha = $ApplicationCommitSha.ToLowerInvariant()
    provider = [ordered]@{
        providerKey = 'cp6-autocad-worker'
        providerVersion = $ProviderVersion
        frozenWorkerEnvironmentSha256 = $FrozenWorkerEnvironmentSha256.ToLowerInvariant()
        releasedCadExperimentSha256 = Get-LowerSha256 $ReleasedCadExperimentPath
        currentCadExperimentSha256 = Get-LowerSha256 $CurrentCadExperimentDllPath
    }
    standardCad = [ordered]@{
        sampleRef = 'urn:cp6-space-golden-cad:v1.0.0:l1-c02'
        license = 'ApprovedOriginalWork'
        originalSourceSha256 = $actualSourceSha256
        originalSourceSizeBytes = (Get-Item -LiteralPath $sourceDxf).Length
        derivation = 'The authorized original DXF is preserved and one terminal DXF 999 comment is padded to exactly 50 MiB. This is a standard I/O and workflow performance envelope, not a claim of 50 MiB customer geometry complexity.'
        standardCadSizeBytes = (Get-Item -LiteralPath $standardCad).Length
        standardCadSha256 = $standardCadSha256
        rawCadCommittedToGit = $false
    }
    workflow = [ordered]@{
        mode = 'ScriptedSoloDeveloperFirstReady'
        trainedUser = $AcceptedBy
        executionAgent = 'Codex desktop automation'
        reviewReadyDefinition = 'Released Primary conversion, coordinate preparation, inventory, frozen mapping preview, semantic parsing and deterministic proposal synthesis completed with canEnterReview=true.'
        firstReadyDefinition = 'The uploaded source reached its first Ready-equivalent semantic state with readyForConfirmation=true; no review decision, Apply or Draft write is included.'
        externalProviderInvoked = $false
        productionDeploymentExecuted = $false
        ruleVersion = 'warehouse-rule-only-v2'
        observationCount = $ObservationCount
        warmupCount = $WarmupCount
        warmupsExcludedFromPercentiles = $true
    }
    observations = @($observations)
    reviewReadyDurationsMinutes = $reviewDurations
    trainedUserReadyDurationsMinutes = $readyDurations
    reviewReadyP95Minutes = $reviewOrdered[$p95Index]
    trainedUserReadyP95Minutes = $readyOrdered[$p95Index]
    failureCount = 0
    environment = [ordered]@{
        osDescription = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
        osArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
        frameworkDescription = [System.Runtime.InteropServices.RuntimeInformation]::FrameworkDescription
        logicalProcessorCount = [Environment]::ProcessorCount
    }
    attestation = [ordered]@{
        acceptedBy = $AcceptedBy
        acceptedAtUtc = $acceptedAtUtc
        source = 'DeliveryOwner accepted the recommended SoloDeveloper automated evidence workflow in the active CP6 conversation.'
    }
}
$evidencePath = Join-Path $output 'performance-evidence.json'
Write-Utf8Json $evidence $evidencePath 30

[ordered]@{
    conclusion = $evidence.conclusion
    standardCadSizeBytes = $evidence.standardCad.standardCadSizeBytes
    standardCadSha256 = $evidence.standardCad.standardCadSha256
    observationCount = $ObservationCount
    reviewReadyP95Minutes = $evidence.reviewReadyP95Minutes
    trainedUserReadyP95Minutes = $evidence.trainedUserReadyP95Minutes
    evidencePath = $evidencePath
    evidenceSha256 = Get-LowerSha256 $evidencePath
} | ConvertTo-Json -Compress
