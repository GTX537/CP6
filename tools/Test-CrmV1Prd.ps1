[CmdletBinding()]
param(
    [switch] $VerifyGitHubEvidence,
    [string] $RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
}
$utf8 = New-Object System.Text.UTF8Encoding($false)
$failures = New-Object 'System.Collections.Generic.List[string]'

$prdPath = 'docs/crm/CRM-V1-PRD.md'
$aggregatePath = 'docs/crm/approvals/cp6-crm-v1-prd.json'
$historyPath = 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner-v4.json'
$publicAggregatePath = 'docs/crm/approvals/cp6-saas-v1-public-contract.json'
$m0Path = 'docs/crm/CRM-M0-READINESS.md'
$approvedPrdDigest = '5e646cc8e394c74c35f9716216be1d12fa5f4f7210e42d8d52ab9b86f4528a3a'
$productSourceRepository = 'GTX537/CP6.CRM'
$productSourceDecisionId = 'CP6-SAAS-V1'
$productSourceMergeCommit = '07a7bb0b50f33b0cb70c18c14f83be77c725626d'
$productSourceDigest = 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b'
$publicContractDecisionId = 'CP6-SAAS-V1-PUBLIC-CONTRACT'
$publicContractMergeCommit = 'd1b5ceeb7c33de70114b934c23f71075057ec436'
$publicContractDigest = '8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9'
$approvalRepository = 'GTX537/CP6'
$approvalPullRequestNumber = 35
$approvalCommentId = 5423567483L
$approvalCommentUri = 'https://github.com/GTX537/CP6/pull/35#issuecomment-5423567483'
$approvalAuthorLogin = 'GTX537'
$approvalTimestamp = '2026-08-26T09:50:21Z'
$approvalCommentBodySha256 = '4092bc5ec3338be408292c5f240579ed036dcd1033858b4d237dc38d39608de1'
$candidateCommit = '00fa3aea66045cb2b949b691824f0fbb830cc739'
$candidateBlob = 'b6f8da119bf700340616e8a2d3cc01ceb0dd38d6'
$historyRecordSha256 = '76b3d5d481ad6c128f70abc7ceb770e430907fed97ca8bdd986873dc492720b3'
$approvalConclusionIds = @(
    'PRD-APPROVAL-01',
    'PRD-APPROVAL-02',
    'PRD-APPROVAL-03',
    'PRD-APPROVAL-04',
    'PRD-APPROVAL-05'
)
$approvalConclusionTexts = @(
    '长期 V1 使用 Frozen SaaS 四仓边界，Portal/移动/双区域/商业化属于产品 V1，但按门禁分切片交付。',
    '第一可见结果是 Lead Pilot C 分栏工作台，不以 Dashboard 或全菜单铺开替代。',
    '前端不复制状态机、权限、DataScope、PII 或 Entitlement；所有写入使用后端命令、幂等和 ETag。',
    'Opportunity 同时支持 Cp6Erp 与 ExternalEvidence，但 Won 必须有合法且不可变的成交依据。',
    '后续升级通过版本化 API、事件、字段定义、原因 code 和连接器边界扩展，不破坏稳定业务语义。'
)

function Fail([string] $Message) {
    $script:failures.Add($Message)
}

function Read-NormalizedText([string] $RelativePath) {
    $path = Join-Path $root $RelativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        Fail "Missing required file: $RelativePath"
        return $null
    }
    $text = [IO.File]::ReadAllText($path, $utf8)
    if ($text.Length -gt 0 -and $text[0] -eq [char]0xFEFF) {
        $text = $text.Substring(1)
    }
    return $text.Replace("`r`n", "`n").Replace("`r", "`n")
}

function Read-JsonFile([string] $RelativePath) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return $null }
    try {
        return $text | ConvertFrom-Json
    }
    catch {
        Fail "Invalid JSON in $RelativePath : $($_.Exception.Message)"
        return $null
    }
}

function Get-TextSha256([string] $Text) {
    return [Convert]::ToHexString(
        [Security.Cryptography.SHA256]::HashData($utf8.GetBytes($Text))
    ).ToLowerInvariant()
}

function Assert-Equal([object] $Actual, [object] $Expected, [string] $Message) {
    if ($Actual -ne $Expected) {
        Fail "$Message. Expected '$Expected'; actual '$Actual'"
    }
}

function Assert-Contains([string] $RelativePath, [string[]] $Values) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return }
    foreach ($value in $Values) {
        if ($text.IndexOf($value, [StringComparison]::Ordinal) -lt 0) {
            Fail "$RelativePath is missing required text: $value"
        }
    }
}

