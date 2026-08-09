[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$SpecPath,
    [ValidateSet("Structure", "Freeze", "VerifySnapshot")]
    [string]$Mode = "Structure",
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$ExpectedVersion,
    [ValidatePattern("^[A-Fa-f0-9]{40}$")]
    [string]$ExpectedGitSha,
    [string]$SnapshotPath,
    [ValidatePattern("^[A-Fa-f0-9]{64}$")]
    [string]$ExpectedSnapshotSha256,
    [string]$OutputSnapshotPath,
    [string]$RepositoryPath,
    [string]$Actor,
    [string]$RunUri
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$nodeScript = Join-Path $repoRoot "cp6.web\scripts\r2-release-readiness.mjs"
$resolvedSpec = (Resolve-Path -LiteralPath $SpecPath -ErrorAction Stop).Path

$arguments = @(
    $nodeScript,
    "--spec", $resolvedSpec,
    "--mode", $Mode
)
if (-not [string]::IsNullOrWhiteSpace($ExpectedVersion)) {
    $arguments += @("--expected-version", $ExpectedVersion)
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedGitSha)) {
    $gitShaArgument = if ($Mode -eq "Freeze") {
        "--git-sha"
    }
    else {
        "--expected-git-sha"
    }
    $arguments += @($gitShaArgument, $ExpectedGitSha)
}
if (-not [string]::IsNullOrWhiteSpace($SnapshotPath)) {
    $arguments += @(
        "--snapshot",
        (Resolve-Path -LiteralPath $SnapshotPath -ErrorAction Stop).Path
    )
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedSnapshotSha256)) {
    $arguments += @(
        "--expected-snapshot-sha256",
        $ExpectedSnapshotSha256
    )
}
if (-not [string]::IsNullOrWhiteSpace($OutputSnapshotPath)) {
    $arguments += @("--output-snapshot", $OutputSnapshotPath)
}
if (-not [string]::IsNullOrWhiteSpace($RepositoryPath)) {
    $arguments += @("--repository-path", $RepositoryPath)
}
if (-not [string]::IsNullOrWhiteSpace($Actor)) {
    $arguments += @("--actor", $Actor)
}
if (-not [string]::IsNullOrWhiteSpace($RunUri)) {
    $arguments += @("--run-uri", $RunUri)
}

& node @arguments
if ($LASTEXITCODE -ne 0) {
    throw "R2 release readiness $Mode gate failed with exit code $LASTEXITCODE."
}
