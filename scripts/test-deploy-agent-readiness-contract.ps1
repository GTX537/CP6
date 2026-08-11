$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pipelinePath = Join-Path $repoRoot "azure-pipelines-deploy-agent-readiness.yml"

if (-not (Test-Path -LiteralPath $pipelinePath -PathType Leaf)) {
    throw "Deploy agent readiness pipeline was not found."
}

$pipeline = Get-Content -LiteralPath $pipelinePath -Raw

$requiredPatterns = [ordered]@{
    "manual trigger" = '(?m)^trigger:\s*none\s*$'
    "PR disabled" = '(?m)^pr:\s*none\s*$'
    "dedicated pool" = "name:\s*'CP6-Deploy'"
    "dedicated agent demand" = 'Agent\.Name\s+-equals\s+LAPTOP-3QQ44FJS'
    "no repository checkout" = 'checkout:\s*none'
    "dedicated Windows identity" = 'LAPTOP-3QQ44FJS\\cp6_deploy_agent'
    "non-admin assertion" = 'WindowsBuiltInRole\]::Administrator'
    "explicit Docker Desktop pipe" = 'dockerDesktopLinuxEngine'
    "Docker engine verification" = 'docker\s+version'
    "Docker Compose verification" = 'docker\s+compose\s+version'
    "SQL endpoint verification" = 'Test-NetConnection'
}

foreach ($entry in $requiredPatterns.GetEnumerator()) {
    if ($pipeline -notmatch $entry.Value) {
        throw "Readiness pipeline is missing $($entry.Key)."
    }
}

$forbiddenPatterns = [ordered]@{
    "Azure variable group" = '(?m)^\s*-?\s*group:'
    "deployment environment" = '(?m)^\s*environment:'
    "Docker build" = 'docker\s+build'
    "Docker deployment" = 'docker\s+compose.+\bup\b'
    "lab deployment script" = 'Invoke-Cp6LabEnvironment'
    "secret-like variable" = '(?i)password|jwt__secret|connectionstrings__'
}

foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($pipeline -match $entry.Value) {
        throw "Readiness pipeline unexpectedly contains $($entry.Key)."
    }
}

Write-Host "CP6 deploy agent readiness contract test passed."
