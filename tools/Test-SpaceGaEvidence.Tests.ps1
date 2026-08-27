$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaEvidence.ps1'
$baseManifestPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
$candidateReference = (
    'docs/space/acceptance/v1.3-ga/' +
    'authorized-golden-cad-candidates-v1.0.0.json')
$candidatePath = Join-Path $repo $candidateReference
$candidateSha256 = (Get-FileHash -LiteralPath $candidatePath `
    -Algorithm SHA256).Hash.ToLowerInvariant()
$fixtureReference = 'tools/test-fixtures/space-ga-evidence/attestation-fixture.txt'
$fixturePath = Join-Path $repo $fixtureReference
$fixtureSha256 = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
$goldenCadTestSuite = Join-Path $PSScriptRoot (
    'Test-SpaceGaGoldenCadEvidence.Tests.ps1')
$kickoffTestSuite = Join-Path $PSScriptRoot (
    'Test-SpaceGaKickoffEvidence.Tests.ps1')
$releaseRehearsalTestSuite = Join-Path $PSScriptRoot (
    'Test-SpaceGaReleaseRehearsalEvidence.Tests.ps1')
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

function Set-Wp8Accepted {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$RehearsalReference,
        [Parameter(Mandatory)][string]$RehearsalSha256
    )

    $gate = @($Manifest.gates | Where-Object {
        $_.id -eq 'WP8_RELEASE_REHEARSAL_AND_SIGNOFF'
    })[0]
    $gate.ownerName = 'Zhang Wei'
    $gate.acceptanceStatus = 'Accepted'
    $gate.verificationManifest = $RehearsalReference
    $gate.acceptedEvidence = @(
        (New-Attestation -Uri $RehearsalReference -Sha256 $RehearsalSha256))
}

function Set-Wp7Accepted {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$GoldenReference,
        [Parameter(Mandatory)][string]$GoldenSha256
    )

    $gate = @($Manifest.gates | Where-Object {
        $_.id -eq 'WP7_GOLDEN_CAD_FORMAL_EVIDENCE'
    })[0]
    $gate.ownerName = 'Zhang Wei'
    $gate.acceptanceStatus = 'Accepted'
    $gate.verificationManifest = $GoldenReference
    $gate.acceptedEvidence = @(
        (New-Attestation -Uri $GoldenReference -Sha256 $GoldenSha256))
}

function Complete-Wp7Prerequisites {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$Attestation,
        [Parameter(Mandatory)][string]$KickoffReference,
        [Parameter(Mandatory)][string]$KickoffSha256
    )

    $inputOwners = [ordered]@{
        AUTHORIZED_GOLDEN_CAD_CANDIDATES = 'BUBAO.GAO'
        PRIMARY_PROVIDER_AND_ISOLATED_WORKER = 'Zhang Wei'
    }
    foreach ($inputId in $inputOwners.Keys) {
        $input = @($Manifest.externalInputs | Where-Object {
            $_.id -eq $inputId
        })[0]
        $input.ownerName = $inputOwners[$inputId]
        $input.status = 'Complete'
        $verificationReference = $KickoffReference
        $verificationSha256 = $KickoffSha256
        if ($inputId -eq 'AUTHORIZED_GOLDEN_CAD_CANDIDATES') {
            $verificationReference = $script:candidateReference
            $verificationSha256 = $script:candidateSha256
        }
        $input.verificationManifest = $verificationReference
        $input.evidence = @(
            (New-Attestation `
                -Uri $verificationReference `
                -Sha256 $verificationSha256 `
                -AcceptedBy $input.ownerName))
    }
    $providerGate = @($Manifest.gates | Where-Object {
        $_.id -eq 'WP3_PRIMARY_PROVIDER_AND_ISOLATED_WORKER'
    })[0]
    $providerGate.ownerName = 'Zhang Wei'
    $providerGate.acceptanceStatus = 'Accepted'
    $providerGate.acceptedEvidence = @($Attestation)
}

