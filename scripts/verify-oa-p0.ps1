param(
    [ValidateSet("Baseline", "Definitions", "Submission", "Draft", "Access", "PurPr", "SqlServer", "All")]
    [string]$Stage = "Baseline",
    [switch]$IncludeE2E,
    [string]$ConnectionString
)

$ErrorActionPreference = "Continue"
$startedAt = [DateTimeOffset]::UtcNow
$results = [System.Collections.Generic.List[object]]::new()

function Invoke-OaCheck {
    param(
        [string]$Name,
        [string]$FilePath,
        [string[]]$Arguments,
        [string]$WorkingDirectory = $PSScriptRoot
    )

    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    Push-Location $WorkingDirectory
    try {
        $commandOutput = @(& $FilePath @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
        $commandOutput | ForEach-Object { Write-Host $_ }
    }
    catch {
        Write-Error $_
        $exitCode = 1
    }
    finally {
        Pop-Location
        $watch.Stop()
    }

    $environmentBlocked = $Name -eq "backend-sqlserver" -and
        (($commandOutput | Out-String) -match "Skipped:\s+[1-9]")
    if ($environmentBlocked -and $exitCode -eq 0) { $exitCode = 3 }

    $displayCommand = "$FilePath $($Arguments -join ' ')"
    if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
        $displayCommand = $displayCommand.Replace($ConnectionString, "<redacted>")
    }

    $results.Add([pscustomobject]@{
        name = $Name
        command = $displayCommand
        durationMs = $watch.ElapsedMilliseconds
        passed = ($exitCode -eq 0)
        environmentBlocked = $environmentBlocked
        exitCode = $exitCode
    })
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$backendFilter = switch ($Stage) {
    "Definitions" { "FullyQualifiedName~Definition|FullyQualifiedName~OaP0Model|FullyQualifiedName~OaP0Migration|FullyQualifiedName~FlowDefinitionPin|FullyQualifiedName~SubFlowDefinitionPin|FullyQualifiedName~WfNotificationOutbox" }
    "Submission"  { "FullyQualifiedName~FormSubmission" }
    "Draft"       { "FullyQualifiedName~Draft" }
    "Access"      { "FullyQualifiedName~OaInstanceAccess|FullyQualifiedName~FormFieldProjection|FullyQualifiedName~InboxDetailAuthorization|FullyQualifiedName~TaskDecision|FullyQualifiedName~OaReadSurface|FullyQualifiedName~InboxQueryPaging" }
    "PurPr"       { "FullyQualifiedName~PurchaseRequestApprovalP0|FullyQualifiedName~PurApproval|FullyQualifiedName~ApprovalPanel" }
    "SqlServer"   { "FullyQualifiedName~SqlServer" }
    default       { $null }
}

$previousSqlServer = $env:CP6_TEST_SQLSERVER
$connectionStringSupplied = -not [string]::IsNullOrWhiteSpace($ConnectionString)
$sqlServerAvailable = $connectionStringSupplied -or
    -not [string]::IsNullOrWhiteSpace($previousSqlServer)
if ($Stage -eq "SqlServer" -and -not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $env:CP6_TEST_SQLSERVER = $ConnectionString
}
elseif ($Stage -eq "All" -and $sqlServerAvailable) {
    Remove-Item Env:CP6_TEST_SQLSERVER -ErrorAction SilentlyContinue
}

if ($Stage -eq "Baseline" -or $Stage -eq "All") {
    Invoke-OaCheck "backend-full" "dotnet" @("test", "CP6.Tests\CP6.Tests.csproj", "--no-restore", "--nologo") $repoRoot
}
elseif ($backendFilter) {
    Invoke-OaCheck "backend-$($Stage.ToLowerInvariant())" "dotnet" @(
        "test", "CP6.Tests\CP6.Tests.csproj", "--no-restore", "--nologo", "--filter", $backendFilter
    ) $repoRoot
}

if ($Stage -ne "SqlServer") {
    Invoke-OaCheck "frontend-tests" "bun" @("run", "test") (Join-Path $repoRoot "cp6.web")
    Invoke-OaCheck "frontend-type-check" "bun" @("run", "type-check") (Join-Path $repoRoot "cp6.web")
    Invoke-OaCheck "migration-drift" "dotnet" @(
        "ef", "migrations", "has-pending-model-changes",
        "--project", "CP6.Core", "--startup-project", "CP6.WebApi"
    ) $repoRoot
}

if ($Stage -eq "All") {
    Invoke-OaCheck "backend-build" "dotnet" @(
        "build", "CP6.slnx", "--no-restore", "--nologo"
    ) $repoRoot
    Invoke-OaCheck "frontend-build" "bun" @("run", "build") (Join-Path $repoRoot "cp6.web")

    if ($sqlServerAvailable) {
        if ($connectionStringSupplied) {
            $env:CP6_TEST_SQLSERVER = $ConnectionString
        }
        else {
            $env:CP6_TEST_SQLSERVER = $previousSqlServer
        }
        Invoke-OaCheck "backend-sqlserver" "dotnet" @(
            "test", "CP6.Tests\CP6.Tests.csproj", "--no-restore", "--nologo",
            "--filter", "FullyQualifiedName~SqlServer"
        ) $repoRoot
    }
}

if ($IncludeE2E) {
    Invoke-OaCheck "frontend-e2e" "bun" @("run", "e2e", "--", "e2e/oa-p0-*.spec.ts") (Join-Path $repoRoot "cp6.web")
}

if ($Stage -eq "SqlServer" -or ($Stage -eq "All" -and $sqlServerAvailable)) {
    $env:CP6_TEST_SQLSERVER = $previousSqlServer
}

$knownBaseline = @()
$summary = [pscustomobject]@{
    stage = $Stage
    startedAtUtc = $startedAt.ToString("O")
    durationMs = ([DateTimeOffset]::UtcNow - $startedAt).TotalMilliseconds
    passed = -not ($results | Where-Object { -not $_.passed })
    commands = $results
    knownBaseline = $knownBaseline
}

$summary | ConvertTo-Json -Depth 6
if (-not $summary.passed) { exit 1 }
exit 0
