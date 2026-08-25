[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "Assert-Cp6CiHostCapacity.ps1"
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "CI host capacity gate script was not found."
}

$sleepCounter = [pscustomobject]@{ Count = 0 }
$immediateSleep = { param([int]$Seconds) $sleepCounter.Count++ }.GetNewClosure()
$immediateResult = & $scriptPath `
    -MinimumFreeMemoryMiB 4608 `
    -MaxWaitSeconds 30 `
    -PollIntervalSeconds 10 `
    -MemoryProbe { [pscustomobject]@{ FreeMemoryMiB = 6144; TotalMemoryMiB = 16384 } } `
    -SleepAction $immediateSleep
if ($immediateResult.FreeMemoryMiB -ne 6144 -or
    $immediateResult.Polls -ne 1 -or
    $sleepCounter.Count -ne 0) {
    throw "CI host capacity gate did not pass an immediately safe host correctly."
}

$samples = [Collections.Generic.Queue[object]]::new()
$samples.Enqueue([pscustomobject]@{ FreeMemoryMiB = 4096; TotalMemoryMiB = 16384 })
$samples.Enqueue([pscustomobject]@{ FreeMemoryMiB = 5888; TotalMemoryMiB = 16384 })
$waitingSleepCounter = [pscustomobject]@{ Count = 0 }
$waitingProbe = { $samples.Dequeue() }.GetNewClosure()
$waitingSleep = {
    param([int]$Seconds)
    $waitingSleepCounter.Count++
}.GetNewClosure()
$waitingResult = & $scriptPath `
    -MinimumFreeMemoryMiB 4608 `
    -MaxWaitSeconds 30 `
    -PollIntervalSeconds 10 `
    -MemoryProbe $waitingProbe `
    -SleepAction $waitingSleep
if ($waitingResult.FreeMemoryMiB -ne 5888 -or
    $waitingResult.Polls -ne 2 -or
    $waitingSleepCounter.Count -ne 1) {
    throw "CI host capacity gate did not wait for a safe host correctly."
}

$unsafeSamples = [Collections.Generic.Queue[object]]::new()
1..3 | ForEach-Object {
    $unsafeSamples.Enqueue(
        [pscustomobject]@{ FreeMemoryMiB = 4096; TotalMemoryMiB = 16384 })
}
$unsafeProbe = { $unsafeSamples.Dequeue() }.GetNewClosure()
$unsafeSleepCounter = [pscustomobject]@{ Count = 0 }
$unsafeSleep = {
    param([int]$Seconds)
    $unsafeSleepCounter.Count++
}.GetNewClosure()
$failure = $null
try {
    & $scriptPath `
        -MinimumFreeMemoryMiB 4608 `
        -MaxWaitSeconds 20 `
        -PollIntervalSeconds 10 `
        -MemoryProbe $unsafeProbe `
        -SleepAction $unsafeSleep |
        Out-Null
}
catch {
    $failure = $_
}
if ($null -eq $failure -or
    $failure.Exception.Message -notmatch 'below the required 4608 MiB' -or
    $unsafeSleepCounter.Count -ne 2) {
    throw "CI host capacity gate did not fail closed after the bounded wait."
}

$invalidProbeFailure = $null
try {
    & $scriptPath `
        -MinimumFreeMemoryMiB 4608 `
        -MaxWaitSeconds 0 `
        -MemoryProbe { [pscustomobject]@{ TotalMemoryMiB = 16384 } } |
        Out-Null
}
catch {
    $invalidProbeFailure = $_
}
if ($null -eq $invalidProbeFailure -or
    $invalidProbeFailure.Exception.Message -notmatch 'did not return FreeMemoryMiB') {
    throw "CI host capacity gate accepted an incomplete memory probe."
}

Write-Host "CP6 CI host capacity gate test passed."
