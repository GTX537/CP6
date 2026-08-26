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

function Test-NewFileNegativeCase(
    [string] $Name,
    [string] $RelativePath,
    [string] $Content,
    [string] $ExpectedFailure
) {
    $path = Join-Path $fixtureRoot $RelativePath
    if (Test-Path -LiteralPath $path) {
        throw "$Name fixture path already exists."
    }
    try {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path)) | Out-Null
        [IO.File]::WriteAllText($path, $Content, $utf8)
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
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
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
    Test-NegativeCase -Name 'approval comment URI drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'https://github.com/GTX537/CP6/pull/35#issuecomment-5423567483' -After 'https://github.com/GTX537/CP6/pull/35#issuecomment-1' -ExpectedFailure 'comment URI mismatch'
    Test-NegativeCase -Name 'candidate blob drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'b6f8da119bf700340616e8a2d3cc01ceb0dd38d6' -After '0000000000000000000000000000000000000000' -ExpectedFailure 'evidence blob mismatch'
    Test-NegativeCase -Name 'aggregate public contract rollback' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"status": "Complete"' -After '"status": "Candidate"' -ExpectedFailure 'public contract status must be Complete'
    Test-NegativeCase -Name 'product source digest drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'product source digest mismatch'
    Test-NegativeCase -Name 'aggregate history digest drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '76b3d5d481ad6c128f70abc7ceb770e430907fed97ca8bdd986873dc492720b3' -After '0000000000000000000000000000000000000000000000000000000000000000' -ExpectedFailure 'approval history digest mismatch'
    Test-NegativeCase -Name 'append-only current history content drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json' -Before 'CP6-CRM-V1-PRD-APPROVAL-20260826-004' -After 'CP6-CRM-V1-PRD-APPROVAL-ALTERED' -ExpectedFailure 'history record content digest mismatch'
    Test-NegativeCase -Name 'PRD status marker conflict' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '<!-- crm-v1-prd-status: Approved -->' -After "<!-- crm-v1-prd-status: Approved -->`n<!-- crm-v1-prd-status: Candidate -->" -ExpectedFailure 'must occur exactly once'
    Test-NegativeCase -Name 'PRD visible status rollback' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '- 状态：**Approved product requirements baseline**' -After '- 状态：**Candidate for Product Approval**' -ExpectedFailure "field '- 状态：' must equal"
    Test-NegativeCase -Name 'duplicate PRD payload start marker' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '<!-- crm-v1-prd-payload:start -->' -After "<!-- crm-v1-prd-payload:start -->`n<!-- crm-v1-prd-payload:start -->" -ExpectedFailure 'Payload markers must each occur exactly once'
    Test-NegativeCase -Name 'PRD payload end marker trailing text' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '<!-- crm-v1-prd-payload:end -->' -After '<!-- crm-v1-prd-payload:end --> trailing' -ExpectedFailure 'End marker must occupy its own line'
    Test-NegativeCase -Name 'invalid PRD aggregate JSON' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"schemaVersion": 1,' -After '"schemaVersion": 1,,' -ExpectedFailure 'Invalid JSON'
    Test-NegativeCase -Name 'history conclusion ID drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json' -Before 'PRD-APPROVAL-05' -After 'PRD-APPROVAL-ALTERED' -ExpectedFailure 'history record content digest mismatch'
    Test-NegativeCase -Name 'PRD public contract header rollback' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '`CP6-SAAS-V1-PUBLIC-CONTRACT` / `Complete` / `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`' -After '`CP6-SAAS-V1-PUBLIC-CONTRACT` / `Pending` / `8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9`' -ExpectedFailure "field '- 公开工程契约：' must equal"
    Test-NegativeCase -Name 'PRD secret injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After '# CP6 CRM 文档入口 access_token="1234567890abcdef"' -ExpectedFailure 'Possible secret found'
    Test-NegativeCase -Name 'merged M0 escalation' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '<!-- crm-m0-status: No-Go -->' -After '<!-- crm-m0-status: Go -->' -ExpectedFailure "field '<!-- crm-m0-status:' must equal"
    Test-NegativeCase -Name 'merged M0 conflicting status marker' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '<!-- crm-m0-status: No-Go -->' -After "<!-- crm-m0-status: No-Go -->`n<!-- crm-m0-status: Go -->" -ExpectedFailure 'must occur exactly once'
    Test-NegativeCase -Name 'private commercial region injection outside PRD' -RelativePath 'docs/crm/CRM-COMPETITIVE-ANALYSIS.md' -Before '受控设计伙伴对每组织价格' -After '受控中国设计伙伴对每组织价格' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'exact mobile rollout injection in PRD' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before '在 Web GA 后通过独立移动 GA 门禁' -After '在 Web GA 后 31 天内通过移动 GA 门禁' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'exact rollout injection in project memory' -RelativePath 'docs/project-memory/06-Todo.md' -Before '设计伙伴、Web GA、移动 GA、Lead Adoption' -After '设计伙伴、Web GA、17 天内移动 GA、Lead Adoption' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'private numeric KPI injection' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before 'GA 采用评估覆盖 signup-to-activation、trial-to-paid、付费留存' -After 'GA 采用评估覆盖 signup-to-activation、trial-to-paid 至少 23%、付费留存' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'numeric Pilot UAT threshold injection' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before 'Pilot 参与角色、组织覆盖和 cohort 构成保留在私有 Pilot Acceptance Manifest' -After 'Pilot 参与角色覆盖至少 9 名销售，其余 cohort 构成保留在私有 Pilot Acceptance Manifest' -ExpectedFailure 'Public private-manifest section contains a numeric threshold or schedule'
    Test-NegativeCase -Name 'Chinese-numeric adoption schedule injection' -RelativePath 'docs/crm/CRM-V1-PRD.md' -Before 'Lead Adoption：观察窗口、Eligible Lead 分母' -After 'Lead Adoption：至少十个工作日，Eligible Lead 分母' -ExpectedFailure 'Public private-manifest section contains a numeric threshold or schedule'
    Test-NegativeCase -Name 'design-partner name injection' -RelativePath 'docs/crm/CRM-COMPETITIVE-ANALYSIS.md' -Before '受控设计伙伴对每组织价格' -After '示例甲公司作为受控设计伙伴对每组织价格' -ExpectedFailure 'Public disclosure surface digest mismatch'
    Test-NegativeCase -Name 'signup KPI injection outside PRD' -RelativePath 'docs/crm/CRM-COMPETITIVE-ANALYSIS.md' -Before '本文不为 Portal 写入具体价格。' -After '本文不为 Portal 写入具体价格。signup-to-activation 至少 35%。' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'cohort name injection outside PRD' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`n商业 cohort 名单：示例甲公司" -ExpectedFailure 'Public disclosure surface digest mismatch'
    Test-NegativeCase -Name 'Pilot cohort region injection outside PRD' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nPilot cohort 位于欧洲" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'generic adoption schedule injection outside PRD' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`n采用应在 30 日内完成" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'Chinese adoption schedule injection outside PRD' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nLead Adoption 至少十个工作日" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'company Pilot injection outside PRD' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`n示例甲公司进入 Pilot" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'Full Journey sample injection outside PRD' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nFull Journey 至少 11 个 Conversion" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'English numeric adoption schedule injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nLead Adoption within 30 days" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'English word adoption schedule injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nAdoption within thirty days" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'free-form Pilot region injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nPilot: EMEA" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'Pilot customer list injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nPilot customer list: Acme" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'named company enters Pilot injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nAcme enters Pilot" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'Conversion sample injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nConversion sample: 20" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'Eligible Lead denominator injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nEligible Lead denominator = 200" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'OrderRequest minimum sample injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nOrderRequest minimum sample 10" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'weekly active org target injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nweekly-active-org target: 42%" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'trial to paid target injection' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`ntrial_to_paid: 23%" -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'unknown public disclosure wording drift' -RelativePath 'docs/crm/README.md' -Before '# CP6 CRM 文档入口' -After "# CP6 CRM 文档入口`nOpaque disclosure wording" -ExpectedFailure 'Public disclosure surface digest mismatch'
    Test-NewFileNegativeCase -Name 'unregistered public CRM document injection' -RelativePath 'docs/crm/CRM-PILOT.md' -Content "# Pilot`nOpaque disclosure wording" -ExpectedFailure 'Unregistered public CRM disclosure file'
    Test-NegativeCase -Name 'legacy M0 Pilot sample regression' -RelativePath 'docs/crm/CRM-M0-READINESS.md' -Before '版本化任务类别' -After '至少 73 个版本化任务' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'legacy Observation table regression' -RelativePath 'docs/crm/CRM-PRODUCT-FRAMEWORK.md' -Before '| Observation Gate | 脱敏定性观察、定量事件基线、角色与部门类别 |' -After '| Observation Gate | 4 人/19 条 Lead 定性观察；7 名用户、3 个部门、91 个事件、12 个工作日脱敏定量基线 |' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'legacy adoption remediation count regression' -RelativePath 'docs/crm/CRM-V1-EXECUTABLE-SPEC.md' -Before '整改窗口和重新立项/终止条件由私有 Adoption Manifest 冻结' -After '采用失败后最多七个固定版本整改窗口' -ExpectedFailure 'Private commercial cohort, rollout schedule, or numeric KPI detail'
    Test-NegativeCase -Name 'current history sanitization drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json' -Before '"containsCommercialCohortCounts": false' -After '"containsCommercialCohortCounts": true' -ExpectedFailure 'history record content digest mismatch'
    Test-NegativeCase -Name 'public numeric scope history drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json' -Before '"containsPublicProductOrTechnicalAcceptanceNumbers": true' -After '"containsPublicProductOrTechnicalAcceptanceNumbers": false' -ExpectedFailure 'history record content digest mismatch'
    Test-NegativeCase -Name 'aggregate invalidated approval count drift' -RelativePath 'docs/crm/approvals/cp6-crm-v1-prd.json' -Before '"invalidatedPreMergeApprovals": 3' -After '"invalidatedPreMergeApprovals": 2' -ExpectedFailure 'invalidated pre-merge approval count mismatch'
    Test-NegativeCase -Name 'history clean ancestry claim drift' -RelativePath 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json' -Before '"invalidatedCommitsExcluded": true' -After '"invalidatedCommitsExcluded": false' -ExpectedFailure 'history record content digest mismatch'

    if ($passed -ne 54) {
        throw "Expected 54 CRM V1 PRD tests; passed $passed."
    }
    Write-Host "CRM V1 PRD negative tests passed: $passed/54"
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
