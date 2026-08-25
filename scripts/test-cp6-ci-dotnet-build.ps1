[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptPath = Join-Path $PSScriptRoot "Invoke-Cp6CiDotNetBuild.ps1"
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "CI isolated .NET build script was not found."
}

$expectedProjects = @{
    BackendRuntime = @(
        "CP6.Entity\CP6.Entity.csproj",
        "CP6.Core\CP6.Core.csproj",
        "CP6.Space.Contracts\CP6.Space.Contracts.csproj",
        "CP6.Space.Domain\CP6.Space.Domain.csproj",
        "CP6.Space.Application\CP6.Space.Application.csproj",
        "CP6.Space.Infrastructure\CP6.Space.Infrastructure.csproj",
        "CP6.WebApi\CP6.WebApi.csproj"
    )
    BackendTests = @("CP6.Tests\CP6.Tests.csproj")
    ClientTests = @(
        "CP6.Client.Api\CP6.Client.Api.csproj",
        "CP6.Client.Core\CP6.Client.Core.csproj",
        "CP6.Client.Tests\CP6.Client.Tests.csproj"
    )
}
$requiredFlags = @(
    "--no-restore",
    "--no-dependencies",
    "--disable-build-servers",
    "-m:1",
    "-p:BuildInParallel=false",
    "-p:UseSharedCompilation=false"
)

foreach ($graph in @("BackendRuntime", "BackendTests", "ClientTests")) {
    $calls = [Collections.Generic.List[object]]::new()
    $invoker = {
        param([string[]]$BuildArguments)
        $calls.Add(@($BuildArguments))
        return 0
    }.GetNewClosure()
    & $scriptPath -Graph $graph -CommandInvoker $invoker

    if ($calls.Count -ne $expectedProjects[$graph].Count) {
        throw "CI '$graph' build graph invoked an unexpected number of projects."
    }
    for ($index = 0; $index -lt $calls.Count; $index++) {
        $arguments = [string[]]$calls[$index]
        $expectedSuffix = $expectedProjects[$graph][$index]
        if ($arguments[0] -ne "build" -or
            -not $arguments[1].EndsWith($expectedSuffix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "CI '$graph' build graph project order is invalid at index $index."
        }
        foreach ($requiredFlag in $requiredFlags) {
            if ($arguments -notcontains $requiredFlag) {
                throw "CI '$graph' build graph omitted '$requiredFlag'."
            }
        }
    }
}

$failureCalls = [pscustomobject]@{ Count = 0 }
$failingInvoker = {
    param([string[]]$BuildArguments)
    $failureCalls.Count++
    return 17
}.GetNewClosure()
$failure = $null
try {
    & $scriptPath -Graph BackendRuntime -CommandInvoker $failingInvoker
}
catch {
    $failure = $_
}
if ($null -eq $failure -or
    $failure.Exception.Message -notmatch 'CP6.Entity\\CP6.Entity.csproj.*exit code 17' -or
    $failureCalls.Count -ne 1) {
    throw "CI isolated .NET build graph did not stop on the first failed project."
}

Write-Host "CP6 CI isolated .NET build graph test passed."
