param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaThreePathEvidence.ps1'
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-three-path\.tmp-' +
    [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0
$executionTime = [DateTimeOffset]::UtcNow.AddMinutes(-10).ToString(
    'yyyy-MM-ddTHH:mm:ssZ',
    [System.Globalization.CultureInfo]::InvariantCulture)
$acceptanceTime = [DateTimeOffset]::UtcNow.AddMinutes(-5).ToString(
    'yyyy-MM-ddTHH:mm:ssZ',
    [System.Globalization.CultureInfo]::InvariantCulture)

function New-ThreePathEvidence([string]$Id) {
    return [pscustomobject]@{
        uri = "urn:cp6-space-ga-evidence:test:three-path:$Id"
        sha256 = '1' * 64
        acceptedBy = 'Zhang Wei'
        acceptedAtUtc = $acceptanceTime
    }
}

function New-Path([string]$Name) {
    return [pscustomobject]@{
        path = $Name
        previewPassed = $true
        draftUnchangedBeforeApply = $true
        explicitApplyPassed = $true
        typedChangesetPassed = $true
        leaseRevisionIdempotencyPassed = $true
    }
}

function New-Cad([string]$Format, [string]$Id) {
    return [pscustomobject]@{
        sampleRef = "urn:cp6-space-golden-cad:v1.0.0:$Id"
        sourceFormat = $Format
        license = 'ApprovedOriginalWork'
        sourceSha256 = '2' * 64
        sourceSizeBytes = 4096
        providerPackageSha256 = '3' * 64
        providerKey = 'cp6-autocad-worker'
        providerVersion = '1.0.0+formal'
    }
}

function New-ValidThreePathManifest {
    return [pscustomobject]@{
        schemaVersion = 1
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'WP4_THREE_PATH_FORMAL_EVIDENCE'
        conclusion = 'Pass'
        ownerName = 'Zhang Wei'
        executedAtUtc = $executionTime
        applicationCommitSha = 'a' * 40
        sourceSetSha256 = 'b' * 64
        goldenDatasetSha256 = 'c' * 64
        workerEnvironmentSha256 = 'd' * 64
        environment = [pscustomobject]@{
            mode = 'ControlledAcceptance'
            databaseEngine = 'SQLServer'
            productionDeploymentPerformed = $false
            productionDataClaimed = $false
        }
        inputs = [pscustomobject]@{
            cad = @(
                (New-Cad 'DWG' 'l1-c01'),
                (New-Cad 'DXF' 'l1-c02'))
            excel = [pscustomobject]@{
                format = 'XLSX'
                dataClass = 'ControlledAcceptanceData'
                sha256 = '4' * 64
                productionDataClaimed = $false
            }
            underlays = @(
                [pscustomobject]@{
                    format = 'PDF'
                    dataClass = 'ControlledAcceptanceData'
                    sha256 = '5' * 64
                    productionDataClaimed = $false
                },
                [pscustomobject]@{
                    format = 'PNG'
                    dataClass = 'ControlledAcceptanceData'
                    sha256 = '6' * 64
                    productionDataClaimed = $false
                })
            blankCanvasIncluded = $true
        }
        paths = @(
            (New-Path 'CAD'),
            (New-Path 'ExcelCad'),
            (New-Path 'ManualUnderlayBlankCanvas'))
        sqlServer = [pscustomobject]@{
            productVersion = '17.0.4025.3'
            edition = 'Express Edition (64-bit)'
            passed = 453
            failed = 0
            skipped = 0
        }
        evidence = [pscustomobject]@{
            cad = New-ThreePathEvidence 'cad'
            excelCad = New-ThreePathEvidence 'excel-cad'
            manualUnderlayBlankCanvas = New-ThreePathEvidence 'manual'
            sqlServer = New-ThreePathEvidence 'sql-server'
        }
    }
}

if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
    $exportPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
    [void](New-Item -ItemType Directory -Path (Split-Path -Parent $exportPath) -Force)
    New-ValidThreePathManifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $exportPath -Encoding UTF8
    [System.IO.Directory]::Delete($tempDirectory, $true)
    exit 0
}

