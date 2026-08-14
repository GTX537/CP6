$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaEvidence.ps1'
$baseManifestPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
$fixtureReference = 'tools/test-fixtures/space-ga-evidence/attestation-fixture.txt'
$fixturePath = Join-Path $repo $fixtureReference
$fixtureSha256 = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
$hostExecutable = (Get-Process -Id $PID).Path
$tempDirectory = Join-Path (
    [System.IO.Path]::GetTempPath()) (
    'cp6-space-ga-evidence-' + [Guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $tempDirectory)
$passed = 0

function New-TestManifest {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][scriptblock]$Mutation
    )

    $manifest = Get-Content -LiteralPath $baseManifestPath -Raw |
        ConvertFrom-Json
    & $Mutation $manifest
    $path = Join-Path $tempDirectory "$Name.json"
    $manifest | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $path -Encoding UTF8
    return $path
}

function New-Attestation {
    param(
        [Parameter(Mandatory)][string]$Uri,
        [Parameter(Mandatory)][string]$Sha256,
        [string]$AcceptedBy = 'Zhang Wei',
        [string]$AcceptedAtUtc
    )

    if ([string]::IsNullOrWhiteSpace($AcceptedAtUtc)) {
        $AcceptedAtUtc = [DateTimeOffset]::UtcNow.AddMinutes(-1).ToString(
            'yyyy-MM-ddTHH:mm:ssZ',
            [System.Globalization.CultureInfo]::InvariantCulture)
    }
    return [pscustomobject]@{
        uri = $Uri
        sha256 = $Sha256
        acceptedBy = $AcceptedBy
        acceptedAtUtc = $AcceptedAtUtc
    }
}

function Set-GateAccepted {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$Attestation
    )

    $gate = @($Manifest.gates | Where-Object {
        $_.id -eq 'WP1_DESIGN_V1_MANUAL_MODELING'
    })[0]
    $gate.ownerName = 'Zhang Wei'
    $gate.acceptanceStatus = 'Accepted'
    $gate.acceptedEvidence = @($Attestation)
}

