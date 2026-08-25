[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$resolver = Join-Path $PSScriptRoot 'Resolve-Cp6DevReleasePlan.ps1'
$oldCommit = '1' * 40
$currentCommit = '2' * 40

function Resolve-Plan {
    param(
        [string]$Reason,
        [string]$Auto,
        [string]$Selected,
        [string]$Current = $currentCommit,
        [string]$Orchestration = $currentCommit,
        [string]$OrchestrationBranch = 'refs/heads/main'
    )

    & $resolver `
        -BuildReason $Reason `
        -AutoDeployEnabled $Auto `
        -CiSourceBranch 'refs/heads/main' `
        -CiSourceCommit $Selected `
        -CurrentMainCommit $Current `
        -OrchestrationBranch $OrchestrationBranch `
        -OrchestrationCommit $Orchestration
}

$disabledAutomatic = Resolve-Plan `
    -Reason ResourceTrigger -Auto false -Selected $currentCommit
if ($disabledAutomatic.DeployAllowed -or
    $disabledAutomatic.SkipReason -ne 'automatic deployment is disabled') {
    throw 'Disabled automatic mode must skip safely.'
}

$supersededAutomatic = Resolve-Plan `
    -Reason ResourceTrigger `
    -Auto true `
    -Selected $oldCommit `
    -Orchestration $oldCommit
if ($supersededAutomatic.DeployAllowed -or
    $supersededAutomatic.SkipReason -notmatch 'superseded') {
    throw 'A superseded automatic run must skip before enforcing current orchestration.'
}

$currentAutomatic = Resolve-Plan `
    -Reason ResourceTrigger -Auto true -Selected $currentCommit
if (-not $currentAutomatic.DeployAllowed -or
    $currentAutomatic.TriggerMode -ne 'automatic') {
    throw 'Current automatic main must deploy when enabled.'
}

$manualCurrent = Resolve-Plan `
    -Reason Manual -Auto false -Selected $currentCommit
if (-not $manualCurrent.DeployAllowed -or $manualCurrent.TriggerMode -ne 'manual') {
    throw 'Manual current-main deployment must remain available.'
}

$manualRollback = Resolve-Plan `
    -Reason Manual -Auto false -Selected $oldCommit
if (-not $manualRollback.DeployAllowed -or $manualRollback.SelectedIsCurrent) {
    throw 'Manual rollback must be allowed only while automatic mode is disabled.'
}

$rollbackBlocked = $false
try {
    Resolve-Plan -Reason Manual -Auto true -Selected $oldCommit | Out-Null
}
catch {
    $rollbackBlocked = $_.Exception.Message -match 'requires CP6_DEV_AUTO_DEPLOY_ENABLED=false'
}
if (-not $rollbackBlocked) {
    throw 'Automatic mode must block a manual rollback.'
}

$staleOrchestrationBlocked = $false
try {
    Resolve-Plan `
        -Reason Manual `
        -Auto false `
        -Selected $currentCommit `
        -Orchestration $oldCommit | Out-Null
}
catch {
    $staleOrchestrationBlocked = $_.Exception.Message -match 'current main orchestration'
}
if (-not $staleOrchestrationBlocked) {
    throw 'A deploying run must reject stale orchestration.'
}

$nonMainBlocked = $false
try {
    & $resolver `
        -BuildReason Manual `
        -CiSourceBranch 'refs/heads/feature' `
        -CiSourceCommit $currentCommit `
        -CurrentMainCommit $currentCommit `
        -OrchestrationBranch 'refs/heads/main' `
        -OrchestrationCommit $currentCommit | Out-Null
}
catch {
    $nonMainBlocked = $_.Exception.Message -match 'requires a main CI run'
}
if (-not $nonMainBlocked) {
    throw 'A non-main CI run must be rejected.'
}

Write-Host 'CP6 DEV release planning behavior test passed (7 scenarios).'