function Complete-Wp8Prerequisites {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$Attestation,
        [Parameter(Mandatory)][string]$KickoffReference,
        [Parameter(Mandatory)][string]$KickoffSha256,
        [Parameter(Mandatory)][string]$GoldenReference,
        [Parameter(Mandatory)][string]$GoldenSha256
    )

    Complete-Wp7Prerequisites `
        -Manifest $Manifest `
        -Attestation $Attestation `
        -KickoffReference $KickoffReference `
        -KickoffSha256 $KickoffSha256
    Set-Wp7Accepted `
        -Manifest $Manifest `
        -GoldenReference $GoldenReference `
        -GoldenSha256 $GoldenSha256
}

function Set-ExternalInputComplete {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)][string]$InputId,
        [Parameter(Mandatory)][string]$OwnerName,
        [Parameter(Mandatory)][string]$KickoffReference,
        [Parameter(Mandatory)][string]$KickoffSha256
    )

    $input = @($Manifest.externalInputs | Where-Object {
        $_.id -eq $InputId
    })[0]
    $input.ownerName = $OwnerName
    $input.status = 'Complete'
    $input.verificationManifest = $KickoffReference
    $input.evidence = @(
        (New-Attestation `
            -Uri $KickoffReference `
            -Sha256 $KickoffSha256 `
            -AcceptedBy $OwnerName))
}

function Set-AllGaSignersSigned {
    param(
        [Parameter(Mandatory)]$Manifest,
        [Parameter(Mandatory)]$Attestation
    )

    $index = 1
    foreach ($signer in @($Manifest.signers)) {
        $signer.name = "Signer Person $index"
        $signer.status = 'Signed'
        $signer.evidence = @([pscustomobject]@{
            uri = $Attestation.uri
            sha256 = $Attestation.sha256
            acceptedBy = $signer.name
            acceptedAtUtc = $Attestation.acceptedAtUtc
        })
        $index++
    }
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

    # The validator exit code has been asserted above. Clear the consumed native
    # process status so a successful suite cannot leak an expected failure to CI.
    $global:LASTEXITCODE = 0
}