function Assert-NotContains([string] $RelativePath, [string[]] $Values) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return }
    foreach ($value in $Values) {
        if ($text.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            Fail "$RelativePath contains forbidden stale text: $value"
        }
    }
}

function Assert-UniqueFieldLine(
    [string] $RelativePath,
    [string] $Prefix,
    [string] $ExpectedLine
) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return }
    $matchingLines = @($text.Split("`n") | Where-Object {
        $_.StartsWith($Prefix, [StringComparison]::Ordinal)
    })
    if ($matchingLines.Count -ne 1) {
        Fail "$RelativePath field '$Prefix' must occur exactly once; found $($matchingLines.Count)"
        return
    }
    if ($matchingLines[0] -cne $ExpectedLine) {
        Fail "$RelativePath field '$Prefix' must equal '$ExpectedLine'; actual '$($matchingLines[0])'"
    }
}

function Get-PayloadDigest(
    [string] $RelativePath,
    [string] $StartMarker,
    [string] $EndMarker
) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return $null }
    $startMatches = [regex]::Matches($text, [regex]::Escape($StartMarker))
    $endMatches = [regex]::Matches($text, [regex]::Escape($EndMarker))
    if ($startMatches.Count -ne 1 -or $endMatches.Count -ne 1) {
        Fail "Payload markers must each occur exactly once in $RelativePath"
        return $null
    }
    $start = $startMatches[0].Index
    $end = $endMatches[0].Index
    if ($end -le $start) {
        Fail "Invalid payload marker order in $RelativePath"
        return $null
    }
    if (($start -gt 0 -and $text[$start - 1] -ne "`n") -or
        $start + $StartMarker.Length -ge $text.Length -or
        $text[$start + $StartMarker.Length] -ne "`n") {
        Fail "Start marker must occupy its own line in $RelativePath"
        return $null
    }
    if (($end -eq 0 -or $text[$end - 1] -ne "`n") -or
        ($end + $EndMarker.Length -lt $text.Length -and $text[$end + $EndMarker.Length] -ne "`n")) {
        Fail "End marker must occupy its own line in $RelativePath"
        return $null
    }
    $payloadStart = $text.IndexOf("`n", $start + $StartMarker.Length, [StringComparison]::Ordinal) + 1
    $payload = $text.Substring($payloadStart, $end - $payloadStart)
    if (-not $payload.EndsWith("`n", [StringComparison]::Ordinal) -or
        $payload.EndsWith("`n`n", [StringComparison]::Ordinal)) {
        Fail "Payload must end with exactly one LF in $RelativePath"
        return $null
    }
    return Get-TextSha256 $payload
}

function ConvertTo-UtcEvidenceTimestamp([object] $Value) {
    if ($null -eq $Value) { return $null }
    try {
        return [DateTimeOffset]::Parse(
            $Value.ToString(),
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal
        ).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return $Value.ToString()
    }
}

function Test-GitEvidenceObject([string] $CommitSha, [string] $BlobSha) {
    if ($CommitSha -notmatch '^[0-9a-f]{40}$') {
        Fail 'PRD candidate commit must be a full lowercase Git SHA'
        return
    }
    if ($BlobSha -notmatch '^[0-9a-f]{40}$') {
        Fail 'PRD candidate blob must be a full lowercase Git SHA-1'
        return
    }
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $actualBlob = & git -C $root rev-parse "$CommitSha`:$prdPath" 2>&1
    $gitExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($gitExitCode -ne 0) {
        Fail "PRD candidate commit does not contain $prdPath : $($actualBlob -join ' ')"
        return
    }
    Assert-Equal ($actualBlob -join '').Trim() $BlobSha 'PRD candidate blob mismatch'

    $candidateParent = & git -C $root rev-parse "$CommitSha^" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Fail "Unable to resolve PRD candidate parent: $($candidateParent -join ' ')"
    }
    else {
        Assert-Equal ($candidateParent -join '').Trim() $publicContractMergeCommit 'PRD candidate must be based directly on the merged public contract main commit'
    }

    & git -C $root merge-base --is-ancestor $CommitSha HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail 'PRD candidate commit must be an ancestor of the verified final commit'
    }
}

