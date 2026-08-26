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
$fixtureDirectories = @(
    'tools',
    'docs/crm',
    'docs/client',
    'docs/devops',
    'docs/approval',
    'docs/finance',
    'docs/project-memory',
    'docs/procurement',
    'docs/pub',
    'docs/space/acceptance',
    'docs/space/requirements'
)
$passed = 0

function Invoke-Validator {
    $hadGitHubActions = Test-Path Env:GITHUB_ACTIONS
    $hadGitHubBaseRef = Test-Path Env:GITHUB_BASE_REF
    $githubActionsValue = $env:GITHUB_ACTIONS
    $githubBaseRefValue = $env:GITHUB_BASE_REF
    try {
        Remove-Item Env:GITHUB_ACTIONS, Env:GITHUB_BASE_REF -ErrorAction SilentlyContinue
        $output = & pwsh -NoProfile -File (Join-Path $fixtureRoot $validatorRelativePath) 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($hadGitHubActions) { $env:GITHUB_ACTIONS = $githubActionsValue }
        else { Remove-Item Env:GITHUB_ACTIONS -ErrorAction SilentlyContinue }
        if ($hadGitHubBaseRef) { $env:GITHUB_BASE_REF = $githubBaseRefValue }
        else { Remove-Item Env:GITHUB_BASE_REF -ErrorAction SilentlyContinue }
    }
    return [pscustomobject]@{
        ExitCode = $exitCode
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
    & git -C $fixtureRoot sparse-checkout init --cone
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize the isolated sparse fixture.' }
    & git -C $fixtureRoot sparse-checkout set @fixtureDirectories
    if ($LASTEXITCODE -ne 0) { throw 'Unable to select CRM public contract fixture files.' }
    & git -C $fixtureRoot -c core.longpaths=true -c filter.lfs.smudge= -c filter.lfs.required=false checkout --quiet --detach $sourceCommit
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

    Test-NegativeCase -Name 'aggregate private product decision ID drift' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"productDecisionId": "CP6-SAAS-V1"' -After '"productDecisionId": "ALTERED"' -ExpectedFailure 'Private product decision ID mismatch'

    Test-NegativeCase -Name 'sanitization claim drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before '"containsPaymentProviderSelection": false' -After '"containsPaymentProviderSelection": true' -ExpectedFailure 'sanitization flag must be false'

    Test-NegativeCase -Name 'Complete rollback to Candidate' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"status": "Complete"' -After '"status": "Candidate"' -ExpectedFailure 'status must be Complete'

    Test-NegativeCase -Name 'payload start marker trailing text' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '<!-- public-contract-payload:start -->' -After '<!-- public-contract-payload:start --> trailing' -ExpectedFailure 'Start marker must occupy its own line'

    Test-NegativeCase -Name 'duplicate payload start marker' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '<!-- public-contract-payload:start -->' -After "<!-- public-contract-payload:start -->`n<!-- public-contract-payload:start -->" -ExpectedFailure 'Payload markers must each occur exactly once'

    Test-NegativeCase -Name 'invalid approval aggregate JSON' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '"schemaVersion": 2,' -After '"schemaVersion": 2,,' -ExpectedFailure 'Invalid JSON'

    Test-NegativeCase -Name 'R00 private source commit drift' -RelativePath 'docs/devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md' -Before '07a7bb0b50f33b0cb70c18c14f83be77c725626d' -After '0000000000000000000000000000000000000000' -ExpectedFailure 'missing required text'

    Test-NegativeCase -Name 'M0 DEC-001 conflicting approval row' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '| DEC-001 |' -After "| DEC-001 | conflict | conflict | Approved |`n| DEC-001 |" -ExpectedFailure 'DEC-001 must occur exactly once'

    Test-NegativeCase -Name 'approval comment body digest drift' -RelativePath 'docs/crm/approvals/cp6-saas-v1-public-contract.json' -Before '68fc9f1c0c8bf525b4e1edfbf1ce11f753d2de5e1ff716ad9a32dd4c1759661b' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'comment body digest mismatch'

    Test-NegativeCase -Name 'public status marker conflict' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '<!-- public-contract-status: Complete -->' -After "<!-- public-contract-status: Complete -->`n<!-- public-contract-status: Candidate -->" -ExpectedFailure 'must occur exactly once'

    Test-NegativeCase -Name 'append-only approval history content drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json' -Before 'CP6-SAAS-V1-PUBLIC-CONTRACT-APPROVAL-20260826-001' -After 'CP6-SAAS-V1-PUBLIC-CONTRACT-APPROVAL-ALTERED' -ExpectedFailure 'Approval history record content digest mismatch'

    Test-NegativeCase -Name 'private PSP detail injection' -RelativePath 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md' -Before '本文件只公开跨仓实现' -After 'Airwallex 本文件只公开跨仓实现' -ExpectedFailure 'Private commercial detail found'

    Test-NegativeCase -Name 'M0 visible status escalation' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '- 状态：**NO-GO**' -After '- 状态：**GO**' -ExpectedFailure "field '- 状态：' must equal"

    Test-NegativeCase -Name 'R00 mirror status marker conflict' -RelativePath 'docs/devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md' -Before '<!-- public-r00-mirror-status: Complete -->' -After "<!-- public-r00-mirror-status: Complete -->`n<!-- public-r00-mirror-status: Candidate -->" -ExpectedFailure 'must occur exactly once'

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
