param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaCadStartEvidence.ps1'
$repo = Split-Path -Parent $PSScriptRoot
$formalPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\cad-start-formal-evidence-v1.0.0.json')
$templatePath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\cad-start-evidence-template.json')
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-cad-start\.tmp-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function Get-TestGitBlobSha256([string]$BlobOid) {
    $start = [System.Diagnostics.ProcessStartInfo]::new()
    $start.FileName = 'git'
    $start.Arguments = "cat-file blob $BlobOid"
    $start.WorkingDirectory = $repo
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $process = [System.Diagnostics.Process]::Start($start)
    $memory = [System.IO.MemoryStream]::new()
    try {
        $process.StandardOutput.BaseStream.CopyTo($memory)
        $errorText = $process.StandardError.ReadToEnd()
        $process.WaitForExit()
        if ($process.ExitCode -ne 0) { throw $errorText }
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $algorithm.ComputeHash($memory.ToArray()))).Replace('-', '').ToLowerInvariant()
        }
        finally { $algorithm.Dispose() }
    }
    finally {
        $memory.Dispose()
        $process.Dispose()
    }
}

function New-ValidCadStartManifest {
    $manifest = Get-Content -LiteralPath $formalPath -Raw | ConvertFrom-Json
    $head = (& git -C $repo rev-parse HEAD).Trim()
    $manifest.applicationCommitSha = $head
    $manifest.ownerName = 'Zhang Wei'
    $executedAt = [DateTimeOffset]::UtcNow.AddMinutes(-10)
    $manifest.executedAtUtc = $executedAt.ToString('yyyy-MM-ddTHH:mm:ssZ')
    $manifest.selfReview.acceptedBy = 'Zhang Wei'
    $manifest.selfReview.acceptedAtUtc = $executedAt.AddMinutes(1).ToString(
        'yyyy-MM-ddTHH:mm:ssZ')
    foreach ($source in @($manifest.sources)) {
        $path = ([string]$source.path).Replace('\', '/')
        $oid = (& git -C $repo rev-parse "$head`:$path").Trim()
        $source.gitBlobOid = $oid
        $source.sha256 = Get-TestGitBlobSha256 $oid
    }
    return $manifest
}

function New-TestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )
    $manifest = New-ValidCadStartManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-ValidatorCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [bool]$AllowFixtures = $true
    )
    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $validator,
        '-ManifestPath', $ManifestPath)
    if ($AllowFixtures) { $arguments += '-AllowTestFixtures' }
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable @arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previous }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and ![string]::IsNullOrWhiteSpace($ExpectedError) -and
        $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    $script:passed++
    $global:LASTEXITCODE = 0
}