function Test-GitHubApprovalComment() {
    if (-not $VerifyGitHubEvidence) { return }
    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
        Fail 'GitHub evidence verification requires the gh CLI'
        return
    }
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $commentJson = & gh api "repos/$approvalRepository/issues/comments/$approvalCommentId" 2>&1
    $ghExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($ghExitCode -ne 0) {
        Fail "Unable to read GitHub PRD approval comment $approvalCommentId : $($commentJson -join ' ')"
        return
    }
    try {
        $comment = ($commentJson -join "`n") | ConvertFrom-Json
    }
    catch {
        Fail "Invalid GitHub PRD approval comment response: $($_.Exception.Message)"
        return
    }
    Assert-Equal $comment.id $approvalCommentId 'GitHub PRD approval comment ID mismatch'
    Assert-Equal $comment.html_url $approvalCommentUri 'GitHub PRD approval comment URI mismatch'
    Assert-Equal $comment.user.login $approvalAuthorLogin 'GitHub PRD approval author mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $comment.created_at) $approvalTimestamp 'GitHub PRD approval timestamp mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $comment.updated_at) $approvalTimestamp 'GitHub PRD approval comment was edited after approval'
    foreach ($requiredText in @(
        'Decision ID: CP6-CRM-V1-PRD',
        "Approved payload SHA-256: $approvedPrdDigest",
        "Candidate commit: $candidateCommit",
        "Candidate PRD blob: $candidateBlob",
        'Decision: Approved product requirements baseline',
        'Public Contract Sync: Complete',
        'M0: No-Go',
        'Sanitization: no commercial cohort counts, regions, names, exact rollout schedule, numeric Pilot UAT participant/sample/KPI/latency/defect thresholds, or private numeric commercial/adoption KPI and sample thresholds are published in the candidate public baseline.',
        'Public numeric scope: published numbers are limited to product behavior and technical acceptance/SLO contracts outside the private Pilot and adoption manifests.',
        'Clean ancestry: the candidate starts directly from merged public main and excludes every invalidated pre-merge candidate/approval commit from its ancestry.',
        'Lifecycle wording: timeless approval rules replace candidate-only status wording inside the approved payload.',
        'This approval invalidates all three pre-merge approval attempts on unmerged pull requests; they do not approve this digest.',
        'This product approval does not authorize CRM01, implementation, migration, deployment, Pilot, UAT, or production. M0 remains No-Go.'
    )) {
        if ($comment.body.IndexOf($requiredText, [StringComparison]::Ordinal) -lt 0) {
            Fail "GitHub PRD approval comment is missing required text: $requiredText"
        }
    }
    foreach ($conclusionText in $approvalConclusionTexts) {
        if ($comment.body.IndexOf($conclusionText, [StringComparison]::Ordinal) -lt 0) {
            Fail "GitHub PRD approval comment is missing approved conclusion: $conclusionText"
        }
    }
    $normalizedBody = $comment.body.Replace("`r`n", "`n").Replace("`r", "`n")
    Assert-Equal (Get-TextSha256 $normalizedBody) $approvalCommentBodySha256 'GitHub PRD approval comment body digest mismatch'
}

function Test-RelativeLinks([string[]] $RelativePaths) {
    foreach ($relativePath in $RelativePaths) {
        $text = Read-NormalizedText $relativePath
        if ($null -eq $text) { continue }
        $sourceDirectory = Split-Path (Join-Path $root $relativePath)
        foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\(([^)]+)\)')) {
            $target = $match.Groups[1].Value.Split('#')[0]
            if ([string]::IsNullOrWhiteSpace($target) -or $target -match '^(https?://|mailto:)') { continue }
            $resolved = [IO.Path]::GetFullPath((Join-Path $sourceDirectory $target))
            if (-not (Test-Path -LiteralPath $resolved)) {
                Fail "Broken relative link in $relativePath : $target"
            }
        }
    }
}

