[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$workflowPath = Join-Path $repoRoot ".github\workflows\client-contract.yml"
$receiverPath = Join-Path $PSScriptRoot "Receive-Cp6GitHubRuntimeArtifact.ps1"
$devPipelinePath = Join-Path $repoRoot "azure-pipelines-dev.yml"
foreach ($path in @($workflowPath, $receiverPath, $devPipelinePath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required GitHub runtime artifact bridge file '$path' was not found."
    }
}

$workflow = Get-Content -LiteralPath $workflowPath -Raw -Encoding utf8
$receiver = Get-Content -LiteralPath $receiverPath -Raw -Encoding utf8
$devPipeline = Get-Content -LiteralPath $devPipelinePath -Raw -Encoding utf8

$workflowPatterns = [ordered]@{
    "manual branch probe" = '(?m)^\s*workflow_dispatch:\s*$'
    "main push" = '(?s)push:\s*branches:\s*\[main\]'
    "Release API build" = 'dotnet build CP6\.WebApi/CP6\.WebApi\.csproj -c Release'
    "Release backend tests" = 'dotnet test CP6\.Tests/CP6\.Tests\.csproj -c Release'
    "Release OpenAPI host" = 'bin/Release/net8\.0/CP6\.WebApi\.dll'
    "commit-addressed Web version" = 'CP6_RELEASE_VERSION:\s*0\.0\.0-dev\.\$\{\{ github\.sha \}\}'
    "commit-addressed Web SHA" = 'CP6_GIT_SHA:\s*\$\{\{ github\.sha \}\}'
    "prebuilt API publish" = '(?s)dotnet publish CP6\.WebApi/CP6\.WebApi\.csproj.*?--no-build.*?--no-restore'
    "hashed artifact creation" = 'New-Cp6DevRuntimeArtifact\.ps1'
    "artifact verification" = 'Test-Cp6DevRuntimeArtifact\.ps1'
    "SHA-addressed upload" = 'name:\s*cp6-dev-runtime-\$\{\{ github\.sha \}\}'
    "short retention" = 'retention-days:\s*3'
}
foreach ($entry in $workflowPatterns.GetEnumerator()) {
    if ($workflow -notmatch $entry.Value) {
        throw "GitHub client-contract runtime artifact is missing $($entry.Key)."
    }
}
if ($workflow -match '(?i)(?:gh release create|softprops/action-gh-release|docker\s+(?:build|push)|ghcr\.io)') {
    throw "GitHub client-contract must not create a Release or publish a Registry candidate."
}

$receiverPatterns = [ordered]@{
    "process-scoped authorization" = 'CP6_GITHUB_AUTHORIZATION'
    "GitHub API only" = 'https://api\.github\.com/repos/\$Repository'
    "exact artifact name" = 'cp6-dev-runtime-\$normalizedGitSha'
    "workflow identity" = "workflowRun\.path\s*-ne\s*'\.github/workflows/client-contract\.yml'"
    "allowed events" = "workflowRun\.event\s*-notin\s*@\('push', 'workflow_dispatch'\)"
    "successful conclusion" = "workflowRun\.conclusion\s*-eq\s*'success'"
    "API archive digest" = 'selectedArtifact\.digest'
    "archive SHA-256 verification" = 'Get-FileHash.+-Algorithm SHA256'
    "archive size bound" = '536870912'
    "zip traversal rejection" = '(?s)destinationPath\.StartsWith\(.*?outputPrefix'
    "inner artifact verification" = 'Test-Cp6DevRuntimeArtifact\.ps1'
    "temporary archive cleanup" = '(?s)finally\s*\{.*?Remove-Item -LiteralPath \$archivePath -Force'
    "credential cleanup" = '(?s)finally\s*\{.*?CP6_GITHUB_AUTHORIZATION.*?\$null'
}
foreach ($entry in $receiverPatterns.GetEnumerator()) {
    if ($receiver -notmatch $entry.Value) {
        throw "GitHub runtime artifact receiver is missing $($entry.Key)."
    }
}
if ($receiver -match '(?i)Write-(?:Host|Output).*(?:authorization|token|credential)') {
    throw "GitHub runtime artifact receiver may not log authorization material."
}

$shaReleasePattern = '0\.0\.0-dev\.\$\(\$env:CP6_CI_SOURCE_COMMIT\)'
if ([regex]::Matches($devPipeline, $shaReleasePattern).Count -ne 3) {
    throw "DEV CD must derive all three release identity uses from the full source SHA."
}
if ($devPipeline -match '0\.0\.0-dev\.\$\(\$env:CP6_CI_RUN_ID\)') {
    throw "DEV CD may not derive binary identity from the Azure bridge Run ID."
}

Write-Host "CP6 GitHub runtime artifact bridge contract test passed."
