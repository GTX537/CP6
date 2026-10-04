# Child processes receive credentials through their private environment only.
Set-StrictMode -Version Latest
$script:Cp6Processes = @{}

function Start-Cp6CompatibilityProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [hashtable]$ChildEnvironment = @{}
    )
    $work = [IO.Path]::GetFullPath($WorkingDirectory)
    $logs = [IO.Path]::GetFullPath($LogDirectory)
    if (!(Test-Path -LiteralPath $work -PathType Container) -or (Test-Path -LiteralPath $logs)) {
        throw 'CP6_COMPAT_PROCESS_DIRECTORY_INVALID'
    }
    $null = New-Item -ItemType Directory -Path $logs
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = $Executable
    $info.WorkingDirectory = $work
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    foreach ($argument in $ArgumentList) { $info.ArgumentList.Add($argument) }
    # Isolate discovery of the selected lane from caller/user environment settings.
    foreach ($name in @($info.Environment.Keys)) {
        if ($name -cmatch '^CP6_(?:COMPAT_|TEST_|(?:CORE|WMS|WP5_CORE|SPACE|SPACE_MIGRATION|OIDC|ERP|BUG150)_TEST_|C02_TEST_)') {
            $null = $info.Environment.Remove($name)
        }
    }
    foreach ($name in $ChildEnvironment.Keys) {
        if ($null -eq $ChildEnvironment[$name]) { $null = $info.Environment.Remove($name) }
        else { $info.Environment[$name] = [string]$ChildEnvironment[$name] }
    }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $started = $false
    try {
        if (!$process.Start()) { throw 'CP6_COMPAT_PROCESS_START_FAILED' }
        $started = $true
        $handle = [Guid]::NewGuid().ToString('N')
        $script:Cp6Processes[$handle] = @{
            Process = $process; OutputTask = $process.StandardOutput.ReadToEndAsync()
            ErrorTask = $process.StandardError.ReadToEndAsync(); LogDirectory = $logs
            StartedUtc = [DateTime]::UtcNow; ForcedTermination = $false
        }
        # The public handle deliberately has no ProcessStartInfo/environment object.
        return [pscustomobject]@{ Handle = $handle; ProcessId = $process.Id; StartedUtc = $script:Cp6Processes[$handle].StartedUtc.ToString('o') }
    }
    catch {
        if ($started -and !$process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
        throw 'CP6_COMPAT_PROCESS_START_FAILED'
    }
}

function Get-Cp6CompatibilityProcessState {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Handle)
    $entry = $script:Cp6Processes[$Handle.Handle]
    if ($null -eq $entry -or $entry.Process.Id -ne $Handle.ProcessId) { throw 'CP6_COMPAT_PROCESS_HANDLE_INVALID' }
    return [pscustomobject]@{ ProcessId = $entry.Process.Id; HasExited = $entry.Process.HasExited; ExitCode = $(if ($entry.Process.HasExited) { $entry.Process.ExitCode } else { $null }) }
}

function Complete-Cp6CompatibilityProcess {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Handle, [switch]$TerminateOwnedProcess)
    $entry = $script:Cp6Processes[$Handle.Handle]
    if ($null -eq $entry -or $entry.Process.Id -ne $Handle.ProcessId) { throw 'CP6_COMPAT_PROCESS_HANDLE_INVALID' }
    $process = $entry.Process
    if (!$process.HasExited) {
        if (!$TerminateOwnedProcess) { throw 'CP6_COMPAT_PROCESS_STILL_RUNNING' }
        $entry.ForcedTermination = $true
        $process.Kill($true)
        if (!$process.WaitForExit(15000)) { throw 'CP6_COMPAT_PROCESS_DID_NOT_EXIT' }
    }
    $stdout = Join-Path $entry.LogDirectory 'stdout.log'
    $stderr = Join-Path $entry.LogDirectory 'stderr.log'
    [IO.File]::WriteAllText($stdout, $entry.OutputTask.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($stderr, $entry.ErrorTask.GetAwaiter().GetResult(), [Text.UTF8Encoding]::new($false))
    $result = [pscustomobject]@{
        ProcessId = $process.Id; ExitCode = $process.ExitCode
        StartedUtc = $entry.StartedUtc.ToString('o'); FinishedUtc = [DateTime]::UtcNow.ToString('o')
        OwnedProcessTerminated = $entry.ForcedTermination
        StandardOutputSha256 = (Get-FileHash -LiteralPath $stdout -Algorithm SHA256).Hash
        StandardErrorSha256 = (Get-FileHash -LiteralPath $stderr -Algorithm SHA256).Hash
    }
    $process.Dispose()
    $script:Cp6Processes.Remove($Handle.Handle)
    return $result
}

function Invoke-Cp6CompatibilityProcess {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string[]]$ArgumentList,
        [Parameter(Mandatory)][string]$WorkingDirectory,
        [Parameter(Mandatory)][string]$LogDirectory,
        [hashtable]$ChildEnvironment = @{},
        [ValidateRange(1,3600)][int]$TimeoutSeconds = 600
    )
    $handle = Start-Cp6CompatibilityProcess -Executable $Executable -ArgumentList $ArgumentList -WorkingDirectory $WorkingDirectory -LogDirectory $LogDirectory -ChildEnvironment $ChildEnvironment
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    try {
        while (!(Get-Cp6CompatibilityProcessState $handle).HasExited -and [DateTime]::UtcNow -lt $deadline) {
            Start-Sleep -Milliseconds 100
        }
        $timedOut = !(Get-Cp6CompatibilityProcessState $handle).HasExited
        $result = Complete-Cp6CompatibilityProcess -Handle $handle -TerminateOwnedProcess:$timedOut
        $result | Add-Member -NotePropertyName TimedOut -NotePropertyValue $timedOut
        return $result
    }
    finally {
        if ($script:Cp6Processes.ContainsKey($handle.Handle)) {
            $null = Complete-Cp6CompatibilityProcess -Handle $handle -TerminateOwnedProcess
        }
    }
}

function Assert-Cp6CompatibilityListener {
    [CmdletBinding()]
    param([Parameter(Mandatory)]$Handle, [Parameter(Mandatory)][int]$Port)
    if ((Get-Cp6CompatibilityProcessState $Handle).HasExited) { throw 'CP6_COMPAT_API_EXITED' }
    # This runner's PID/listener evidence is currently supported on Windows.
    if (!$IsWindows) { throw 'CP6_COMPAT_LISTENER_IDENTITY_REQUIRES_WINDOWS' }
    $listeners = @(Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue)
    if ($listeners.Count -eq 0) { return $false }
    if (@($listeners | Where-Object { $_.OwningProcess -ne $Handle.ProcessId -or $_.LocalAddress -notin @('127.0.0.1','::1') }).Count -ne 0) {
        throw 'CP6_COMPAT_LISTENER_IDENTITY_MISMATCH'
    }
    return $true
}

Export-ModuleMember -Function Start-Cp6CompatibilityProcess, Get-Cp6CompatibilityProcessState, Complete-Cp6CompatibilityProcess, Invoke-Cp6CompatibilityProcess, Assert-Cp6CompatibilityListener
