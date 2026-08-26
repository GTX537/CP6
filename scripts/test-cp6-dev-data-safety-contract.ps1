[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$backupPath = Join-Path $PSScriptRoot 'Backup-Cp6DevDatabase.ps1'
$backupReadinessPath = Join-Path $PSScriptRoot 'Wait-Cp6DevBackupReadiness.ps1'
$backupReadinessTestPath = Join-Path $PSScriptRoot 'test-cp6-dev-backup-readiness.ps1'
$sqlcmdModulePath = Join-Path $PSScriptRoot 'Cp6.Sqlcmd.psm1'
$sqlcmdTestPath = Join-Path $PSScriptRoot 'test-cp6-sqlcmd-resolution.ps1'
$exportPath = Join-Path $PSScriptRoot 'Export-Cp6DevSnapshot.ps1'
$importPath = Join-Path $PSScriptRoot 'Import-Cp6DevSnapshot.ps1'
$labPath = Join-Path $PSScriptRoot 'Invoke-Cp6LabEnvironment.ps1'
$pipelinePath = Join-Path $repoRoot 'azure-pipelines-dev.yml'

foreach ($path in @(
    $backupPath,
    $backupReadinessPath,
    $backupReadinessTestPath,
    $sqlcmdModulePath,
    $sqlcmdTestPath,
    $exportPath,
    $importPath,
    $labPath,
    $pipelinePath
)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required DEV data-safety file is missing: $path"
    }
}

$backup = Get-Content -LiteralPath $backupPath -Raw -Encoding utf8
$backupReadiness = Get-Content -LiteralPath $backupReadinessPath -Raw -Encoding utf8
$backupReadinessTest = Get-Content -LiteralPath $backupReadinessTestPath -Raw -Encoding utf8
$sqlcmdModule = Get-Content -LiteralPath $sqlcmdModulePath -Raw -Encoding utf8
$sqlcmdTest = Get-Content -LiteralPath $sqlcmdTestPath -Raw -Encoding utf8
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
    'Join-Path \$PSScriptRoot ''Cp6\.Sqlcmd\.psm1''',
    'Import-Module \$sqlcmdModulePath -Force',
    'Resolve-Cp6SqlcmdPath',
    '& \$SqlcmdPath',
    'Get-FileHash.+SHA256',
    'NewGuid\(\).+Substring\(0, 8\)'
)) {
    if ($backup -notmatch $pattern) {
        throw "CP6_DEV backup contract is missing '$pattern'."
    }
}

foreach ($pattern in @(
    'Client SDK\\ODBC\\180\\Tools\\Binn\\SQLCMD\.EXE',
    'Client SDK\\ODBC\\170\\Tools\\Binn\\SQLCMD\.EXE',
    'IsPathRooted',
    'Test-Path.+PathType Leaf'
)) {
    if ($sqlcmdModule -notmatch $pattern) {
        throw "CP6 sqlcmd resolver contract is missing '$pattern'."
    }
}

foreach ($pattern in @(
    'Resolve-Cp6SqlcmdPath -StandardPaths @\(\)',
    "SqlcmdPath '\.\\sqlcmd\.exe'",
    'missing\\sqlcmd\.exe',
    'fallbackCandidate',
    '7 scenarios'
)) {
    if ($sqlcmdTest -notmatch $pattern) {
        throw "CP6 sqlcmd behavior test is missing '$pattern'."
    }
}
if ($backup -match '(?m)(?:^|\s)-P(?:\s|$)' -or $backup -match 'CP6DB') {
    throw 'CP6_DEV backup must not put a password on the command line or address CP6DB.'
}

foreach ($pattern in @(
    "\[ValidateSet\('CP6_DEV'\)\]",
    "\[ValidateSet\('cp6_dev_backup'\)\]",
    'MinimumFreeMemoryMiB',
    'RequiredConsecutiveSuccesses',
    'SQLCMDPASSWORD',
    '-l 5',
    '-t 5',
    "\[ValidateSet\('passed', 'failed'\)\]",
    'Write-ReadinessEvidence'
)) {
    if ($backupReadiness -notmatch $pattern) {
        throw "CP6_DEV backup readiness contract is missing '$pattern'."
    }
}
foreach ($pattern in @(
    'three consecutive safe samples',
    'transient SQL failure',
    'fail closed before SQL access',
    '5 scenarios'
)) {
    if ($backupReadinessTest -notmatch $pattern) {
        throw "CP6_DEV backup readiness behavior test is missing '$pattern'."
    }
}
if ($backupReadiness -match '(?m)(?:^|\s)-P(?:\s|$)' -or $backupReadiness -match 'CP6DB') {
    throw 'CP6_DEV backup readiness must not expose a password or address CP6DB.'
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

$pipelineReadinessIndex = $pipeline.IndexOf('Wait-Cp6DevBackupReadiness.ps1')
$pipelineBackupIndex = $pipeline.IndexOf('Backup-Cp6DevDatabase.ps1')
$pipelineDeployIndex = $pipeline.IndexOf('-Action Deploy')
if ($pipelineReadinessIndex -lt 0 -or
    $pipelineBackupIndex -le $pipelineReadinessIndex -or
    $pipelineDeployIndex -le $pipelineBackupIndex) {
    throw 'Pipeline must pass readiness and the verified CP6_DEV backup before app stop/migration.'
}

Write-Host 'CP6 DEV backup and side-by-side import safety contract passed.'
