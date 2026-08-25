[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet("BackendRuntime", "BackendTests", "ClientTests")]
    [string]$Graph,

    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",

    [scriptblock]$CommandInvoker
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$graphs = @{
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

foreach ($relativeProjectPath in $graphs[$Graph]) {
    $projectPath = Join-Path $repoRoot $relativeProjectPath
    if (-not (Test-Path -LiteralPath $projectPath -PathType Leaf)) {
        throw "The CI build graph project '$relativeProjectPath' was not found."
    }

    $buildArguments = @(
        "build", $projectPath,
        "-c", $Configuration,
        "--no-restore",
        "--no-dependencies",
        "--disable-build-servers",
        "-m:1",
        "-p:BuildInParallel=false",
        "-p:UseSharedCompilation=false"
    )
    Write-Host "Building '$relativeProjectPath' as an isolated single-node process."

    if ($null -ne $CommandInvoker) {
        $exitCode = & $CommandInvoker -BuildArguments $buildArguments
        if ($exitCode -isnot [int]) {
            throw "The CI build command test hook did not return one integer exit code."
        }
    }
    else {
        & dotnet @buildArguments
        $exitCode = $LASTEXITCODE
    }

    if ($exitCode -ne 0) {
        throw "The isolated CI build failed for '$relativeProjectPath' with exit code $exitCode."
    }
}

Write-Host "CP6 CI '$Graph' build graph completed."
