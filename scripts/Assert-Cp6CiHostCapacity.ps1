[CmdletBinding()]
param(
    [ValidateRange(1, 1048576)]
    [int]$MinimumFreeMemoryMiB = 5632,

    [ValidateRange(0, 86400)]
    [int]$MaxWaitSeconds = 600,

    [ValidateRange(1, 3600)]
    [int]$PollIntervalSeconds = 15,

    [scriptblock]$MemoryProbe = {
        $operatingSystem = Get-CimInstance Win32_OperatingSystem
        [pscustomobject]@{
            FreeMemoryMiB = [math]::Floor($operatingSystem.FreePhysicalMemory / 1024)
            TotalMemoryMiB = [math]::Floor($operatingSystem.TotalVisibleMemorySize / 1024)
        }
    },

    [scriptblock]$SleepAction = {
        param([int]$Seconds)
        Start-Sleep -Seconds $Seconds
    }
)

$ErrorActionPreference = "Stop"
$maximumPolls = [math]::Ceiling($MaxWaitSeconds / [double]$PollIntervalSeconds)

for ($poll = 0; $poll -le $maximumPolls; $poll++) {
    $snapshot = & $MemoryProbe
    if ($null -eq $snapshot -or $null -eq $snapshot.FreeMemoryMiB) {
        throw "The CI host memory probe did not return FreeMemoryMiB."
    }

    $freeMemoryMiB = [int]$snapshot.FreeMemoryMiB
    $totalMemoryMiB = if ($null -ne $snapshot.TotalMemoryMiB) {
        [int]$snapshot.TotalMemoryMiB
    }
    else {
        0
    }
    if ($freeMemoryMiB -lt 0 -or $totalMemoryMiB -lt 0) {
        throw "The CI host memory probe returned a negative memory value."
    }

    Write-Host (
        "CI host capacity sample {0}/{1}: free={2} MiB, total={3} MiB, required={4} MiB." -f
        ($poll + 1),
        ($maximumPolls + 1),
        $freeMemoryMiB,
        $totalMemoryMiB,
        $MinimumFreeMemoryMiB)

    if ($freeMemoryMiB -ge $MinimumFreeMemoryMiB) {
        Write-Host "CI host capacity gate passed."
        return [pscustomobject]@{
            FreeMemoryMiB = $freeMemoryMiB
            TotalMemoryMiB = $totalMemoryMiB
            MinimumFreeMemoryMiB = $MinimumFreeMemoryMiB
            Polls = $poll + 1
        }
    }

    if ($poll -eq $maximumPolls) {
        throw (
            "CI host capacity gate failed: free memory remained at {0} MiB, below the required {1} MiB after waiting up to {2} seconds." -f
            $freeMemoryMiB,
            $MinimumFreeMemoryMiB,
            $MaxWaitSeconds)
    }

    & $SleepAction $PollIntervalSeconds
}
