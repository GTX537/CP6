param(
    [string]$ManifestPath,
    [string]$GaEvidenceIndexPath
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ManifestPath)) {
    $ManifestPath = Join-Path $repo (
        'docs\space\acceptance\v1.3-ga\development-personnel-seed.json')
}
if ([string]::IsNullOrWhiteSpace($GaEvidenceIndexPath)) {
    $GaEvidenceIndexPath = Join-Path $repo (
        'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$gaIndex = Get-Content -LiteralPath $GaEvidenceIndexPath -Raw | ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()

function Add-SeedError {
    param([Parameter(Mandatory)][string]$Message)
    $errors.Add($Message)
}

$expectedCodes = @('00001', '00002', '00003', '00004', '00005')
$allowedAssignments = @(
    'Product', 'Backend', 'DevOps', 'QA', 'WMS', 'Frontend3D',
    'Architecture', 'Security')
$personas = @($manifest.personas)
$codes = @($personas | ForEach-Object { [string]$_.personCode })

if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.evidenceClass -ne 'DevelopmentSeed') {
    Add-SeedError 'SPACE_DEV_PERSONNEL_IDENTITY_INVALID: seed identity is invalid.'
}
if ($manifest.singleDeveloperMode -ne $true -or $manifest.operatorCount -ne 1) {
    Add-SeedError 'SPACE_DEV_PERSONNEL_OPERATOR_INVALID: exactly one real operator is declared.'
}
if ($manifest.formalGaEligible -ne $false) {
    Add-SeedError 'SPACE_DEV_PERSONNEL_FORMAL_GA_FORBIDDEN: development seed cannot be GA eligible.'
}
if ($personas.Count -ne 5 -or
    @($codes | Sort-Object -Unique).Count -ne 5 -or
    @($expectedCodes | Where-Object { $_ -notin $codes }).Count -gt 0 -or
    @($codes | Where-Object { $_ -notin $expectedCodes }).Count -gt 0) {
    Add-SeedError 'SPACE_DEV_PERSONNEL_SET_INVALID: exact codes 00001 through 00005 are required.'
}

foreach ($persona in $personas) {
    $code = [string]$persona.personCode
    if ([string]$persona.displayName -ne $code) {
        Add-SeedError "SPACE_DEV_PERSONNEL_DISPLAY_INVALID: $code display name must equal its code."
    }
    if ($persona.simulated -ne $true -or
        $persona.productionAccess -ne $false -or
        $persona.formalSignoffEligible -ne $false) {
        Add-SeedError "SPACE_DEV_PERSONNEL_BOUNDARY_INVALID: $code must remain simulated, non-production and non-signing."
    }
    foreach ($assignment in @($persona.assignments)) {
        if ([string]$assignment -notin $allowedAssignments) {
            Add-SeedError "SPACE_DEV_PERSONNEL_ROLE_INVALID: $code has unsupported assignment $assignment."
        }
    }
}

$allAssignments = @($personas | ForEach-Object { @($_.assignments) })
$requiredShared = @('Product', 'WMS', 'Architecture', 'Security', 'DevOps')
if (@($personas | Where-Object { 'Backend' -in @($_.assignments) }).Count -lt 2 -or
    @($personas | Where-Object { 'Frontend3D' -in @($_.assignments) }).Count -lt 2 -or
    @($personas | Where-Object { 'QA' -in @($_.assignments) }).Count -lt 1 -or
    @($requiredShared | Where-Object { $_ -notin $allAssignments }).Count -gt 0) {
    Add-SeedError 'SPACE_DEV_PERSONNEL_ROLE_COVERAGE_INVALID: development test role coverage is incomplete.'
}

$formalNames = @(
    @($gaIndex.signers | ForEach-Object { [string]$_.name })
    @($gaIndex.externalInputs | ForEach-Object { [string]$_.ownerName })
    @($gaIndex.gates | ForEach-Object { [string]$_.ownerName })
    @($gaIndex.signers | ForEach-Object {
        @($_.evidence) | ForEach-Object { [string]$_.acceptedBy }
    })
    @($gaIndex.externalInputs | ForEach-Object {
        @($_.evidence) | ForEach-Object { [string]$_.acceptedBy }
    })
    @($gaIndex.gates | ForEach-Object {
        @($_.acceptedEvidence) | ForEach-Object { [string]$_.acceptedBy }
    })) | Where-Object { ![string]::IsNullOrWhiteSpace($_) }
$formalReferences = @(
    @($gaIndex.externalInputs | ForEach-Object { [string]$_.verificationManifest })
    @($gaIndex.gates | ForEach-Object { [string]$_.verificationManifest })) |
    Where-Object { ![string]::IsNullOrWhiteSpace($_) }

if (@($formalNames | Where-Object { $_ -in $expectedCodes }).Count -gt 0) {
    Add-SeedError 'SPACE_DEV_PERSONNEL_FORMAL_IDENTITY_LEAK: a development code appears in a formal person field.'
}
if (@($formalReferences | Where-Object {
    $_ -match '(?i)development-personnel-seed'
}).Count -gt 0) {
    Add-SeedError 'SPACE_DEV_PERSONNEL_EVIDENCE_LEAK: development seed is referenced as formal GA evidence.'
}

if ($errors.Count -gt 0) {
    $errors | ForEach-Object { [Console]::Error.WriteLine($_) }
    exit 1
}

Write-Host (
    'Space development personnel seed is valid: 5 simulated personas, ' +
    '1 real operator, formal GA eligibility=false.')
