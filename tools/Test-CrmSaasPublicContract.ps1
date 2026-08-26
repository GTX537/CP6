[CmdletBinding()]
param(
    [switch] $VerifyGitHubEvidence
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$failures = New-Object 'System.Collections.Generic.List[string]'

$publicPath = 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md'
$approvalPath = 'docs/crm/approvals/cp6-saas-v1-public-contract.json'
$approvalHistoryPath = 'docs/crm/approvals/history/2026-08-26-cp6-saas-v1-public-contract-program-owner.json'
$m0Path = 'docs/crm/CRM-M0-READINESS.md'
$r00Path = 'docs/devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md'
$approvedPublicDigest = '8950c63c9ed37d01a8c39c4e7df9267e69596057340eb48fbd668049eeca06d9'
$sourceRepository = 'GTX537/CP6.CRM'
$sourceMergeCommit = '07a7bb0b50f33b0cb70c18c14f83be77c725626d'
$sourceProductDigest = 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b'
$sourceR00Digest = '64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03'
$approvalRepository = 'GTX537/CP6'
$approvalPullRequestNumber = 8
$approvalCommentId = 5422466809L
$approvalCommentUri = 'https://github.com/GTX537/CP6/pull/8#issuecomment-5422466809'
$supersededCommentUri = 'https://github.com/GTX537/CP6/pull/8#issuecomment-5422419376'
$approvalAuthorLogin = 'GTX537'
$approvalEvidenceCommit = 'b0c0edff2415984c4875d818e6a4db42b8fbdc0d'
$approvalEvidenceBlob = '1ced3f50363059b3df3fb7b216b525fd817b0af1'
$approvalTimestamp = '2026-08-26T08:10:43Z'
$approvalCommentBodySha256 = '68fc9f1c0c8bf525b4e1edfbf1ce11f753d2de5e1ff716ad9a32dd4c1759661b'
$approvalHistoryRecordSha256 = 'fab7d44920dc8528940c610f6f426cbfc26e75123fbb58a3189be347d0b680dc'

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
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($utf8.GetBytes($Text))
    }
    finally {
        $sha.Dispose()
    }
    return ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
}

function Assert-Equal([object] $Actual, [object] $Expected, [string] $Message) {
    if ($Actual -ne $Expected) {
        Fail "$Message. Expected '$Expected'; actual '$Actual'"
    }
}

function ConvertTo-UtcEvidenceTimestamp([object] $Value) {
    if ($null -eq $Value) { return $null }
    try {
        $timestamp = [DateTimeOffset]::Parse(
            $Value.ToString(),
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::AssumeUniversal
        )
        return $timestamp.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ', [Globalization.CultureInfo]::InvariantCulture)
    }
    catch {
        return $Value.ToString()
    }
}

