$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaViewerEvidence.ps1'
$repo = Split-Path -Parent $PSScriptRoot
$formalPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\viewer-formal-evidence-v1.0.0.json')
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-viewer\.tmp-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function New-TestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )
    $manifest = Get-Content -LiteralPath $formalPath -Raw | ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-ValidatorCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [bool]$AllowFixtures = $true
    )
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $validator,
        '-ManifestPath', $ManifestPath)
    if ($AllowFixtures) { $arguments += '-AllowTestFixtures' }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable @arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and ![string]::IsNullOrWhiteSpace($ExpectedError) -and
        $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    $script:passed++
    $global:LASTEXITCODE = 0
}

try {
    $formalRelativePath = (
        'docs/space/acceptance/v1.3-ga/' +
        'viewer-formal-evidence-v1.0.0.json')
    $textAttribute = (& git -C $repo check-attr text -- $formalRelativePath) |
        Out-String
    if ($LASTEXITCODE -ne 0 -or $textAttribute -notmatch ': text: unset') {
        throw (
            'Formal Viewer evidence must be marked binary in .gitattributes ' +
            'so its attested SHA-256 survives cross-platform checkout.')
    }
    $passed++

    & $validator -ManifestPath $formalPath -ExpectedOwnerName 'BUBAO.GAO' |
        Out-Null
    $passed++

    $validPath = New-TestManifest 'valid' { param($manifest) }
    Invoke-ValidatorCase 'valid Viewer evidence' $validPath $true
    Invoke-ValidatorCase 'fixture cannot close WP5' $validPath $false `
        'SPACE_GA_VIEWER_SYNTHETIC' $false

    $pendingPath = New-TestManifest 'pending' {
        param($manifest) $manifest.conclusion = 'Pending'
    }
    Invoke-ValidatorCase 'pending conclusion' $pendingPath $false `
        'SPACE_GA_VIEWER_CONCLUSION_INVALID'

    $gpuPatternPath = New-TestManifest 'gpu-brand' {
        param($manifest) $manifest.verification.performance.requiredGpuPattern = 'Iris.*Xe'
    }
    Invoke-ValidatorCase 'GPU brand cannot replace hardware budget gates' `
        $gpuPatternPath $false 'SPACE_GA_VIEWER_PERFORMANCE_CONTRACT_INVALID'

    $softwarePath = New-TestManifest 'software-renderer' {
        param($manifest) $manifest.boundaries.softwareRendererAllowed = $true
    }
    Invoke-ValidatorCase 'software rendering remains forbidden' $softwarePath $false `
        'SPACE_GA_VIEWER_BOUNDARY_INVALID'

    $hardwarePath = New-TestManifest 'hardware' {
        param($manifest) $manifest.verification.performance.hardwareRenderer = $false
    }
    Invoke-ValidatorCase 'hardware rendering must pass' $hardwarePath $false `
        'SPACE_GA_VIEWER_PERFORMANCE_CONTRACT_INVALID'

    $runsPath = New-TestManifest 'runs' {
        param($manifest) $manifest.verification.performance.coldRuns = 29
    }
    Invoke-ValidatorCase 'thirty cold runs are required' $runsPath $false `
        'SPACE_GA_VIEWER_PERFORMANCE_CONTRACT_INVALID'

    $framePath = New-TestManifest 'frame-budget' {
        param($manifest)
        $manifest.verification.performance.observed.frameP95Milliseconds = 20.1
    }
    Invoke-ValidatorCase 'frame P95 cannot exceed budget' $framePath $false `
        'SPACE_GA_VIEWER_PERFORMANCE_BUDGET_FAILED'

    $pickPath = New-TestManifest 'pick-integrity' {
        param($manifest) $manifest.verification.performance.pickHits = 2999
    }
    Invoke-ValidatorCase 'all picks must hit' $pickPath $false `
        'SPACE_GA_VIEWER_PERFORMANCE_CONTRACT_INVALID'

    $skipPath = New-TestManifest 'accessibility-skip' {
        param($manifest) $manifest.verification.accessibility.skipped = 1
    }
    Invoke-ValidatorCase 'accessibility cannot skip' $skipPath $false `
        'SPACE_GA_VIEWER_ACCESSIBILITY_FAILED'

    $viewportPath = New-TestManifest 'viewport' {
        param($manifest)
        $manifest.verification.accessibility.viewports = @('1440x900')
    }
    Invoke-ValidatorCase 'both viewports are required' $viewportPath $false `
        'SPACE_GA_VIEWER_ACCESSIBILITY_FAILED'

    $fixtureClaimPath = New-TestManifest 'fixture-claim' {
        param($manifest)
        $manifest.boundaries.uiFixtureUsedForProductionDataClaim = $true
    }
    Invoke-ValidatorCase 'fixture cannot claim production data' $fixtureClaimPath $false `
        'SPACE_GA_VIEWER_BOUNDARY_INVALID'

    $rawHashPath = New-TestManifest 'raw-hash' {
        param($manifest) $manifest.rawEvidence[0].sha256 = 'invalid'
    }
    Invoke-ValidatorCase 'raw evidence needs SHA-256' $rawHashPath $false `
        'SPACE_GA_VIEWER_RAW_EVIDENCE_INVALID'

    $sourcePath = New-TestManifest 'source-blob' {
        param($manifest) $manifest.sources[0].gitBlobOid = '0' * 40
    }
    Invoke-ValidatorCase 'source blob must match commit' $sourcePath $false `
        'SPACE_GA_VIEWER_SOURCE_BLOB_MISMATCH'

    $ownerPath = New-TestManifest 'owner' {
        param($manifest)
        $manifest.ownerName = 'TBD'
        $manifest.selfReview.acceptedBy = 'TBD'
    }
    Invoke-ValidatorCase 'owner must be real' $ownerPath $false `
        'SPACE_GA_VIEWER_OWNER_INVALID'

    $reviewTimePath = New-TestManifest 'review-time' {
        param($manifest)
        $manifest.selfReview.acceptedAtUtc = '2020-01-01T00:00:00Z'
    }
    Invoke-ValidatorCase 'review follows execution' $reviewTimePath $false `
        'SPACE_GA_VIEWER_REVIEW_TIME_INVALID'
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}

Write-Output "Viewer evidence validator tests passed: $passed"
