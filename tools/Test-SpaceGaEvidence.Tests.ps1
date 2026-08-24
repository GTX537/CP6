$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$validator = Join-Path $PSScriptRoot 'Test-SpaceGaEvidence.ps1'
$baseManifestPath = Join-Path $repo (
    'docs\space\acceptance\v1.3-ga\ga-evidence-index.json')
$fixtureReference = 'tools/test-fixtures/space-ga-evidence/attestation-fixture.txt'
$fixturePath = Join-Path $repo $fixtureReference
$fixtureSha256 = (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash.ToLowerInvariant()
$pilotFixturePath = Join-Path $repo (
    'tools\test-fixtures\space-ga-pilot-evidence\valid-pilot-evidence.json')
$goldenCadTestSuite = Join-Path $PSScriptRoot (
    'Test-SpaceGaGoldenCadEvidence.Tests.ps1')
$kickoffTestSuite = Join-Path $PSScriptRoot (
    'Test-SpaceGaKickoffEvidence.Tests.ps1')
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
        [Parameter(Mandatory)][string]$PilotReference,
        [Parameter(Mandatory)][string]$PilotSha256
    )

    $gate = @($Manifest.gates | Where-Object {
        $_.id -eq 'WP8_TWO_SITE_PILOT_AND_SIGNOFF'
    })[0]
    $gate.ownerName = 'Zhang Wei'
    $gate.acceptanceStatus = 'Accepted'
    $gate.verificationManifest = $PilotReference
    $gate.acceptedEvidence = @(
        (New-Attestation -Uri $PilotReference -Sha256 $PilotSha256))
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
        AUTHORIZED_GOLDEN_CAD_CANDIDATES = 'Liu Yan'
        PROVIDER_APPROVALS_AND_ISOLATED_WORKER = 'Qian Lin'
    }
    foreach ($inputId in $inputOwners.Keys) {
        $input = @($Manifest.externalInputs | Where-Object {
            $_.id -eq $inputId
        })[0]
        $input.ownerName = $inputOwners[$inputId]
        $input.status = 'Complete'
        $input.verificationManifest = $KickoffReference
        $input.evidence = @(
            (New-Attestation `
                -Uri $KickoffReference `
                -Sha256 $KickoffSha256 `
                -AcceptedBy $input.ownerName))
    }
    $providerGate = @($Manifest.gates | Where-Object {
        $_.id -eq 'WP3_SITE_PRIMARY_BACKUP_PROVIDERS'
    })[0]
    $providerGate.ownerName = 'Zhang Wei'
    $providerGate.acceptanceStatus = 'Accepted'
    $providerGate.acceptedEvidence = @($Attestation)
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
}

try {
    $pilotAcceptanceReference = (
        'docs/space/acceptance/v1.3-ga/.tmp-' +
        [Guid]::NewGuid().ToString('N') + '/pilot-evidence.json')
    $pilotAcceptancePath = Join-Path $repo $pilotAcceptanceReference
    $pilotAcceptanceDirectory = Split-Path -Parent $pilotAcceptancePath
    [void](New-Item -ItemType Directory -Path $pilotAcceptanceDirectory)
    $pilotAcceptance = Get-Content -LiteralPath $pilotFixturePath -Raw |
        ConvertFrom-Json
    $pilotNestedEvidenceReference = (
        'docs/space/reports/2026-08-14-ga-evidence-attestation.md')
    $pilotNestedEvidenceSha256 = (Get-FileHash `
        -LiteralPath (Join-Path $repo $pilotNestedEvidenceReference) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    foreach ($site in @($pilotAcceptance.sites)) {
        foreach ($evidenceName in @(
            'runLog',
            'metrics',
            'defectClosure',
            'businessOutcome',
            'openIssuesAppendix')) {
            $site.evidence.$evidenceName.uri = $pilotNestedEvidenceReference
            $site.evidence.$evidenceName.sha256 = $pilotNestedEvidenceSha256
        }
        foreach ($confirmationName in @(
            'customerWarehouseRepresentative',
            'implementationLead')) {
            $site.confirmations.$confirmationName.evidence.uri = (
                $pilotNestedEvidenceReference)
            $site.confirmations.$confirmationName.evidence.sha256 = (
                $pilotNestedEvidenceSha256)
        }
    }
    $pilotAcceptance | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $pilotAcceptancePath -Encoding UTF8
    $pilotAcceptanceSha256 = (Get-FileHash -LiteralPath $pilotAcceptancePath `
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
        $pilotNestedEvidenceReference)
    $goldenAcceptance.dataset.integrityAuditEvidence.sha256 = (
        $pilotNestedEvidenceSha256)
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
        $signer = @($manifest.signers | Where-Object { $_.role -eq 'Product' })[0]
        $signer.name = 'Zhang Wei'
        $signer.status = 'Signed'
        $signer.evidence = @($localAttestation)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'CORE_TEAM_ALLOCATION' `
            -OwnerName 'Zhang Wei' `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
    }
    Invoke-ValidatorCase `
        -Name 'local evidence with matching content hash' `
        -ManifestPath $positivePath `
        -ShouldPass $true

    $numericOwnerPath = New-TestManifest 'numeric-development-owner' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'CORE_TEAM_ALLOCATION' `
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
            $_.role -eq 'Product'
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
            $_.id -eq 'CORE_TEAM_ALLOCATION'
        })[0]
        $input.ownerName = 'Zhang Wei'
        $input.status = 'Complete'
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
            -InputId 'CORE_TEAM_ALLOCATION' `
            -OwnerName 'Zhang Wei' `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
        $input = @($manifest.externalInputs | Where-Object {
            $_.id -eq 'CORE_TEAM_ALLOCATION'
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
        'invalid-kickoff-evidence.json')
    $invalidKickoffPath = Join-Path $repo $invalidKickoffReference
    $invalidKickoff = Get-Content -LiteralPath $kickoffAcceptancePath -Raw |
        ConvertFrom-Json
    $invalidKickoff.coreTeamAllocation.members = @(
        $invalidKickoff.coreTeamAllocation.members | Where-Object {
            $_.name -ne 'Backend Two'
        })
    $invalidKickoff | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $invalidKickoffPath -Encoding UTF8
    $invalidKickoffSha256 = (Get-FileHash -LiteralPath $invalidKickoffPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidKickoffInputPath = New-TestManifest 'invalid-kickoff-input' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'CORE_TEAM_ALLOCATION' `
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
            -InputId 'CORE_TEAM_ALLOCATION' `
            -OwnerName 'Zhang Wei' `
            -KickoffReference $kickoffTemplateReference `
            -KickoffSha256 $kickoffTemplateSha256
    }
    Invoke-ValidatorCase `
        -Name 'external input rejects the blank kickoff template' `
        -ManifestPath $templateKickoffPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_MANIFEST_SYNTHETIC'

    $signerIndexPath = New-TestManifest 'kickoff-signer-index' {
        param($manifest)
        Set-ExternalInputComplete `
            -Manifest $manifest `
            -InputId 'NAMED_GA_SIGNERS' `
            -OwnerName 'Zhang Wei' `
            -KickoffReference $kickoffAcceptanceReference `
            -KickoffSha256 $kickoffAcceptanceSha256
        $kickoff = Get-Content -LiteralPath $kickoffAcceptancePath -Raw |
            ConvertFrom-Json
        foreach ($signer in @($manifest.signers)) {
            $namedSigner = @($kickoff.namedGaSigners.signers | Where-Object {
                $_.role -eq $signer.role
            })[0]
            $signer.name = $namedSigner.name
        }
        $manifest.signers[0].name = 'Different Person'
    }
    Invoke-ValidatorCase `
        -Name 'GA signer index matches the kickoff register' `
        -ManifestPath $signerIndexPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_KICKOFF_SIGNER_INDEX_MISMATCH'

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

    $mismatchedSignerPath = New-TestManifest 'mismatched-signer-evidence' {
        param($manifest)
        $signer = @($manifest.signers | Where-Object {
            $_.role -eq 'Product'
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

    $missingPilotManifestPath = New-TestManifest 'missing-pilot-manifest' {
        param($manifest)
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP8_TWO_SITE_PILOT_AND_SIGNOFF'
        })[0]
        $gate.ownerName = 'Zhang Wei'
        $gate.acceptanceStatus = 'Accepted'
        $gate.acceptedEvidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'accepted WP8 requires a structured pilot manifest' `
        -ManifestPath $missingPilotManifestPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_MANIFEST_REQUIRED'

    $unsignedPilotPath = New-TestManifest 'unsigned-pilot-manifest' {
        param($manifest)
        Set-Wp8Accepted `
            -Manifest $manifest `
            -PilotReference $pilotAcceptanceReference `
            -PilotSha256 $pilotAcceptanceSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP8 cannot be accepted before all five internal signers' `
        -ManifestPath $unsignedPilotPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_SIGNERS_INCOMPLETE'

    $validPilotPath = New-TestManifest 'valid-pilot-manifest' {
        param($manifest)
        Set-Wp8Accepted `
            -Manifest $manifest `
            -PilotReference $pilotAcceptanceReference `
            -PilotSha256 $pilotAcceptanceSha256
        Set-AllGaSignersSigned `
            -Manifest $manifest `
            -Attestation $localAttestation
    }
    Invoke-ValidatorCase `
        -Name 'accepted WP8 validates the pilot manifest and signers' `
        -ManifestPath $validPilotPath `
        -ShouldPass $true

    $unattestedPilotPath = New-TestManifest 'unattested-pilot-manifest' {
        param($manifest)
        Set-Wp8Accepted `
            -Manifest $manifest `
            -PilotReference $pilotAcceptanceReference `
            -PilotSha256 $pilotAcceptanceSha256
        $gate = @($manifest.gates | Where-Object {
            $_.id -eq 'WP8_TWO_SITE_PILOT_AND_SIGNOFF'
        })[0]
        $gate.acceptedEvidence = @($localAttestation)
    }
    Invoke-ValidatorCase `
        -Name 'WP8 must attest the structured manifest itself' `
        -ManifestPath $unattestedPilotPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_MANIFEST_UNATTESTED'

    $invalidPilotReference = $pilotAcceptanceReference.Replace(
        'pilot-evidence.json',
        'invalid-pilot-evidence.json')
    $invalidPilotPath = Join-Path $repo $invalidPilotReference
    $invalidPilot = Get-Content -LiteralPath $pilotAcceptancePath -Raw |
        ConvertFrom-Json
    $invalidPilot.sites[0].defects.s1Count = 1
    $invalidPilot | ConvertTo-Json -Depth 100 |
        Set-Content -LiteralPath $invalidPilotPath -Encoding UTF8
    $invalidPilotSha256 = (Get-FileHash -LiteralPath $invalidPilotPath `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $invalidPilotGatePath = New-TestManifest 'invalid-pilot-gate' {
        param($manifest)
        Set-Wp8Accepted `
            -Manifest $manifest `
            -PilotReference $invalidPilotReference `
            -PilotSha256 $invalidPilotSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP8 cannot accept a semantically invalid pilot manifest' `
        -ManifestPath $invalidPilotGatePath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_EVIDENCE_INVALID'

    $templateReference = (
        'docs/space/acceptance/v1.3-ga/pilot-evidence-template.json')
    $templateSha256 = (Get-FileHash -LiteralPath (Join-Path $repo $templateReference) `
        -Algorithm SHA256).Hash.ToLowerInvariant()
    $templatePilotPath = New-TestManifest 'template-pilot-manifest' {
        param($manifest)
        Set-Wp8Accepted `
            -Manifest $manifest `
            -PilotReference $templateReference `
            -PilotSha256 $templateSha256
    }
    Invoke-ValidatorCase `
        -Name 'WP8 cannot accept the blank pilot template' `
        -ManifestPath $templatePilotPath `
        -ShouldPass $false `
        -ExpectedError 'SPACE_GA_PILOT_MANIFEST_SYNTHETIC'

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
    if ($null -ne $pilotAcceptanceDirectory -and
        (Test-Path -LiteralPath $pilotAcceptanceDirectory)) {
        [System.IO.Directory]::Delete($pilotAcceptanceDirectory, $true)
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