function Test-GitEvidenceObject([string] $CommitSha, [string] $BlobSha) {
    if ($CommitSha -notmatch '^[0-9a-f]{40}$') {
        Fail 'Approval evidence commit must be a full lowercase Git SHA'
        return
    }
    if ($BlobSha -notmatch '^[0-9a-f]{40}$') {
        Fail 'Approval evidence blob must be a full lowercase Git SHA-1'
        return
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $actualBlob = & git -C $root rev-parse "$CommitSha`:$publicPath" 2>&1
    $gitExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($gitExitCode -ne 0) {
        Fail "Approval evidence commit does not contain $publicPath : $($actualBlob -join ' ')"
        return
    }
    if (($actualBlob | Select-Object -First 1).Trim() -ne $BlobSha) {
        Fail "Approval evidence blob mismatch. Expected $BlobSha; actual $actualBlob"
    }
}

function Test-GitHubApprovalComment() {
    if (-not $VerifyGitHubEvidence) { return }
    $ghCommand = Get-Command gh -ErrorAction SilentlyContinue
    if ($null -eq $ghCommand) {
        Fail 'GitHub evidence verification requires the gh CLI'
        return
    }

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $commentJson = & gh api "repos/GTX537/CP6/issues/comments/$approvalCommentId" 2>&1
    $ghExitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousErrorActionPreference
    if ($ghExitCode -ne 0) {
        Fail "Unable to read GitHub approval comment $approvalCommentId : $($commentJson -join ' ')"
        return
    }

    try {
        $comment = ($commentJson -join "`n") | ConvertFrom-Json
    }
    catch {
        Fail "Invalid GitHub approval comment response: $($_.Exception.Message)"
        return
    }

    Assert-Equal $comment.id $approvalCommentId 'GitHub approval comment ID mismatch'
    Assert-Equal $comment.html_url $approvalCommentUri 'GitHub approval comment URI mismatch'
    Assert-Equal $comment.user.login $approvalAuthorLogin 'GitHub approval comment author mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $comment.created_at) $approvalTimestamp 'GitHub approval comment timestamp mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $comment.updated_at) $approvalTimestamp 'GitHub approval comment was edited after approval'
    foreach ($requiredText in @(
        'Decision ID: CP6-SAAS-V1-PUBLIC-CONTRACT',
        "supersedes $supersededCommentUri",
        'Role ID: ProgramOwner',
        'Decision: Approved',
        "Decision payload SHA-256: $approvedPublicDigest",
        "Evidence commit: $approvalEvidenceCommit",
        "Evidence blob: $approvalEvidenceBlob",
        'M0 status after this approval: No-Go'
    )) {
        if ($comment.body.IndexOf($requiredText, [StringComparison]::Ordinal) -lt 0) {
            Fail "GitHub approval comment is missing required text: $requiredText"
        }
    }
    $normalizedBody = $comment.body.Replace("`r`n", "`n").Replace("`r", "`n")
    Assert-Equal (Get-TextSha256 $normalizedBody) $approvalCommentBodySha256 'GitHub approval comment body digest mismatch'
}

