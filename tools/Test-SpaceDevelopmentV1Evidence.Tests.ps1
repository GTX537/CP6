$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceDevelopmentV1Evidence.ps1'
$baseManifestPath = Join-Path $repo (
    'docs\space\acceptance\development-v1\development-evidence-index.json')
$baseFormalPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    'cp6-space-development-v1-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory)
$passed = 0

function New-DevelopmentFixture {
    param([string]$Name, [scriptblock]$Mutation)
    $manifest = Get-Content -LiteralPath $baseManifestPath -Raw |
        ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function New-FormalFixture {
    param([string]$Name, [scriptblock]$Mutation)
    $manifest = Get-Content -LiteralPath $baseFormalPath -Raw |
        ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name-formal.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-DevelopmentCase {
    param(
        [string]$Name,
        [string]$ManifestPath,
        [string]$FormalGaIndexPath = $baseFormalPath,
        [bool]$ShouldPass,
        [string]$ExpectedError
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass `
            -File $validator -ManifestPath $ManifestPath `
            -FormalGaIndexPath $FormalGaIndexPath `
            -RequireDevelopmentComplete 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previous
    }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and
        $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report $ExpectedError.`n$output"
    }
    $script:passed++
    $global:LASTEXITCODE = 0
}

try {
    Invoke-DevelopmentCase -Name 'valid Development V1 acceptance' `
        -ManifestPath $baseManifestPath -ShouldPass $true

    $formalEligible = New-DevelopmentFixture 'formal-eligible' {
        param($manifest) $manifest.formalGaEligible = $true
    }
    Invoke-DevelopmentCase -Name 'development cannot become formal GA' `
        -ManifestPath $formalEligible -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_V1_FORMAL_GA_FORBIDDEN'

    $missingGate = New-DevelopmentFixture 'missing-gate' {
        param($manifest)
        $manifest.gates = @($manifest.gates | Select-Object -First 5)
    }
    Invoke-DevelopmentCase -Name 'all six gates are required' `
        -ManifestPath $missingGate -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_V1_GATE_SET_INVALID'

    $pendingGate = New-DevelopmentFixture 'pending-gate' {
        param($manifest) $manifest.gates[0].status = 'Pending'
    }
    Invoke-DevelopmentCase -Name '100 percent cannot hide a pending gate' `
        -ManifestPath $pendingGate -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_V1_DERIVED_STATUS_MISMATCH'

    $missingEvidence = New-DevelopmentFixture 'missing-evidence' {
        param($manifest)
        $manifest.gates[0].evidencePaths[0] = 'docs/space/reports/not-real.md'
    }
    Invoke-DevelopmentCase -Name 'evidence files must exist' `
        -ManifestPath $missingEvidence -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_V1_PATH_MISSING'

    $expandedScope = New-DevelopmentFixture 'production-scope' {
        param($manifest)
        $manifest.scope.requiresAuthorizedCustomerCad = $true
    }
    Invoke-DevelopmentCase -Name 'development scope cannot claim customer CAD' `
        -ManifestPath $expandedScope -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_V1_SCOPE_EXPANSION'

    $rewrittenSnapshot = New-DevelopmentFixture 'rewritten-formal-snapshot' {
        param($manifest)
        $manifest.formalBoundary.snapshotAtAcceptance.declaredStatus = 'GaReady'
        $manifest.formalBoundary.snapshotAtAcceptance.pendingGates = 0
    }
    Invoke-DevelopmentCase -Name 'development cannot rewrite the formal snapshot' `
        -ManifestPath $rewrittenSnapshot -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_V1_FORMAL_SNAPSHOT_INVALID'

    $formalLeak = New-FormalFixture 'formal-leak' {
        param($manifest)
        $manifest.gates[0].acceptedEvidence = @(
            [pscustomobject]@{
                uri = 'docs/space/acceptance/development-v1/development-evidence-index.json'
                sha256 = ('a' * 64)
                acceptedBy = 'BUBAO.GAO'
                acceptedAtUtc = '2026-08-27T00:00:00Z'
            })
    }
    Invoke-DevelopmentCase -Name 'formal accepted evidence cannot use development artifacts' `
        -ManifestPath $baseManifestPath -FormalGaIndexPath $formalLeak `
        -ShouldPass $false -ExpectedError 'SPACE_DEV_V1_FORMAL_EVIDENCE_LEAK'
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

if ($global:LASTEXITCODE -ne 0) {
    throw "Test suite leaked child process exit code $global:LASTEXITCODE."
}

Write-Host "Space Development V1 evidence tests passed: $passed"
