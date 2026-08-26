[CmdletBinding()]
param(
    [string]$AgentRoot = 'C:\agent',
    [string]$ExpectedAgentName = 'CP6-Windows',
    [string]$ExpectedPoolName = 'Default',
    [switch]$ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$resolvedAgentRoot = [IO.Path]::GetFullPath($AgentRoot)
$configurationPath = Join-Path $resolvedAgentRoot '.agent'
$listenerPath = Join-Path $resolvedAgentRoot 'bin\Agent.Listener.exe'

if (-not (Test-Path -LiteralPath $configurationPath -PathType Leaf)) {
    throw "Azure CI Agent configuration was not found at '$configurationPath'."
}
if (-not (Test-Path -LiteralPath $listenerPath -PathType Leaf)) {
    throw "Azure CI Agent listener was not found at '$listenerPath'."
}

$configuration = Get-Content -LiteralPath $configurationPath -Raw | ConvertFrom-Json
foreach ($propertyName in @('agentName', 'poolName')) {
    if ($configuration.PSObject.Properties.Name -notcontains $propertyName -or
        [string]::IsNullOrWhiteSpace([string]$configuration.$propertyName)) {
        throw "Azure CI Agent configuration is missing '$propertyName'."
    }
}
if (-not ([string]$configuration.agentName).Equals(
        $ExpectedAgentName,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected Azure CI Agent '$ExpectedAgentName', but found '$($configuration.agentName)'."
}
if (-not ([string]$configuration.poolName).Equals(
        $ExpectedPoolName,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw "Expected Azure CI pool '$ExpectedPoolName', but found '$($configuration.poolName)'."
}

$validated = [pscustomobject]@{
    AgentRoot = $resolvedAgentRoot
    AgentName = [string]$configuration.agentName
    PoolName = [string]$configuration.poolName
    ListenerPath = $listenerPath
}
if ($ValidateOnly) {
    $validated
    exit 0
}

$hadPsModulePath = Test-Path -LiteralPath 'Env:\PSModulePath'
$previousPsModulePath = if ($hadPsModulePath) { $env:PSModulePath } else { $null }
try {
    # Agent.Listener inherits its parent environment. An inherited PowerShell 7
    # PSModulePath makes Windows PowerShell tasks load duplicate type data.
    $env:PSModulePath = ''
    Write-Host "Starting Azure CI Agent '$ExpectedAgentName' in pool '$ExpectedPoolName'."
    Write-Host 'This foreground process can be stopped with Ctrl+C.'
    & $listenerPath run
    $listenerExitCode = $LASTEXITCODE
    if ($listenerExitCode -ne 0) {
        Write-Warning "Azure CI Agent listener exited with code $listenerExitCode."
    }
    exit $listenerExitCode
}
finally {
    if ($hadPsModulePath) {
        $env:PSModulePath = $previousPsModulePath
    }
    else {
        Remove-Item -LiteralPath 'Env:\PSModulePath' -ErrorAction SilentlyContinue
    }
}
