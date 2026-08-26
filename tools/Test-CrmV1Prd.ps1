[CmdletBinding()]
param(
    [switch] $VerifyGitHubEvidence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$failures = New-Object 'System.Collections.Generic.List[string]'

$prdPath = 'docs/crm/CRM-V1-PRD.md'
$aggregatePath = 'docs/crm/approvals/cp6-crm-v1-prd.json'
$historyPath = 'docs/crm/approvals/history/2026-08-26-cp6-crm-v1-prd-program-owner.json'
$publicAggregatePath = 'docs/crm/approvals/cp6-saas-v1-public-contract.json'
$m0Path = 'docs/crm/CRM-M0-READINESS.md'
$approvedPrdDigest = '128bda13277a50fa024c8912676d7ed9e842fd6837b7de11d6055eb8e176fc53'
$productSourceRepository = 'GTX537/CP6.CRM'
$productSourceDecisionId = 'CP6-SAAS-V1'
$productSourceMergeCommit = '07a7bb0b50f33b0cb70c18c14f83be77c725626d'
$productSourceDigest = 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b'
$publicContractDecisionId = 'CP6-SAAS-V1-PUBLIC-CONTRACT'
$publicContractDigest = '8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9'
$approvalRepository = 'GTX537/CP6'
$approvalPullRequestNumber = 33
$approvalCommentId = 5422991497L
$approvalCommentUri = 'https://github.com/GTX537/CP6/pull/33#issuecomment-5422991497'
$approvalAuthorLogin = 'GTX537'
$approvalTimestamp = '2026-08-26T09:00:05Z'
$approvalCommentBodySha256 = '9ac80797566fe3a456cf7f74ae32a476c431ff5c1bdda7fa448b9adbaa0dfa92'
$candidateCommit = 'ef29aef21ee241d0af49808ec16299d0b66395e3'
$candidateBlob = 'b91af0e69d95aa78c8151bae17b3ef02c04a5d92'
$historyRecordSha256 = 'fe832dba00cc79f5f4a50d1777fff6954cc9cd2f8f8854a693561ab82d1da85b'
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
        '1. Long-term V1 uses the Frozen SaaS four-repository boundary',
        '2. The first visible result is the Lead Pilot C split-workbench',
        '3. Frontends do not duplicate state machines',
        '4. Opportunity supports both Cp6Erp and ExternalEvidence',
        '5. Future upgrades use versioned APIs'
    )) {
        if ($comment.body.IndexOf($requiredText, [StringComparison]::Ordinal) -lt 0) {
            Fail "GitHub PRD approval comment is missing required text: $requiredText"
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
Assert-UniqueFieldLine $prdPath '- 审批状态：' '- 审批状态：**ProgramOwner approved exact payload digest**'
Assert-UniqueFieldLine $prdPath '- M0：' '- M0：**No-Go**'
Assert-NotContains $prdPath @('Public Contract Sync 仍为 Pending', 'Draft for Product Review', '<!-- crm-v1-prd-status: Candidate -->')
Assert-Contains $prdPath $approvalConclusionTexts

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
    Assert-Equal $history.recordId 'CP6-CRM-V1-PRD-APPROVAL-20260826-001' 'PRD history record ID mismatch'
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
}

$publicAggregate = Read-JsonFile $publicAggregatePath
if ($null -ne $publicAggregate) {
    Assert-Equal $publicAggregate.decisionId $publicContractDecisionId 'Merged public contract decision ID mismatch'
    Assert-Equal $publicAggregate.decisionPayloadSha256 $publicContractDigest 'Merged public contract digest mismatch'
    Assert-Equal $publicAggregate.status 'Complete' 'Merged public contract must remain Complete'
    Assert-Equal $publicAggregate.m0Status 'No-Go' 'Merged public contract must preserve M0 No-Go'
}
Assert-Contains $m0Path @('<!-- crm-m0-status: No-Go -->')

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
