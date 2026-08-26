[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$parser = Join-Path $PSScriptRoot 'Test-Cp6ReleaseShadowCandidate.ps1'
$fixtureRoot = Join-Path $repoRoot 'tests\fixtures\release-shadow\v1.2.3'
$pipelinePath = Join-Path $repoRoot 'azure-pipelines-release-shadow.yml'
$expectedVersion = '1.2.3'
$allowedRoot = 's3://cp6-release-evidence/releases'
$candidateUri = "$allowedRoot/v$expectedVersion/candidate-result.json"
$tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$scenarioRoot = [IO.Path]::GetFullPath((Join-Path $tempRoot (
    'cp6-release-shadow-s0-' + [guid]::NewGuid().ToString('N')
)))
if (-not $scenarioRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -or
    [IO.Path]::GetFileName($scenarioRoot) -notmatch '^cp6-release-shadow-s0-[a-f0-9]{32}$') {
    throw 'Refusing to use a release-shadow scenario directory outside the verified temp root.'
}

function Read-Json {
    param([Parameter(Mandatory = $true)][string]$Path)

    return [IO.File]::ReadAllText($Path, [Text.Encoding]::UTF8) |
        ConvertFrom-Json
}

function Write-Json {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Value
    )

    [IO.File]::WriteAllText(
        $Path,
        ($Value | ConvertTo-Json -Depth 20) + [Environment]::NewLine,
        [Text.UTF8Encoding]::new($false)
    )
}

function Update-CandidateHash {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)]
        [ValidateSet('Manifest', 'FreezeSnapshot', 'ExecutionSpec')]
        [string]$ObjectName
    )

    $candidatePath = Join-Path $Directory 'candidate-result.json'
    $candidate = Read-Json -Path $candidatePath
    $fileName = switch ($ObjectName) {
        Manifest { 'release-manifest.json' }
        FreezeSnapshot { 'release-freeze.json' }
        ExecutionSpec { 'candidate.yaml' }
    }
    $candidate."${ObjectName}Sha256" = (
        Get-FileHash -LiteralPath (Join-Path $Directory $fileName) -Algorithm SHA256
    ).Hash
    Write-Json -Path $candidatePath -Value $candidate
}

function New-Scenario {
    param([Parameter(Mandatory = $true)][string]$Name)

    $path = Join-Path $scenarioRoot $Name
    Copy-Item -LiteralPath $fixtureRoot -Destination $path -Recurse
    return $path
}

function Invoke-Parser {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [string]$Uri = $candidateUri
    )

    return & $parser `
        -ExpectedVersion $expectedVersion `
        -CandidateResultUri $Uri `
        -AllowedEvidenceRootUri $allowedRoot `
        -FixtureDirectory $Directory
}

function Assert-FailsClosed {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][scriptblock]$Mutation,
        [Parameter(Mandatory = $true)][string]$ExpectedMessage,
        [string]$Uri = $candidateUri
    )

    $path = New-Scenario -Name $Name
    & $Mutation $path
    $failed = $false
    try {
        Invoke-Parser -Directory $path -Uri $Uri | Out-Null
    }
    catch {
        $failed = $_.Exception.Message -match $ExpectedMessage
        if (-not $failed) {
            throw "Scenario '$Name' failed for the wrong reason: $($_.Exception.Message)"
        }
    }
    if (-not $failed) {
        throw "Scenario '$Name' did not fail closed."
    }
}

