$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$pipelinePath = Join-Path $repoRoot 'azure-pipelines-dev.yml'
$apiDockerfilePath = Join-Path $repoRoot 'CP6.WebApi\Dockerfile'

if (-not (Test-Path -LiteralPath $pipelinePath -PathType Leaf)) {
    throw 'DEV CD pipeline was not found.'
}
if (-not (Test-Path -LiteralPath $apiDockerfilePath -PathType Leaf)) {
    throw 'CP6 API Dockerfile was not found.'
}

$pipeline = Get-Content -LiteralPath $pipelinePath -Raw -Encoding utf8
$apiDockerfile = Get-Content -LiteralPath $apiDockerfilePath -Raw -Encoding utf8

$requiredPatterns = [ordered]@{
    'CI-only repository trigger' = '(?m)^trigger:\s*none\s*$'
    'PR disabled' = '(?m)^pr:\s*none\s*$'
    'CI pipeline resource' = '(?m)^\s*source:\s*GTX537\.CP6\s*$'
    'main completion trigger' = '(?m)^\s*-\s*refs/heads/main\s*$'
    'dedicated pool' = "name:\s*'CP6-Deploy'"
    'dedicated agent demand' = 'Agent\.Name\s+-equals\s+LAPTOP-3QQ44FJS'
    'release planning stage' = '(?m)^- stage:\s*PlanRelease\s*$'
    'candidate build stage' = '(?m)^- stage:\s*BuildCandidate\s*$'
    'selected CI artifact download' = '(?s)- download:\s*cp6ci\s+artifact:\s*cp6-dev-runtime'
    'selected CI artifact path' = 'cp6ci\\cp6-dev-runtime'
    'commit-addressed release version' = '0\.0\.0-dev\.\$\(\$env:CP6_CI_SOURCE_COMMIT\)'
    'runtime artifact contract' = 'test-cp6-dev-runtime-artifact\.ps1'
    'runtime artifact packaging input' = '-RuntimeArtifactRoot\s+\$runtimeArtifactRoot'
    'DEV deployment stage' = '(?m)^- stage:\s*DeployDev\s*$'
    'release policy resolver' = 'Resolve-Cp6DevReleasePlan\.ps1'
    'release behavior test' = 'test-cp6-dev-release-plan\.ps1'
    'sqlcmd resolution behavior test' = 'test-cp6-sqlcmd-resolution\.ps1'
    'backup readiness behavior test' = 'test-cp6-dev-backup-readiness\.ps1'
    'automatic deployment switch' = 'CP6_DEV_AUTO_DEPLOY_ENABLED'
    'successful CI REST validation' = "selectedRun\.result\s*-ne\s*'succeeded'"
    'current main freshness input' = '-CurrentMainCommit \$currentMain'
    'selected source worktree' = 'git worktree add --detach'
    'commit-addressed API image' = 'cp6-api:dev-\$\(\$env:CP6_CI_SOURCE_COMMIT\)'
    'commit-addressed Web image' = 'cp6-web:dev-\$\(\$env:CP6_CI_SOURCE_COMMIT\)'
    'API immutable image ID output' = '##vso\[task\.setvariable variable=apiImageId;isOutput=true\]'
    'Web immutable image ID output' = '##vso\[task\.setvariable variable=webImageId;isOutput=true\]'
    'API immutable image ID deployment' = "CP6_API_CANDIDATE_ID: '\$\(CP6_API_CANDIDATE_ID\)'"
    'Web immutable image ID deployment' = "CP6_WEB_CANDIDATE_ID: '\$\(CP6_WEB_CANDIDATE_ID\)'"
    'running API immutable image verification' = 'runningApiImageId\s*-ne\s*\$apiImageId'
    'running Web immutable image verification' = 'runningWebImageId\s*-ne\s*\$webImageId'
    'secret-free running container lookup' = 'label=com\.docker\.compose\.project=cp6-dev'
    'deployment job' = '(?m)^\s*- deployment:\s*DeployDev\s*$'
    'DEV environment' = '(?m)^\s*name:\s*cp6-dev\s*$'
    'sequential lock behavior' = '(?m)^\s*lockBehavior:\s*sequential\s*$'
    'DEV secret group' = '(?m)^\s*- group:\s*cp6-dev-secrets\s*$'
    'backup secret mapping' = "CP6_DEV_DB_BACKUP_PASSWORD:\s*'\$\(CP6_DEV_DB_BACKUP_PASSWORD\)'"
    'database migrator secret mapping' = "CP6_DB_MIGRATOR_PASSWORD:\s*'\$\(CP6_DEV_DB_MIGRATOR_PASSWORD\)'"
    'database runtime secret mapping' = "CP6_DB_RUNTIME_PASSWORD:\s*'\$\(CP6_DEV_DB_RUNTIME_PASSWORD\)'"
    'RabbitMQ secret mapping' = "CP6_RABBITMQ_PASSWORD:\s*'\$\(CP6_DEV_RABBITMQ_PASSWORD\)'"
    'JWT secret mapping' = "CP6_JWT_SECRET:\s*'\$\(CP6_DEV_JWT_SECRET\)'"
    'verified database backup' = 'Backup-Cp6DevDatabase\.ps1'
    'post-package backup readiness gate' = 'Wait-Cp6DevBackupReadiness\.ps1'
    'two GiB backup memory threshold' = '-MinimumFreeMemoryMiB 2048'
    'three consecutive readiness successes' = '-RequiredConsecutiveSuccesses 3'
    'bounded backup readiness wait' = '(?s)-MaxWaitSeconds 300.*?-PollIntervalSeconds 10'
    'in-lock freshness revalidation' = 'Revalidate automatic freshness inside DEV lock'
    'superseded automatic evidence' = 'deployment-skipped\.json'
    'conditional protected tasks' = "condition: and\(succeeded\(\), eq\(variables\['CP6_DEPLOY_ALLOWED'\], 'true'\)\)"
    'promoted commit deployment' = '-AllowPromotedGitSha'
    'live verification' = '/health/live'
    'ready verification' = '/health/ready'
    'API release verification' = '/health/release'
    'Web release verification' = '/release\.json'
    'optional public verification switch' = 'CP6_DEV_PUBLIC_VERIFICATION_ENABLED'
    'trigger evidence' = 'trigger\s*=\s*\[ordered\]'
    'backup evidence' = 'databaseBackup\s*=\s*\$databaseBackup'
    'backup readiness evidence' = 'backupReadiness\s*=\s*\$backupReadiness'
    'non-secret evidence artifact' = "artifact:\s*'cp6-dev-evidence'"
}

