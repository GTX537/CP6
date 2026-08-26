[CmdletBinding()]
param(
    [ValidateRange(1, 1048576)]
    [int]$MinimumFreeMemoryMiB = 2048,

    [ValidateRange(1, 20)]
    [int]$RequiredConsecutiveSuccesses = 3,

    [ValidateRange(0, 86400)]
    [int]$MaxWaitSeconds = 600,

    [ValidateRange(1, 3600)]
    [int]$PollIntervalSeconds = 10,

    [ValidateSet('CP6_DEV')]
    [string]$Database = 'CP6_DEV',

    [string]$ServerInstance = 'localhost\KOUSQLSERVER',

    [ValidateSet('cp6_dev_backup')]
    [string]$BackupAccount = 'cp6_dev_backup',

    [string]$PasswordEnvironmentVariable = 'CP6_DEV_DB_BACKUP_PASSWORD',

    [string]$EvidencePath = '',

    [string]$SqlcmdPath = '',

    [scriptblock]$MemoryProbe = {
        $operatingSystem = Get-CimInstance Win32_OperatingSystem
        [pscustomobject]@{
            FreeMemoryMiB = [math]::Floor($operatingSystem.FreePhysicalMemory / 1024)
            TotalMemoryMiB = [math]::Floor($operatingSystem.TotalVisibleMemorySize / 1024)
        }
    },

    [scriptblock]$SqlProbe = {
        param(
            [string]$Executable,
            [string]$Instance,
            [string]$Account,
            [string]$DatabaseName
        )

        $query = @"
SET NOCOUNT ON;
IF DB_ID(N'$DatabaseName') IS NULL THROW 51000, '$DatabaseName does not exist.', 1;
IF EXISTS (SELECT 1 FROM sys.databases WHERE name = N'$DatabaseName' AND state_desc <> N'ONLINE')
    THROW 51001, '$DatabaseName is not ONLINE.', 1;
SELECT 1;
"@
        $output = @(& $Executable `
            -S $Instance `
            -U $Account `
            -C `
            -b `
            -V 16 `
            -l 5 `
            -t 5 `
            -h -1 `
            -W `
            -Q $query 2>&1)
        $exitCode = $LASTEXITCODE
        foreach ($line in $output) {
            Write-Host $line
        }
        [pscustomobject]@{
            Succeeded = $exitCode -eq 0
            ExitCode = $exitCode
        }
    },

    [scriptblock]$SleepAction = {
        param([int]$Seconds)
        Start-Sleep -Seconds $Seconds
    }
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$sqlcmdModulePath = Join-Path $PSScriptRoot 'Cp6.Sqlcmd.psm1'
Import-Module $sqlcmdModulePath -Force
$SqlcmdPath = Resolve-Cp6SqlcmdPath -SqlcmdPath $SqlcmdPath

$password = [Environment]::GetEnvironmentVariable($PasswordEnvironmentVariable, 'Process')
if ([string]::IsNullOrWhiteSpace($password)) {
    throw "Required backup Secret '$PasswordEnvironmentVariable' is missing."
}

$resolvedEvidencePath = ''
if (-not [string]::IsNullOrWhiteSpace($EvidencePath)) {
    $resolvedEvidencePath = [IO.Path]::GetFullPath($EvidencePath)
    [IO.Directory]::CreateDirectory((Split-Path -Parent $resolvedEvidencePath)) | Out-Null
}

$startedAtUtc = [DateTime]::UtcNow
$maximumPolls = [math]::Ceiling($MaxWaitSeconds / [double]$PollIntervalSeconds)
$samples = [Collections.Generic.List[object]]::new()
$consecutiveSuccesses = 0
$lastSqlError = ''

function Write-ReadinessEvidence {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('passed', 'failed')]
        [string]$Status,

        [Parameter(Mandatory = $true)]
        [string]$Reason
    )

    $evidence = [ordered]@{
        schemaVersion = 1
        environment = 'cp6-dev'
        database = $Database
        status = $Status
        reason = $Reason
        startedAtUtc = $startedAtUtc.ToString('o')
        completedAtUtc = [DateTime]::UtcNow.ToString('o')
        policy = [ordered]@{
            minimumFreeMemoryMiB = $MinimumFreeMemoryMiB
            requiredConsecutiveSuccesses = $RequiredConsecutiveSuccesses
            maxWaitSeconds = $MaxWaitSeconds
            pollIntervalSeconds = $PollIntervalSeconds
            sqlLoginTimeoutSeconds = 5
            sqlQueryTimeoutSeconds = 5
        }
        samples = @($samples)
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedEvidencePath)) {
        $evidence | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath $resolvedEvidencePath -Encoding utf8
    }

    return [pscustomobject]$evidence
}