$actualPrdDigest = Get-PayloadDigest $prdPath '<!-- crm-v1-prd-payload:start -->' '<!-- crm-v1-prd-payload:end -->'
Assert-Equal $actualPrdDigest $approvedPrdDigest 'CRM V1 PRD payload digest mismatch'
Assert-UniqueFieldLine $prdPath '<!-- crm-v1-prd-status:' '<!-- crm-v1-prd-status: Approved -->'
Assert-UniqueFieldLine $prdPath '- 文档 ID：' '- 文档 ID：`CP6-CRM-V1-PRD`'
Assert-UniqueFieldLine $prdPath '- 版本：' '- 版本：`0.2`'
Assert-UniqueFieldLine $prdPath '- 状态：' '- 状态：**Approved product requirements baseline**'
Assert-UniqueFieldLine $prdPath '- 产品决策摘要：' "- 产品决策摘要：``$productSourceDigest``"
Assert-UniqueFieldLine $prdPath '- 公开工程契约：' "- 公开工程契约：``$publicContractDecisionId`` / ``Complete`` / ``$publicContractDigest``"
Assert-UniqueFieldLine $prdPath '- 审批状态：' '- 审批状态：**ProgramOwner approved fully sanitized clean payload digest**'
Assert-UniqueFieldLine $prdPath '- M0：' '- M0：**No-Go**'
Assert-NotContains $prdPath @(
    'Public Contract Sync 仍为 Pending',
    'Draft for Product Review',
    '<!-- crm-v1-prd-status: Candidate -->',
    '本版本是等待精确摘要批准的产品候选'
)
Assert-Contains $prdPath @(
    '商业 cohort 的数量、地域、名单和精确推广时间表保留在私有采用 Manifest',
    '数值阈值保留在受控私有采用 Manifest',
    '精确推广时间表保留在私有采用 Manifest',
    '各门禁的数值窗口保留在私有采用 Manifest',
    'Pilot 参与角色、组织覆盖和 cohort 构成保留在私有 Pilot Acceptance Manifest',
    '固定任务总量、每人任务量和 Website/Manual',
    'Lead Adoption：观察窗口、Eligible Lead 分母',
    'Full Journey：观察窗口、Conversion/OrderRequest 最小样本'
)
Assert-Contains $prdPath $approvalConclusionTexts
$expectedPublicDisclosureSurfaceSha256 = [ordered]@{
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
$expectedCrmDisclosurePaths = @(
    $expectedPublicDisclosureSurfaceSha256.Keys |
        Where-Object { $_.StartsWith('docs/crm/', [StringComparison]::Ordinal) } |
        Sort-Object
)
$crmRoot = Join-Path $root 'docs/crm'
$discoveredCrmDisclosurePaths = @(
    if (Test-Path -LiteralPath $crmRoot -PathType Container) {
        Get-ChildItem -LiteralPath $crmRoot -Recurse -File |
            ForEach-Object {
                [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
            } |
            Sort-Object -Unique
    }
)
foreach ($difference in @(Compare-Object $expectedCrmDisclosurePaths $discoveredCrmDisclosurePaths)) {
    if ($difference.SideIndicator -eq '=>') {
        Fail "Unregistered public CRM disclosure file: $($difference.InputObject)"
    }
    else {
        Fail "Registered public CRM disclosure file is missing: $($difference.InputObject)"
    }
}
$ancillaryDisclosureScanFiles = @(
    'README.md'
    'docs/project-memory/PROJECT_STATE.md'
    'docs/project-memory/05-Completed.md'
    'docs/project-memory/06-Todo.md'
    'docs/project-memory/CHANGELOG-AI.md'
)
$publicBaselineScanFiles = @($discoveredCrmDisclosurePaths) + $ancillaryDisclosureScanFiles
$privateCommercialPatterns = @(
    '(?i)\d+\s*家[^\n]{0,80}(中国|北美|设计伙伴)',
    '(?i)(中国|北美)[^\n]{0,40}设计伙伴',
    '(?i)(Web\s*GA[^\n]{0,80}\d+\s*(个\s*)?(工作日|自然日|日|天|周|月)|\d+\s*(个\s*)?(工作日|自然日|日|天|周|月)[^\n]{0,80}移动\s*GA)',
    '(?i)(Lead\s*(Pilot\s*UAT|Adoption)|Full\s*Journey)[^\n]*(至少|最多|不超过|>=|≥|<=|≤)?\s*\d+\s*(个\s*)?(工作日|自然日|日|天|周|月|条|名|人|个|%)',
    '(?i)\d+\s*(个\s*)?(工作日|自然日|日|天|周|月|条|名|人|个|%)[^\n]{0,80}(Lead\s*(Pilot\s*UAT|Adoption)|Full\s*Journey)',
    '(?i)(Web\s*GA|移动\s*GA|Lead\s*Adoption|Full\s*Journey)[^\n]{0,80}20\d{2}[-/年]\d{1,2}',
    '(?i)(trial[-\s]*(到|to)[-\s]*paid|signup[-\s]*(到|to)[-\s]*activation|weekly\s*active\s*org|支持首次响应|付费[^\n]{0,20}((Logo\s*)?retention|留存))[^\n]{0,60}(至少|最多|不超过|>=|≥|<=|≤)\s*\d+%',
    '(?i)(采用|adoption)[^\n]{0,60}(至少|最多|不超过|应在|within)?\s*(\d+|[一二三四五六七八九十百千万两〇零]+)\s*(个\s*)?(工作日|自然日|日|天|周|月)',
    '(?i)(\d+|[一二三四五六七八九十百千万两〇零]+)\s*(个\s*)?(工作日|自然日|日|天|周|月)[^\n]{0,60}(采用|adoption)',
    '(?i)(Pilot|试点)[^\n]{0,50}(位于|来自|地区|区域|region|中国|北美|欧洲|亚太)|(?i)(位于|来自|地区|区域|region|中国|北美|欧洲|亚太)[^\n]{0,50}(Pilot|试点)',
    '(?i)(公司|集团|Inc\.?|LLC|Ltd\.?|科技|包装)[^\n]{0,50}(Pilot|试点)|(?i)(Pilot|试点)[^\n]{0,50}(公司|集团|Inc\.?|LLC|Ltd\.?|科技|包装)',
    '(?i)(Eligible\s*Lead|Conversion|OrderRequest)[^\n]{0,50}(至少|最多|不超过|>=|≥|<=|≤)\s*\d+|(?i)(至少|最多|不超过|>=|≥|<=|≤)\s*\d+[^\n]{0,50}(Eligible\s*Lead|Conversion|OrderRequest)',
    '(?i)(adoption|采用)[^\n]{0,60}(within|at\s*least|no\s*more\s*than)?\s*(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|twenty|thirty|forty|fifty|sixty|ninety)\s*(business\s*)?(day|days|week|weeks|month|months)',
    '(?i)(\d+|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|fifteen|twenty|thirty|forty|fifty|sixty|ninety)\s*(business\s*)?(day|days|week|weeks|month|months)[^\n]{0,60}(adoption|采用)',
    '(?im)^\s*(?:[-*]\s*)?Pilot\s*[:：]\s*(?!sha256\b)\S|Pilot\s+customer\s+list\s*[:：]|\b[A-Z][A-Za-z0-9.-]{2,}\s+(enters|joins)\s+Pilot',
    '(?i)(Eligible\s*Lead|Conversion|OrderRequest)[^\n]{0,40}(sample|denominator|minimum|target|[:=])\s*\d+',
    '(?i)(signup[-_\s]*(to|到)[-_\s]*activation|trial[-_\s]*(to|到)[-_\s]*paid|weekly[-_\s]*active[-_\s]*org)[^\n]{0,40}(target|minimum|denominator|[:=]|至少|>=|≥)\s*\d+%?',
    '(?i)\|\s*(Observation(?:\s*Gate)?|Pilot\s*UAT)\s*\|[^\n]*\d+[^\n]*(人|名|部门|任务|Lead|事件|工作日|自然日|秒|%)',
    '(?i)(Pilot|试点)[^\n]{0,100}(至少|最多|不超过|>=|≥|<=|≤)\s*\d+\s*(个\s*)?(版本化)?(任务|样本|名|人|部门)',
    '(?i)(采用|adoption)[^\n]{0,80}(至少|最多|不超过)?\s*(\d+|[一二三四五六七八九十百千万两〇零]+)\s*(个\s*)?[^\n]{0,20}(整改|版本|窗口)'
)
foreach ($file in $publicBaselineScanFiles) {
    $text = Read-NormalizedText $file
    if ($null -eq $text) { continue }
    foreach ($pattern in $privateCommercialPatterns) {
        if ($text -match $pattern) {
            Fail "Private commercial cohort, rollout schedule, or numeric KPI detail found in $file"
        }
    }
}

foreach ($entry in $expectedPublicDisclosureSurfaceSha256.GetEnumerator()) {
    $text = Read-NormalizedText $entry.Key
    if ($null -eq $text) { continue }
    Assert-Equal (Get-TextSha256 $text) $entry.Value "Public disclosure surface digest mismatch: $($entry.Key)"
}

$prdText = Read-NormalizedText $prdPath
if ($null -ne $prdText) {
    foreach ($section in @(
        @('### 14.3 Lead Pilot UAT', '### 14.4 上市与采用'),
        @('### 14.4 上市与采用', '## 15. 交付切片与依赖')
    )) {
        $sectionStart = $prdText.IndexOf("$($section[0])`n", [StringComparison]::Ordinal)
        $sectionEnd = $prdText.IndexOf("`n$($section[1])", $sectionStart + 1, [StringComparison]::Ordinal)
        if ($sectionStart -lt 0 -or $sectionEnd -lt 0) {
            Fail "Unable to isolate private-manifest section: $($section[0])"
            continue
        }
        $bodyStart = $sectionStart + $section[0].Length + 1
        $sectionBody = $prdText.Substring($bodyStart, $sectionEnd - $bodyStart).Replace('CP6', 'CP')
        if ($sectionBody -match '[0-9０-９一二三四五六七八九十百千万两〇零]') {
            Fail "Public private-manifest section contains a numeric threshold or schedule: $($section[0])"
        }
    }
}

$expectedControlledDisclosureLineHashes = @(
    'cedb9baee5aac60bbd7167804b364aa960619b9c5530160766f4c8cc1b446b97',
    'b49367ad5e1ab251746fa8aa220040d49876c7fb3a0bdb446182569d0dfb644f',
    '8715ce9b73cf71d9f6bff8956abae2dc7e606aa7d768667e733f6996a8b48874',
    'cc444db32312f5f85b333dfc34e183567e931150aa789ce6eaf861f0b42f3076',
    '3320e5ab4de4aed8899003d5b1296da75d6019467546f632747679954ef921e9',
    '2d5ccefa1fe20c1059639827e5039237cc25ed40b5c513f4f61e684480da6c4e',
    '315b43111bfa9368aadc5ecb0bbee7bfb2605cba5dc9805749c9a7852a29ae4f',
    '875b99c37691542e0dd7b18fc73c182d05f509066f9e96541113352c32b49f2e',
    'afdfc5c0446e2865839c0903302b97e2416a5cc03d9716f043b60cffad0e55be',
    '972728c75de992f377ed6fa33cb9d812e38e74393de9a8ca394da78aaab32120',
    'cbd380000f9f29c87ab2e55b1333d4e0558da262421ffd9fcd7d939dde66c812'
)
$actualControlledDisclosureLineHashes = New-Object 'System.Collections.Generic.List[string]'
foreach ($file in $ancillaryDisclosureScanFiles) {
    $text = Read-NormalizedText $file
    if ($null -eq $text) { continue }
    foreach ($line in $text.Split("`n")) {
        if ($line -match '(?i)cohort|设计伙伴|signup[-\s]*(到|to)[-\s]*activation|trial[-\s]*(到|to)[-\s]*paid|weekly\s*active\s*org|付费留存') {
            $actualControlledDisclosureLineHashes.Add((Get-TextSha256 "$file`n$line"))
        }
    }
}
if (@(Compare-Object $expectedControlledDisclosureLineHashes @($actualControlledDisclosureLineHashes)).Count -ne 0) {
    Fail 'Public controlled-disclosure lines changed or a possible private cohort/KPI disclosure was introduced'
}

$aggregate = Read-JsonFile $aggregatePath
if ($null -ne $aggregate) {
    Assert-Equal $aggregate.schemaVersion 1 'PRD aggregate schema version mismatch'
    Assert-Equal $aggregate.decisionId 'CP6-CRM-V1-PRD' 'PRD aggregate decision ID mismatch'
    Assert-Equal $aggregate.version '0.2' 'PRD aggregate version mismatch'
    Assert-Equal $aggregate.status 'Approved' 'PRD aggregate status must be Approved'
    Assert-Equal $aggregate.documentPath $prdPath 'PRD aggregate document path mismatch'
    Assert-Equal $aggregate.payloadSha256 $approvedPrdDigest 'PRD aggregate payload digest mismatch'
    Assert-Equal $aggregate.productSource.repository $productSourceRepository 'PRD product source repository mismatch'
    Assert-Equal $aggregate.productSource.decisionId $productSourceDecisionId 'PRD product source decision ID mismatch'
    Assert-Equal $aggregate.productSource.mergeCommit $productSourceMergeCommit 'PRD product source merge commit mismatch'
    Assert-Equal $aggregate.productSource.payloadSha256 $productSourceDigest 'PRD product source digest mismatch'
    Assert-Equal $aggregate.productSource.status 'Frozen' 'PRD product source status mismatch'
    Assert-Equal $aggregate.publicContract.decisionId $publicContractDecisionId 'PRD public contract decision ID mismatch'
    Assert-Equal $aggregate.publicContract.payloadSha256 $publicContractDigest 'PRD public contract digest mismatch'
    Assert-Equal $aggregate.publicContract.status 'Complete' 'PRD public contract status must be Complete'
    Assert-Equal $aggregate.requiredApproverRole 'ProgramOwner' 'PRD required approver role mismatch'
    Assert-Equal $aggregate.approval.roleId 'ProgramOwner' 'PRD approval role mismatch'
    Assert-Equal $aggregate.approval.decision 'Approved product requirements baseline' 'PRD approval decision mismatch'
    Assert-Equal $aggregate.approval.approverLogin $approvalAuthorLogin 'PRD approver login mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $aggregate.approval.approvedAt) $approvalTimestamp 'PRD aggregate approval timestamp mismatch'
    Assert-Equal $aggregate.approvalEvidence.type 'GitHubPullRequestComment' 'PRD approval evidence type mismatch'
    Assert-Equal $aggregate.approvalEvidence.repository $approvalRepository 'PRD approval evidence repository mismatch'
    Assert-Equal $aggregate.approvalEvidence.pullRequestNumber $approvalPullRequestNumber 'PRD approval evidence pull request mismatch'
    Assert-Equal $aggregate.approvalEvidence.commentId $approvalCommentId 'PRD approval evidence comment ID mismatch'
    Assert-Equal $aggregate.approvalEvidence.commentUri $approvalCommentUri 'PRD approval evidence comment URI mismatch'
    Assert-Equal $aggregate.approvalEvidence.commentBodySha256 $approvalCommentBodySha256 'PRD approval evidence comment body digest mismatch'
    Assert-Equal $aggregate.approvalEvidence.candidateCommit $candidateCommit 'PRD approval evidence commit mismatch'
    Assert-Equal $aggregate.approvalEvidence.candidateBlob $candidateBlob 'PRD approval evidence blob mismatch'
    Assert-Equal $aggregate.approvalHistory.path $historyPath 'PRD approval history path mismatch'
    Assert-Equal $aggregate.approvalHistory.recordSha256 $historyRecordSha256 'PRD approval history digest mismatch'
    Assert-Equal $aggregate.invalidatedPreMergeApprovals 3 'PRD aggregate invalidated pre-merge approval count mismatch'
    Assert-Equal $aggregate.invalidatedEvidenceLocation 'Unmerged pull request audit trail; excluded from main ancestry and public product baseline' 'PRD invalidated evidence location mismatch'
    Assert-Equal $aggregate.m0Status 'No-Go' 'PRD approval must not change M0 away from No-Go'
    $aggregateConclusionIds = @($aggregate.approvedConclusions)
    if ($aggregateConclusionIds.Count -ne $approvalConclusionIds.Count -or
        @(Compare-Object $approvalConclusionIds $aggregateConclusionIds).Count -ne 0) {
        Fail 'PRD aggregate approved conclusion IDs mismatch'
    }
}

$historyText = Read-NormalizedText $historyPath
if ($null -ne $historyText) {
    Assert-Equal (Get-TextSha256 $historyText) $historyRecordSha256 'PRD approval history record content digest mismatch'
}
$history = Read-JsonFile $historyPath
if ($null -ne $history) {
    Assert-Equal $history.recordId 'CP6-CRM-V1-PRD-APPROVAL-20260826-004' 'PRD history record ID mismatch'
    Assert-Equal $history.decisionId 'CP6-CRM-V1-PRD' 'PRD history decision ID mismatch'
    Assert-Equal $history.status 'Approved' 'PRD history status mismatch'
    Assert-Equal $history.payloadSha256 $approvedPrdDigest 'PRD history payload digest mismatch'
    Assert-Equal $history.approval.roleId 'ProgramOwner' 'PRD history approval role mismatch'
    Assert-Equal $history.approval.decision 'Approved product requirements baseline' 'PRD history approval decision mismatch'
    Assert-Equal $history.approval.approverLogin $approvalAuthorLogin 'PRD history approver login mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $history.approval.approvedAt) $approvalTimestamp 'PRD history approval timestamp mismatch'
    Assert-Equal $history.approvalEvidence.repository $approvalRepository 'PRD history evidence repository mismatch'
    Assert-Equal $history.approvalEvidence.pullRequestNumber $approvalPullRequestNumber 'PRD history evidence pull request mismatch'
    Assert-Equal $history.approvalEvidence.commentId $approvalCommentId 'PRD history evidence comment ID mismatch'
    Assert-Equal $history.approvalEvidence.commentUri $approvalCommentUri 'PRD history evidence comment URI mismatch'
    Assert-Equal $history.approvalEvidence.commentBodySha256 $approvalCommentBodySha256 'PRD history evidence comment body digest mismatch'
    Assert-Equal $history.approvalEvidence.candidateCommit $candidateCommit 'PRD history candidate commit mismatch'
    Assert-Equal $history.approvalEvidence.candidateBlob $candidateBlob 'PRD history candidate blob mismatch'
    Assert-Equal $history.publicContract.mergeCommit $publicContractMergeCommit 'PRD history public contract merge commit mismatch'
    Assert-Equal $history.publicContract.status 'Complete' 'PRD history public contract must be Complete'
    Assert-Equal $history.m0Status 'No-Go' 'PRD history must preserve M0 No-Go'
    $historyConclusions = @($history.approvedConclusions)
    if ($historyConclusions.Count -ne $approvalConclusionIds.Count) {
        Fail "PRD history must contain exactly five approved conclusions; found $($historyConclusions.Count)"
    }
    else {
        for ($index = 0; $index -lt $approvalConclusionIds.Count; $index++) {
            Assert-Equal $historyConclusions[$index].id $approvalConclusionIds[$index] "PRD history conclusion ID mismatch at index $index"
            Assert-Equal $historyConclusions[$index].text $approvalConclusionTexts[$index] "PRD history conclusion text mismatch at index $index"
        }
    }
    foreach ($propertyName in @(
        'containsCommercialCohortCounts',
        'containsCommercialCohortRegions',
        'containsCommercialCohortNames',
        'containsExactCommercialRolloutSchedule',
        'containsNumericPilotUatThresholds',
        'containsPrivateNumericCommercialOrAdoptionThresholds'
    )) {
        if ($history.sanitization.$propertyName -ne $false) {
            Fail "PRD history sanitization flag must be false: $propertyName"
        }
    }
    Assert-Equal $history.sanitization.containsPublicProductOrTechnicalAcceptanceNumbers $true 'PRD history must preserve the public technical-number scope distinction'
    Assert-Equal $history.cleanAncestry.baseCommit $publicContractMergeCommit 'PRD history clean ancestry base mismatch'
    Assert-Equal $history.cleanAncestry.invalidatedPreMergeApprovalCount 3 'PRD history invalidated approval count mismatch'
    Assert-Equal $history.cleanAncestry.invalidatedCommitsExcluded $true 'PRD history must assert invalidated commits are excluded'
    Assert-Equal $history.cleanAncestry.invalidatedObjectReferencesPublished $false 'PRD history must not publish invalidated object references'
}

$publicAggregate = Read-JsonFile $publicAggregatePath
if ($null -ne $publicAggregate) {
    Assert-Equal $publicAggregate.decisionId $publicContractDecisionId 'Merged public contract decision ID mismatch'
    Assert-Equal $publicAggregate.decisionPayloadSha256 $publicContractDigest 'Merged public contract digest mismatch'
    Assert-Equal $publicAggregate.status 'Complete' 'Merged public contract must remain Complete'
    Assert-Equal $publicAggregate.m0Status 'No-Go' 'Merged public contract must preserve M0 No-Go'
}
Assert-UniqueFieldLine $m0Path '<!-- crm-m0-status:' '<!-- crm-m0-status: No-Go -->'
Assert-UniqueFieldLine $m0Path '- 状态：' '- 状态：**NO-GO**'

if ($env:GITHUB_ACTIONS -eq 'true' -and -not $VerifyGitHubEvidence) {
    Fail 'GitHub Actions must run PRD verification with -VerifyGitHubEvidence'
}
Test-GitEvidenceObject $candidateCommit $candidateBlob
Test-GitHubApprovalComment

$secretScanFiles = @(
    'README.md',
    'docs/crm/README.md',
    $prdPath,
    'docs/crm/CRM-COMPETITIVE-ANALYSIS.md',
    $aggregatePath,
    $historyPath,
    'docs/project-memory/PROJECT_STATE.md',
    'docs/project-memory/05-Completed.md',
    'docs/project-memory/06-Todo.md',
    'docs/project-memory/CHANGELOG-AI.md'
)
$secretPatterns = @(
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)(client_secret|access_token|refresh_token|password)\s*[:=]\s*["''][^"'']{8,}',
    '(?i)AccountKey=[A-Za-z0-9+/=]{20,}',
    '(?i)gh[pousr]_[A-Za-z0-9]{20,}'
)
foreach ($file in $secretScanFiles) {
    $text = Read-NormalizedText $file
    if ($null -eq $text) { continue }
    foreach ($pattern in $secretPatterns) {
        if ($text -match $pattern) { Fail "Possible secret found in $file" }
    }
}

Test-RelativeLinks @(
    'README.md',
    'docs/crm/README.md',
    $prdPath,
    'docs/crm/CRM-COMPETITIVE-ANALYSIS.md'
)

$gitDiffChecks = @(
    @('diff', '--check'),
    @('diff', '--cached', '--check')
)
if ($env:GITHUB_ACTIONS -eq 'true') {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_BASE_REF)) {
        $gitDiffChecks += ,@('diff', '--check', "origin/$($env:GITHUB_BASE_REF)...HEAD")
    }
    else {
        $gitDiffChecks += ,@('diff', '--check', 'HEAD^', 'HEAD')
    }
}
foreach ($arguments in $gitDiffChecks) {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $diffCheck = & git -C $root @arguments 2>&1
    $gitExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($gitExitCode -ne 0) {
        Fail "git $($arguments -join ' ') failed: $($diffCheck -join ' ')"
    }
}

if ($failures.Count -gt 0) {
    foreach ($failure in $failures) {
        Write-Host "ERROR: $failure" -ForegroundColor Red
    }
    throw "CRM V1 PRD verification failed with $($failures.Count) error(s)."
}

Write-Host "CRM V1 PRD payload SHA-256: $actualPrdDigest"
Write-Host 'CRM V1 PRD approval verification passed.'
