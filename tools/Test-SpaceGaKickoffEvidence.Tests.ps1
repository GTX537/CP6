param([string]$ExportValidManifestPath)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaKickoffEvidence.ps1'
$hostExecutable = (Get-Process -Id $PID).Path
$fixtureDirectory = Join-Path $PSScriptRoot (
    'test-fixtures\space-ga-kickoff-evidence')
$tempDirectory = Join-Path $fixtureDirectory (
    '.tmp-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory -Force)
$passed = 0

function New-KickoffAttestation {
    param(
        [Parameter(Mandatory)][string]$Id,
        [string]$AcceptedBy = 'Zhang Wei'
    )

    return [pscustomobject]@{
        uri = "urn:cp6-space-ga-evidence:test:kickoff:$Id"
        sha256 = ('a' * 64)
        acceptedBy = $AcceptedBy
        acceptedAtUtc = '2026-08-14T12:00:00Z'
    }
}

function Get-TestCandidateSetSha256 {
    param([Parameter(Mandatory)][array]$Candidates)

    $payload = [string]::Join("`n", @($Candidates |
        Sort-Object { [string]$_.sampleRef } |
        ForEach-Object {
            ([string]$_.sampleRef) + ':' +
                ([string]$_.sourceSha256).ToLowerInvariant()
        }))
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($payload)
    try {
        $algorithm = [System.Security.Cryptography.SHA256]::Create()
        try {
            return ([BitConverter]::ToString(
                $algorithm.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        [Array]::Clear($bytes, 0, $bytes.Length)
    }
}

function New-ValidKickoffManifest {
    $candidates = for ($index = 0; $index -lt 20; $index++) {
        $ordinal = $index + 1
        [pscustomobject]@{
            sampleRef = ('urn:cp6-space-golden-cad:candidate-{0:D2}' -f $ordinal)
            sourceSha256 = ('{0:x64}' -f $ordinal)
            sourceSizeBytes = 1000000 + $ordinal
            sourceFormat = if ($index % 2 -eq 0) { 'DWG' } else { 'DXF' }
            layoutFamily = 'L' + ([Math]::Floor($index / 4) + 1)
            license = 'ApprovedCustomerDerived'
            authorizedForGoldenEvaluation = $true
            authorizationEvidence = New-KickoffAttestation `
                -Id "cad-authorization-$ordinal" `
                -AcceptedBy 'Liu Yan'
            deidentificationEvidence = New-KickoffAttestation `
                -Id "cad-deidentification-$ordinal" `
                -AcceptedBy 'Liu Yan'
        }
    }
    $candidateSetSha256 = Get-TestCandidateSetSha256 -Candidates $candidates

    $providers = @([pscustomobject]@{
            role = 'Primary'
            providerKey = 'provider-a'
            providerVersion = '1.0.0'
            adapterContract = 'ICadConverter'
            dataBoundary = 'ControlledIsolatedWorker'
            licensingApproved = $true
            securityApproved = $true
            retentionDeletionApproved = $true
            licensingEvidence = New-KickoffAttestation -Id 'provider-a-license'
            securityEvidence = New-KickoffAttestation -Id 'provider-a-security'
            retentionDeletionEvidence = New-KickoffAttestation -Id 'provider-a-retention'
            cloudApprovals = [pscustomobject]@{
                tenantApproved = $false
                customerApproved = $false
                securityApproved = $false
                tenantEvidence = [pscustomobject]@{}
                customerEvidence = [pscustomobject]@{}
                securityEvidence = [pscustomobject]@{}
            }
        })

    return [pscustomobject]@{
        schemaVersion = 3
        programId = 'CP6_SPACE_STUDIO_V1_CORE_GA'
        deliveryMode = 'SoloDeveloper'
        evidenceClass = 'M0_EXTERNAL_INPUT_READINESS'
        conclusion = 'Pass'
        kickoffDate = '2026-08-01'
        targetGaDate = '2026-10-24'
        authorizedGoldenCadCandidates = [pscustomobject]@{
            inputId = 'AUTHORIZED_GOLDEN_CAD_CANDIDATES'
            status = 'Complete'
            ownerName = 'Zhang Wei'
            completionEvidence = New-KickoffAttestation `
                -Id 'cad-completion' -AcceptedBy 'Zhang Wei'
            candidateSetVersion = 'golden-candidates-2026-08-v1'
            candidateSetSha256 = $candidateSetSha256
            candidates = $candidates
        }
        primaryProviderAndIsolatedWorker = [pscustomobject]@{
            inputId = 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER'
            status = 'Complete'
            ownerName = 'Zhang Wei'
            completionEvidence = New-KickoffAttestation `
                -Id 'provider-completion' -AcceptedBy 'Zhang Wei'
            candidateProviders = $providers
            worker = [pscustomobject]@{
                workerRef = 'urn:cp6-space-ga-worker:isolated-01'
                environmentSha256 = ('b' * 64)
                isolated = $true
                secretsByReferenceOnly = $true
                rawCadRetentionMode = 'Ephemeral'
                outboundNetworkPolicy = 'DenyByDefault'
                readinessEvidence = New-KickoffAttestation -Id 'worker-readiness'
            }
        }
    }
}

if (![string]::IsNullOrWhiteSpace($ExportValidManifestPath)) {
    $exportPath = [System.IO.Path]::GetFullPath($ExportValidManifestPath)
    $exportDirectory = Split-Path -Parent $exportPath
    [void](New-Item -ItemType Directory -Path $exportDirectory -Force)
    New-ValidKickoffManifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $exportPath -Encoding UTF8
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
    if ((Test-Path -LiteralPath $fixtureDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $fixtureDirectory -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($fixtureDirectory)
    }
    exit 0
}

function New-KickoffTestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )

    $manifest = New-ValidKickoffManifest
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function Invoke-KickoffValidatorCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError,
        [string]$InputId,
        [string]$ExpectedOwnerName,
        [bool]$AllowTestFixtures = $true
    )

    $arguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', $validator,
        '-ManifestPath', $ManifestPath)
    if (![string]::IsNullOrWhiteSpace($InputId)) {
        $arguments += @('-InputId', $InputId)
    }
    if (![string]::IsNullOrWhiteSpace($ExpectedOwnerName)) {
        $arguments += @('-ExpectedOwnerName', $ExpectedOwnerName)
    }
    if ($AllowTestFixtures) {
        $arguments += '-AllowTestFixtures'
    }
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable @arguments 2>&1 | Out-String
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($ShouldPass -and $exitCode -ne 0) {
        throw "$Name should pass but exited $exitCode.`n$output"
    }
    if (!$ShouldPass -and $exitCode -eq 0) {
        throw "$Name should fail but exited 0.`n$output"
    }
    if (!$ShouldPass -and
        ![string]::IsNullOrWhiteSpace($ExpectedError) -and
        $output -notmatch [regex]::Escape($ExpectedError)) {
        throw "$Name did not report '$ExpectedError'.`n$output"
    }
    if ($output -match 'ParameterBindingValidationException') {
        throw "$Name produced a PowerShell parameter binding failure.`n$output"
    }
    $script:passed++

    # The validator exit code has been asserted above. Clear the consumed native
    # process status so a successful suite cannot leak an expected failure to CI.
    $global:LASTEXITCODE = 0
}

try {
    $validPath = New-KickoffTestManifest 'valid' { param($manifest) }
    Invoke-KickoffValidatorCase -Name 'one owner may close every external input' `
        -ManifestPath $validPath -ShouldPass $true

    Invoke-KickoffValidatorCase `
        -Name 'formal mode rejects synthetic evidence' `
        -ManifestPath $validPath -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_EVIDENCE_SYNTHETIC' `
        -AllowTestFixtures $false

    Invoke-KickoffValidatorCase `
        -Name 'blank template fails semantically' `
        -ManifestPath (Join-Path $repo `
            'docs/space/acceptance/v1.3-ga/kickoff-evidence-template.json') `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_CONCLUSION_INVALID' `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -AllowTestFixtures $false

    $incrementalPath = New-KickoffTestManifest 'incremental' {
        param($manifest)
        $manifest.conclusion = 'InProgress'
        $manifest.primaryProviderAndIsolatedWorker.status = 'Pending'
    }
    Invoke-KickoffValidatorCase `
        -Name 'one input can close from an incremental package' `
        -ManifestPath $incrementalPath -ShouldPass $true `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedOwnerName 'Zhang Wei'

    Invoke-KickoffValidatorCase `
        -Name 'index owner must match section owner' `
        -ManifestPath $validPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedOwnerName 'Different Person' `
        -ExpectedError 'SPACE_GA_KICKOFF_OWNER_MISMATCH'

    $numericOwnerPath = New-KickoffTestManifest 'numeric-owner' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.ownerName = '00001'
    }
    Invoke-KickoffValidatorCase `
        -Name 'development code is not a real external input owner' `
        -ManifestPath $numericOwnerPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_OWNER_INVALID'

    $cadCountPath = New-KickoffTestManifest 'cad-count' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.candidates = @(
            $manifest.authorizedGoldenCadCandidates.candidates |
                Select-Object -First 19)
    }
    Invoke-KickoffValidatorCase -Name 'twenty CAD candidates are required' `
        -ManifestPath $cadCountPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_CAD_COUNT_INVALID'

    $cadDuplicatePath = New-KickoffTestManifest 'cad-duplicate' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.candidates[1].sourceSha256 = (
            $manifest.authorizedGoldenCadCandidates.candidates[0].sourceSha256)
    }
    Invoke-KickoffValidatorCase -Name 'CAD identities are unique' `
        -ManifestPath $cadDuplicatePath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_CAD_IDENTITY_DUPLICATE'

    $cadAuthorizationPath = New-KickoffTestManifest 'cad-authorization' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.candidates[0].authorizedForGoldenEvaluation = $false
    }
    Invoke-KickoffValidatorCase -Name 'CAD authorization is explicit' `
        -ManifestPath $cadAuthorizationPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_CAD_CANDIDATE_INVALID'

    $cadFormatPath = New-KickoffTestManifest 'cad-format' {
        param($manifest)
        foreach ($candidate in $manifest.authorizedGoldenCadCandidates.candidates) {
            $candidate.sourceFormat = 'DXF'
        }
    }
    Invoke-KickoffValidatorCase -Name 'DWG and DXF are both represented' `
        -ManifestPath $cadFormatPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_CAD_FORMAT_COVERAGE_INVALID'

    $cadLayoutPath = New-KickoffTestManifest 'cad-layout' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.candidates[19].layoutFamily = 'L1'
    }
    Invoke-KickoffValidatorCase -Name 'five layout families have four samples' `
        -ManifestPath $cadLayoutPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_CAD_LAYOUT_COVERAGE_INVALID'

    $cadHashPath = New-KickoffTestManifest 'cad-set-hash' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.candidateSetSha256 = 'f' * 64
    }
    Invoke-KickoffValidatorCase -Name 'candidate set hash seals the list' `
        -ManifestPath $cadHashPath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_CAD_SET_HASH_MISMATCH'

    $providerCountPath = New-KickoffTestManifest 'provider-count' {
        param($manifest)
        $manifest.primaryProviderAndIsolatedWorker.candidateProviders = @()
    }
    Invoke-KickoffValidatorCase -Name 'one Primary Provider candidate is required' `
        -ManifestPath $providerCountPath -ShouldPass $false `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -ExpectedError 'SPACE_GA_KICKOFF_PROVIDER_COUNT_INVALID'

    $providerRolePath = New-KickoffTestManifest 'provider-role' {
        param($manifest)
        $manifest.primaryProviderAndIsolatedWorker.candidateProviders[0].role = 'Backup'
    }
    Invoke-KickoffValidatorCase -Name 'the Provider role is Primary' `
        -ManifestPath $providerRolePath -ShouldPass $false `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -ExpectedError 'SPACE_GA_KICKOFF_PROVIDER_COUNT_INVALID'

    $providerApprovalPath = New-KickoffTestManifest 'provider-approval' {
        param($manifest)
        $manifest.primaryProviderAndIsolatedWorker.candidateProviders[0].licensingApproved = $false
    }
    Invoke-KickoffValidatorCase -Name 'Provider approvals are complete' `
        -ManifestPath $providerApprovalPath -ShouldPass $false `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -ExpectedError 'SPACE_GA_KICKOFF_PROVIDER_APPROVALS_INCOMPLETE'

    $cloudApprovalPath = New-KickoffTestManifest 'cloud-approval' {
        param($manifest)
        $manifest.primaryProviderAndIsolatedWorker.candidateProviders[0].dataBoundary = 'ApprovedCloud'
    }
    Invoke-KickoffValidatorCase -Name 'cloud requires tenant customer security approvals' `
        -ManifestPath $cloudApprovalPath -ShouldPass $false `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -ExpectedError 'SPACE_GA_KICKOFF_CLOUD_APPROVALS_INCOMPLETE'

    $approvedCloudPath = New-KickoffTestManifest 'approved-cloud' {
        param($manifest)
        $provider = $manifest.primaryProviderAndIsolatedWorker.candidateProviders[0]
        $provider.dataBoundary = 'ApprovedCloud'
        $provider.cloudApprovals.tenantApproved = $true
        $provider.cloudApprovals.customerApproved = $true
        $provider.cloudApprovals.securityApproved = $true
        $provider.cloudApprovals.tenantEvidence = $provider.securityEvidence
        $provider.cloudApprovals.customerEvidence = $provider.securityEvidence
        $provider.cloudApprovals.securityEvidence = $provider.securityEvidence
    }
    Invoke-KickoffValidatorCase -Name 'approved cloud keeps isolated boundary' `
        -ManifestPath $approvedCloudPath -ShouldPass $true `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER'

    $workerPath = New-KickoffTestManifest 'worker' {
        param($manifest)
        $manifest.primaryProviderAndIsolatedWorker.worker.secretsByReferenceOnly = $false
    }
    Invoke-KickoffValidatorCase -Name 'Worker isolation boundary is enforced' `
        -ManifestPath $workerPath -ShouldPass $false `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -ExpectedError 'SPACE_GA_KICKOFF_WORKER_INVALID'

    $localBoundaryPath = New-KickoffTestManifest 'local-boundary' {
        param($manifest)
        $provider = $manifest.primaryProviderAndIsolatedWorker.candidateProviders[0]
        $provider.dataBoundary = 'LocalControlledProcess'
        $worker = $manifest.primaryProviderAndIsolatedWorker.worker
        $worker.isolated = $false
        $worker.outboundNetworkPolicy = 'OwnerAcceptedLocalBoundary'
        $worker | Add-Member -NotePropertyName networkListenerStarted `
            -NotePropertyValue $false
        $worker | Add-Member -NotePropertyName businessCredentialsUnavailable `
            -NotePropertyValue $true
    }
    Invoke-KickoffValidatorCase `
        -Name 'Owner-approved local V1 boundary is accepted' `
        -ManifestPath $localBoundaryPath -ShouldPass $true `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER'

    $localListenerPath = New-KickoffTestManifest 'local-listener' {
        param($manifest)
        $provider = $manifest.primaryProviderAndIsolatedWorker.candidateProviders[0]
        $provider.dataBoundary = 'LocalControlledProcess'
        $worker = $manifest.primaryProviderAndIsolatedWorker.worker
        $worker.isolated = $false
        $worker.outboundNetworkPolicy = 'OwnerAcceptedLocalBoundary'
        $worker | Add-Member -NotePropertyName networkListenerStarted `
            -NotePropertyValue $true
        $worker | Add-Member -NotePropertyName businessCredentialsUnavailable `
            -NotePropertyValue $true
    }
    Invoke-KickoffValidatorCase `
        -Name 'local V1 boundary rejects a network listener' `
        -ManifestPath $localListenerPath -ShouldPass $false `
        -InputId 'PRIMARY_PROVIDER_AND_ISOLATED_WORKER' `
        -ExpectedError 'SPACE_GA_KICKOFF_WORKER_INVALID'

    $sectionEvidencePath = New-KickoffTestManifest 'section-evidence' {
        param($manifest)
        $manifest.authorizedGoldenCadCandidates.completionEvidence.acceptedBy = 'Different Person'
    }
    Invoke-KickoffValidatorCase -Name 'completion evidence binds section owner' `
        -ManifestPath $sectionEvidencePath -ShouldPass $false `
        -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
        -ExpectedError 'SPACE_GA_KICKOFF_EVIDENCE_ACCEPTOR_MISMATCH'

    if ($global:LASTEXITCODE -ne 0) {
        throw "Test suite leaked child process exit code $global:LASTEXITCODE."
    }

    [ordered]@{
        suite = 'CP6_SPACE_GA_KICKOFF_EVIDENCE'
        passed = $passed
        failed = 0
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
    if ((Test-Path -LiteralPath $fixtureDirectory -PathType Container) -and
        @(Get-ChildItem -LiteralPath $fixtureDirectory -Force).Count -eq 0) {
        [System.IO.Directory]::Delete($fixtureDirectory)
    }
}