try {
    $rehearsalAcceptanceReference = (
        'docs/space/acceptance/v1.3-ga/.tmp-' +
        [Guid]::NewGuid().ToString('N') + '/release-rehearsal-evidence.json')
    $rehearsalAcceptancePath = Join-Path $repo $rehearsalAcceptanceReference
    $rehearsalAcceptanceDirectory = Split-Path -Parent $rehearsalAcceptancePath
    [void](New-Item -ItemType Directory -Path $rehearsalAcceptanceDirectory)
    $exportOutput = & $hostExecutable `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $releaseRehearsalTestSuite `
        -ExportValidManifestPath $rehearsalAcceptancePath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Could not export valid release rehearsal fixture.`n$exportOutput"
    }
    $rehearsalAcceptance = Get-Content -LiteralPath $rehearsalAcceptancePath -Raw |
        ConvertFrom-Json
    foreach ($evidence in @(
        $rehearsalAcceptance.evidence.execution,
        $rehearsalAcceptance.evidence.publishWms,
        $rehearsalAcceptance.evidence.viewer,
        $rehearsalAcceptance.evidence.recovery,
        $rehearsalAcceptance.evidence.security)) {
        $evidence.uri = ([string]$evidence.uri).Replace(':test:', ':integration:')
    }
    $rehearsalAcceptance | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $rehearsalAcceptancePath -Encoding UTF8
    $rehearsalAcceptanceSha256 = (Get-FileHash -LiteralPath $rehearsalAcceptancePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()

    $sharedNestedEvidenceReference = (
        'docs/space/reports/2026-08-14-ga-evidence-attestation.md')
    $sharedNestedEvidenceSha256 = (Get-FileHash `
        -LiteralPath (Join-Path $repo $sharedNestedEvidenceReference) `
        -Algorithm SHA256).Hash.ToLowerInvariant()

    $goldenAcceptanceReference = (
        'docs/space/acceptance/v1.3-ga/.tmp-' +
        [Guid]::NewGuid().ToString('N') + '/golden-cad-evidence.json')
    $goldenAcceptancePath = Join-Path $repo $goldenAcceptanceReference
    $goldenAcceptanceDirectory = Split-Path -Parent $goldenAcceptancePath
    [void](New-Item -ItemType Directory -Path $goldenAcceptanceDirectory)
    $exportOutput = & $hostExecutable `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $goldenCadTestSuite `
        -ExportValidManifestPath $goldenAcceptancePath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Could not export valid golden CAD fixture.`n$exportOutput"
    }
    $goldenAcceptance = Get-Content -LiteralPath $goldenAcceptancePath -Raw |
        ConvertFrom-Json
    $goldenAcceptance.dataset.integrityAuditEvidence.uri = (
        $sharedNestedEvidenceReference)
    $goldenAcceptance.dataset.integrityAuditEvidence.sha256 = (
        $sharedNestedEvidenceSha256)
    foreach ($sample in @($goldenAcceptance.dataset.samples)) {
        foreach ($evidence in @(
            $sample.authorizationEvidence,
            $sample.deidentificationEvidence,
            $sample.annotation.evidence)) {
            $evidence.uri = ([string]$evidence.uri).Replace(
                ':test:',
                ':integration:')
        }
    }
    foreach ($provider in @($goldenAcceptance.providers)) {
        foreach ($evidence in @(
            $provider.qualificationEvidence,
            $provider.evaluationEvidence,
            $provider.performance.evidence)) {
            $evidence.uri = ([string]$evidence.uri).Replace(
                ':test:',
                ':integration:')
        }
    }
    $goldenAcceptance | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $goldenAcceptancePath -Encoding UTF8
    $goldenAcceptanceSha256 = (Get-FileHash -LiteralPath $goldenAcceptancePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()

    $kickoffAcceptanceReference = (
        'docs/space/acceptance/v1.3-ga/.tmp-' +
        [Guid]::NewGuid().ToString('N') + '/kickoff-evidence.json')
    $kickoffAcceptancePath = Join-Path $repo $kickoffAcceptanceReference
    $kickoffAcceptanceDirectory = Split-Path -Parent $kickoffAcceptancePath
    [void](New-Item -ItemType Directory -Path $kickoffAcceptanceDirectory)
    $kickoffExportOutput = & $hostExecutable `
        -NoProfile `
        -ExecutionPolicy Bypass `
        -File $kickoffTestSuite `
        -ExportValidManifestPath $kickoffAcceptancePath 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) {
        throw "Could not export valid kickoff fixture.`n$kickoffExportOutput"
    }
    $kickoffAcceptanceContent = Get-Content `
        -LiteralPath $kickoffAcceptancePath -Raw
    $kickoffAcceptanceContent.Replace(':test:', ':integration:') |
        Set-Content -LiteralPath $kickoffAcceptancePath -Encoding UTF8
    $kickoffAcceptanceSha256 = (Get-FileHash `
        -LiteralPath $kickoffAcceptancePath `
        -Algorithm SHA256).Hash.ToLowerInvariant()

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
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'DeliveryOwner' })[0]
        $signer.name = 'Zhang Wei'
        $signer.status = 'Signed'
        $signer.evidence = @($localAttestation)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
            -OwnerName 'BUBAO.GAO' `
            -KickoffReference $candidateReference `
            -KickoffSha256 $candidateSha256
    }
    Invoke-ValidatorCase `
        -Name 'local evidence with matching content hash' `
        -ManifestPath $positivePath `
        -ShouldPass $true

    $numericOwnerPath = New-TestManifest 'numeric-development-owner' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
            -OwnerName '00001' `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
    }
    Invoke-ValidatorCase `
        -Name 'development code is not a formal input owner' `
        -ManifestPath $numericOwnerPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_INPUT_OWNER_INVALID'

    $numericSignerPath = New-TestManifest 'numeric-development-signer' {
        param($manifest)
        $signer = @($manifest.signers | Where-Object {
            $_.role -eq 'DeliveryOwner'
        })[0]
        $signer.name = '00001'
        $signer.status = 'Signed'
        $signer.evidence = @(
            (New-Attestation `
                -Uri $fixtureReference `
                -Sha256 $fixtureSha256 `
                -AcceptedBy '00001'))
    }
    Invoke-ValidatorCase `
        -Name 'development code is not a formal GA signer' `
        -ManifestPath $numericSignerPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_SIGNER_NAME_INVALID'

    $missingKickoffPath = New-TestManifest 'missing-kickoff-manifest' {
        param($manifest)
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'AUTHORIZED_GOLDEN_CAD_CANDIDATES'
        })[0]
        $input.ownerName = 'Zhang Wei'
        $input.status = 'Complete'
        $input.verificationManifest = $null
        $input.evidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'complete external input requires a kickoff manifest' `
        -ManifestPath $missingKickoffPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_MANIFEST_REQUIRED'

    $unattestedKickoffPath = New-TestManifest 'unattested-kickoff-manifest' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
            -OwnerName 'BUBAO.GAO' `
            -KickoffReference $candidateReference `
            -KickoffSha256 $candidateSha256
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'AUTHORIZED_GOLDEN_CAD_CANDIDATES'
        })[0]
        $input.evidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'complete external input attests the kickoff manifest itself' `
        -ManifestPath $unattestedKickoffPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_MANIFEST_UNATTESTED'

    $invalidKickoffReference = $kickoffAcceptanceReference.Replace(
        'kickoff-evidence.json',
        'invalid-cad-candidates.json')
    $invalidKickoffPath = Join-Path $repo $invalidKickoffReference
    $invalidKickoff = Get-Content -LiteralPath $candidatePath -Raw |
        ConvertFrom-Json
    $invalidKickoff.dataset.samples = @(
        $invalidKickoff.dataset.samples |
            Select-Object -First 19)
    $invalidKickoff | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $invalidKickoffPath -Encoding UTF8
    $invalidKickoffSha256 = (Get-FileHash -LiteralPath $invalidKickoffPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidKickoffInputPath = New-TestManifest 'invalid-kickoff-input' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
            -OwnerName 'Zhang Wei' `
            -KickoffReference $invalidKickoffReference `
            -KickoffSha256 $invalidKickoffSha256
    }
    Invoke-ValidatorCase `
        -Name 'external input rejects a semantically invalid kickoff package' `
        -ManifestPath $invalidKickoffInputPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_EVIDENCE_INVALID'

    $kickoffTemplateReference = (
        'docs/space/acceptance/v1.3-ga/kickoff-evidence-template.json')
    $kickoffTemplateSha256 = (Get-FileHash `
        -LiteralPath (Join-Path $repo $kickoffTemplateReference) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $templateKickoffPath = New-TestManifest 'template-kickoff-input' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'AUTHORIZED_GOLDEN_CAD_CANDIDATES' `
            -OwnerName 'Zhang Wei' `
            -KickoffReference $kickoffTemplateReference `
            -KickoffSha256 $kickoffTemplateSha256
    }
    Invoke-ValidatorCase `
        -Name 'external input rejects the blank kickoff template' `
        -ManifestPath $templateKickoffPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_MANIFEST_SYNTHETIC'

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
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'DeliveryOwner' })[0]
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
            $_.id -eq 'AUTHORIZED_GOLDEN_CAD_CANDIDATES'
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
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'DeliveryOwner' })[0]
        $signer.name = 'Product'
        $signer.status = 'Signed'
        $signer.evidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'signed signer requires a real person name' `
        -ManifestPath $placeholderSignerNamePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_SIGNER_NAME_INVALID'

    $mismatchedSignerPath = New-TestManifest 'mismatched-signer-evidence' {
        param($manifest)
        $signer = @($manifest.signers | Where-Object {
            $_.role -eq 'DeliveryOwner'
        })[0]
        $signer.name = 'Zhang Wei'
        $signer.status = 'Signed'
        $signer.evidence = @(
            (New-Attestation `
                -Uri $fixtureReference `
                -Sha256 $fixtureSha256 `
                -AcceptedBy 'Li Ming'))
    }
    Invoke-ValidatorCase `
        -Name 'signer evidence must be accepted by the named signer' `
        -ManifestPath $mismatchedSignerPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_SIGNER_EVIDENCE_MISMATCH'

    $placeholderInputOwnerPath = New-TestManifest 'placeholder-input-owner' {
        param($manifest)
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'AUTHORIZED_GOLDEN_CAD_CANDIDATES'
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

    $missingGoldenManifestPath = New-TestManifest 'missing-golden-manifest' {
        param($manifest)
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP7_GOLDEN_CAD_FORMAL_EVIDENCE'
        })[0]
        $gate.ownerName = 'Zhang Wei'
        $gate.acceptanceStatus = 'Accepted'
        $gate.acceptedEvidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'accepted WP7 requires a structured golden CAD manifest' `
        -ManifestPath $missingGoldenManifestPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_MANIFEST_REQUIRED'

    $goldenPrerequisitePath = New-TestManifest 'golden-prerequisites' {
        param($manifest)
        Set-Wp7Accepted `
            -Manifest $manifest `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP7 requires authorized CAD Provider and Worker prerequisites' `
        -ManifestPath $goldenPrerequisitePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_PREREQUISITES_INCOMPLETE'

    $validGoldenPath = New-TestManifest 'valid-golden-manifest' {
        param($manifest)
        Complete-Wp7Prerequisites `
            -Manifest $manifest `
            -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
        Set-Wp7Accepted `
            -Manifest $manifest `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
    }
    Invoke-ValidatorCase `
        -Name 'accepted WP7 validates manifest and prerequisites' `
        -ManifestPath $validGoldenPath `
        -ShouldPass $true

    $unattestedGoldenPath = New-TestManifest 'unattested-golden-manifest' {
        param($manifest)
        Complete-Wp7Prerequisites `
            -Manifest $manifest `
            -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
        Set-Wp7Accepted `
            -Manifest $manifest `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP7_GOLDEN_CAD_FORMAL_EVIDENCE'
        })[0]
        $gate.acceptedEvidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'WP7 must attest the structured manifest itself' `
        -ManifestPath $unattestedGoldenPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_MANIFEST_UNATTESTED'

    $invalidGoldenReference = $goldenAcceptanceReference.Replace(
        'golden-cad-evidence.json',
        'invalid-golden-cad-evidence.json')
    $invalidGoldenPath = Join-Path $repo $invalidGoldenReference
    $invalidGolden = Get-Content -LiteralPath $goldenAcceptancePath -Raw |
        ConvertFrom-Json
    $invalidGolden.providers[0].overallMetrics.targetCoveragePercent = 79
    $invalidGolden | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $invalidGoldenPath -Encoding UTF8
    $invalidGoldenSha256 = (Get-FileHash -LiteralPath $invalidGoldenPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidGoldenGatePath = New-TestManifest 'invalid-golden-gate' {
        param($manifest)
        Complete-Wp7Prerequisites `
            -Manifest $manifest `
            -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
        Set-Wp7Accepted `
            -Manifest $manifest `
            -GoldenReference $invalidGoldenReference `
            -GoldenSha256 $invalidGoldenSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP7 cannot accept a semantically invalid golden CAD manifest' `
        -ManifestPath $invalidGoldenGatePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_EVIDENCE_INVALID'

    $goldenTemplateReference = (
        'docs/space/acceptance/v1.3-ga/golden-cad-evidence-template.json')
    $goldenTemplateSha256 = (Get-FileHash `
        -LiteralPath (Join-Path $repo $goldenTemplateReference) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $templateGoldenPath = New-TestManifest 'template-golden-manifest' {
        param($manifest)
        Complete-Wp7Prerequisites `
            -Manifest $manifest `
            -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
        Set-Wp7Accepted `
            -Manifest $manifest `
            -GoldenReference $goldenTemplateReference `
            -GoldenSha256 $goldenTemplateSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP7 cannot accept the blank golden CAD template' `
        -ManifestPath $templateGoldenPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_GOLDEN_MANIFEST_SYNTHETIC'

    $rehearsalPrerequisitePath = New-TestManifest 'rehearsal-prerequisites' {
        param($manifest)
        Set-Wp8Accepted `
            -Manifest $manifest `
            -RehearsalReference $rehearsalAcceptanceReference `
            -RehearsalSha256 $rehearsalAcceptanceSha256
        Set-AllGaSignersSigned `
            -Manifest $manifest `
            -Attestation $localAttestation
    }
    Invoke-ValidatorCase `
        -Name 'WP8 waits for Primary, WP3 and WP7' `
        -ManifestPath $rehearsalPrerequisitePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_PREREQUISITES_INCOMPLETE'

    $missingRehearsalManifestPath = New-TestManifest 'missing-rehearsal-manifest' {
        param($manifest)
        Complete-Wp8Prerequisites `
            -Manifest $manifest -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256 `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP8_RELEASE_REHEARSAL_AND_SIGNOFF'
        })[0]
        $gate.ownerName = 'Zhang Wei'
        $gate.acceptanceStatus = 'Accepted'
        $gate.acceptedEvidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'accepted WP8 requires a structured release rehearsal manifest' `
        -ManifestPath $missingRehearsalManifestPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_MANIFEST_REQUIRED'

    $unsignedRehearsalPath = New-TestManifest 'unsigned-rehearsal-manifest' {
        param($manifest)
        Complete-Wp8Prerequisites `
            -Manifest $manifest -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256 `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        Set-Wp8Accepted `
            -Manifest $manifest `
            -RehearsalReference $rehearsalAcceptanceReference `
            -RehearsalSha256 $rehearsalAcceptanceSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP8 cannot be accepted before the delivery owner signs' `
        -ManifestPath $unsignedRehearsalPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_SIGNER_INCOMPLETE'

    $validRehearsalPath = New-TestManifest 'valid-rehearsal-manifest' {
        param($manifest)
        Complete-Wp8Prerequisites `
            -Manifest $manifest -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256 `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        Set-Wp8Accepted `
            -Manifest $manifest `
            -RehearsalReference $rehearsalAcceptanceReference `
            -RehearsalSha256 $rehearsalAcceptanceSha256
        Set-AllGaSignersSigned `
            -Manifest $manifest `
            -Attestation $localAttestation
    }
    Invoke-ValidatorCase `
        -Name 'accepted WP8 validates release rehearsal and signer' `
        -ManifestPath $validRehearsalPath `
        -ShouldPass $true

    $unattestedRehearsalPath = New-TestManifest 'unattested-rehearsal-manifest' {
        param($manifest)
        Complete-Wp8Prerequisites `
            -Manifest $manifest -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256 `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        Set-Wp8Accepted `
            -Manifest $manifest `
            -RehearsalReference $rehearsalAcceptanceReference `
            -RehearsalSha256 $rehearsalAcceptanceSha256
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP8_RELEASE_REHEARSAL_AND_SIGNOFF'
        })[0]
        $gate.acceptedEvidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'WP8 must attest the structured manifest itself' `
        -ManifestPath $unattestedRehearsalPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_MANIFEST_UNATTESTED'

    $invalidRehearsalReference = $rehearsalAcceptanceReference.Replace(
        'release-rehearsal-evidence.json',
        'invalid-release-rehearsal-evidence.json')
    $invalidRehearsalPath = Join-Path $repo $invalidRehearsalReference
    $invalidRehearsal = Get-Content -LiteralPath $rehearsalAcceptancePath -Raw |
        ConvertFrom-Json
    $invalidRehearsal.defects.s1Open = 1
    $invalidRehearsal | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $invalidRehearsalPath -Encoding UTF8
    $invalidRehearsalSha256 = (Get-FileHash -LiteralPath $invalidRehearsalPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidRehearsalGatePath = New-TestManifest 'invalid-rehearsal-gate' {
        param($manifest)
        Complete-Wp8Prerequisites `
            -Manifest $manifest -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256 `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        Set-Wp8Accepted `
            -Manifest $manifest `
            -RehearsalReference $invalidRehearsalReference `
            -RehearsalSha256 $invalidRehearsalSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP8 cannot accept an invalid release rehearsal manifest' `
        -ManifestPath $invalidRehearsalGatePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_EVIDENCE_INVALID'

    $templateReference = (
        'docs/space/acceptance/v1.3-ga/release-rehearsal-evidence-template.json')
    $templateSha256 = (Get-FileHash -LiteralPath (Join-Path $repo $templateReference) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $templateRehearsalPath = New-TestManifest 'template-rehearsal-manifest' {
        param($manifest)
        Complete-Wp8Prerequisites `
            -Manifest $manifest -Attestation $localAttestation `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256 `
            -GoldenReference $goldenAcceptanceReference `
            -GoldenSha256 $goldenAcceptanceSha256
        Set-Wp8Accepted `
            -Manifest $manifest `
            -RehearsalReference $templateReference `
            -RehearsalSha256 $templateSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP8 cannot accept the blank rehearsal template' `
        -ManifestPath $templateRehearsalPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_REHEARSAL_MANIFEST_SYNTHETIC'

    if ($global:LASTEXITCODE -ne 0) {
        throw "Test suite leaked child process exit code $global:LASTEXITCODE."
    }

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
    if ($null -ne $rehearsalAcceptanceDirectory -and
        (Test-Path -LiteralPath $rehearsalAcceptanceDirectory)) {
        [System.IO.Directory]::Delete($rehearsalAcceptanceDirectory, $true)
    }
    if ($null -ne $goldenAcceptanceDirectory -and
        (Test-Path -LiteralPath $goldenAcceptanceDirectory)) {
        [System.IO.Directory]::Delete($goldenAcceptanceDirectory, $true)
    }
    if ($null -ne $kickoffAcceptanceDirectory -and
        (Test-Path -LiteralPath $kickoffAcceptanceDirectory)) {
        [System.IO.Directory]::Delete($kickoffAcceptanceDirectory, $true)
    }
}
