$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaGoldenCadCandidates.ps1'
$baseline = Join-Path $repo 'docs\space\acceptance\v1.3-ga\authorized-golden-cad-candidates-v1.0.0.json'
$tempRoot = Join-Path (Join-Path $repo 'tmp') ("cad-candidate-tests-" + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $tempRoot | Out-Null
$passed = 0

function Write-Manifest($Value, [string]$Path) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 40) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
}
function Assert-Pass([string]$Name, [string]$Path) {
    & $validator -ManifestPath $Path | Out-Null
    $script:passed++
}
function Assert-Fail([string]$Name, [scriptblock]$Mutation, [string]$Expected) {
    $manifest = Get-Content -Raw -LiteralPath $baseline | ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempRoot (($Name -replace '[^A-Za-z0-9-]','-') + '.json')
    Write-Manifest $manifest $path
    try {
        & $validator -ManifestPath $path | Out-Null
        throw "Expected failure for $Name."
    }
    catch {
        if ($_.Exception.Message -notmatch [regex]::Escape($Expected)) { throw }
    }
    $script:passed++
}

try {
    Assert-Pass 'sealed original-work candidate register' $baseline
    Assert-Fail 'sample count' { param($m) $m.dataset.samples = @($m.dataset.samples)[0..18] } 'SPACE_GA_CAD_CANDIDATE_COUNT_INVALID'
    Assert-Fail 'duplicate source hash' { param($m) $m.dataset.samples[1].sourceSha256 = $m.dataset.samples[0].sourceSha256 } 'SPACE_GA_CAD_CANDIDATE_IDENTITY_DUPLICATE'
    Assert-Fail 'invalid split' { param($m) $m.dataset.samples[0].split = 'DevelopmentSeed' } 'SPACE_GA_CAD_CANDIDATE_SPLIT_INVALID'
    Assert-Fail 'missing layout coverage' { param($m) $m.dataset.samples[4].layoutFamily = 'L1' } 'SPACE_GA_CAD_CANDIDATE_LAYOUT_COVERAGE_INVALID'
    Assert-Fail 'synthetic license' { param($m) $m.dataset.samples[0].license = 'Synthetic' } 'SPACE_GA_CAD_CANDIDATE_METADATA_INVALID'
    Assert-Fail 'holdout tuning leak' { param($m) $m.dataset.samples[3].usedForTuning = $true } 'SPACE_GA_CAD_CANDIDATE_TUNING_POLICY_INVALID'
    Assert-Fail 'source set seal' { param($m) $m.dataset.sourceSetSha256 = ('0' * 64) } 'SPACE_GA_CAD_CANDIDATE_SOURCE_SET_HASH_MISMATCH'
    "Golden CAD candidate validator tests passed: $passed/8"
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