foreach ($entry in $requiredPatterns.GetEnumerator()) {
    if ($pipeline -notmatch $entry.Value) {
        throw "DEV CD pipeline is missing $($entry.Key)."
    }
}

$planStageIndex = $pipeline.IndexOf('- stage: PlanRelease')
$buildStageIndex = $pipeline.IndexOf('- stage: BuildCandidate')
$deployStageIndex = $pipeline.IndexOf('- stage: DeployDev')
$groupIndex = $pipeline.IndexOf('- group: cp6-dev-secrets')
$readinessIndex = $pipeline.IndexOf('Wait-Cp6DevBackupReadiness.ps1')
$backupIndex = $pipeline.IndexOf('Backup-Cp6DevDatabase.ps1')
$deployActionIndex = $pipeline.IndexOf('-Action Deploy')
if ($planStageIndex -lt 0 -or
    $buildStageIndex -le $planStageIndex -or
    $deployStageIndex -le $buildStageIndex -or
    $groupIndex -le $deployStageIndex -or
    $readinessIndex -le $groupIndex -or
    $backupIndex -le $readinessIndex -or
    $deployActionIndex -le $backupIndex) {
    throw 'DEV CD stage, secret, backup, and deployment order is unsafe.'
}

$contractStepIndex = $pipeline.IndexOf("displayName: 'Verify deployment identity and contracts'")
$packageStepIndex = $pipeline.IndexOf("displayName: 'Package commit-addressed API and Web images'")
if ($contractStepIndex -lt 0 -or $packageStepIndex -le $contractStepIndex) {
    throw 'DEV contract verification step boundary is invalid.'
}
$contractStep = $pipeline.Substring(
    $contractStepIndex,
    $packageStepIndex - $contractStepIndex)
if ($contractStep -match '\$LASTEXITCODE') {
    throw 'PowerShell contract scripts must not be judged by inherited LASTEXITCODE state.'
}

$forbiddenPatterns = [ordered]@{
    'mutable latest tag' = '(?i)(?:^|[:\s-])latest(?:$|[\s''"])'
    'UAT deployment' = '(?m)^\s*name:\s*cp6-uat\s*$'
    'PROD-LAB deployment' = '(?m)^\s*name:\s*cp6-prod-lab\s*$'
    'production deployment' = '(?m)^\s*name:\s*cp6-prod\s*$'
    'inline connection string' = '(?i)(?:Password|Pwd)\s*='
    'secret echoed to log' = '(?i)Write-Host.+(?:PASSWORD|JWT_SECRET|AZURE_TOKEN)'
    'automatic local snapshot import' = 'Import-Cp6DevSnapshot\.ps1'
    'root stack deployment' = 'docker-compose\.yml'
    'destructive compose removal' = '(?i)down\s+(?:--volumes|-v)'
    'Docker volume pruning' = '(?i)docker\s+volume\s+prune'
    'duplicate API compilation' = '(?i)dotnet\s+(?:restore|build|publish)'
    'duplicate Web compilation' = '(?i)npm(?:\.cmd)?\s+(?:ci|run)'
    'bridge Run ID as binary version' = '0\.0\.0-dev\.\$\(\$env:CP6_CI_RUN_ID\)'
}

foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($pipeline -match $entry.Value) {
        throw "DEV CD pipeline unexpectedly contains $($entry.Key)."
    }
}

if ([regex]::Matches(
    $pipeline,
    '0\.0\.0-dev\.\$\(\$env:CP6_CI_SOURCE_COMMIT\)').Count -ne 3) {
    throw 'DEV CD release version must use the full source SHA in all three stages.'
}

$lowMemoryPublishPatterns = [ordered]@{
    'disabled persistent build servers' = '--disable-build-servers'
    'single MSBuild node' = '(?m)^\s*-m:1\s*\\\s*$'
    'disabled project build parallelism' = '-p:BuildInParallel=false'
    'disabled shared compiler process' = '-p:UseSharedCompilation=false'
}
foreach ($entry in $lowMemoryPublishPatterns.GetEnumerator()) {
    if ($apiDockerfile -notmatch $entry.Value) {
        throw "CP6 API Docker publish is missing $($entry.Key)."
    }
}

$groupMatches = [regex]::Matches(
    $pipeline,
    '(?m)^\s*- group:\s*cp6-dev-secrets\s*$')
if ($groupMatches.Count -ne 1) {
    throw 'DEV secret group must be referenced exactly once.'
}

Write-Host 'CP6 DEV CD dual-mode contract test passed.'
