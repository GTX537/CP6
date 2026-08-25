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
    "main trigger" = '(?s)trigger:\s*branches:\s*include:\s*- main'
    "PR disabled" = '(?m)^pr:\s*none\s*$'
    "self-hosted bridge pool" = "(?s)pool:\s*name:\s*'Default'"
    "clean workspace" = '(?s)workspace:\s*clean:\s*all'
    "credential-preserving checkout" = '(?s)- checkout:\s*self.*?clean:\s*true.*?fetchDepth:\s*0.*?persistCredentials:\s*true'
    "bridge behavior contract" = 'test-cp6-github-runtime-artifact-bridge\.ps1'
    "GitHub artifact receiver" = 'Receive-Cp6GitHubRuntimeArtifact\.ps1'
    "source SHA input" = "CP6_GIT_SHA:\s*'\$\(Build\.SourceVersion\)'"
    "isolated staging root" = "CP6_RUNTIME_ARTIFACT_ROOT:\s*'\$\(Build\.ArtifactStagingDirectory\)\\cp6-dev-runtime'"
    "bounded GitHub wait" = '(?s)-MaxWaitSeconds 1800.*?-PollIntervalSeconds 20'
    "Azure artifact publication" = "(?s)- publish:\s*'\$\(Build\.ArtifactStagingDirectory\)\\cp6-dev-runtime'\s+artifact:\s*'cp6-dev-runtime'"
}
foreach ($entry in $requiredPatterns.GetEnumerator()) {
    if ($pipeline -notmatch $entry.Value) {
        throw "Azure CI artifact bridge is missing $($entry.Key)."
    }
}

$contractIndex = $pipeline.IndexOf("displayName: 'Verify runtime artifact contracts'")
$downloadIndex = $pipeline.IndexOf("displayName: 'Download verified GitHub runtime artifact'")
$publishIndex = $pipeline.IndexOf("displayName: 'Publish DEV runtime artifact'")
if ($contractIndex -lt 0 -or
    $downloadIndex -le $contractIndex -or
    $publishIndex -le $downloadIndex) {
    throw "Azure CI artifact bridge verification, download, and publish order is invalid."
}

$downloadStepIndex = $pipeline.LastIndexOf("    - powershell: |", $downloadIndex)
if ($downloadStepIndex -lt 0) {
    throw "Azure CI artifact bridge download step boundary is invalid."
}
$downloadStep = $pipeline.Substring($downloadStepIndex, $publishIndex - $downloadStepIndex)
if ($downloadStep -notmatch 'git config --get-all "http\.https://github\.com/\$env:CP6_GITHUB_REPOSITORY\.extraheader"' -or
    $downloadStep -notmatch "CP6_GITHUB_REPOSITORY: 'GTX537/CP6'" -or
    $downloadStep -notmatch 'CP6_GITHUB_AUTHORIZATION' -or
    $downloadStep -notmatch '(?s)finally\s*\{.*?CP6_GITHUB_AUTHORIZATION.*?\$null') {
    throw "Azure CI artifact bridge must scope and clear the authorized checkout credential."
}

$forbiddenPatterns = [ordered]@{
    "local dotnet compilation" = '(?i)dotnet\s+(?:restore|build|test|publish)'
    "local Node compilation" = '(?i)npm(?:\.cmd)?\s+(?:ci|test|run)'
    "Docker image build" = '(?i)docker\s+(?:build|push)'
    "environment deployment" = '(?m)^\s*- deployment:'
    "production registry" = '(?i)(?:ghcr\.io|\.azurecr\.io)'
    "inline secret" = '(?i)(?:Password|Pwd|Bearer)\s*='
    "obsolete host capacity gate" = 'Assert-Cp6CiHostCapacity\.ps1'
}
foreach ($entry in $forbiddenPatterns.GetEnumerator()) {
    if ($pipeline -match $entry.Value) {
        throw "Azure CI artifact bridge unexpectedly contains $($entry.Key)."
    }
}

Write-Host "Azure CI GitHub runtime artifact bridge contract test passed."
