param(
    [Parameter(Mandatory)][string]$ManifestPath,
    [string]$ExpectedOwnerName,
    [string]$ExpectedKickoffDate,
    [string]$ExpectedTargetGaDate
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SpaceGaJson.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$repoFullPath = [System.IO.Path]::GetFullPath($repo).TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar)
$repoPrefix = $repoFullPath + [System.IO.Path]::DirectorySeparatorChar
$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$errors = [System.Collections.Generic.List[string]]::new()

function Fail-Baseline([string]$Message) { $errors.Add($Message) }
function Test-BaselineText($Value) {
    return $null -ne $Value -and ![string]::IsNullOrWhiteSpace([string]$Value)
}
function Test-BaselineSha($Value) { return [string]$Value -match '^[a-fA-F0-9]{64}$' }
function Test-BaselinePerson($Value) {
    if (!(Test-BaselineText $Value) -or ([string]$Value).Length -gt 200) { return $false }
    $name = ([string]$Value).Trim()
    if ($name -match '^\d+$') { return $false }
    return $name -notmatch (
        '^(?i:tbd|pending|unknown|n/?a|owner|team|product|qa|wms|' +
        'admin|administrator|test|demo|simulated|\u5f85\u5b9a|' +
        '\u672a\u5b9a|\u8d1f\u8d23\u4eba|\u56e2\u961f)$')
}
function Test-BaselineDate($Value) {
    if ([string]$Value -notmatch '^\d{4}-\d{2}-\d{2}$') { return $false }
    [DateTime]$parsed = [DateTime]::MinValue
    return [DateTime]::TryParseExact(
        [string]$Value,
        'yyyy-MM-dd',
        [System.Globalization.CultureInfo]::InvariantCulture,
        [System.Globalization.DateTimeStyles]::None,
        [ref]$parsed)
}
function ConvertTo-BaselineUtc($Value) {
    if ([string]$Value -notmatch '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z$') {
        return $null
    }
    try {
        $parsed = [DateTimeOffset]::Parse(
            [string]$Value,
            [System.Globalization.CultureInfo]::InvariantCulture,
            [System.Globalization.DateTimeStyles]::RoundtripKind)
        if ($parsed.Offset -ne [TimeSpan]::Zero) { return $null }
        return $parsed
    }
    catch { return $null }
}
function ConvertTo-BaselineInteger($Value) {
    [long]$number = 0
    if ($null -eq $Value -or ![long]::TryParse(
        [string]$Value,
        [System.Globalization.NumberStyles]::Integer,
        [System.Globalization.CultureInfo]::InvariantCulture,
        [ref]$number)) { return $null }
    return $number
}

