[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Wait-Cp6DevBackupReadiness.ps1'
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw 'DEV backup readiness gate script was not found.'
}

function Assert-Throws {
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$ExpectedMessage
    )

    try {
        & $Action
    }
    catch {
        if ($_.Exception.Message -notmatch [regex]::Escape($ExpectedMessage)) {
            throw "Expected '$ExpectedMessage', received '$($_.Exception.Message)'."
        }
        return
    }

    throw "Expected '$ExpectedMessage', but the action succeeded."
}

$originalBackupPassword = [Environment]::GetEnvironmentVariable(
    'CP6_DEV_DB_BACKUP_PASSWORD',
    'Process')
$originalSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
$tempRoot = Join-Path ([IO.Path]::GetTempPath()) "cp6-dev-backup-readiness-$([Guid]::NewGuid().ToString('N'))"
$resolvedTempRoot = [IO.Path]::GetFullPath($tempRoot)
$resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
if (-not $resolvedTempRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing unsafe test directory '$resolvedTempRoot'."
}

try {
    [IO.Directory]::CreateDirectory($resolvedTempRoot) | Out-Null
    $fakeSqlcmdPath = Join-Path $resolvedTempRoot 'sqlcmd.exe'
    [IO.File]::WriteAllBytes($fakeSqlcmdPath, [byte[]]@(0))
    [Environment]::SetEnvironmentVariable('CP6_DEV_DB_BACKUP_PASSWORD', $null, 'Process')
    Assert-Throws `
        -Action {
            & $scriptPath `
                -SqlcmdPath $fakeSqlcmdPath `
                -MaxWaitSeconds 0 `
                -MemoryProbe { [pscustomobject]@{ FreeMemoryMiB = 4096; TotalMemoryMiB = 16384 } } `
                -SqlProbe { $true } |
                Out-Null
        } `
        -ExpectedMessage "Required backup Secret 'CP6_DEV_DB_BACKUP_PASSWORD' is missing."

    [Environment]::SetEnvironmentVariable(
        'CP6_DEV_DB_BACKUP_PASSWORD',
        'test-only-secret',
        'Process')
    $sqlcmdSentinel = [Guid]::NewGuid().ToString('N')
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $sqlcmdSentinel, 'Process')

    $immediateSleepCount = [pscustomobject]@{ Value = 0 }
    $immediateResult = & $scriptPath `
        -SqlcmdPath $fakeSqlcmdPath `
        -MinimumFreeMemoryMiB 2048 `
        -RequiredConsecutiveSuccesses 3 `
        -MaxWaitSeconds 30 `
        -PollIntervalSeconds 10 `
        -MemoryProbe { [pscustomobject]@{ FreeMemoryMiB = 3072; TotalMemoryMiB = 16384 } } `
        -SqlProbe { [pscustomobject]@{ Succeeded = $true; ExitCode = 0 } } `
        -SleepAction { param([int]$Seconds) $immediateSleepCount.Value++ }.GetNewClosure()
    if ($immediateResult.status -ne 'passed' -or
        $immediateResult.samples.Count -ne 3 -or
        $immediateSleepCount.Value -ne 2) {
        throw 'DEV backup readiness gate did not require three consecutive safe samples.'
    }

    $memorySamples = [Collections.Generic.Queue[object]]::new()
    $memorySamples.Enqueue([pscustomobject]@{ FreeMemoryMiB = 768; TotalMemoryMiB = 16384 })
    1..4 | ForEach-Object {
        $memorySamples.Enqueue([pscustomobject]@{ FreeMemoryMiB = 2560; TotalMemoryMiB = 16384 })
    }
    $sqlSamples = [Collections.Generic.Queue[object]]::new()
    $sqlSamples.Enqueue([pscustomobject]@{ Succeeded = $false; ExitCode = 1 })
    1..3 | ForEach-Object {
        $sqlSamples.Enqueue([pscustomobject]@{ Succeeded = $true; ExitCode = 0 })
    }
    $waitingSleepCount = [pscustomobject]@{ Value = 0 }
    $waitingResult = & $scriptPath `
        -SqlcmdPath $fakeSqlcmdPath `
        -MinimumFreeMemoryMiB 2048 `
        -RequiredConsecutiveSuccesses 3 `
        -MaxWaitSeconds 50 `
        -PollIntervalSeconds 10 `
        -MemoryProbe { $memorySamples.Dequeue() }.GetNewClosure() `
        -SqlProbe { $sqlSamples.Dequeue() }.GetNewClosure() `
        -SleepAction { param([int]$Seconds) $waitingSleepCount.Value++ }.GetNewClosure()
    if ($waitingResult.status -ne 'passed' -or
        $waitingResult.samples.Count -ne 5 -or
        $waitingSleepCount.Value -ne 4 -or
        $waitingResult.samples[0].sqlReady) {
        throw 'DEV backup readiness gate did not recover safely from low memory and a transient SQL failure.'
    }

    $failureEvidencePath = Join-Path $resolvedTempRoot 'failed-readiness.json'
    $sqlProbeCount = [pscustomobject]@{ Value = 0 }
    Assert-Throws `
        -Action {
            & $scriptPath `
                -SqlcmdPath $fakeSqlcmdPath `
                -MinimumFreeMemoryMiB 2048 `
                -RequiredConsecutiveSuccesses 3 `
                -MaxWaitSeconds 20 `
                -PollIntervalSeconds 10 `
                -EvidencePath $failureEvidencePath `
                -MemoryProbe { [pscustomobject]@{ FreeMemoryMiB = 768; TotalMemoryMiB = 16384 } } `
                -SqlProbe { $sqlProbeCount.Value++; $true }.GetNewClosure() `
                -SleepAction { param([int]$Seconds) }
        } `
        -ExpectedMessage 'DEV backup readiness gate failed'
    $failureEvidence = Get-Content -LiteralPath $failureEvidencePath -Raw | ConvertFrom-Json
    if ($failureEvidence.status -ne 'failed' -or
        $failureEvidence.samples.Count -ne 3 -or
        $sqlProbeCount.Value -ne 0) {
        throw 'DEV backup readiness gate did not fail closed before SQL access on a low-memory host.'
    }

    Assert-Throws `
        -Action {
            & $scriptPath `
                -SqlcmdPath $fakeSqlcmdPath `
                -MaxWaitSeconds 0 `
                -MemoryProbe { [pscustomobject]@{ FreeMemoryMiB = 4096 } } `
                -SqlProbe { $true } |
                Out-Null
        } `
        -ExpectedMessage 'must return FreeMemoryMiB and TotalMemoryMiB'

    $restoredSqlcmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
    if ($restoredSqlcmdPassword -ne $sqlcmdSentinel) {
        throw 'DEV backup readiness gate did not restore the previous SQLCMDPASSWORD value.'
    }

    Write-Host 'CP6 DEV backup readiness behavior test passed (5 scenarios).'
}
finally {
    [Environment]::SetEnvironmentVariable(
        'CP6_DEV_DB_BACKUP_PASSWORD',
        $originalBackupPassword,
        'Process')
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $originalSqlcmdPassword, 'Process')
    if (Test-Path -LiteralPath $resolvedTempRoot) {
        Remove-Item -LiteralPath $resolvedTempRoot -Recurse -Force
    }
}
