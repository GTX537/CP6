[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$utf8 = New-Object System.Text.UTF8Encoding($false)
$failures = New-Object 'System.Collections.Generic.List[string]'

$publicPath = 'docs/crm/CP6-SAAS-V1-PUBLIC-CONTRACT.md'
$approvalPath = 'docs/crm/approvals/cp6-saas-v1-public-contract.json'
$m0Path = 'docs/crm/CRM-M0-READINESS.md'
$r00Path = 'docs/devops/adr/ADR-CRM-R00-RELEASE-AUTHORITY.md'
$sourceRepository = 'GTX537/CP6.CRM'
$sourceMergeCommit = '07a7bb0b50f33b0cb70c18c14f83be77c725626d'
$sourceProductDigest = 'e210cb804d5b499e725c0ddeca84bb1157d09eb5304bc3b77b031142db84287b'
$sourceR00Digest = '64a53dd895aedc20a51288ad0ffdb69f60ddc7c22012c1df83984efba5adbc03'

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
    'Partial / Gap',
    'first-writer-wins',
    'bucket + key + VersionId + SHA-256',
    'CandidateLocator',
    'Lead Adoption',
    'P09/P10',
    'Pending'
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
Assert-Contains 'docs/crm/CRM-PRODUCT-FRAMEWORK.md' @('Historical planning baseline', 'CP6-SAAS-V1-PUBLIC-CONTRACT.md')
Assert-Contains 'docs/crm/CRM-V1-EXECUTABLE-SPEC.md' @('Historical planning baseline', 'CP6-SAAS-V1-PUBLIC-CONTRACT.md')
Assert-Contains 'docs/crm/CRM-V1-SPEC.md' @('Historical Foundation baseline', 'CP6-SAAS-V1-PUBLIC-CONTRACT.md')
Assert-Contains 'docs/crm/README.md' @('CP6-SAAS-V1-PUBLIC-CONTRACT.md', $sourceProductDigest, 'M0', 'No-Go')
Assert-Contains 'docs/devops/README.md' @('ADR-CRM-R00', $sourceR00Digest, 'P09/P10')
Assert-Contains 'docs/project-memory/PROJECT_STATE.md' @('CP6-SAAS-V1-PUBLIC-CONTRACT', $sourceProductDigest, $sourceR00Digest)
Assert-Contains 'docs/project-memory/05-Completed.md' @($sourceMergeCommit, $sourceProductDigest)
Assert-Contains 'docs/project-memory/06-Todo.md' @('Complete', 'ProgramOwner', 'DEC-001')
Assert-Contains 'docs/project-memory/CHANGELOG-AI.md' @('CP6-SAAS-V1-PUBLIC-CONTRACT', $sourceProductDigest)

$approvalFile = Join-Path $root $approvalPath
try {
    $approval = Get-Content -LiteralPath $approvalFile -Raw -Encoding utf8 | ConvertFrom-Json
}
catch {
    Fail "Invalid JSON in $approvalPath : $($_.Exception.Message)"
    $approval = $null
}

if ($null -ne $approval) {
    if ($approval.decisionId -ne 'CP6-SAAS-V1-PUBLIC-CONTRACT') { Fail 'Approval decisionId mismatch' }
    if ($approval.decisionPath -ne $publicPath) { Fail 'Approval decisionPath mismatch' }
    if ($approval.decisionPayloadSha256 -ne $publicDigest) { Fail 'Approval public digest mismatch' }
    if ($approval.approvalModel -ne 'SingleProgramOwner') { Fail 'Approval model must be SingleProgramOwner' }
    if ($approval.m0Status -ne 'No-Go') { Fail 'Public sync must not change M0 away from No-Go' }
    if ($approval.sourcePrivate.repository -ne $sourceRepository) { Fail 'Private source repository mismatch' }
    if ($approval.sourcePrivate.mergeCommitSha -ne $sourceMergeCommit) { Fail 'Private source merge commit mismatch' }
    if ($approval.sourcePrivate.productDecisionPayloadSha256 -ne $sourceProductDigest) { Fail 'Private product digest mismatch' }
    if ($approval.sourcePrivate.productStatus -ne 'Frozen') { Fail 'Private product status must be Frozen' }
    if ($approval.sourcePrivate.r00DecisionPayloadSha256 -ne $sourceR00Digest) { Fail 'Private R00 digest mismatch' }
    if ($approval.sourcePrivate.r00Status -ne 'Accepted') { Fail 'Private R00 status must be Accepted' }
    if ($approval.status -notin @('Candidate', 'Complete')) { Fail 'Approval status must be Candidate or Complete' }

    $requiredRoles = @($approval.requiredForComplete)
    if ($requiredRoles.Count -ne 1 -or $requiredRoles[0] -ne 'ProgramOwner') {
        Fail 'requiredForComplete must contain exactly ProgramOwner'
    }

    if ($approval.status -eq 'Candidate' -and @($approval.approvals).Count -ne 0) {
        Fail 'Candidate must not contain effective approvals'
    }
    if ($approval.status -eq 'Candidate') {
        Assert-Contains $publicPath @('<!-- public-contract-status: Candidate -->')
        Assert-Contains $r00Path @('<!-- public-r00-mirror-status: Candidate -->')
    }
    if ($approval.status -eq 'Complete') {
        Assert-Contains $publicPath @('<!-- public-contract-status: Complete -->')
        Assert-Contains $r00Path @('<!-- public-r00-mirror-status: Complete -->')
        $validApprovals = @($approval.approvals | Where-Object {
            $_.roleId -eq 'ProgramOwner' -and
            $_.decision -eq 'Approved' -and
            $_.decisionPayloadSha256 -eq $publicDigest
        })
        if ($validApprovals.Count -ne 1) { Fail 'Complete requires exactly one matching ProgramOwner approval' }
        if ($null -eq $approval.approvalEvidence) { Fail 'Complete requires immutable approvalEvidence' }
    }
}

Assert-Contains $m0Path @('<!-- crm-m0-status: No-Go -->')

$sanitizedFiles = @($publicPath, $approvalPath, $m0Path, $r00Path)
$forbiddenCommercialPatterns = @(
    '(?i)USD\s+[0-9]',
    '(?i)CNY\s+[0-9]',
    '(?i)Stripe|PayPal',
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
    $gitDiffChecks += ,@('diff', '--check', 'HEAD^', 'HEAD')
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
