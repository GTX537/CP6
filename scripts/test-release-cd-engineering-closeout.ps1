[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$evidencePath = Join-Path $repoRoot 'docs\devops\release-cd-engineering-closeout.json'
$reportPath = Join-Path $repoRoot 'docs\devops\RELEASE-CD-ENGINEERING-CLOSEOUT.md'
$candidatePath = Join-Path $repoRoot 'docs\client\r2\releases\v1.0.0\candidate.yaml'

function Assert-Equal {
    param(
        [Parameter(Mandatory = $true)]$Actual,
        [Parameter(Mandatory = $true)]$Expected,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($Actual -ne $Expected) {
        throw "$Description mismatch. Expected '$Expected', found '$Actual'."
    }
}

$requiredPaths = @(
    '.github\workflows\client-contract.yml',
    '.github\workflows\wms-production-sql.yml',
    '.github\workflows\r2-freeze.yml',
    '.github\workflows\r2-candidate.yml',
    '.github\workflows\r2-deploy.yml',
    'azure-pipelines.yml',
    'azure-pipelines-dev.yml',
    'azure-pipelines-release-shadow.yml',
    'deploy\production\compose\compose.yaml',
    'deploy\production\kubernetes',
    'docs\devops\adr\ADR-DEVOPS-001-RELEASE-AUTHORITY-AND-REGISTRY.md'
)
foreach ($relativePath in $requiredPaths) {
    if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $relativePath))) {
        throw "Release/CD closeout dependency is missing: $relativePath"
    }
}

$evidence = [IO.File]::ReadAllText($evidencePath, [Text.Encoding]::UTF8) |
    ConvertFrom-Json
Assert-Equal $evidence.schemaVersion 1 'Closeout schema version'
Assert-Equal $evidence.status 'Complete' 'Engineering closeout status'
Assert-Equal $evidence.scope 'RepositoryAndPlatformEngineering' 'Closeout scope'
Assert-Equal $evidence.authority.candidateAndDeployment 'GitHubR2' 'Candidate authority'
Assert-Equal $evidence.authority.registry 'GHCR' 'Registry authority'
Assert-Equal $evidence.authority.azure 'Shadow' 'Azure authority'
Assert-Equal $evidence.baseline.azurePipelineDefinitionId 5 'Azure Shadow definition'
Assert-Equal $evidence.baseline.azureShadowRunId 145 'Azure Shadow evidence run'
Assert-Equal $evidence.baseline.azureShadowRunResult 'Succeeded' 'Azure Shadow run result'
Assert-Equal $evidence.prValidation.authority 'GitHub' 'PR validation authority'
Assert-Equal $evidence.prValidation.azurePrTrigger $false 'Azure PR trigger boundary'
Assert-Equal $evidence.operationalRelease.status 'NoGo' 'Operational release status'
Assert-Equal $evidence.operationalRelease.candidateState 'Draft' 'Candidate state'
Assert-Equal $evidence.operationalRelease.pendingExternalInputCount 20 'Pending input count'

$expectedContexts = @(
    'windows-and-web',
    'android',
    'sql-integration',
    'crm-saas-public-contract',
    'crm-v1-prd'
)
Assert-Equal (($evidence.prValidation.requiredContexts | Sort-Object) -join ',') `
    (($expectedContexts | Sort-Object) -join ',') `
    'Required GitHub contexts'

$candidate = [IO.File]::ReadAllText($candidatePath, [Text.Encoding]::UTF8)
if ($candidate -notmatch '(?m)^\s*state:\s*Draft\s*$') {
    throw 'R2 v1.0.0 must remain Draft at engineering closeout.'
}
$pendingCount = [regex]::Matches($candidate, '(?m)^\s*status:\s*Pending\s*$').Count
Assert-Equal $pendingCount $evidence.operationalRelease.pendingExternalInputCount `
    'Candidate pending input count'

$report = [IO.File]::ReadAllText($reportPath, [Text.Encoding]::UTF8)
foreach ($requiredStatement in @(
    '状态：`Complete`（仓库与平台工程范围）',
    '生产发行状态：`No-Go`',
    'GitHub R2 是唯一候选/部署权威',
    'Azure Release Shadow S0 Run #145',
    '最终结果：`Succeeded`'
)) {
    if ($report.IndexOf($requiredStatement, [StringComparison]::Ordinal) -lt 0) {
        throw "Release/CD closeout report is missing: $requiredStatement"
    }
}

Write-Host 'Release/CD engineering closeout contract passed; operational R2 release remains No-Go.'
