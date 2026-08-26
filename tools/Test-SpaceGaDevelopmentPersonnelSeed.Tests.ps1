$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaDevelopmentPersonnelSeed.ps1'
$baseManifestPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\development-personnel-seed.json')
$baseGaIndexPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path ([System.IO.Path]::GetTempPath()) (
    'cp6-space-development-personnel-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory)
$passed = 0

function New-SeedFixture {
    param([string]$Name, [scriptblock]$Mutation)
    $manifest = Get-Content -LiteralPath $baseManifestPath -Raw | ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function New-GaIndexFixture {
    param([string]$Name, [scriptblock]$Mutation)
    $manifest = Get-Content -LiteralPath $baseGaIndexPath -Raw | ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name-ga.json"
    $manifest | ConvertTo-Json -Depth 100 | Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-SeedCase {
    param(
        [string]$Name,
        [string]$ManifestPath,
        [string]$GaIndexPath = $baseGaIndexPath,
        [bool]$ShouldPass,
        [string]$ExpectedError
    )
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass `
            -File $validator -ManifestPath $ManifestPath `
            -GaEvidenceIndexPath $GaIndexPath 2>&1 | Out-String
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
    $normalizedOutput = $output -replace '\s', ''
    $normalizedExpectedError = $ExpectedError -replace '\s', ''
    if (!$ShouldPass -and
        $normalizedOutput -notmatch [regex]::Escape($normalizedExpectedError)) {
        throw "$Name did not report $ExpectedError.`n$output"
    }
    $script:passed++

    # The validator exit code has been asserted above. Clear the consumed native
    # process status so a successful suite cannot leak an expected failure to CI.
    $global:LASTEXITCODE = 0
}

try {
    Invoke-SeedCase -Name 'valid development personnel seed' `
        -ManifestPath $baseManifestPath -ShouldPass $true

    $formalPath = New-SeedFixture 'formal-ga' {
        param($manifest) $manifest.formalGaEligible = $true
    }
    Invoke-SeedCase -Name 'seed cannot become formal evidence' `
        -ManifestPath $formalPath -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_PERSONNEL_FORMAL_GA_FORBIDDEN'

    $missingPath = New-SeedFixture 'missing-persona' {
        param($manifest) $manifest.personas = @($manifest.personas | Select-Object -First 4)
    }
    Invoke-SeedCase -Name 'five exact personas are required' `
        -ManifestPath $missingPath -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_PERSONNEL_SET_INVALID'

    $productionPath = New-SeedFixture 'production-access' {
        param($manifest) $manifest.personas[0].productionAccess = $true
    }
    Invoke-SeedCase -Name 'development persona cannot access production' `
        -ManifestPath $productionPath -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_PERSONNEL_BOUNDARY_INVALID'

    $signerPath = New-SeedFixture 'signer-eligible' {
        param($manifest) $manifest.personas[1].formalSignoffEligible = $true
    }
    Invoke-SeedCase -Name 'development persona cannot sign GA' `
        -ManifestPath $signerPath -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_PERSONNEL_BOUNDARY_INVALID'

    $coveragePath = New-SeedFixture 'role-coverage' {
        param($manifest)
        $manifest.personas[3].assignments = @('Architecture')
    }
    Invoke-SeedCase -Name 'role test coverage remains complete' `
        -ManifestPath $coveragePath -ShouldPass $false `
        -ExpectedError 'SPACE_DEV_PERSONNEL_ROLE_COVERAGE_INVALID'

    $identityLeakPath = New-GaIndexFixture 'identity-leak' {
        param($manifest) $manifest.signers[0].name = '00001'
    }
    Invoke-SeedCase -Name 'development code cannot enter formal identity fields' `
        -ManifestPath $baseManifestPath -GaIndexPath $identityLeakPath `
        -ShouldPass $false -ExpectedError 'SPACE_DEV_PERSONNEL_FORMAL_IDENTITY_LEAK'

    $evidenceLeakPath = New-GaIndexFixture 'evidence-leak' {
        param($manifest)
        $manifest.gates[0] | Add-Member -MemberType NoteProperty `
            -Name verificationManifest `
            -Value 'docs/space/acceptance/v1.3-ga/development-personnel-seed.json' `
            -Force
    }
    Invoke-SeedCase -Name 'seed cannot be referenced as formal evidence' `
        -ManifestPath $baseManifestPath -GaIndexPath $evidenceLeakPath `
        -ShouldPass $false -ExpectedError 'SPACE_DEV_PERSONNEL_EVIDENCE_LEAK'
}
finally {
    Remove-Item -LiteralPath $tempDirectory -Recurse -Force
}

if ($global:LASTEXITCODE -ne 0) {
    throw "Test suite leaked child process exit code $global:LASTEXITCODE."
}

Write-Host "Space development personnel seed tests passed: $passed"
