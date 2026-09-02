[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $repoRoot ".github\workflows\client-contract.yml"
$workflowLines = [IO.File]::ReadAllLines(
    $workflowPath,
    [Text.Encoding]::UTF8)

function Get-WorkflowStepLines {
    param([Parameter(Mandatory = $true)][string]$Name)

    $stepStart = -1
    for ($index = 0; $index -lt $workflowLines.Count; $index++) {
        if ($workflowLines[$index].Trim() -eq "- name: $Name") {
            $stepStart = $index
            break
        }
    }
    if ($stepStart -lt 0) {
        throw "client-contract is missing the $Name step."
    }

    $stepEnd = $workflowLines.Count
    for ($index = $stepStart + 1; $index -lt $workflowLines.Count; $index++) {
        if ($workflowLines[$index] -match '^\s{6}- name: ') {
            $stepEnd = $index
            break
        }
    }
    return @($workflowLines[$stepStart..($stepEnd - 1)])
}

function Assert-StepFailsClosed {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][Collections.IDictionary]$Commands
    )

    $stepLines = @(Get-WorkflowStepLines -Name $Name)
    if (-not ($stepLines | Where-Object { $_.Trim() -eq "shell: pwsh" })) {
        throw "$Name must explicitly use pwsh."
    }

    foreach ($entry in $Commands.GetEnumerator()) {
        $commandIndexes = @()
        for ($index = 0; $index -lt $stepLines.Count; $index++) {
            if ($stepLines[$index].Trim() -eq $entry.Key) {
                $commandIndexes += $index
            }
        }
        if ($commandIndexes.Count -ne 1) {
            throw "$Name must contain exactly one '$($entry.Key)' command."
        }

        $nextIndex = $commandIndexes[0] + 1
        while ($nextIndex -lt $stepLines.Count -and
               [string]::IsNullOrWhiteSpace($stepLines[$nextIndex])) {
            $nextIndex++
        }
        if ($nextIndex -ge $stepLines.Count -or
            $stepLines[$nextIndex].Trim() -ne $entry.Value) {
            throw "'$($entry.Key)' is not immediately guarded by its expected exit-code check."
        }
    }
}

$restoreCommands = [ordered]@{
    "dotnet restore CP6.WebApi/CP6.WebApi.csproj" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Web API restore failed.' }"
    "dotnet restore CP6.Desktop/CP6.Desktop.csproj" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Desktop restore failed.' }"
    "dotnet restore CP6.Client.Tests/CP6.Client.Tests.csproj" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Client test restore failed.' }"
    "dotnet tool restore" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Local tool restore failed.' }"
}

$requiredCommands = [ordered]@{
    "dotnet build CP6.WebApi/CP6.WebApi.csproj -c Release --no-restore" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Web API build failed.' }"
    "dotnet build CP6.Desktop/CP6.Desktop.csproj -c Release --no-restore" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Desktop build failed.' }"
    "dotnet test CP6.Tests/CP6.Tests.csproj -c Release" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Server tests failed.' }"
    "dotnet test CP6.Client.Tests/CP6.Client.Tests.csproj -c Release --no-restore" =
        "if (`$LASTEXITCODE -ne 0) { throw 'Client tests failed.' }"
}

Assert-StepFailsClosed -Name "Restore" -Commands $restoreCommands
Assert-StepFailsClosed -Name "Build and test .NET" -Commands $requiredCommands

$workflowText = $workflowLines -join "`n"
if ($workflowText -notmatch
    '(?ms)^\s{6}- name: Enforce client contract fail-closed behavior\s+shell: pwsh\s+run: \./scripts/test-client-contract-workflow\.ps1\s*$') {
    throw "client-contract must run its fail-closed workflow contract test."
}

Write-Host "Client contract workflow fails closed for every .NET build and test command."
