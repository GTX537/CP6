[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the source commit for CRM public contract tests.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot "cp6-crm-public-contract-tests-$([Guid]::NewGuid().ToString('N'))"))
if (-not $fixtureRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($fixtureRoot) -notmatch '^cp6-crm-public-contract-tests-[0-9a-f]{32}$') {
    throw "Unsafe test fixture path: $fixtureRoot"
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$validatorRelativePath = 'tools/Test-CrmSaasPublicContract.ps1'
$passed = 0

function Invoke-Validator {
    $output = & pwsh -NoProfile -File (Join-Path $fixtureRoot $validatorRelativePath) 2>&1
    return [pscustomobject]@{
        ExitCode = $LASTEXITCODE
        Output = ($output -join "`n")
    }
}

function Test-NegativeCase(
    [string] $Name,
    [string] $RelativePath,
    [string] $Before,
    [string] $After,
    [string] $ExpectedFailure
) {
    $path = Join-Path $fixtureRoot $RelativePath
    $original = [IO.File]::ReadAllText($path, $utf8)
    if ($original.IndexOf($Before, [StringComparison]::Ordinal) -lt 0) {
        throw "$Name fixture did not contain the expected source text."
    }

    try {
        [IO.File]::WriteAllText($path, $original.Replace($Before, $After), $utf8)
        $result = Invoke-Validator
        if ($result.ExitCode -eq 0) {
            throw "$Name did not fail closed."
        }
        if ($result.Output.IndexOf($ExpectedFailure, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
            throw "$Name failed for the wrong reason. Expected '$ExpectedFailure'. Output: $($result.Output)"
        }
        $global:LASTEXITCODE = 0
        $script:passed++
        Write-Host "PASS: $Name"
    }
    finally {
        [IO.File]::WriteAllText($path, $original, $utf8)
    }
}

try {
    & git clone --quiet --shared --no-checkout $root $fixtureRoot
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the isolated Git fixture.' }
    & git -C $fixtureRoot checkout --quiet --detach $sourceCommit
    if ($LASTEXITCODE -ne 0) { throw 'Unable to check out the source commit in the isolated Git fixture.' }

    $baseline = Invoke-Validator
    if ($baseline.ExitCode -ne 0) {
        throw "Baseline verifier failed: $($baseline.Output)"
    }
    $passed++
    Write-Host 'PASS: approved baseline'

    Test-NegativeCase -Name 'public payload digest drift' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before 'CRM 是第一个商业化模块' -After 'CRM 是首个商业化模块' -ExpectedFailure 'Public contract digest mismatch'

    Test-NegativeCase -Name 'aggregate role mismatch' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"roleId": "ProgramOwner"' -After '"roleId": "Sponsor"' -ExpectedFailure 'matching ProgramOwner approval'

    Test-NegativeCase -Name 'aggregate M0 escalation' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"m0Status": "No-Go"' -After '"m0Status": "Go"' -ExpectedFailure 'must not change M0 away from No-Go'

    Test-NegativeCase -Name 'approval comment URI drift' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before 'https://github.com/GTX537/CP6/pull/8#issuecomment-5422466809' -After 'https://github.com/GTX537/CP6/pull/8#issuecomment-1' -ExpectedFailure 'comment URI mismatch'

    Test-NegativeCase -Name 'approval evidence blob drift' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '1ced3f50363059b3df3fb7b216b525fd817b0af1' -After '0000000000000000000000000000000000000000' -ExpectedFailure 'evidence blob mismatch'

    Test-NegativeCase -Name 'private source digest drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'private product digest mismatch'

    Test-NegativeCase -Name 'sanitization claim drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before '"containsPaymentProviderSelection": false' -After '"containsPaymentProviderSelection": true' -ExpectedFailure 'sanitization flag must be false'

    Test-NegativeCase -Name 'Complete rollback to Candidate' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"status": "Complete"' -After '"status": "Candidate"' -ExpectedFailure 'status must be Complete'

    Test-NegativeCase -Name 'payload start marker trailing text' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '<!-- public-contract-payload:start -->' -After '<!-- public-contract-payload:start --> trailing' -ExpectedFailure 'Start marker must occupy its own line'

    Test-NegativeCase -Name 'duplicate payload start marker' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '<!-- public-contract-payload:start -->' -After "<!-- public-contract-payload:start -->`n<!-- public-contract-payload:start -->" -ExpectedFailure 'Payload markers must each occur exactly once'

    Test-NegativeCase -Name 'invalid approval aggregate JSON' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"schemaVersion": 2,' -After '"schemaVersion": 2,,' -ExpectedFailure 'Invalid JSON'

    Test-NegativeCase -Name 'R00 private source commit drift' -RelativePath 'docs/devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md' -Before '07a7bb0b50f33b0cb70c18c14f83be77c725626d' -After '0000000000000000000000000000000000000000' -ExpectedFailure 'missing required text'

    Test-NegativeCase -Name 'M0 DEC-001 status row missing' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '| DEC-001 |' -After '| DEC-001-REMOVED |' -ExpectedFailure 'DEC-001 must remain Pending'

    Test-NegativeCase -Name 'approval comment body digest drift' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '68fc9f1c0c8bf525b4e1edfbf1ce11f753d2de5e1ff716ad9a32dd4c1759661b' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'comment body digest mismatch'

    Test-NegativeCase -Name 'approval history schema drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before '"schemaVersion": 1' -After '"schemaVersion": 99' -ExpectedFailure 'Approval history schemaVersion mismatch'

    Test-NegativeCase -Name 'append-only approval history content drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before 'CP6-SAAS-V1-PUBLIC-CONTRACT-APPROVAL-20260826-001' -After 'CP6-SAAS-V1-PUBLIC-CONTRACT-APPROVAL-ALTERED' -ExpectedFailure 'Approval history record content digest mismatch'

    Test-NegativeCase -Name 'private PSP detail injection' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '本文件只公开跨仓实现' -After 'Airwallex 本文件只公开跨仓实现' -ExpectedFailure 'Private commercial detail found'

    Test-NegativeCase -Name 'broken CRM documentation link' -RelativePath 'docs/crm/README.md' -Before './CP6-SAAS-V1-PUBLIC-CONTRACT.md' -After './MISSING-PUBLIC-CONTRACT.md' -ExpectedFailure 'Broken relative link'

    Test-NegativeCase -Name 'approval history timestamp drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before '2026-08-26T08:10:43Z' -After 'not-a-timestamp' -ExpectedFailure 'Approval history timestamp mismatch'

    if ($passed -ne 20) {
        throw "Expected 20 CRM public contract tests; passed $passed."
    }
    Write-Host "CRM SaaS public contract negative tests passed: $passed/20"
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

if ($LASTEXITCODE -ne 0) {
    throw "Consumed negative validator exit code leaked from the test suite: $LASTEXITCODE"
}
$global:LASTEXITCODE = 0