function Invoke-ValidatorCase {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$ManifestPath,
        [Parameter(Mandatory)][bool]$ShouldPass,
        [string]$ExpectedError
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & $hostExecutable -NoProfile -ExecutionPolicy Bypass -File $validator `
            -ManifestPath $ManifestPath 2>&1 | Out-String
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
    $script:passed++
}

try {
    Invoke-ValidatorCase `
        -Name 'current honest NoGo manifest' `
        -ManifestPath $baseManifestPath `
        -ShouldPass $true

    $localAttestation = New-Attestation `
        -Uri $fixtureReference `
        -Sha256 $fixtureSha256
    $positivePath = New-TestManifest 'positive-local-attestations' {
        param($manifest)
        Set-GateAccepted -Manifest $manifest -Attestation $localAttestation
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'Product' })[0]
        $signer.name = 'Zhang Wei'
        $signer.status = 'Signed'
        $signer.evidence = @($localAttestation)
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'CORE_TEAM_ALLOCATION'
        })[0]
        $input.ownerName = 'Zhang Wei'
        $input.status = 'Complete'
        $input.evidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'local evidence with matching content hash' `
        -ManifestPath $positivePath `
        -ShouldPass $true

    $remotePath = New-TestManifest 'positive-controlled-https' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri 'https://evidence.example.com/cp6/space/wp1-report.json' `
            -Sha256 ('a' * 64)
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'controlled HTTPS evidence reference' `
        -ManifestPath $remotePath `
        -ShouldPass $true

    $urnPath = New-TestManifest 'positive-controlled-urn' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri 'urn:cp6-space-ga-evidence:wp1:2026-08-14:report-001' `
            -Sha256 ('b' * 64)
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'controlled CP6 evidence URN' `
        -ManifestPath $urnPath `
        -ShouldPass $true

    $invalidSignerPath = New-TestManifest 'invalid-signer-attestation' {
        param($manifest)
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'Product' })[0]
        $signer.name = 'Zhang Wei'
        $signer.status = 'Signed'
        $signer.evidence = @('not-an-attestation-object')
    }
    Invoke-ValidatorCase `
        -Name 'signed signer requires an attestation object' `
        -ManifestPath $invalidSignerPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_URI_REQUIRED'

    $invalidInputPath = New-TestManifest 'invalid-input-attestation' {
        param($manifest)
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'CORE_TEAM_ALLOCATION'
        })[0]
        $input.ownerName = 'Zhang Wei'
        $input.status = 'Complete'
        $input.evidence = @('not-an-attestation-object')
    }
    Invoke-ValidatorCase `
        -Name 'complete input requires an attestation object' `
        -ManifestPath $invalidInputPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_URI_REQUIRED'

    $placeholderSignerNamePath = New-TestManifest 'placeholder-signer-name' {
        param($manifest)
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'Product' })[0]
        $signer.name = 'Product'
        $signer.status = 'Signed'
        $signer.evidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'signed signer requires a real person name' `
        -ManifestPath $placeholderSignerNamePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_SIGNER_NAME_INVALID'

    $placeholderInputOwnerPath = New-TestManifest 'placeholder-input-owner' {
        param($manifest)
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'CORE_TEAM_ALLOCATION'
        })[0]
        $input.ownerName = 'QA'
        $input.status = 'Complete'
        $input.evidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'complete input requires a real person owner' `
        -ManifestPath $placeholderInputOwnerPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_INPUT_OWNER_INVALID'

    $placeholderGateOwnerPath = New-TestManifest 'placeholder-gate-owner' {
        param($manifest)
        Set-GateAccepted -Manifest $manifest -Attestation $localAttestation
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP1_DESIGN_V1_MANUAL_MODELING'
        })[0]
        $gate.ownerName = 'Architecture'
    }
    Invoke-ValidatorCase `
        -Name 'accepted gate requires a real person owner' `
        -ManifestPath $placeholderGateOwnerPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GATE_OWNER_INVALID'

    $hashMismatchPath = New-TestManifest 'hash-mismatch' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri $fixtureReference `
            -Sha256 ('0' * 64)
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'local evidence hash mismatch' `
        -ManifestPath $hashMismatchPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_SHA_MISMATCH'

    $missingPath = New-TestManifest 'missing-evidence' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri 'docs/space/acceptance/v1.3-ga/missing-evidence.json' `
            -Sha256 ('1' * 64)
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'missing repository evidence' `
        -ManifestPath $missingPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_PATH_MISSING'

    $rawCadPath = New-TestManifest 'raw-cad-reference' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri 'docs/space/acceptance/v1.3-ga/customer-source.dwg' `
            -Sha256 ('3' * 64)
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'raw customer CAD repository reference' `
        -ManifestPath $rawCadPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_RAW_CAD_FORBIDDEN'

    $unsafeSchemePath = New-TestManifest 'unsafe-scheme' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri 'file:///tmp/fake-evidence.json' `
            -Sha256 ('2' * 64)
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'unsafe evidence URI scheme' `
        -ManifestPath $unsafeSchemePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_URI_UNCONTROLLED'

    $nonUtcPath = New-TestManifest 'non-utc-time' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri $fixtureReference `
            -Sha256 $fixtureSha256 `
            -AcceptedAtUtc '2026-08-14T08:00:00-04:00'
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'non-UTC acceptance time' `
        -ManifestPath $nonUtcPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_TIME_INVALID'

    $futureAcceptedAt = [DateTimeOffset]::UtcNow.AddDays(1).ToString(
        'yyyy-MM-ddTHH:mm:ssZ',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $futurePath = New-TestManifest 'future-time' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri $fixtureReference `
            -Sha256 $fixtureSha256 `
            -AcceptedAtUtc $futureAcceptedAt
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'future acceptance time' `
        -ManifestPath $futurePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_TIME_FUTURE'

    $placeholderPath = New-TestManifest 'placeholder-acceptor' {
        param($manifest)
        $attestation = New-Attestation `
            -Uri $fixtureReference `
            -Sha256 $fixtureSha256 `
            -AcceptedBy 'TBD'
        Set-GateAccepted -Manifest $manifest -Attestation $attestation
    }
    Invoke-ValidatorCase `
        -Name 'placeholder accepting person' `
        -ManifestPath $placeholderPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_EVIDENCE_ACCEPTOR_INVALID'

    [ordered]@{
        suite = 'CP6_SPACE_GA_EVIDENCE_ATTESTATION'
        passed = $passed
        failed = 0
    } | ConvertTo-Json -Compress
}
finally {
    if (Test-Path -LiteralPath $tempDirectory) {
        [System.IO.Directory]::Delete($tempDirectory, $true)
    }
}