function Get-PayloadDigest(
    [string] $RelativePath,
    [string] $StartMarker,
    [string] $EndMarker
) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return $null }

    $start = $text.IndexOf($StartMarker, [StringComparison]::Ordinal)
    $end = $text.IndexOf($EndMarker, [StringComparison]::Ordinal)
    if ($start -lt 0 -or $end -le $start) {
        Fail "Invalid payload markers in $RelativePath"
        return $null
    }
    if ($text.IndexOf($StartMarker, $start + $StartMarker.Length, [StringComparison]::Ordinal) -ge 0 -or
        $text.IndexOf($EndMarker, $end + $EndMarker.Length, [StringComparison]::Ordinal) -ge 0) {
        Fail "Payload markers must each occur exactly once in $RelativePath"
        return $null
    }

    if (($start -gt 0 -and $text[$start - 1] -ne "`n") -or
        ($start + $StartMarker.Length -ge $text.Length) -or
        $text[$start + $StartMarker.Length] -ne "`n") {
        Fail "Start marker must occupy its own line in $RelativePath"
        return $null
    }
    if (($end -eq 0 -or $text[$end - 1] -ne "`n") -or
        ($end + $EndMarker.Length -lt $text.Length -and $text[$end + $EndMarker.Length] -ne "`n")) {
        Fail "End marker must occupy its own line in $RelativePath"
        return $null
    }

    $payloadStart = $text.IndexOf("`n", $start + $StartMarker.Length, [StringComparison]::Ordinal)
    if ($payloadStart -lt 0) {
        Fail "Start marker must occupy its own line in $RelativePath"
        return $null
    }
    $payload = $text.Substring($payloadStart + 1, $end - $payloadStart - 1)
    if (-not $payload.EndsWith("`n", [StringComparison]::Ordinal) -or
        $payload.EndsWith("`n`n", [StringComparison]::Ordinal)) {
        Fail "Payload must end with exactly one LF in $RelativePath"
        return $null
    }

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $hash = $sha.ComputeHash($utf8.GetBytes($payload))
    }
    finally {
        $sha.Dispose()
    }
    return ([BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
}

function Assert-Contains([string] $RelativePath, [string[]] $Values) {
    $text = Read-NormalizedText $RelativePath
    if ($null -eq $text) { return }
    foreach ($value in $Values) {
        if ($text.IndexOf($value, [StringComparison]::OrdinalIgnoreCase) -lt 0) {
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

function Test-RelativeLinks([string[]] $RelativePaths) {
    $linkPattern = [regex]'\[[^\]]+\]\((?<target>[^)]+)\)'
    foreach ($relativePath in $RelativePaths) {
        $path = Join-Path $root $relativePath
        $text = Read-NormalizedText $relativePath
        if ($null -eq $text) { continue }
        foreach ($match in $linkPattern.Matches($text)) {
            $target = $match.Groups['target'].Value.Trim().Trim('<', '>')
            if ($target -match '^(https?:|mailto:|#)') { continue }
            $pathPart = ($target -split '#', 2)[0]
            if ([string]::IsNullOrWhiteSpace($pathPart)) { continue }
            $decoded = [Uri]::UnescapeDataString($pathPart).Replace('/', [IO.Path]::DirectorySeparatorChar)
            $resolved = Join-Path (Split-Path $path -Parent) $decoded
            if (-not (Test-Path -LiteralPath $resolved)) {
                Fail "Broken relative link in $relativePath : $target"
            }
        }
    }
}

$publicDigest = Get-PayloadDigest $publicPath '<!-- public-contract-payload:start -->' '<!-- public-contract-payload:end -->'
$r00Digest = Get-PayloadDigest $r00Path '<!-- release-decision-payload:start -->' '<!-- release-decision-payload:end -->'

if ($publicDigest -ne $approvedPublicDigest) {
    Fail "Public contract digest mismatch. Expected approved digest $approvedPublicDigest; actual $publicDigest"
}
if ($r00Digest -ne $sourceR00Digest) {
    Fail "Public R00 mirror digest mismatch. Expected $sourceR00Digest; actual $r00Digest"
}

$publicText = Read-NormalizedText $publicPath
if ($null -ne $publicText -and $null -ne $publicDigest) {
    $digestCount = ([regex]::Matches($publicText, [regex]::Escape($publicDigest), 'IgnoreCase')).Count
    if ($digestCount -ne 2) {
        Fail "$publicPath must declare the public digest exactly twice; found $digestCount"
    }
    if ($publicText -match 'decisionPayloadSha256[^\n]*PENDING') {
        Fail "$publicPath still has a PENDING digest"
    }
}

Assert-Contains $publicPath @(
    $sourceRepository,
    $sourceMergeCommit,
    $sourceProductDigest,
    $sourceR00Digest,
    'CP6.Platform',
    'CP6.CRM',
    'CP6.Portal',
    'SingleProgramOwner',
    'CandidateLocator',
    'M0',
    'No-Go'
)
Assert-Contains $r00Path @(
    '<!-- public-r00-mirror-status: Complete -->',
    '镜像状态：**Complete**',
    '私有源状态：**Accepted**',
    $sourceMergeCommit,
    $sourceR00Digest,
    'Partial / Gap',
    'first-writer-wins',
    'bucket + key + VersionId + SHA-256',
    'CandidateLocator',
    'Lead Adoption',
    'P09/P10',
    'P09/P10 implementation：Pending',
    '公开同步：Complete',
    'M0：No-Go'
)
Assert-Contains $m0Path @(
    'approved_human_role_ids == { ProgramOwner }',
    'DEC-001',
    'DEC-009',
    'public_contract_sync == Complete',
    'M2/M5/M6/CRM12',
    'NO-GO'
)
Assert-NotContains $m0Path @(
    'approved_human_role_ids == { Sponsor',
    'all_M0_hard_gate_named_roles',
    'M0 GO'
)
$m0Text = Read-NormalizedText $m0Path
if ($null -ne $m0Text) {
    foreach ($approvedDecisionId in @('DEC-000', 'DEC-002')) {
        if ($m0Text -notmatch "(?m)^\|\s*$approvedDecisionId\s*\|[^\n]*\|\s*Approved\s*\|\s*$") {
            Fail "$approvedDecisionId must remain Approved in $m0Path"
        }
    }
    foreach ($pendingDecisionId in @('DEC-001', 'DEC-003', 'DEC-004', 'DEC-005', 'DEC-006', 'DEC-007', 'DEC-008', 'DEC-009')) {
        if ($m0Text -notmatch "(?m)^\|\s*$pendingDecisionId\s*\|[^\n]*\|\s*Pending\s*\|\s*$") {
            Fail "$pendingDecisionId must remain Pending in $m0Path"
        }
    }
}
Assert-Contains 'docs/crm/CRM-PRODUCT-FRAMEWORK.md' @('Historical planning baseline', 'CP6-SAAS-V1-PUBLIC-CONTRACT.md')
Assert-Contains 'docs/crm/CRM-V1-EXECUTABLE-SPEC.md' @('Historical planning baseline', 'CP6-SAAS-V1-PUBLIC-CONTRACT.md')
Assert-Contains 'docs/crm/CRM-V1-SPEC.md' @('Historical Foundation baseline', 'CP6-SAAS-V1-PUBLIC-CONTRACT.md')
Assert-Contains 'docs/crm/README.md' @('CP6-SAAS-V1-PUBLIC-CONTRACT.md', $sourceProductDigest, 'M0', 'No-Go')
Assert-Contains 'docs/devops/README.md' @('ADR-CRM-R00', $sourceR00Digest, 'P09/P10')
Assert-Contains 'docs/project-memory/PROJECT_STATE.md' @('CP6-SAAS-V1-PUBLIC-CONTRACT', $sourceProductDigest, $sourceR00Digest)
Assert-Contains 'docs/project-memory/05-Completed.md' @($sourceMergeCommit, $sourceProductDigest)
Assert-Contains 'docs/project-memory/06-Todo.md' @('Complete', 'ProgramOwner', 'DEC-001')
Assert-Contains 'docs/project-memory/CHANGELOG-AI.md' @('CP6-SAAS-V1-PUBLIC-CONTRACT', $sourceProductDigest)

$approval = Read-JsonFile $approvalPath
$approvalHistory = Read-JsonFile $approvalHistoryPath

if ($null -ne $approval) {
    if ($approval.schemaVersion -ne 2) { Fail 'Complete approval aggregate schemaVersion must be 2' }
    if ($approval.decisionId -ne 'CP6-SAAS-V1-PUBLIC-CONTRACT') { Fail 'Approval decisionId mismatch' }
    if ($approval.decisionPath -ne $publicPath) { Fail 'Approval decisionPath mismatch' }
    if ($approval.decisionPayloadSha256 -ne $publicDigest) { Fail 'Approval public digest mismatch' }
    if ($approval.approvalModel -ne 'SingleProgramOwner') { Fail 'Approval model must be SingleProgramOwner' }
    Assert-Equal $approval.approvalHistoryRecordSha256 $approvalHistoryRecordSha256 'Approval history record digest declaration mismatch'
    if ($approval.m0Status -ne 'No-Go') { Fail 'Public sync must not change M0 away from No-Go' }
    if ($approval.sourcePrivate.repository -ne $sourceRepository) { Fail 'Private source repository mismatch' }
    if ($approval.sourcePrivate.mergeCommitSha -ne $sourceMergeCommit) { Fail 'Private source merge commit mismatch' }
    if ($approval.sourcePrivate.productDecisionPayloadSha256 -ne $sourceProductDigest) { Fail 'Private product digest mismatch' }
    if ($approval.sourcePrivate.productStatus -ne 'Frozen') { Fail 'Private product status must be Frozen' }
    if ($approval.sourcePrivate.r00DecisionPayloadSha256 -ne $sourceR00Digest) { Fail 'Private R00 digest mismatch' }
    if ($approval.sourcePrivate.r00Status -ne 'Accepted') { Fail 'Private R00 status must be Accepted' }
    if ($approval.status -ne 'Complete') { Fail 'Approved public synchronization status must be Complete' }

    $requiredRoles = @($approval.requiredForComplete)
    if ($requiredRoles.Count -ne 1 -or $requiredRoles[0] -ne 'ProgramOwner') {
        Fail 'requiredForComplete must contain exactly ProgramOwner'
    }

    if ($approval.status -eq 'Complete') {
        Assert-Contains $publicPath @('<!-- public-contract-status: Complete -->')
        Assert-Contains $r00Path @('<!-- public-r00-mirror-status: Complete -->')
        if (@($approval.approvals).Count -ne 1) { Fail 'Complete must contain exactly one approval record' }
        $validApprovals = @($approval.approvals | Where-Object {
            $_.recordId -eq 'CP6-SAAS-V1-PUBLIC-CONTRACT-APPROVAL-20260826-001' -and
            $_.recordPath -eq $approvalHistoryPath -and
            $_.roleId -eq 'ProgramOwner' -and
            $_.decision -eq 'Approved' -and
            $_.decisionPayloadSha256 -eq $publicDigest -and
            (ConvertTo-UtcEvidenceTimestamp $_.approvedAtUtc) -eq $approvalTimestamp
        })
        if ($validApprovals.Count -ne 1) { Fail 'Complete requires exactly one matching ProgramOwner approval' }
        if ($null -eq $approval.approvalEvidence) { Fail 'Complete requires immutable approvalEvidence' }
        else {
            Assert-Equal $approval.approvalEvidence.type 'GitHubPullRequestComment' 'Approval evidence type mismatch'
            Assert-Equal $approval.approvalEvidence.repository $approvalRepository 'Approval evidence repository mismatch'
            Assert-Equal $approval.approvalEvidence.pullRequestNumber $approvalPullRequestNumber 'Approval evidence pull request mismatch'
            Assert-Equal $approval.approvalEvidence.commentId $approvalCommentId 'Approval evidence comment ID mismatch'
            Assert-Equal $approval.approvalEvidence.commentUri $approvalCommentUri 'Approval evidence comment URI mismatch'
            Assert-Equal $approval.approvalEvidence.supersedesCommentUri $supersededCommentUri 'Approval evidence superseded comment URI mismatch'
            Assert-Equal $approval.approvalEvidence.authorLogin $approvalAuthorLogin 'Approval evidence author mismatch'
            Assert-Equal $approval.approvalEvidence.evidenceCommitSha $approvalEvidenceCommit 'Approval evidence commit mismatch'
            Assert-Equal $approval.approvalEvidence.evidenceBlobSha $approvalEvidenceBlob 'Approval evidence blob mismatch'
            Assert-Equal $approval.approvalEvidence.commentBodySha256 $approvalCommentBodySha256 'Approval evidence comment body digest mismatch'
        }
    }
}

if ($null -ne $approvalHistory) {
    Assert-Equal (Get-TextSha256 (Read-NormalizedText $approvalHistoryPath)) $approvalHistoryRecordSha256 'Approval history record content digest mismatch'
    Assert-Equal $approvalHistory.schemaVersion 1 'Approval history schemaVersion mismatch'
    Assert-Equal $approvalHistory.recordId 'CP6-SAAS-V1-PUBLIC-CONTRACT-APPROVAL-20260826-001' 'Approval history recordId mismatch'
    Assert-Equal $approvalHistory.decisionId 'CP6-SAAS-V1-PUBLIC-CONTRACT' 'Approval history decisionId mismatch'
    Assert-Equal $approvalHistory.decisionPath $publicPath 'Approval history decisionPath mismatch'
    Assert-Equal $approvalHistory.roleId 'ProgramOwner' 'Approval history roleId mismatch'
    Assert-Equal $approvalHistory.decision 'Approved' 'Approval history decision mismatch'
    Assert-Equal $approvalHistory.decisionPayloadSha256 $publicDigest 'Approval history public digest mismatch'
    Assert-Equal (ConvertTo-UtcEvidenceTimestamp $approvalHistory.approvedAtUtc) $approvalTimestamp 'Approval history timestamp mismatch'
    Assert-Equal $approvalHistory.m0Status 'No-Go' 'Approval history must preserve M0 No-Go'

    Assert-Equal $approvalHistory.approvalEvidence.type 'GitHubPullRequestComment' 'History evidence type mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.repository $approvalRepository 'History evidence repository mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.pullRequestNumber $approvalPullRequestNumber 'History evidence pull request mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.commentId $approvalCommentId 'History evidence comment ID mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.commentUri $approvalCommentUri 'History evidence comment URI mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.supersedesCommentUri $supersededCommentUri 'History evidence superseded comment URI mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.authorLogin $approvalAuthorLogin 'History evidence author mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.evidenceCommitSha $approvalEvidenceCommit 'History evidence commit mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.evidenceBlobSha $approvalEvidenceBlob 'History evidence blob mismatch'
    Assert-Equal $approvalHistory.approvalEvidence.commentBodySha256 $approvalCommentBodySha256 'History evidence comment body digest mismatch'

    Assert-Equal $approvalHistory.sourcePrivate.repository $sourceRepository 'History private source repository mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.mergeCommitSha $sourceMergeCommit 'History private source merge commit mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.productDecisionId 'CP6-SAAS-V1' 'History private product decision ID mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.productDecisionPayloadSha256 $sourceProductDigest 'History private product digest mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.productStatus 'Frozen' 'History private product status mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.r00DecisionId 'CP6-SAAS-R00' 'History private R00 decision ID mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.r00DecisionPayloadSha256 $sourceR00Digest 'History private R00 digest mismatch'
    Assert-Equal $approvalHistory.sourcePrivate.r00Status 'Accepted' 'History private R00 status mismatch'

    foreach ($propertyName in @(
        'containsCommercialTerms',
        'containsPaymentProviderSelection',
        'containsPilotCohort',
        'containsPrivatePersonalApproverIdentity'
    )) {
        if ($approvalHistory.sanitization.$propertyName -ne $false) {
            Fail "Approval history sanitization flag must be false: $propertyName"
        }
    }
}

if ($env:GITHUB_ACTIONS -eq 'true' -and -not $VerifyGitHubEvidence) {
    Fail 'GitHub Actions must run with -VerifyGitHubEvidence'
}
Test-GitEvidenceObject $approvalEvidenceCommit $approvalEvidenceBlob
Test-GitHubApprovalComment

Assert-Contains $m0Path @('<!-- crm-m0-status: No-Go -->')

$sanitizedFiles = @(
    'README.md',
    'docs/crm/README.md',
    $publicPath,
    $approvalPath,
    $approvalHistoryPath,
    $m0Path,
    'docs/devops/README.md',
    $r00Path,
    'docs/devops/adr/README.md',
    'docs/project-memory/PROJECT_STATE.md',
    'docs/project-memory/05-Completed.md',
    'docs/project-memory/06-Todo.md',
    'docs/project-memory/CHANGELOG-AI.md'
)
$forbiddenCommercialPatterns = @(
    '(?i)USD\s+[0-9]',
    '(?i)CNY\s+[0-9]',
    '(?i)Stripe|PayPal|Airwallex|Alipay|WeChat\s*Pay',
    '\u652F\u4ED8\u5B9D',
    '\u5FAE\u4FE1\u652F\u4ED8'
)
$secretPatterns = @(
    '-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----',
    '(?i)(client_secret|access_token|refresh_token|password)\s*[:=]\s*["''][^"'']{8,}',
    '(?i)AccountKey=[A-Za-z0-9+/=]{20,}',
    '(?i)gh[pousr]_[A-Za-z0-9]{20,}'
)
foreach ($file in $sanitizedFiles) {
    $text = Read-NormalizedText $file
    if ($null -eq $text) { continue }
    foreach ($pattern in $forbiddenCommercialPatterns) {
        if ($text -match $pattern) { Fail "Private commercial detail found in $file" }
    }
    foreach ($pattern in $secretPatterns) {
        if ($text -match $pattern) { Fail "Possible secret found in $file" }
    }
}

Test-RelativeLinks @(
    'README.md',
    'docs/crm/README.md',
    $publicPath,
    $m0Path,
    $r00Path,
    'docs/devops/adr/README.md',
    'docs/devops/README.md',
    'docs/crm/CRM-PRODUCT-FRAMEWORK.md',
    'docs/crm/CRM-V1-EXECUTABLE-SPEC.md',
    'docs/crm/CRM-V1-SPEC.md'
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
    throw "CRM SaaS public contract verification failed with $($failures.Count) error(s)."
}

Write-Host "Public contract SHA-256: $publicDigest"
Write-Host "Private R00 mirror SHA-256: $r00Digest"
Write-Host 'CRM SaaS public contract verification passed.'
