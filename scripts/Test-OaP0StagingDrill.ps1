[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'OaP0StagingDrill.Common.psm1') -Force

$passed = 0
function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
    $script:passed++
}

function Assert-Throws {
    param([scriptblock]$Action, [string]$Message)
    try {
        & $Action
    }
    catch {
        $script:passed++
        return
    }
    throw $Message
}

$identity = New-OaP0StageIdentity `
    -UtcNow ([datetime]::SpecifyKind([datetime]'2026-07-23T12:34:56', 'Utc')) `
    -HexSuffix '12ab34cd'
Assert-True ($identity.DatabaseName -ceq 'CP6OaP0Stage_20260723123456_12ab34cd') `
    'Generated database name was not exact.'
Assert-True (Test-OaP0StageDatabaseName $identity.DatabaseName) `
    'Generated database name did not validate.'
Assert-True (-not (Test-OaP0StageDatabaseName 'CP6DB')) `
    'CP6DB must never validate as an isolated stage database.'
Assert-True (-not (Test-OaP0StageDatabaseName 'CP6OaP0Stage_20260723123456_12AB34CD')) `
    'Uppercase suffix must fail the exact database regex.'
Assert-True (Test-OaP0ContainerBackupPath $identity.ContainerBackupPath) `
    'Generated copied-backup path did not validate.'
Assert-True (-not (Test-OaP0ContainerBackupPath '/var/opt/mssql/backup/CP6DB.bak')) `
    'Unscoped copied-backup path must fail validation.'

$table = ConvertFrom-OaP0SqlCmdTable -Lines @(
    'Name^Rows',
    '----^----',
    'Wf_FlowDef^3',
    'Pur_PurchaseRequest^7',
    '(2 rows affected)'
)
Assert-True ($table.Count -eq 2) 'sqlcmd parser returned the wrong row count.'
Assert-True ($table[1].Rows -eq '7') 'sqlcmd parser returned the wrong value.'

$idempotent = [pscustomobject]@{}
foreach ($category in @(
    'FlowVersions', 'FormVersions', 'FlowPins', 'FormDataPins',
    'Bindings', 'Dependencies', 'Drafts')) {
    $idempotent | Add-Member -NotePropertyName $category `
        -NotePropertyValue ([pscustomobject]@{ Expected = 2; Inserted = 0; Skipped = 2; Errors = 0 })
}
Assert-OaP0SecondBackfillIsIdempotent -Report $idempotent
$passed++

$notIdempotent = $idempotent | ConvertTo-Json -Depth 5 | ConvertFrom-Json
$notIdempotent.FlowPins.Inserted = 1
Assert-Throws {
    Assert-OaP0SecondBackfillIsIdempotent -Report $notIdempotent
} 'A non-idempotent second backfill was accepted.'

$state = [pscustomobject]@{
    runId = $identity.RunId
    databaseName = $identity.DatabaseName
    containerBackupPath = $identity.ContainerBackupPath
}
Assert-OaP0CleanupState -State $state
$passed++
$badState = $state | ConvertTo-Json | ConvertFrom-Json
$badState.databaseName = 'CP6DB'
Assert-Throws { Assert-OaP0CleanupState -State $badState } `
    'Cleanup state accepted CP6DB.'

[pscustomobject]@{
    passed = $true
    assertions = $passed
} | ConvertTo-Json