try {
    [IO.Directory]::CreateDirectory($scenarioRoot) | Out-Null

    $report = Invoke-Parser -Directory $fixtureRoot
    if ($report.Authority -ne 'Shadow' -or
        $report.Deployable -ne $false -or
        $report.Mode -ne 'S0OfflineFixture') {
        throw 'Valid S0 output must remain Shadow and non-deployable.'
    }

    Assert-FailsClosed `
        -Name 'wrong-source' `
        -Mutation {} `
        -Uri 's3://unapproved/releases/v1.2.3/candidate-result.json' `
        -ExpectedMessage 'CandidateResultUri must equal'

    Assert-FailsClosed -Name 'wrong-version' -ExpectedMessage 'ReleaseVersion' -Mutation {
        param($path)
        $candidatePath = Join-Path $path 'candidate-result.json'
        $candidate = Read-Json -Path $candidatePath
        $candidate.ReleaseVersion = '1.2.4'
        Write-Json -Path $candidatePath -Value $candidate
    }

    Assert-FailsClosed -Name 'short-sha' -ExpectedMessage 'GitSha' -Mutation {
        param($path)
        $candidatePath = Join-Path $path 'candidate-result.json'
        $candidate = Read-Json -Path $candidatePath
        $candidate.GitSha = 'abc123'
        Write-Json -Path $candidatePath -Value $candidate
    }

    Assert-FailsClosed -Name 'mutable-tag' -ExpectedMessage 'Tag' -Mutation {
        param($path)
        $candidatePath = Join-Path $path 'candidate-result.json'
        $candidate = Read-Json -Path $candidatePath
        $candidate.Tag = 'latest'
        Write-Json -Path $candidatePath -Value $candidate
    }

    Assert-FailsClosed -Name 'manifest-hash' -ExpectedMessage 'SHA-256' -Mutation {
        param($path)
        Add-Content -LiteralPath (Join-Path $path 'release-manifest.json') `
            -Value ' ' -Encoding utf8
    }

    Assert-FailsClosed -Name 'spec-hash' -ExpectedMessage 'candidate.yaml SHA-256' -Mutation {
        param($path)
        Add-Content -LiteralPath (Join-Path $path 'candidate.yaml') `
            -Value '# tampered' -Encoding utf8
    }

    Assert-FailsClosed -Name 'wrong-repository' -ExpectedMessage 'allowlist' -Mutation {
        param($path)
        $manifestPath = Join-Path $path 'release-manifest.json'
        $manifest = Read-Json -Path $manifestPath
        $manifest.Images.Api.Repository = 'ghcr.io/unapproved/cp6-api'
        Write-Json -Path $manifestPath -Value $manifest
        Update-CandidateHash -Directory $path -ObjectName Manifest
    }

    Assert-FailsClosed -Name 'mutable-digest' -ExpectedMessage 'immutable digest' -Mutation {
        param($path)
        $manifestPath = Join-Path $path 'release-manifest.json'
        $manifest = Read-Json -Path $manifestPath
        $manifest.Images.Web.Digest = 'latest'
        Write-Json -Path $manifestPath -Value $manifest
        Update-CandidateHash -Directory $path -ObjectName Manifest
    }

    Assert-FailsClosed -Name 'freeze-binding' -ExpectedMessage 'releaseVersion' -Mutation {
        param($path)
        $freezePath = Join-Path $path 'release-freeze.json'
        $freeze = Read-Json -Path $freezePath
        $freeze.releaseVersion = '1.2.4'
        Write-Json -Path $freezePath -Value $freeze
        Update-CandidateHash -Directory $path -ObjectName FreezeSnapshot

        $manifestPath = Join-Path $path 'release-manifest.json'
        $manifest = Read-Json -Path $manifestPath
        $manifest.ExecutionSpec.FreezeSnapshotSha256 = (
            Get-FileHash -LiteralPath $freezePath -Algorithm SHA256
        ).Hash
        Write-Json -Path $manifestPath -Value $manifest
        Update-CandidateHash -Directory $path -ObjectName Manifest
    }

    Assert-FailsClosed -Name 'deployable-override' -ExpectedMessage 'Unknown=\[Deployable\]' -Mutation {
        param($path)
        $candidatePath = Join-Path $path 'candidate-result.json'
        $candidate = Read-Json -Path $candidatePath
        $candidate | Add-Member -NotePropertyName Deployable -NotePropertyValue $true
        Write-Json -Path $candidatePath -Value $candidate
    }

    $pipeline = [IO.File]::ReadAllText($pipelinePath, [Text.Encoding]::UTF8)
    $parserText = [IO.File]::ReadAllText($parser, [Text.Encoding]::UTF8)
    foreach ($required in @(
        '(?m)^trigger:\s*none\s*$',
        '(?m)^pr:\s*none\s*$',
        'persistCredentials:\s*false',
        'test-release-shadow-contract\.ps1',
        'Test-Cp6ReleaseShadowCandidate\.ps1',
        'PublishPipelineArtifact@1',
        'cp6-release-shadow-s0-\$\(Build\.BuildId\)'
    )) {
        if ($pipeline -notmatch $required) {
            throw "Release Shadow S0 pipeline is missing contract '$required'."
        }
    }

    $forbidden = [ordered]@{
        'Docker build/push task' = 'Docker@2|docker\s+(?:build|buildx|push|pull|tag|login)\b'
        'ACR mutation' = 'az\s+acr\s+(?:build|import|login|repository)\b'
        'Git or GitHub mutation' = 'git\s+(?:push|tag)\b|gh\s+api[^\r\n]*(?:--method|-X)\s+(?:POST|PUT|PATCH|DELETE)'
        'Deployment command' = 'deploy-r2\.ps1|azure-pipelines-dev\.yml|docker\s+compose\b|kubectl\b'
        'External fetch' = 'Invoke-WebRequest|Invoke-RestMethod|curl(?:\.exe)?\b|aws\s+'
        'Service connection' = 'containerRegistry|serviceConnection|azureSubscription|variableGroups?:'
        'Automatic trigger' = '(?m)^\s*(?:branches|resources):\s*$'
    }
    foreach ($entry in $forbidden.GetEnumerator()) {
        if ($pipeline -match $entry.Value -or $parserText -match $entry.Value) {
            throw "Release Shadow S0 contains forbidden capability '$($entry.Key)'."
        }
    }

    if ($parserText -notmatch "Authority\s*=\s*'Shadow'" -or
        $parserText -notmatch 'Deployable\s*=\s*\$false') {
        throw 'Release Shadow parser must hard-code Shadow/non-deployable semantics.'
    }

    Write-Host 'CP6 Release Shadow S0 contract passed (1 valid + 10 fail-closed scenarios + static YAML capability gate).'
}
finally {
    if (Test-Path -LiteralPath $scenarioRoot) {
        Remove-Item -LiteralPath $scenarioRoot -Recurse -Force
    }
}
