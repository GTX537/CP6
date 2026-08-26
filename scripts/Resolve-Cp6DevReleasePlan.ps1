[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('ResourceTrigger', 'Manual')]
    [string]$BuildReason,

    [string]$AutoDeployEnabled = '',

    [Parameter(Mandatory = $true)]
    [string]$CiSourceBranch,

    [Parameter(Mandatory = $true)]
    [string]$CiSourceCommit,

    [Parameter(Mandatory = $true)]
    [string]$CurrentMainCommit,

    [Parameter(Mandatory = $true)]
    [string]$OrchestrationBranch,

    [Parameter(Mandatory = $true)]
    [string]$OrchestrationCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

foreach ($commit in @($CiSourceCommit, $CurrentMainCommit, $OrchestrationCommit)) {
    if ($commit -notmatch '^[0-9a-fA-F]{40}$') {
        throw 'Release planning requires complete 40-character Git commits.'
    }
}
if ($CiSourceBranch -ne 'refs/heads/main') {
    throw "DEV deployment requires a main CI run; received '$CiSourceBranch'."
}

$triggerMode = if ($BuildReason -eq 'ResourceTrigger') { 'automatic' } else { 'manual' }
$autoEnabled = $AutoDeployEnabled -match '^(?i:true|1|yes)$'
$selectedIsCurrent = $CurrentMainCommit.Equals(
    $CiSourceCommit,
    [StringComparison]::OrdinalIgnoreCase)
$deployAllowed = $true
$skipReason = ''

if ($triggerMode -eq 'automatic' -and -not $autoEnabled) {
    $deployAllowed = $false
    $skipReason = 'automatic deployment is disabled'
}
elseif ($triggerMode -eq 'automatic' -and -not $selectedIsCurrent) {
    $deployAllowed = $false
    $skipReason = 'the automatic CI run was superseded by a newer main commit'
}
elseif ($triggerMode -eq 'manual' -and $autoEnabled -and -not $selectedIsCurrent) {
    throw 'Manual rollback to an older CI run requires CP6_DEV_AUTO_DEPLOY_ENABLED=false first.'
}

if ($deployAllowed -and
    ($OrchestrationBranch -ne 'refs/heads/main' -or
     -not $OrchestrationCommit.Equals(
         $CurrentMainCommit,
         [StringComparison]::OrdinalIgnoreCase))) {
    throw "A deploying DEV CD run must use current main orchestration; branch=$OrchestrationBranch, checkout=$OrchestrationCommit, main=$CurrentMainCommit."
}

[pscustomobject]@{
    TriggerMode = $triggerMode
    AutoEnabled = $autoEnabled
    DeployAllowed = $deployAllowed
    SkipReason = $skipReason
    SelectedIsCurrent = $selectedIsCurrent
}