$previousSqlCmdPassword = [Environment]::GetEnvironmentVariable('SQLCMDPASSWORD', 'Process')
try {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $password, 'Process')

    for ($poll = 0; $poll -le $maximumPolls; $poll++) {
        $snapshot = & $MemoryProbe
        if ($null -eq $snapshot -or
            $null -eq $snapshot.PSObject.Properties['FreeMemoryMiB'] -or
            $null -eq $snapshot.PSObject.Properties['TotalMemoryMiB']) {
            throw 'The DEV backup readiness memory probe must return FreeMemoryMiB and TotalMemoryMiB.'
        }

        $freeMemoryMiB = [long]$snapshot.FreeMemoryMiB
        $totalMemoryMiB = [long]$snapshot.TotalMemoryMiB
        if ($freeMemoryMiB -lt 0 -or
            $totalMemoryMiB -le 0 -or
            $freeMemoryMiB -gt $totalMemoryMiB) {
            throw 'The DEV backup readiness memory probe returned invalid memory values.'
        }

        $freeMemoryPercent = [math]::Round(
            ($freeMemoryMiB / [double]$totalMemoryMiB) * 100,
            2)
        $memoryReady = $freeMemoryMiB -ge $MinimumFreeMemoryMiB
        $sqlReady = $false
        $sqlExitCode = $null
        $sqlError = ''

        if ($memoryReady) {
            try {
                $sqlResult = & $SqlProbe $SqlcmdPath $ServerInstance $BackupAccount $Database
                if ($sqlResult -is [bool]) {
                    $sqlReady = $sqlResult
                }
                elseif ($null -ne $sqlResult -and
                    $null -ne $sqlResult.PSObject.Properties['Succeeded']) {
                    $sqlReady = [bool]$sqlResult.Succeeded
                    if ($null -ne $sqlResult.PSObject.Properties['ExitCode'] -and
                        $null -ne $sqlResult.ExitCode) {
                        $sqlExitCode = [int]$sqlResult.ExitCode
                    }
                }
                else {
                    throw 'The SQL readiness probe did not return a success result.'
                }
            }
            catch {
                $sqlError = $_.Exception.Message
                $lastSqlError = $sqlError
            }
        }

        if ($memoryReady -and $sqlReady) {
            $consecutiveSuccesses++
        }
        else {
            $consecutiveSuccesses = 0
        }

        $samples.Add([pscustomobject]@{
            sampledAtUtc = [DateTime]::UtcNow.ToString('o')
            freeMemoryMiB = $freeMemoryMiB
            totalMemoryMiB = $totalMemoryMiB
            freeMemoryPercent = $freeMemoryPercent
            memoryReady = $memoryReady
            sqlReady = $sqlReady
            sqlExitCode = $sqlExitCode
            sqlError = $sqlError
            consecutiveSuccesses = $consecutiveSuccesses
        })

        Write-Host (
            'DEV backup readiness sample {0}/{1}: free={2} MiB ({3}%), required={4} MiB, SQL={5}, consecutive={6}/{7}.' -f
            ($poll + 1),
            ($maximumPolls + 1),
            $freeMemoryMiB,
            $freeMemoryPercent,
            $MinimumFreeMemoryMiB,
            $sqlReady,
            $consecutiveSuccesses,
            $RequiredConsecutiveSuccesses)

        if ($consecutiveSuccesses -ge $RequiredConsecutiveSuccesses) {
            $result = Write-ReadinessEvidence `
                -Status passed `
                -Reason 'Memory and independent SQL login readiness requirements were satisfied.'
            Write-Host 'DEV backup readiness gate passed.'
            return $result
        }

        if ($poll -eq $maximumPolls) {
            $failureReason = (
                'Readiness did not reach {0} consecutive successes with at least {1} MiB free memory before the {2}-second timeout.' -f
                $RequiredConsecutiveSuccesses,
                $MinimumFreeMemoryMiB,
                $MaxWaitSeconds)
            if (-not [string]::IsNullOrWhiteSpace($lastSqlError)) {
                $failureReason += " Last SQL probe error: $lastSqlError"
            }
            Write-ReadinessEvidence -Status failed -Reason $failureReason | Out-Null
            throw "DEV backup readiness gate failed: $failureReason"
        }

        & $SleepAction $PollIntervalSeconds
    }
}
finally {
    [Environment]::SetEnvironmentVariable('SQLCMDPASSWORD', $previousSqlCmdPassword, 'Process')
}
