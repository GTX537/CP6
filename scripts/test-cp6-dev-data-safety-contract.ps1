[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$backupPath = Join-Path $PSScriptRoot 'Backup-Cp6DevDatabase.ps1'
$exportPath = Join-Path $PSScriptRoot 'Export-Cp6DevSnapshot.ps1'
$importPath = Join-Path $PSScriptRoot 'Import-Cp6DevSnapshot.ps1'
$labPath = Join-Path $PSScriptRoot 'Invoke-Cp6LabEnvironment.ps1'
$pipelinePath = Join-Path $repoRoot 'azure-pipelines-dev.yml'

foreach ($path in @($backupPath, $exportPath, $importPath, $labPath, $pipelinePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required DEV data-safety file is missing: $path"
    }
}

$backup = Get-Content -LiteralPath $backupPath -Raw -Encoding utf8
$export = Get-Content -LiteralPath $exportPath -Raw -Encoding utf8
$import = Get-Content -LiteralPath $importPath -Raw -Encoding utf8
$lab = Get-Content -LiteralPath $labPath -Raw -Encoding utf8
$pipeline = Get-Content -LiteralPath $pipelinePath -Raw -Encoding utf8

foreach ($pattern in @(
    "\[ValidateSet\('CP6_DEV'\)\]",
    "\[ValidateSet\('cp6_dev_backup'\)\]",
    'WITH COPY_ONLY, INIT, COMPRESSION, CHECKSUM',
    'RESTORE VERIFYONLY',
    'SQLCMDPASSWORD',
    'Get-FileHash.+SHA256',
    'NewGuid\(\).+Substring\(0, 8\)'
)) {
    if ($backup -notmatch $pattern) {
        throw "CP6_DEV backup contract is missing '$pattern'."
    }
}
if ($backup -match '(?m)(?:^|\s)-P(?:\s|$)' -or $backup -match 'CP6DB') {
    throw 'CP6_DEV backup must not put a password on the command line or address CP6DB.'
}

if ($export -notmatch 'Get-Credential' -or
    $export -notmatch "Credential\.UserName -ne 'cp6_dev_backup'" -or
    $export -notmatch 'Backup-Cp6DevDatabase\.ps1') {
    throw 'Manual CP6_DEV export does not enforce the dedicated backup identity.'
}

foreach ($pattern in @(
    '\[CmdletBinding\(SupportsShouldProcess = \$true',
    '\^CP6DEV_IMPORT_\\d\{8\}_\\d\{6\}\$',
    "TargetDatabase -eq 'CP6DB'",
    "TargetDatabase -eq 'CP6_DEV'",
    "Name -eq 'cp6_cp6-db-data'",
    'RESTORE VERIFYONLY',
    'already exists; overwrite is forbidden',
    'CP6DB was not overwritten or merged',
    'if \(\$cleanupRequired\)',
    '\$global:LASTEXITCODE = 0'
)) {
    if ($import -notmatch $pattern) {
        throw "Side-by-side import contract is missing '$pattern'."
    }
}
foreach ($forbidden in @(
    '(?i)WITH\s+REPLACE',
    '(?i)DROP\s+DATABASE',
    '(?i)docker\s+volume\s+prune',
    '(?i)down\s+(?:--volumes|-v)'
)) {
    if ($import -match $forbidden) {
        throw "Side-by-side import contains forbidden operation '$forbidden'."
    }
}

$infraIndex = $lab.IndexOf('@("up", "-d", "--wait", "--wait-timeout", "240", "redis", "rabbitmq", "kafka")')
$stopIndex = $lab.IndexOf('@("stop", "web", "api")')
$migrationIndex = $lab.IndexOf('@("--profile", "migration", "run", "--rm", "db-init")')
$apiIndex = $lab.IndexOf('@("up", "-d", "--wait", "--wait-timeout", "240", "api")')
$webIndex = $lab.IndexOf('@("up", "-d", "--wait", "--wait-timeout", "240", "web")')
if ($infraIndex -lt 0 -or
    $stopIndex -le $infraIndex -or
    $migrationIndex -le $stopIndex -or
    $apiIndex -le $migrationIndex -or
    $webIndex -le $apiIndex) {
    throw 'Lab deploy must start infrastructure, stop Web/API, migrate, verify API, then start Web.'
}

$pipelineBackupIndex = $pipeline.IndexOf('Backup-Cp6DevDatabase.ps1')
$pipelineDeployIndex = $pipeline.IndexOf('-Action Deploy')
if ($pipelineBackupIndex -lt 0 -or $pipelineDeployIndex -le $pipelineBackupIndex) {
    throw 'Pipeline must complete the verified CP6_DEV backup before app stop/migration.'
}

Write-Host 'CP6 DEV backup and side-by-side import safety contract passed.'