function New-ThreePathTestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )
    $manifest = New-ValidThreePathManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-ThreePathCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [string]$ExpectedOwnerName,
        [bool]$AllowTestFixtures = $true
    )
    $args = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $validator,
        '-ManifestPath', $ManifestPath)
    if ($AllowTestFixtures) { $args += '-AllowTestFixtures' }
    if (![string]::IsNullOrWhiteSpace($ExpectedOwnerName)) {
        $args += @('-ExpectedOwnerName', $ExpectedOwnerName)
    }
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
    $validPath = New-ThreePathTestManifest 'valid' { param($manifest) }
    Invoke-ThreePathCase -Name 'valid controlled three-path evidence' `
        -ManifestPath $validPath -ShouldPass $true

    Invoke-ThreePathCase -Name 'formal mode rejects test evidence' `
        -ManifestPath $validPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_EVIDENCE_SYNTHETIC' `
        -AllowTestFixtures $false

    $ownerPath = New-ThreePathTestManifest 'owner' {
        param($manifest); $manifest.ownerName = '00001'
    }
    Invoke-ThreePathCase -Name 'owner must be real' `
        -ManifestPath $ownerPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_OWNER_INVALID'

    Invoke-ThreePathCase -Name 'owner matches WP4 owner' `
        -ManifestPath $validPath -ShouldPass $false `
        -ExpectedOwnerName 'Different Person' `
        -ExpectedError 'SPACE_GA_THREE_PATH_OWNER_MISMATCH'

    $environmentPath = New-ThreePathTestManifest 'environment' {
        param($manifest); $manifest.environment.productionDataClaimed = $true
    }
    Invoke-ThreePathCase -Name 'production data cannot be claimed' `
        -ManifestPath $environmentPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_ENVIRONMENT_INVALID'

    $cadPath = New-ThreePathTestManifest 'cad' {
        param($manifest); $manifest.inputs.cad[1].sourceFormat = 'DWG'
    }
    Invoke-ThreePathCase -Name 'DWG and DXF are both required' `
        -ManifestPath $cadPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_CAD_SET_INVALID'

    $excelPath = New-ThreePathTestManifest 'excel' {
        param($manifest); $manifest.inputs.excel.sha256 = 'not-a-hash'
    }
    Invoke-ThreePathCase -Name 'Excel input is hash bound' `
        -ManifestPath $excelPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_EXCEL_INPUT_INVALID'

    $manualPath = New-ThreePathTestManifest 'manual' {
        param($manifest); $manifest.inputs.underlays = @($manifest.inputs.underlays[1])
    }
    Invoke-ThreePathCase -Name 'PDF PNG and blank canvas are required' `
        -ManifestPath $manualPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_MANUAL_INPUT_INVALID'

    $resultPath = New-ThreePathTestManifest 'result' {
        param($manifest); $manifest.paths[0].draftUnchangedBeforeApply = $false
    }
    Invoke-ThreePathCase -Name 'Draft must remain unchanged before Apply' `
        -ManifestPath $resultPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_RESULT_FAILED'

    $sqlPath = New-ThreePathTestManifest 'sql' {
        param($manifest); $manifest.sqlServer.skipped = 1
    }
    Invoke-ThreePathCase -Name 'SQL Server run cannot skip tests' `
        -ManifestPath $sqlPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_SQLSERVER_FAILED'

    $timePath = New-ThreePathTestManifest 'time' {
        param($manifest)
        $manifest.evidence.sqlServer.acceptedAtUtc =
            [DateTimeOffset]::UtcNow.AddMinutes(-20).ToString(
                'yyyy-MM-ddTHH:mm:ssZ',
                [System.Globalization.CultureInfo]::InvariantCulture)
    }
    Invoke-ThreePathCase -Name 'evidence follows execution' `
        -ManifestPath $timePath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_THREE_PATH_EVIDENCE_TIME_INVALID'

    [ordered]@{
        suite = 'CP6_SPACE_GA_THREE_PATH_EVIDENCE'
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