try {
    $validPath = New-TestManifest 'valid' { param($manifest) }
    Invoke-ValidatorCase 'valid controlled CAD Start package' $validPath $true

    Invoke-ValidatorCase `
        'template cannot close WP2' $templatePath $false `
        'SPACE_GA_CAD_START_SYNTHETIC' $false

    $pendingPath = New-TestManifest 'pending' {
        param($manifest) $manifest.conclusion = 'Pending'
    }
    Invoke-ValidatorCase 'pending conclusion' $pendingPath $false `
        'SPACE_GA_CAD_START_CONCLUSION_INVALID'

    $badExecutionPath = New-TestManifest 'execution-hash' {
        param($manifest) $manifest.externalExecution.sha256 = 'invalid'
    }
    Invoke-ValidatorCase 'external execution needs SHA-256' $badExecutionPath $false `
        'SPACE_GA_CAD_START_EXECUTION_INVALID'

    $missingDxfPath = New-TestManifest 'missing-dxf' {
        param($manifest) $manifest.samples = @($manifest.samples | Select-Object -First 1)
    }
    Invoke-ValidatorCase 'DWG and DXF are both required' $missingDxfPath $false `
        'SPACE_GA_CAD_START_SAMPLE_SET_INVALID'

    $unauthorizedPath = New-TestManifest 'unauthorized' {
        param($manifest) $manifest.samples[0].license = 'Synthetic'
    }
    Invoke-ValidatorCase 'sample must stay authorized' $unauthorizedPath $false `
        'SPACE_GA_CAD_START_SAMPLE_IDENTITY_INVALID'

    $providerPath = New-TestManifest 'provider' {
        param($manifest) $manifest.provider.providerVersion = 'development'
    }
    Invoke-ValidatorCase 'exact Primary release is required' $providerPath $false `
        'SPACE_GA_CAD_START_PROVIDER_INVALID'

    $unitPath = New-TestManifest 'unit' {
        param($manifest) $manifest.samples[0].selection.confirmedUnit = 'Unknown'
    }
    Invoke-ValidatorCase 'unit must be explicit' $unitPath $false `
        'SPACE_GA_CAD_START_SELECTION_INVALID'

    $transformPath = New-TestManifest 'transform' {
        param($manifest) $manifest.samples[0].selection.transform.rotationZDegrees = $null
    }
    Invoke-ValidatorCase 'transform fields are required' $transformPath $false `
        'SPACE_GA_CAD_START_TRANSFORM_INVALID'

    $previewWritePath = New-TestManifest 'preview-write' {
        param($manifest) $manifest.samples[0].audit.draftUnchangedDuringPreview = $false
    }
    Invoke-ValidatorCase 'preview cannot mutate Draft' $previewWritePath $false `
        'SPACE_GA_CAD_START_AUDIT_INVALID'

    $tamperPath = New-TestManifest 'tamper-write' {
        param($manifest) $manifest.tamperTest.jobsAfter = 3
    }
    Invoke-ValidatorCase 'tamper rejection is zero-write' $tamperPath $false `
        'SPACE_GA_CAD_START_TAMPER_FAILED'

    $productionPath = New-TestManifest 'production-claim' {
        param($manifest) $manifest.boundaries.productionDeploymentPerformed = $true
    }
    Invoke-ValidatorCase 'production claims stay false' $productionPath $false `
        'SPACE_GA_CAD_START_BOUNDARY_INVALID'

    $sourcePath = New-TestManifest 'source-blob' {
        param($manifest) $manifest.sources[0].gitBlobOid = '0' * 40
    }
    Invoke-ValidatorCase 'source blob must match commit' $sourcePath $false `
        'SPACE_GA_CAD_START_SOURCE_BLOB_MISMATCH'

    $webPath = New-TestManifest 'web-skip' {
        param($manifest) $manifest.verification.web.skipped = 1
    }
    Invoke-ValidatorCase 'Web verification has zero skips' $webPath $false `
        'SPACE_GA_CAD_START_WEB_FAILED'

    $placeholderPath = New-TestManifest 'placeholder-owner' {
        param($manifest)
        $manifest.ownerName = 'TBD'
        $manifest.selfReview.acceptedBy = 'TBD'
    }
    Invoke-ValidatorCase 'owner must be real' $placeholderPath $false `
        'SPACE_GA_CAD_START_OWNER_INVALID'

    $reviewTimePath = New-TestManifest 'review-time' {
        param($manifest) $manifest.selfReview.acceptedAtUtc = '2020-01-01T00:00:00Z'
    }
    Invoke-ValidatorCase 'review follows execution' $reviewTimePath $false `
        'SPACE_GA_CAD_START_REVIEW_TIME_INVALID'

    if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
        $exportFullPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
        [void](New-Item -ItemType Directory `
            -Path (Split-Path -Parent $exportFullPath) -Force)
        New-ValidCadStartManifest | ConvertTo-Json -Depth 100 |
            Set-Content -LiteralPath $exportFullPath -Encoding UTF8
    }
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        Remove-Item -LiteralPath $tempDirectory -Recurse -Force
    }
}

Write-Output "CAD Start evidence validator tests passed: $passed"
