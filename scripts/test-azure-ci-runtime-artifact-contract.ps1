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
    "host capacity behavior test" = 'test-cp6-ci-host-capacity\.ps1'
    "host capacity gate" = 'Assert-Cp6CiHostCapacity\.ps1'
    "four and a half GiB start threshold" = '-MinimumFreeMemoryMiB 4608'
    "bounded capacity wait" = '(?s)-MaxWaitSeconds 600.*?-PollIntervalSeconds 15'
    "isolated build graph behavior test" = 'test-cp6-ci-dotnet-build\.ps1'
    "isolated API build graph" = '(?s)Invoke-Cp6CiDotNetBuild\.ps1.*?-Graph BackendRuntime'
    "backend tests" = 'dotnet test CP6\.Tests/CP6\.Tests\.csproj'
    "client tests" = 'dotnet test CP6\.Client\.Tests/CP6\.Client\.Tests\.csproj'
    "Vue type check" = 'npm run type-check'
    "bounded Vue unit test workers" = 'npm test -- --maxWorkers=2'
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

$restoreDisplayIndex = $pipeline.IndexOf("displayName: 'Restore .NET Projects'")
$restoreStepIndex = if ($restoreDisplayIndex -ge 0) {
    $pipeline.LastIndexOf("    - powershell: |", $restoreDisplayIndex)
}
else {
    -1
}
$apiBuildDisplayIndex = $pipeline.IndexOf("displayName: 'Build CP6 API'")
if ($restoreStepIndex -lt 0 -or $apiBuildDisplayIndex -le $restoreStepIndex) {
    throw "Azure CI .NET restore step boundary is invalid."
}
$restoreStep = $pipeline.Substring(
    $restoreStepIndex,
    $apiBuildDisplayIndex - $restoreStepIndex)
if ($restoreStep -notmatch '--disable-build-servers' -or
    $restoreStep -notmatch '--disable-parallel') {
    throw "Azure CI .NET restore must disable persistent servers and parallel restore."
}

$buildGraphsByStep = [ordered]@{
    'Build CP6 API' = 'BackendRuntime'
    'Run Backend Tests' = 'BackendTests'
    'Run Client Tests' = 'ClientTests'
}
foreach ($entry in $buildGraphsByStep.GetEnumerator()) {
    $displayName = $entry.Key
    $displayIndex = $pipeline.IndexOf("displayName: '$displayName'")
    $stepIndex = if ($displayIndex -ge 0) {
        $pipeline.LastIndexOf("    - powershell: |", $displayIndex)
    }
    else {
        -1
    }
    if ($stepIndex -lt 0 -or $displayIndex -le $stepIndex) {
        throw "Azure CI '$displayName' step boundary is invalid."
    }
    $step = $pipeline.Substring($stepIndex, $displayIndex - $stepIndex)
    if ($step -notmatch 'Invoke-Cp6CiDotNetBuild\.ps1' -or
        $step -notmatch ("-Graph\s+{0}" -f $entry.Value)) {
        throw "Azure CI '$displayName' must use the '$($entry.Value)' isolated build graph."
    }
    if ($displayName -ne 'Build CP6 API' -and
        ($step -notmatch 'dotnet test' -or
        $step -notmatch '--no-build' -or
        $step -notmatch '--disable-build-servers')) {
        throw "Azure CI '$displayName' must execute the prebuilt test assembly."
    }
}

$apiBuildIndex = $pipeline.IndexOf("displayName: 'Build CP6 API'")
$capacityGateIndex = $pipeline.IndexOf("displayName: 'Wait for safe CI host capacity'")
$restoreIndex = $pipeline.IndexOf("displayName: 'Restore .NET Projects'")
if ($capacityGateIndex -lt 0 -or
    $restoreIndex -le $capacityGateIndex -or
    $apiBuildIndex -le $restoreIndex) {
    throw "Azure CI host capacity, restore, and API build order is invalid."
}
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
