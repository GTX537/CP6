param(
    [Parameter(Mandatory)][string]$CollectorDll,
    [Parameter(Mandatory)][string]$DotnetHost
)

$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $CollectorDll -PathType Leaf)) { throw 'Preflight collector DLL is missing.' }
if (-not (Test-Path -LiteralPath $DotnetHost -PathType Leaf)) { throw 'The selected dotnet host is missing.' }
$taskCollector = (Resolve-Path -LiteralPath $CollectorDll).Path
$taskHost = (Resolve-Path -LiteralPath $DotnetHost).Path
$taskScratch = [IO.Directory]::CreateTempSubdirectory('cp6-p10-preflight-boundaries-').FullName
$taskExisting = [IO.Directory]::CreateDirectory((Join-Path $taskScratch 'existing')).FullName
$taskMarker = Join-Path $taskExisting 'marker.txt'
[IO.File]::WriteAllText($taskMarker, 'caller-data-must-remain')
$taskCount = 0

function Expect-Refusal([string[]]$CaseArguments, [string]$Token, [int]$ExpectedExit, [string]$ExpectedCode) {
    $taskStart = [Diagnostics.ProcessStartInfo]::new($taskHost)
    $taskStart.UseShellExecute = $false
    $taskStart.CreateNoWindow = $true
    $taskStart.RedirectStandardOutput = $true
    $taskStart.RedirectStandardError = $true
    $taskStart.Environment.Clear()
    foreach ($taskName in @('SystemRoot', 'WINDIR')) {
        $taskValue = [Environment]::GetEnvironmentVariable($taskName)
        if ($taskValue) { $taskStart.Environment[$taskName] = $taskValue }
    }
    $taskStart.Environment['DOTNET_ROOT'] = Split-Path -Parent $taskHost
    foreach ($taskName in @('HOME', 'USERPROFILE', 'TMP', 'TEMP', 'TMPDIR', 'DOTNET_CLI_HOME')) {
        $taskStart.Environment[$taskName] = $taskScratch
    }
    if ($Token) { $taskStart.Environment['P10_FEED_READ_TOKEN'] = $Token }
    $taskStart.ArgumentList.Add($taskCollector)
    foreach ($taskArgument in $CaseArguments) { $taskStart.ArgumentList.Add($taskArgument) }
    $taskProcess = [Diagnostics.Process]::Start($taskStart)
    try {
        $taskStdout = $taskProcess.StandardOutput.ReadToEndAsync()
        $taskStderr = $taskProcess.StandardError.ReadToEndAsync()
        if (-not $taskProcess.WaitForExit(30000)) { throw 'Boundary refusal did not finish before the timeout.' }
        $taskOutput = $taskStdout.GetAwaiter().GetResult()
        $taskError = $taskStderr.GetAwaiter().GetResult()
        if ($taskProcess.ExitCode -ne $ExpectedExit -or $taskError.Trim() -cne $ExpectedCode -or $taskOutput.Length -ne 0) {
            throw "Unexpected preflight boundary result for $ExpectedCode."
        }
        $script:taskCount++
    } finally {
        if (-not $taskProcess.HasExited) { $taskProcess.Kill($true); $taskProcess.WaitForExit() }
        $taskProcess.Dispose()
    }
}

Expect-Refusal @() '' 64 'preflight-input-args'
Expect-Refusal @('relative-output') 'not-a-real-token' 64 'preflight-input-path'
Expect-Refusal @($taskExisting) 'not-a-real-token' 64 'preflight-input-path'
$taskAbsent = Join-Path $taskScratch 'never-created'
Expect-Refusal @($taskAbsent) '' 65 'preflight-input-credential'
Expect-Refusal @($taskAbsent) "invalid`ntoken" 65 'preflight-input-credential'
$taskLink = Join-Path $taskScratch 'link'
$taskLinkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
New-Item -ItemType $taskLinkType -Path $taskLink -Target $taskExisting | Out-Null
Expect-Refusal @($taskLink) 'not-a-real-token' 64 'preflight-input-path'
Expect-Refusal @((Join-Path $taskLink 'nested')) 'not-a-real-token' 64 'preflight-input-path'
if (Test-Path -LiteralPath $taskAbsent) { throw 'Refused output path was created.' }
if (Test-Path -LiteralPath (Join-Path $taskExisting 'nested')) { throw 'Symlink-parent output was created.' }
if ([IO.File]::ReadAllText($taskMarker) -cne 'caller-data-must-remain') { throw 'Caller marker changed.' }
Write-Output "p10-preflight-input-boundaries passed=$taskCount"