if (!(Test-Path -LiteralPath $manifestFullPath -PathType Leaf)) {
    throw "Baseline governance evidence manifest was not found: $manifestFullPath"
}
if (!$manifestFullPath.StartsWith(
    $repoPrefix,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Baseline governance evidence manifest must remain inside the repository.'
}

$manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-SpaceGaJson
if ($manifest.schemaVersion -ne 1 -or
    $manifest.programId -ne 'CP6_SPACE_STUDIO_V1_CORE_GA' -or
    $manifest.deliveryMode -ne 'SoloDeveloper' -or
    $manifest.evidenceClass -ne 'WP0_BASELINE_GOVERNANCE_FORMAL_EVIDENCE') {
    Fail-Baseline 'SPACE_GA_BASELINE_SCHEMA_INVALID: schema, program, delivery mode or evidence class is invalid.'
}
if ($manifest.conclusion -ne 'Pass') {
    Fail-Baseline 'SPACE_GA_BASELINE_CONCLUSION_INVALID: only a final Pass package can close WP0.'
}
$owner = [string]$manifest.ownerName
if (!(Test-BaselinePerson $owner)) {
    Fail-Baseline 'SPACE_GA_BASELINE_OWNER_INVALID: one real DeliveryOwner is required.'
}
elseif ((Test-BaselinePerson $ExpectedOwnerName) -and
    !$owner.Equals($ExpectedOwnerName, [System.StringComparison]::OrdinalIgnoreCase)) {
    Fail-Baseline 'SPACE_GA_BASELINE_OWNER_MISMATCH: evidence owner must match the WP0 owner.'
}
$acceptedAt = ConvertTo-BaselineUtc $manifest.acceptedAtUtc
if ($null -eq $acceptedAt -or $acceptedAt -gt [DateTimeOffset]::UtcNow.AddMinutes(5)) {
    Fail-Baseline 'SPACE_GA_BASELINE_TIME_INVALID: acceptedAtUtc must be a non-future UTC timestamp.'
}
if (!(Test-BaselineDate $manifest.kickoffDate) -or
    !(Test-BaselineDate $manifest.targetGaDate) -or
    [string]$manifest.targetGaDate -lt [string]$manifest.kickoffDate) {
    Fail-Baseline 'SPACE_GA_BASELINE_DATES_INVALID: valid ordered Kickoff and target GA dates are required.'
}
if ((Test-BaselineDate $ExpectedKickoffDate) -and
    [string]$manifest.kickoffDate -ne $ExpectedKickoffDate) {
    Fail-Baseline 'SPACE_GA_BASELINE_KICKOFF_MISMATCH: Kickoff date must match the GA index.'
}
if ((Test-BaselineDate $ExpectedTargetGaDate) -and
    [string]$manifest.targetGaDate -ne $ExpectedTargetGaDate) {
    Fail-Baseline 'SPACE_GA_BASELINE_TARGET_MISMATCH: target GA date must match the GA index.'
}

$baseline = $manifest.baseline
$commitSha = [string]$baseline.mainCommitSha
if ($commitSha -notmatch '^[a-fA-F0-9]{40}$' -or
    $baseline.branch -ne 'main' -or $baseline.remote -ne 'origin' -or
    $baseline.commitPresent -ne $true -or
    $baseline.postMergeSmokePassed -ne $true -or
    $baseline.workspaceClean -ne $true -or
    $baseline.productionDeploymentPerformed -ne $false) {
    Fail-Baseline 'SPACE_GA_BASELINE_MAIN_INVALID: an existing clean post-merge main baseline with no production deployment is required.'
}
if ($commitSha -match '^[a-fA-F0-9]{40}$') {
    & git -C $repo cat-file -e "$commitSha`^{commit}" 2>$null
    if ($LASTEXITCODE -ne 0) {
        Fail-Baseline 'SPACE_GA_BASELINE_COMMIT_MISSING: mainCommitSha is not present in the repository.'
    }
    else {
        & git -C $repo merge-base --is-ancestor $commitSha HEAD 2>$null
        if ($LASTEXITCODE -ne 0) {
            Fail-Baseline 'SPACE_GA_BASELINE_COMMIT_NOT_ANCESTOR: mainCommitSha must be an ancestor of the evidence commit.'
        }
    }
}

$governance = $manifest.governance
if ($governance.singleDeliveryOwner -ne $true -or
    $governance.distinctPersonQuorumRequired -ne $false -or
    $governance.allGateOwnersAssigned -ne $true -or
    $governance.externalInputOwnersAssigned -ne $true) {
    Fail-Baseline 'SPACE_GA_BASELINE_GOVERNANCE_INVALID: solo ownership must be complete without a distinct-person quorum.'
}

$requiredInputs = @(
    'AUTHORIZED_GOLDEN_CAD_CANDIDATES',
    'PRIMARY_PROVIDER_AND_ISOLATED_WORKER')
$inputs = @($manifest.externalInputs)
$inputIds = @($inputs | ForEach-Object { [string]$_.id })
if ($inputs.Count -ne 2 -or @($inputIds | Sort-Object -Unique).Count -ne 2 -or
    @($requiredInputs | Where-Object { $_ -notin $inputIds }).Count -gt 0) {
    Fail-Baseline 'SPACE_GA_BASELINE_INPUT_SET_INVALID: exactly the two Core GA external inputs are required.'
}
foreach ($input in $inputs) {
    $reference = [string]$input.verificationManifest
    if (!(Test-BaselinePerson $input.ownerName) -or $input.status -ne 'Complete' -or
        !(Test-BaselineSha $input.evidenceSha256) -or
        !(Test-BaselineText $reference) -or [System.IO.Path]::IsPathRooted($reference)) {
        Fail-Baseline 'SPACE_GA_BASELINE_INPUT_INVALID: each input requires a real Owner, Complete status, relative Manifest and SHA-256.'
        continue
    }
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repo $reference))
    if (!$fullPath.StartsWith($repoPrefix, [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetExtension($fullPath) -ne '.json' -or
        !(Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        Fail-Baseline 'SPACE_GA_BASELINE_INPUT_MANIFEST_INVALID: input Manifest must be repository-relative JSON.'
        continue
    }
    if (!((Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash.Equals(
        [string]$input.evidenceSha256,
        [System.StringComparison]::OrdinalIgnoreCase))) {
        Fail-Baseline 'SPACE_GA_BASELINE_INPUT_HASH_MISMATCH: input Manifest hash does not match.'
    }
}

$requiredDependencies = @(
    'WP3_PRIMARY_PROVIDER_AND_ISOLATED_WORKER',
    'WP4_THREE_PATH_END_TO_END',
    'WP7_GOLDEN_CAD_FORMAL_EVIDENCE')
$dependencies = @($manifest.acceptedDependencies | ForEach-Object { [string]$_ })
if ($dependencies.Count -ne 3 -or
    @($dependencies | Sort-Object -Unique).Count -ne 3 -or
    @($requiredDependencies | Where-Object { $_ -notin $dependencies }).Count -gt 0) {
    Fail-Baseline 'SPACE_GA_BASELINE_DEPENDENCIES_INVALID: accepted WP3, WP4 and WP7 baselines are required.'
}

$gaSmoke = $manifest.smoke.gaValidator
$threePathSmoke = $manifest.smoke.threePathEvidenceTests
$attestationSmoke = $manifest.smoke.gaEvidenceAttestationTests
if ($gaSmoke.passed -ne $true -or
    (ConvertTo-BaselineInteger $gaSmoke.pendingInputs) -ne 0 -or
    (ConvertTo-BaselineInteger $gaSmoke.pendingGatesBeforeWp0Acceptance) -ne 6 -or
    (ConvertTo-BaselineInteger $gaSmoke.pendingSigners) -ne 1 -or
    (ConvertTo-BaselineInteger $threePathSmoke.passed) -le 0 -or
    (ConvertTo-BaselineInteger $threePathSmoke.failed) -ne 0 -or
    (ConvertTo-BaselineInteger $attestationSmoke.passed) -le 0 -or
    (ConvertTo-BaselineInteger $attestationSmoke.failed) -ne 0) {
    Fail-Baseline 'SPACE_GA_BASELINE_SMOKE_FAILED: post-merge GA and evidence tests must pass with the pre-WP0 pending counts.'
}

if ($errors.Count -gt 0) {
    throw ("Baseline governance evidence validation failed:`n" + ($errors -join "`n"))
}
[ordered]@{
    programId = $manifest.programId
    evidenceClass = $manifest.evidenceClass
    conclusion = $manifest.conclusion
    mainCommitSha = $commitSha
    externalInputCount = $inputs.Count
    dependencyCount = $dependencies.Count
} | ConvertTo-Json -Compress
