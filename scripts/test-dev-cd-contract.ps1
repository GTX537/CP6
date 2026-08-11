$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$pipelinePath = Join-Path $repoRoot "azure-pipelines-dev.yml"

if (-not (Test-Path -LiteralPath $pipelinePath -PathType Leaf)) {
    throw "DEV CD pipeline was not found."
}

$pipeline = Get-Content -LiteralPath $pipelinePath -Raw -Encoding utf8

$requiredPatterns = [ordered]@{
    "CI-only repository trigger" = '(?m)^trigger:\s*none\s*$'
    "PR disabled" = '(?m)^pr:\s*none\s*$'
    "CI pipeline resource" = '(?m)^\s*source:\s*GTX537\.CP6\s*$'
    "main completion trigger" = '(?m)^\s*-\s*refs/heads/main\s*$'
    "dedicated pool" = "name:\s*'CP6-Deploy'"
    "dedicated agent demand" = 'Agent\.Name\s+-equals\s+LAPTOP-3QQ44FJS'
    "candidate build stage" = '(?m)^- stage:\s*BuildCandidate\s*$'
    "DEV deployment stage" = '(?m)^- stage:\s*DeployDev\s*$'
    "build dependency" = '(?m)^\s*dependsOn:\s*BuildCandidate\s*$'
    "deployment job" = '(?m)^\s*- deployment:\s*DeployDev\s*$'
    "DEV environment" = '(?m)^\s*name:\s*cp6-dev\s*$'
    "DEV secret group" = '(?m)^\s*- group:\s*cp6-dev-secrets\s*$'
    "database migrator secret mapping" = 'CP6_DB_MIGRATOR_PASSWORD:\s*''\$\(CP6_DEV_DB_MIGRATOR_PASSWORD\)'''
    "database runtime secret mapping" = 'CP6_DB_RUNTIME_PASSWORD:\s*''\$\(CP6_DEV_DB_RUNTIME_PASSWORD\)'''
    "RabbitMQ secret mapping" = 'CP6_RABBITMQ_PASSWORD:\s*''\$\(CP6_DEV_RABBITMQ_PASSWORD\)'''
    "JWT secret mapping" = 'CP6_JWT_SECRET:\s*''\$\(CP6_DEV_JWT_SECRET\)'''
    "database initialization" = '-Action\s+Deploy'
    "live verification" = '/health/live'
    "ready verification" = '/health/ready'
    "API release verification" = '/health/release'
    "Web release verification" = '/release\.json'
    "non-secret evidence" = "artifact:\s*'cp6-dev-evidence'"
}

foreach ($entry in $requiredPatterns.GetEnumerator()) {
    if ($pipeline -notmatch $entry.Value) {
        throw "DEV CD pipeline is missing $($entry.Key)."
    }
}

$buildStageIndex = $pipeline.IndexOf("- stage: BuildCandidate")
$deployStageIndex = $pipeline.IndexOf("- stage: DeployDev")
$groupIndex = $pipeline.IndexOf("- group: cp6-dev-secrets")
if ($buildStageIndex -lt 0 -or
    $deployStageIndex -le $buildStageIndex -or
    $groupIndex -le $deployStageIndex) {
    throw "DEV secrets must be scoped to the deployment stage after candidate build."
}

$forbiddenPatterns = [ordered]@{
    "mutable latest tag" = '(?i)(?:^|[:\s-])latest(?:$|[\s''"])'
    "UAT deployment" = '(?m)^\s*name:\s*cp6-uat\s*$'
    "PROD-LAB deployment" = '(?m)^\s*name:\s*cp6-prod-lab\s*$'
    "production deployment" = '(?m)^\s*name:\s*cp6-prod\s*$'
    "inline connection string" = '(?i)(?:Password|Pwd)\s*='
    "secret echoed to log" = '(?i)Write-Host.+(?:PASSWORD|JWT_SECRET)'
}

foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($pipeline -match $entry.Value) {
        throw "DEV CD pipeline unexpectedly contains $($entry.Key)."
    }
}

$groupMatches = [regex]::Matches($pipeline, '(?m)^\s*- group:\s*cp6-dev-secrets\s*$')
if ($groupMatches.Count -ne 1) {
    throw "DEV secret group must be referenced exactly once."
}

Write-Host "CP6 DEV CD contract test passed."
