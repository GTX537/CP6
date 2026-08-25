[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$startScript = Join-Path $PSScriptRoot 'Start-Cp6CiAgent.ps1'
$failures = @()

function Add-ContractFailure {
    param([string]$Message)
    $script:failures += $Message
}

if (-not (Test-Path -LiteralPath $startScript -PathType Leaf)) {
    Add-ContractFailure "Missing file: $startScript"
}
else {
    $tokens = $null
    $parseErrors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile(
        $startScript,
        [ref]$tokens,
        [ref]$parseErrors)
    foreach ($parseError in $parseErrors) {
        Add-ContractFailure "PowerShell parse error: $($parseError.Message)"
    }

    $scriptText = Get-Content -LiteralPath $startScript -Raw
    $requiredPatterns = [ordered]@{
        'default CI agent root' = 'AgentRoot\s*=\s*''C:\\agent'''
        'expected CI agent name' = 'ExpectedAgentName\s*=\s*''CP6-Windows'''
        'expected CI pool name' = 'ExpectedPoolName\s*=\s*''Default'''
        'configuration validation' = 'Join-Path\s+\$resolvedAgentRoot\s+''\.agent'''
        'foreground listener' = '&\s+\$listenerPath\s+run'
        'empty inherited module path' = '\$env:PSModulePath\s*=\s*'''''
        'environment restoration' = '(?s)finally\s*\{.*?PSModulePath'
        'non-starting validation mode' = '\$ValidateOnly'
    }
    foreach ($entry in $requiredPatterns.GetEnumerator()) {
        if ($scriptText -notmatch $entry.Value) {
            Add-ContractFailure "Missing contract: $($entry.Key)"
        }
    }

    foreach ($pattern in @(
        '(?i)Start-Process',
        '(?i)SetEnvironmentVariable[^\r\n]+(?:Machine|User)',
        '(?i)powercfg|schtasks|SetSuspendState',
        '(?i)Stop-Service|Restart-Service',
        '(?i)token|password|secret'
    )) {
        if ($scriptText -match $pattern) {
            Add-ContractFailure "Forbidden pattern found: $pattern"
        }
    }

    $temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) "cp6-ci-agent-contract-$([Guid]::NewGuid().ToString('N'))"
    try {
        [IO.Directory]::CreateDirectory((Join-Path $temporaryRoot 'bin')) | Out-Null
        [IO.File]::WriteAllBytes((Join-Path $temporaryRoot 'bin\Agent.Listener.exe'), [byte[]]@())
        [IO.File]::WriteAllText(
            (Join-Path $temporaryRoot '.agent'),
            '{"agentName":"CP6-Windows","poolName":"Default"}',
            [Text.UTF8Encoding]::new($false))

        $validated = & $startScript -AgentRoot $temporaryRoot -ValidateOnly
        if ($validated.AgentName -ne 'CP6-Windows' -or $validated.PoolName -ne 'Default') {
            Add-ContractFailure 'ValidateOnly did not return the expected CI Agent identity.'
        }

        [IO.File]::WriteAllText(
            (Join-Path $temporaryRoot '.agent'),
            '{"agentName":"CP6-Windows","poolName":"WrongPool"}',
            [Text.UTF8Encoding]::new($false))
        $rejectedWrongPool = $false
        try {
            & $startScript -AgentRoot $temporaryRoot -ValidateOnly | Out-Null
        }
        catch {
            $rejectedWrongPool = $_.Exception.Message -match "Expected Azure CI pool 'Default'"
        }
        if (-not $rejectedWrongPool) {
            Add-ContractFailure 'ValidateOnly did not reject the wrong Agent pool.'
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryRoot) {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host 'CP6 CI Agent start contract test failed:'
    $failures | ForEach-Object { Write-Host " - $_" }
    exit 1
}

Write-Host 'CP6 CI Agent start contract test passed.'
Write-Host 'Verified identity, pool, foreground launch, PSModulePath isolation, restoration, validation mode, and safety boundaries.'
exit 0
