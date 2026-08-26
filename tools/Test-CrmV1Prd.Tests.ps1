[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$sourceCommit = (& git -C $root rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $sourceCommit -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve the source commit for CRM V1 PRD tests.'
}

$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot "cp6-crm-v1-prd-tests-$([Guid]::NewGuid().ToString('N'))"))
if (-not $fixtureRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($fixtureRoot) -notmatch '^cp6-crm-v1-prd-tests-[0-9a-f]{32}$') {
    throw "Unsafe PRD test fixture path: $fixtureRoot"
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
$validatorRelativePath = 'tools/Test-CrmV1Prd.ps1'
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
    if ($LASTEXITCODE -ne 0) { throw 'Unable to create the isolated PRD Git fixture.' }
    & git -C $fixtureRoot sparse-checkout init --cone
    if ($LASTEXITCODE -ne 0) { throw 'Unable to initialize the isolated PRD sparse fixture.' }
    & git -C $fixtureRoot sparse-checkout set @fixtureDirectories
    if ($LASTEXITCODE -ne 0) { throw 'Unable to select CRM V1 PRD fixture directories.' }
    & git -C $fixtureRoot -c core.longpaths=true -c filter.lfs.smudge= -c filter.lfs.required=false checkout --quiet --detach $sourceCommit
    if ($LASTEXITCODE -ne 0) { throw 'Unable to check out the source commit in the isolated PRD fixture.' }

    $baseline = Invoke-Validator
    if ($baseline.ExitCode -ne 0) {
        throw "Baseline PRD verifier failed: $($baseline.Output)"
    }
    $passed++
    Write-Host 'PASS: approved PRD baseline'

    Test-NegativeCase -Name 'PRD payload digest drift' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before 'CP6 CRM 是面向包装及相邻离散制造企业的售前工作台' -After 'CP6 CRM 是面向制造企业的售前工作台' -ExpectedFailure 'PRD payload digest mismatch'
    Test-NegativeCase -Name 'aggregate Approved rollback' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"status": "Approved"' -After '"status": "Candidate"' -ExpectedFailure 'status must be Approved'
    Test-NegativeCase -Name 'aggregate M0 escalation' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"m0Status": "No-Go"' -After '"m0Status": "Go"' -ExpectedFailure 'must not change M0 away from No-Go'
    Test-NegativeCase -Name 'aggregate approval role mismatch' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"roleId": "ProgramOwner"' -After '"roleId": "Sponsor"' -ExpectedFailure 'approval role mismatch'
    Test-NegativeCase -Name 'approval comment URI drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'https://github.com/GTX537/CP6/pull/33#issuecomment-5422991497' -After 'https://github.com/GTX537/CP6/pull/33#issuecomment-1' -ExpectedFailure 'comment URI mismatch'
    Test-NegativeCase -Name 'candidate blob drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'b91af0e69d95aa78c8151bae17b3ef02c04a5d92' -After '0000000000000000000000000000000000000000' -ExpectedFailure 'evidence blob mismatch'
    Test-NegativeCase -Name 'aggregate public contract rollback' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"status": "Complete"' -After '"status": "Candidate"' -ExpectedFailure 'public contract status must be Complete'
    Test-NegativeCase -Name 'product source digest drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'product source digest mismatch'
    Test-NegativeCase -Name 'aggregate history digest drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'fe832dba00cc79f5f4a50d1777fff6954cc9cd2f8f8854a693561ab82d1da85b' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'approval history digest mismatch'
    Test-NegativeCase -Name 'append-only history content drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner.json' -Before 'CP6-CRM-V1-PRD-APPROVAL-20260826-001' -After 'CP6-CRM-V1-PRD-APPROVAL-ALTERED' -ExpectedFailure 'history record content digest mismatch'
    Test-NegativeCase -Name 'PRD status marker conflict' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '<!-- crm-v1-prd-status: Approved -->' -After "<!-- crm-v1-prd-status: Approved -->`n<!-- crm-v1-prd-status: Candidate -->" -ExpectedFailure 'must occur exactly once'
    Test-NegativeCase -Name 'PRD visible status rollback' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '- 状态：**Approved product requirements baseline**' -After '- 状态：**Candidate for Product Approval**' -ExpectedFailure "field '- 状态：' must equal"
    Test-NegativeCase -Name 'duplicate PRD payload start marker' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '<!-- crm-v1-prd-payload:start -->' -After "<!-- crm-v1-prd-payload:start -->`n<!-- crm-v1-prd-payload:start -->" -ExpectedFailure 'Payload markers must each occur exactly once'
    Test-NegativeCase -Name 'PRD payload end marker trailing text' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '<!-- crm-v1-prd-payload:end -->' -After '<!-- crm-v1-prd-payload:end --> trailing' -ExpectedFailure 'End marker must occupy its own line'
    Test-NegativeCase -Name 'invalid PRD aggregate JSON' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"schemaVersion": 1,' -After '"schemaVersion": 1,,' -ExpectedFailure 'Invalid JSON'
    Test-NegativeCase -Name 'history conclusion ID drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner.json' -Before 'PRD-APPROVAL-05' -After 'PRD-APPROVAL-ALTERED' -ExpectedFailure 'history record content digest mismatch'
    Test-NegativeCase -Name 'PRD public contract header rollback' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '`CP6-SAAS-V1-PUBLIC-CONTRACT` / `Complete` / `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`' -After '`CP6-SAAS-V1-PUBLIC-CONTRACT` / `Pending` / `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`' -ExpectedFailure "field '- 公开工程契约：' must equal"
    Test-NegativeCase -Name 'PRD secret injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After '# CP6 CRM 文档入口 access_token="1234567890abcdef"' -ExpectedFailure 'Possible secret found'
    Test-NegativeCase -Name 'merged M0 escalation' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '<!-- crm-m0-status: No-Go -->' -After '<!-- crm-m0-status: Go -->' -ExpectedFailure 'missing required text'

    if ($passed -ne 20) {
        throw "Expected 20 CRM V1 PRD tests; passed $passed."
    }
    Write-Host "CRM V1 PRD negative tests passed: $passed/20"
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

if ($LASTEXITCODE -ne 0) {
    throw "Consumed negative PRD validator exit code leaked from the test suite: $LASTEXITCODE"
}
$global:LASTEXITCODE = 0
