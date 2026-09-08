[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'CrmPublicDisclosureSurface.psm1') -Force

# Independent fixture values keep the reviewed sets explicit in the regression.
$historical = [ordered]@{
    'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' = 'bb72e0955e7beb8a0d82529830d2c31db8c8e040452c0bcde52cb2ed651cc818'
    'docs/crm/CRM-V1-PRD.md' = 'e63ebb6dfadbfe04750a24ff3bd6d53de67bfd0d4226e872f753a25158996da7'
    'docs/crm/CRM-COMPETITIVE-ANALYSIS.md' = '4881b2a3e212d0b57446915b3a60139b71877263e201b6e8222782436f8d6d4a'
    'docs/crm/CRM-M0-READINESS.md' = '9d301a01c3028eb27d49c391c03d9cbead55267e95fbc8f2eab4ad1a7518076e'
    'docs/crm/CRM-PRODUCT-FRAMEWORK.md' = 'd6c47066e233b607780a084f66d39b484e2f578f7430908ce252c6458cf97bfb'
    'docs/crm/CRM-V1-EXECUTABLE-SPEC.md' = '5a9af3f9e47225964dd55b7f05d210bca2b1f042546041d1587fabf9b5c72216'
    'docs/crm/CRM-V1-SPEC.md' = '7d1a08c891dc2ba8b522f00aad91445ce6f04a1f3ed815cc86992c36487062bd'
    'docs/crm/README.md' = '9543bc859003469dd5773bd4992d884f356e176b04e96c3b7fda68b0fcf3089d'
    'docs/crm/approvals/cp6-crm-v1-prd.json' = 'cec71e7e5b0435f4b6740f259b0bada95649a15e56a406fd3d2de4b876a9b891'
    'docs/crm/approvals/cp6-saas-v1-public-contract.json' = '946ba40573ff98012cf9a1520099b58aad8d55ba2e63bb473f669c5b4f9361d6'
    'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json' = '76b3d5d481ad6c128f70abc7ceb770e430907fed97ca8bdd986873dc492720b3'
    'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' = 'fab7d44920dc8528940c610f6f426cbfc26e75123fbb58a3189be347d0b680dc'
}
$firstSliceChanges = [ordered]@{
    'docs/crm/CRM-V1-PRD.md' = '95c6f99519096e0799df261d68bc519ff93418c85243a6ec20fd3894ccff20b1'
    'docs/crm/CRM-V1-EXECUTABLE-SPEC.md' = '0278b62a2d5310f6a8f6ebad247ccf1a73c2f93a75b3acd3a209fa3f76475f3f'
    'docs/crm/README.md' = '2a104b73b4d420456b230e0f4e5681c7793adbbcb56b58cc13dcd533d289391c'
    'docs/crm/CRM-OIDC-FIRST-SLICE.md' = '1571de9422a0d6b0bd949770bc889ab113c84b2fbf208fe317eae70fe5c05356'
}
$passed = 0

function Copy-Surface([System.Collections.IDictionary] $Source) {
    $copy = [ordered]@{}
    foreach ($entry in $Source.GetEnumerator()) { $copy.Add($entry.Key, $entry.Value) }
    return $copy
}

function Assert-Surface([string] $Name, [System.Collections.IDictionary] $Actual, [string] $ExpectedName) {
    $result = Test-CrmPublicDisclosureSurface -ActualSha256 $Actual
    if ([string]::IsNullOrEmpty($ExpectedName)) {
        if ($null -ne $result.Name -or $result.Failures.Count -eq 0) { throw "$Name was accepted." }
    }
    elseif ($result.Name -cne $ExpectedName -or $result.Failures.Count -ne 0) {
        throw "$Name did not match $ExpectedName : $($result.Failures -join '; ')"
    }
    $script:passed++
}

$firstSlice = Copy-Surface $historical
foreach ($entry in $firstSliceChanges.GetEnumerator()) { $firstSlice[$entry.Key] = $entry.Value }
$fixtures = [ordered]@{ 'historical-20260826' = $historical; 'first-slice-20260908' = $firstSlice }
foreach ($fixture in $fixtures.GetEnumerator()) {
    Assert-Surface "complete $($fixture.Key)" $fixture.Value $fixture.Key
    foreach ($path in @($fixture.Value.Keys)) {
        $missing = Copy-Surface $fixture.Value
        $missing.Remove($path)
        Assert-Surface "missing $path in $($fixture.Key)" $missing ''
        $drift = Copy-Surface $fixture.Value
        $drift[$path] = '0000000000000000000000000000000000000000000000000000000000000000'
        Assert-Surface "arbitrary digest drift in $path in $($fixture.Key)" $drift ''
    }
    $extra = Copy-Surface $fixture.Value
    $extra['docs/crm/unknown/note.txt'] = $historical['docs/crm/README.md']
    Assert-Surface "unregistered nested path in $($fixture.Key)" $extra ''
    $caseDrift = Copy-Surface $fixture.Value
    $caseDrift.Remove('docs/crm/README.md')
    $caseDrift['docs/crm/readme.md'] = $fixture.Value['docs/crm/README.md']
    Assert-Surface "case-sensitive path drift in $($fixture.Key)" $caseDrift ''
    $caseAlias = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $fixture.Value.GetEnumerator()) { $caseAlias.Add($entry.Key, $entry.Value) }
    $caseAlias.Add('docs/crm/readme.md', $fixture.Value['docs/crm/README.md'])
    Assert-Surface "extra path differing only by case in $($fixture.Key)" $caseAlias ''
}

# All fourteen partial combinations must fail; only 0000 and 1111 are accepted.
$changedPaths = @($firstSliceChanges.Keys)
foreach ($mask in 1..14) {
    $mixed = Copy-Surface $historical
    for ($index = 0; $index -lt $changedPaths.Count; $index++) {
        if (($mask -band (1 -shl $index)) -ne 0) {
            $mixed[$changedPaths[$index]] = $firstSliceChanges[$changedPaths[$index]]
        }
    }
    Assert-Surface "partial first-slice combination $mask" $mixed ''
}

$workflowPath = Join-Path $PSScriptRoot '../.github/workflows/crm-v1-prd.yml'
$workflowText = [IO.File]::ReadAllText($workflowPath)
$pushBlock = [regex]::Match($workflowText, '(?ms)^  push:\r?\n(?<body>.*?)(?=^\S|\z)').Groups['body'].Value
foreach ($path in @('tools/CrmPublicDisclosureSurface.psm1', 'tools/Test-CrmPublicDisclosureSurface.Tests.ps1')) {
    if ($pushBlock -notmatch ('(?m)^\s+-\s+"' + [regex]::Escape($path) + '"\s*$')) {
        throw "Main push verification does not watch $path."
    }
    $passed++
}

if ($passed -ne 74) { throw "Expected 74 disclosure set/trigger checks; passed $passed." }
Write-Host "CRM public disclosure set/trigger tests passed: $passed/74"
