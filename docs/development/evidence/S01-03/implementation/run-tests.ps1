param([Parameter(Mandatory = $true)][ValidatePattern('^[a-zA-Z0-9-]+$')][string]$Stage)
$ErrorActionPreference = 'Stop'
$taskRepo = (Resolve-Path (Join-Path $PSScriptRoot '../../../../..')).Path
foreach ($record in @("$Stage-metadata.json", "$Stage-environment.log", "$Stage.log", "$Stage.trx")) {
    if (Test-Path -LiteralPath (Join-Path $PSScriptRoot $record)) { throw "Stage already recorded: $Stage" }
}
Push-Location $taskRepo
try {
    $sourceFiles = @((Get-ChildItem -LiteralPath 'CP6.Entity/Contracts/FinCredit' -Filter '*.cs' -File | ForEach-Object { 'CP6.Entity/Contracts/FinCredit/' + $_.Name })) + @(
        'eng/contracts/exact-values-tests/CreditAmountTests.cs',
        'eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj',
        'CP6.Entity/CP6.Entity.csproj', 'CP6.Entity/packages.lock.json', 'Directory.Build.props',
        'CP6.Entity/Contracts/Wire/StrictJson.cs', 'CP6.Entity/Contracts/Wire/NullOnly.cs', 'CP6.Entity/Contracts/Wire/JsonSemanticEquality.cs',
        'eng/contracts/s01/profile-inputs.json', 'eng/contracts/s01/vectors.json',
        'docs/development/dispatch/20261012-wave3/dispatch.json')
    $sourceHashes = @($sourceFiles | ForEach-Object { [ordered]@{ path = $_; sha256_file_bytes = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant() } })
    $started = [DateTime]::UtcNow.ToString('o')
    $headSha = git -c core.longpaths=true rev-parse HEAD
    dotnet --info | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Stage-environment.log") -Encoding utf8
    # Windows PowerShell turns native stderr into NativeCommandError records. xUnit's
    # expected RED failure output must finish and produce TRX, rather than abort here.
    $ErrorActionPreference = 'Continue'
    try {
        dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~CreditAmountTests --logger 'console;verbosity=normal' --logger "trx;LogFileName=$Stage.trx" --results-directory $PSScriptRoot 2>&1 | Tee-Object -FilePath (Join-Path $PSScriptRoot "$Stage.log")
        $testExitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = 'Stop' }
    $artifacts = @('eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.ExactValues.Tests.dll', 'eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.Entity.dll', 'eng/contracts/exact-values-tests/bin/Debug/net8.0/CP6.ExactValues.Tests.runtimeconfig.json', 'eng/contracts/exact-values-tests/obj/project.assets.json', 'CP6.Entity/obj/project.assets.json')
    $artifactHashes = @($artifacts | Where-Object { Test-Path -LiteralPath $_ } | ForEach-Object { [ordered]@{ path = $_; sha256_file_bytes = (Get-FileHash -Algorithm SHA256 -LiteralPath $_).Hash.ToLowerInvariant() } })
    $sourcesUnchanged = @($sourceHashes | Where-Object { (Get-FileHash -Algorithm SHA256 -LiteralPath $_.path).Hash.ToLowerInvariant() -ne $_.sha256_file_bytes }).Count -eq 0
    [ordered]@{
        stage = $Stage; started_utc = $started; finished_utc = [DateTime]::UtcNow.ToString('o')
        powershell_version = $PSVersionTable.PSVersion.ToString()
        base_head = $headSha
        source_state = 'Working files identified by raw file SHA-256; base_head alone is not the tested source.'
        command = 'dotnet test eng/contracts/exact-values-tests/CP6.ExactValues.Tests.csproj --filter FullyQualifiedName~CreditAmountTests'
        logging_arguments = "--logger console;verbosity=normal --logger trx;LogFileName=$Stage.trx --results-directory docs/development/evidence/S01-03/implementation"
        exit_code = $testExitCode; sources_unchanged_during_run = $sourcesUnchanged
        sources = $sourceHashes; artifacts = $artifactHashes
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot "$Stage-metadata.json") -Encoding utf8
    if (-not $sourcesUnchanged) { throw 'Source changed during test execution' }
    exit $testExitCode
}
finally { Pop-Location }
