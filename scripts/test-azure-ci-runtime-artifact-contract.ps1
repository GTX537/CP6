[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$pipelinePath = Join-Path $repoRoot "azure-pipelines.yml"
if (-not (Test-Path -LiteralPath $pipelinePath -PathType Leaf)) {
    throw "Azure CI pipeline was not found."
}

$pipeline = Get-Content -LiteralPath $pipelinePath -Raw -Encoding utf8
$requiredPatterns = [ordered]@{
    "main CI trigger" = '(?s)trigger:\s*branches:\s*include:\s*- main'
    "Microsoft-hosted Windows pool" = "(?s)pool:\s*vmImage:\s*'windows-latest'"
    "API build" = 'dotnet build CP6\.WebApi/CP6\.WebApi\.csproj'
    "backend tests" = 'dotnet test CP6\.Tests/CP6\.Tests\.csproj'
    "client tests" = 'dotnet test CP6\.Client\.Tests/CP6\.Client\.Tests\.csproj'
    "Vue type check" = 'npm run type-check'
    "Vue unit tests" = 'npm test -- --maxWorkers=4'
    "Vue production build" = 'npm run build-only'
    "Web artifact release version" = "CP6_RELEASE_VERSION:\s*'0\.0\.0-dev\.\$\(Build\.BuildId\)'"
    "Web artifact Git SHA" = "CP6_GIT_SHA:\s*'\$\(Build\.SourceVersion\)'"
    "API payload copied without recompilation" = '(?s)dotnet publish CP6\.WebApi/CP6\.WebApi\.csproj.*?--no-build.*?--no-restore'
    "hashed runtime artifact creation" = 'New-Cp6DevRuntimeArtifact\.ps1'
    "runtime artifact verification" = 'Test-Cp6DevRuntimeArtifact\.ps1'
    "pipeline artifact publication" = "(?s)- publish:\s*'\$\(Build\.ArtifactStagingDirectory\)\\cp6-dev-runtime'\s+artifact:\s*'cp6-dev-runtime'"
}
foreach ($entry in $requiredPatterns.GetEnumerator()) {
    if ($pipeline -notmatch $entry.Value) {
        throw "Azure CI pipeline is missing $($entry.Key)."
    }
}

$apiBuildIndex = $pipeline.IndexOf("displayName: 'Build CP6 API'")
$webBuildIndex = $pipeline.IndexOf("displayName: 'Build Vue Production'")
$artifactCreateIndex = $pipeline.IndexOf("displayName: 'Create hashed DEV runtime artifact'")
$artifactPublishIndex = $pipeline.IndexOf("displayName: 'Publish DEV runtime artifact'")
if ($apiBuildIndex -lt 0 -or
    $webBuildIndex -le $apiBuildIndex -or
    $artifactCreateIndex -le $webBuildIndex -or
    $artifactPublishIndex -le $artifactCreateIndex) {
    throw "Azure CI build, test, runtime artifact, and publication order is invalid."
}

$contractDisplayIndex = $pipeline.IndexOf("displayName: 'Verify runtime artifact contracts'")
$contractStepIndex = if ($contractDisplayIndex -ge 0) {
    $pipeline.LastIndexOf("    - powershell: |", $contractDisplayIndex)
}
else {
    -1
}
$backendSectionIndex = $pipeline.IndexOf("# .NET Backend")
if ($contractStepIndex -lt 0 -or $backendSectionIndex -le $contractStepIndex) {
    throw "Azure CI runtime artifact contract step boundary is invalid."
}
$contractStep = $pipeline.Substring(
    $contractStepIndex,
    $backendSectionIndex - $contractStepIndex)
if ($contractStep -match '\$LASTEXITCODE') {
    throw "PowerShell contract scripts must not be judged by inherited LASTEXITCODE state."
}

$forbiddenPatterns = [ordered]@{
    "shared self-hosted Default pool" = "(?m)^\s*name:\s*'Default'\s*$"
    "Docker image build" = '(?i)docker\s+(?:build|push)'
    "environment deployment" = '(?m)^\s*- deployment:'
    "production registry" = '(?i)(?:ghcr\.io|\.azurecr\.io)'
    "inline password" = '(?i)(?:Password|Pwd)\s*='
}
foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($pipeline -match $entry.Value) {
        throw "Azure CI pipeline unexpectedly contains $($entry.Key)."
    }
}

Write-Host "Azure CI runtime artifact contract test passed."
