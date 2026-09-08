Set-StrictMode -Version Latest

# These are complete, reviewed public disclosure sets, not per-file alternatives.
# The first-slice registration records the user's 2026-09-08 implementation/docs
# authorization. It does not amend the historical approved payload or authorize
# production. Keep this module beside the trusted validator; never load it from
# the candidate RepositoryRoot or accept approved hashes from a caller.
# Reviewed source: b92e0a23f55e83ba92b6755b5381ef7d0b1e1e27. This preparatory
# change leaves docs/crm at the historical baseline; publication is a later PR.
$historicalSha256 = [ordered]@{
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
$firstSliceSha256 = [ordered]@{}
foreach ($entry in $historicalSha256.GetEnumerator()) { $firstSliceSha256.Add($entry.Key, $entry.Value) }
$firstSliceSha256['docs/crm/CRM-V1-PRD.md'] = '95c6f99519096e0799df261d68bc519ff93418c85243a6ec20fd3894ccff20b1'
$firstSliceSha256['docs/crm/CRM-V1-EXECUTABLE-SPEC.md'] = '0278b62a2d5310f6a8f6ebad247ccf1a73c2f93a75b3acd3a209fa3f76475f3f'
$firstSliceSha256['docs/crm/README.md'] = '2a104b73b4d420456b230e0f4e5681c7793adbbcb56b58cc13dcd533d289391c'
$firstSliceSha256['docs/crm/CRM-OIDC-FIRST-SLICE.md'] = '1571de9422a0d6b0bd949770bc889ab113c84b2fbf208fe317eae70fe5c05356'
$registeredSurfaces = [ordered]@{
    'historical-20260826' = $historicalSha256
    'first-slice-20260908' = $firstSliceSha256
}

function Test-CrmPublicDisclosureSurface {
    [CmdletBinding()]
    param([Parameter(Mandatory)] [System.Collections.IDictionary] $ActualSha256)

    $actual = [System.Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($entry in $ActualSha256.GetEnumerator()) {
        if ($entry.Key -isnot [string] -or $entry.Value -isnot [string]) {
            throw 'Public disclosure paths and SHA-256 values must be strings.'
        }
        $actual.Add($entry.Key, $entry.Value)
    }
    foreach ($surface in $registeredSurfaces.GetEnumerator()) {
        $expected = $surface.Value
        if ($actual.Count -ne $expected.Count) { continue }
        $matches = $true
        foreach ($entry in $expected.GetEnumerator()) {
            if (-not $actual.ContainsKey($entry.Key) -or
                -not [string]::Equals($actual[$entry.Key], $entry.Value, [StringComparison]::Ordinal)) {
                $matches = $false
                break
            }
        }
        if ($matches) { return [pscustomobject]@{ Name = $surface.Key; Failures = @() } }
    }

    $errors = [System.Collections.Generic.List[string]]::new()
    $errors.Add('Public disclosure surface digest mismatch: no complete registered document set matches; mixed versions are not permitted.')
    # Pick one set only for diagnostics. This never authorizes per-file mixtures.
    $diagnosticSurface = if ($actual.ContainsKey('docs/crm/CRM-OIDC-FIRST-SLICE.md')) { $firstSliceSha256 } else { $historicalSha256 }
    foreach ($path in $actual.Keys) {
        if (-not (@($diagnosticSurface.Keys) -ccontains $path)) {
            $errors.Add("Unregistered public CRM disclosure file: $path")
        }
    }
    foreach ($entry in $diagnosticSurface.GetEnumerator()) {
        if (-not $actual.ContainsKey($entry.Key)) {
            $errors.Add("Registered public CRM disclosure file is missing: $($entry.Key)")
        }
        elseif (-not [string]::Equals($actual[$entry.Key], $entry.Value, [StringComparison]::Ordinal)) {
            $errors.Add("Public disclosure surface digest mismatch: $($entry.Key)")
        }
    }
    return [pscustomobject]@{ Name = $null; Failures = @($errors.ToArray()) }
}

Export-ModuleMember -Function Test-CrmPublicDisclosureSurface
