param([Parameter(Mandatory = $true)][string]$Stage)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
Push-Location $repo
try {
    $sourceFiles = @('CP6.Entity/Contracts/Wire/NullOnly.cs', 'CP6.Entity/Contracts/Wire/StrictJson.cs', 'CP6.Entity/Contracts/Wire/JsonSemanticEquality.cs', 'eng/contracts/exact-values-tests/StrictJsonTests.cs', 'eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj')
    $sourceHashes = @($sourceFiles | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant() } })
    $started = [DateTime]::UtcNow.ToString('o')
    $headSha = git -c core.longpaths=true rev-parse HEAD
    dotnet --info | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Stage-environment.log") -Encoding utf8
    dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~StrictJsonTests --logger 'console;verbosity=normal' --logger "trx;LogFileName=$Stage.trx" --results-directory $PSScriptRoot 2>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot "$Stage.log")
    $testExitCode = $LASTEXITCODE
    $artifacts = @('eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.ExactValues.Tests.dll', 'eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.Entity.dll', 'eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.ExactValues.Tests.runtimeconfig.json')
    $artifactHashes = @($artifacts | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { [ordered]@{ path = $_; sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant() } })
    [ordered]@{
        stage = $Stage
        started_utc = $started
        finished_utc = [DateTime]::UtcNow.ToString('o')
        base_head = $headSha
        source_state = 'Uncommitted task files identified by SHA-256 below; base_head alone is not the tested source.'
        command = 'dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~StrictJsonTests'
        logging_arguments = "--logger console;verbosity=normal --logger trx;LogFileName=$Stage.trx --results-directory docs/development/evidence/S01-02/implementation"
        exit_code = $testExitCode
        sources = $sourceHashes
        artifacts = $artifactHashes
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Stage-metadata.json") -Encoding utf8
    exit $testExitCode
}
finally { Pop-Location }
