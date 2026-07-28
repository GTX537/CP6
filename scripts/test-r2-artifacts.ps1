[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ArtifactDirectory,
    [ValidatePattern("^\d+\.\d+\.\d+$")]
    [string]$ExpectedVersion = "1.0.0",
    [Parameter(Mandatory = $true)][string]$ExpectedWindowsPublisher,
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[A-Fa-f0-9]{64}$")]
    [string]$ExpectedAndroidSignerSha256,
    [Parameter(Mandatory = $true)][string]$ResolvedSettingsPath,
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^[A-Fa-f0-9]{40}$")]
    [string]$GitSha,
    [Parameter(Mandatory = $true)][string]$ApiImageRepository,
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^sha256:[A-Fa-f0-9]{64}$")]
    [string]$ApiImageDigest,
    [Parameter(Mandatory = $true)][string]$WebImageRepository,
    [Parameter(Mandatory = $true)]
    [ValidatePattern("^sha256:[A-Fa-f0-9]{64}$")]
    [string]$WebImageDigest,
    [Parameter(Mandatory = $true)][string]$SbomPath,
    [Parameter(Mandatory = $true)][string]$VulnerabilityReportPath,
    [Parameter(Mandatory = $true)][string]$DatabaseInitializationArtifactPath,
    [Parameter(Mandatory = $true)][string]$SourceGateReportPath,
    [Parameter(Mandatory = $true)][string]$SqlIntegrationReportPath,
    [Parameter(Mandatory = $true)][string]$LatestMigration,
    [Parameter(Mandatory = $true)][string]$EvidenceRootUri,
    [string]$OutputManifestPath
)

$ErrorActionPreference = "Stop"

function Get-SingleArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Filter
    )

    $matches = @(Get-ChildItem -LiteralPath $Directory -File -Filter $Filter)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one '$Filter' artifact in '$Directory'; found $($matches.Count)."
    }
    return $matches[0]
}

function Resolve-WindowsSdkTool {
    param([Parameter(Mandatory = $true)][string]$ToolName)

    $fromPath = Get-Command $ToolName -ErrorAction SilentlyContinue
    if ($null -ne $fromPath) {
        return $fromPath.Source
    }

    $sdkRoots = @(
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Windows Kits\10\bin"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Windows Kits\10\bin")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) }

    $candidates = foreach ($sdkRoot in $sdkRoots) {
        Get-ChildItem -LiteralPath $sdkRoot -Directory |
            Where-Object { $null -ne ($_.Name -as [version]) } |
            ForEach-Object {
                $candidate = Join-Path $_.FullName "x64\$ToolName"
                if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                    [pscustomobject]@{
                        Version = [version]$_.Name
                        Path = $candidate
                    }
                }
            }
    }

    $resolved = $candidates | Sort-Object Version -Descending | Select-Object -First 1
    if ($null -eq $resolved) {
        throw "$ToolName was not found in PATH or an installed Windows SDK."
    }
    return $resolved.Path
}

function Resolve-AndroidBuildTool {
    param([Parameter(Mandatory = $true)][string]$ToolName)

    $roots = @(
        $env:ANDROID_HOME,
        $env:ANDROID_SDK_ROOT,
        (Join-Path ([Environment]::GetFolderPath("LocalApplicationData")) "Android\Sdk"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Android\android-sdk")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Container) } |
        Select-Object -Unique

    $candidates = foreach ($root in $roots) {
        $buildTools = Join-Path $root "build-tools"
        if (Test-Path -LiteralPath $buildTools -PathType Container) {
            Get-ChildItem -LiteralPath $buildTools -Directory |
                Where-Object { $null -ne ($_.Name -as [version]) } |
                ForEach-Object {
                    $candidate = Join-Path $_.FullName $ToolName
                    if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                        [pscustomobject]@{
                            Version = [version]$_.Name
                            Path = $candidate
                        }
                    }
                }
        }
    }

    $resolved = $candidates | Sort-Object Version -Descending | Select-Object -First 1
    if ($null -eq $resolved) {
        throw "$ToolName was not found in an Android SDK build-tools directory."
    }
    return $resolved.Path
}

function Resolve-JavaHome {
    if ($env:JAVA_HOME -and
        (Test-Path -LiteralPath (Join-Path $env:JAVA_HOME "bin\java.exe") -PathType Leaf)) {
        return $env:JAVA_HOME
    }

    $roots = @(
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Android\openjdk"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Microsoft")
    ) | Where-Object { Test-Path -LiteralPath $_ -PathType Container }

    $java = foreach ($root in $roots) {
        Get-ChildItem -LiteralPath $root -Recurse -File -Filter "java.exe" -ErrorAction SilentlyContinue |
            Where-Object { $_.Directory.Name -eq "bin" } |
            Select-Object -First 1
    }
    $java = $java | Select-Object -First 1
    if ($null -eq $java) {
        throw "A Java runtime for apksigner was not found. Set JAVA_HOME."
    }
    return $java.Directory.Parent.FullName
}

function Invoke-CapturedCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $FilePath @ArgumentList 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($exitCode -ne 0) {
        $message = ($output | Out-String).Trim()
        throw "$Description failed with exit code $exitCode.`n$message"
    }
    return @($output)
}

function Read-MsixManifest {
    param([Parameter(Mandatory = $true)][string]$Path)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.Entries |
            Where-Object { $_.FullName -ieq "AppxManifest.xml" } |
            Select-Object -First 1
        if ($null -eq $entry) {
            throw "AppxManifest.xml was not found in the MSIX."
        }
        $reader = [IO.StreamReader]::new($entry.Open(), [Text.Encoding]::UTF8)
        try {
            return [xml]$reader.ReadToEnd()
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Get-MsixEntryLength {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$EntryName
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try {
        $entry = $archive.Entries |
            Where-Object {
                $_.FullName.Replace("/", "\") -ieq $EntryName.Replace("/", "\")
            } |
            Select-Object -First 1
        if ($null -eq $entry) {
            throw "Required MSIX entry '$EntryName' was not found."
        }
        return $entry.Length
    }
    finally {
        $archive.Dispose()
    }
}

function Assert-HttpsUri {
    param(
        [Parameter(Mandatory = $true)][string]$Value,
        [Parameter(Mandatory = $true)][string]$Description,
        [string]$RequiredExtension
    )

    $uri = $null
    if (-not [uri]::TryCreate($Value, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -ne [uri]::UriSchemeHttps) {
        throw "$Description must be an absolute HTTPS URI."
    }
    if (-not [string]::IsNullOrWhiteSpace($RequiredExtension) -and
        -not $uri.AbsolutePath.EndsWith(
            $RequiredExtension,
            [StringComparison]::OrdinalIgnoreCase
        )) {
        throw "$Description must end with '$RequiredExtension'."
    }
}

function Normalize-Hex {
    param([Parameter(Mandatory = $true)][string]$Value)
    return ($Value -replace "[^A-Fa-f0-9]", "").ToUpperInvariant()
}

function Get-EvidenceFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Description
    )

    $resolved = Get-Item -LiteralPath (
        Resolve-Path -LiteralPath $Path -ErrorAction Stop
    ).Path
    if ($resolved.Length -le 0) {
        throw "$Description must not be empty."
    }
    return [ordered]@{
        FileName = $resolved.Name
        Bytes = $resolved.Length
        Sha256 = (Get-FileHash -LiteralPath $resolved.FullName -Algorithm SHA256).Hash
    }
}

function Assert-EvidenceRootUri {
    param([Parameter(Mandatory = $true)][string]$Value)

    $uri = $Value -as [Uri]
    if ($null -eq $uri -or -not $uri.IsAbsoluteUri -or
        ($uri.Scheme -ne "s3" -and $uri.Scheme -ne [Uri]::UriSchemeHttps) -or
        [string]::IsNullOrWhiteSpace($uri.Host) -or
        -not [string]::IsNullOrWhiteSpace($uri.UserInfo)) {
        throw "EvidenceRootUri must be an absolute s3:// or HTTPS URI without credentials."
    }
}

$resolvedArtifactDirectory = (Resolve-Path -LiteralPath $ArtifactDirectory -ErrorAction Stop).Path
if ([string]::IsNullOrWhiteSpace($OutputManifestPath)) {
    $OutputManifestPath = Join-Path $resolvedArtifactDirectory "release-manifest.json"
}
elseif (-not [IO.Path]::IsPathRooted($OutputManifestPath)) {
    $OutputManifestPath = Join-Path $resolvedArtifactDirectory $OutputManifestPath
}

$msix = Get-SingleArtifact -Directory $resolvedArtifactDirectory -Filter "*.msix"
$appInstaller = Get-SingleArtifact -Directory $resolvedArtifactDirectory -Filter "*.appinstaller"
$apk = Get-SingleArtifact -Directory $resolvedArtifactDirectory -Filter "*-Signed.apk"

$signTool = Resolve-WindowsSdkTool -ToolName "SignTool.exe"
[void](Invoke-CapturedCommand -FilePath $signTool `
    -ArgumentList @("verify", "/pa", "/v", $msix.FullName) `
    -Description "MSIX signature verification")

$authenticode = Get-AuthenticodeSignature -LiteralPath $msix.FullName
if ($authenticode.Status -ne [System.Management.Automation.SignatureStatus]::Valid -or
    $null -eq $authenticode.SignerCertificate) {
    throw "MSIX Authenticode signature is not valid: $($authenticode.StatusMessage)"
}
if ($authenticode.SignerCertificate.Subject -ne $ExpectedWindowsPublisher) {
    throw "MSIX signer subject does not match ExpectedWindowsPublisher."
}

$msixManifest = Read-MsixManifest -Path $msix.FullName
$identity = $msixManifest.Package.Identity
if ([string]$identity.Name -ne "CP6.Desktop") {
    throw "Unexpected MSIX package identity '$($identity.Name)'."
}
if ([string]$identity.Publisher -ne $ExpectedWindowsPublisher) {
    throw "MSIX manifest publisher does not match ExpectedWindowsPublisher."
}
if ([string]$identity.Version -ne "$ExpectedVersion.0") {
    throw "MSIX version '$($identity.Version)' does not match '$ExpectedVersion.0'."
}
if ([string]$identity.ProcessorArchitecture -ne "x64") {
    throw "MSIX ProcessorArchitecture must be x64."
}
$packageAssets = @(
    "Assets\StoreLogo.png",
    "Assets\Square150x150Logo.png",
    "Assets\Square44x44Logo.png"
)
foreach ($packageAsset in $packageAssets) {
    if ((Get-MsixEntryLength -Path $msix.FullName -EntryName $packageAsset) -le 100) {
        throw "MSIX asset '$packageAsset' is still a packaging placeholder."
    }
}

[xml]$appInstallerXml = [IO.File]::ReadAllText($appInstaller.FullName, [Text.Encoding]::UTF8)
$appInstallerRoot = $appInstallerXml.DocumentElement
$mainPackage = $appInstallerXml.SelectSingleNode(
    "/*[local-name()='AppInstaller']/*[local-name()='MainPackage']"
)
if ($null -eq $mainPackage) {
    throw "The AppInstaller file does not contain MainPackage."
}
if ($appInstallerRoot.Version -ne "$ExpectedVersion.0" -or
    $mainPackage.Version -ne "$ExpectedVersion.0") {
    throw "AppInstaller versions do not match '$ExpectedVersion.0'."
}
if ($mainPackage.Name -ne [string]$identity.Name -or
    $mainPackage.Publisher -ne [string]$identity.Publisher -or
    $mainPackage.ProcessorArchitecture -ne [string]$identity.ProcessorArchitecture) {
    throw "AppInstaller MainPackage identity does not match the MSIX manifest."
}
Assert-HttpsUri -Value $appInstallerRoot.Uri `
    -Description "AppInstaller self URI" `
    -RequiredExtension ".appinstaller"
Assert-HttpsUri -Value $mainPackage.Uri `
    -Description "AppInstaller package URI" `
    -RequiredExtension ".msix"

$previousJavaHome = $env:JAVA_HOME
$env:JAVA_HOME = Resolve-JavaHome
try {
    $apkSigner = Resolve-AndroidBuildTool -ToolName "apksigner.bat"
    $apkSignatureOutput = Invoke-CapturedCommand -FilePath $apkSigner `
        -ArgumentList @("verify", "--verbose", "--print-certs", $apk.FullName) `
        -Description "APK signature verification"
}
finally {
    $env:JAVA_HOME = $previousJavaHome
}
$apkSignatureText = ($apkSignatureOutput | Out-String)
if ($apkSignatureText -notmatch "Verified using v2 scheme .*:\s*true") {
    throw "APK Signature Scheme v2 verification did not pass."
}
$signerDnMatch = [regex]::Match(
    $apkSignatureText,
    "Signer #1 certificate DN:\s*(.+)"
)
$signerDigestMatch = [regex]::Match(
    $apkSignatureText,
    "Signer #1 certificate SHA-256 digest:\s*([A-Fa-f0-9]+)"
)
if (-not $signerDnMatch.Success -or -not $signerDigestMatch.Success) {
    throw "Could not read the APK signer identity."
}
$androidSignerDn = $signerDnMatch.Groups[1].Value.Trim()
$androidSignerDigest = Normalize-Hex -Value $signerDigestMatch.Groups[1].Value
if ($androidSignerDn -match "Android Debug") {
    throw "Debug-signed Android APKs cannot be release candidates."
}
if ($androidSignerDigest -ne (Normalize-Hex -Value $ExpectedAndroidSignerSha256)) {
    throw "APK signer SHA-256 does not match the approved release certificate."
}

$aapt2 = Resolve-AndroidBuildTool -ToolName "aapt2.exe"
$apkManifestOutput = Invoke-CapturedCommand -FilePath $aapt2 `
    -ArgumentList @("dump", "xmltree", $apk.FullName, "--file", "AndroidManifest.xml") `
    -Description "APK manifest inspection"
$apkManifestText = ($apkManifestOutput | Out-String)
if ($apkManifestText -notmatch "versionName.*=`"$([regex]::Escape($ExpectedVersion))`"") {
    throw "APK versionName does not match '$ExpectedVersion'."
}
if ($apkManifestText -notmatch "usesCleartextTraffic.*=false") {
    throw "APK release manifest must set usesCleartextTraffic=false."
}
if ($apkManifestText -notmatch 'package="com\.cp6\.wms\.mobile"') {
    throw "Unexpected Android application ID."
}

$msixHash = (Get-FileHash -LiteralPath $msix.FullName -Algorithm SHA256).Hash
$appInstallerHash = (Get-FileHash -LiteralPath $appInstaller.FullName -Algorithm SHA256).Hash
$apkHash = (Get-FileHash -LiteralPath $apk.FullName -Algorithm SHA256).Hash

$windowsDownloadUrl = $null
$androidDownloadUrl = $null
if (-not [string]::IsNullOrWhiteSpace($ResolvedSettingsPath)) {
    $settingsPath = (Resolve-Path -LiteralPath $ResolvedSettingsPath -ErrorAction Stop).Path
    $settings = [IO.File]::ReadAllText($settingsPath, [Text.Encoding]::UTF8) |
        ConvertFrom-Json
    $windows = $settings.Security.NativeClient.Windows
    $android = $settings.Security.NativeClient.Android
    if ($windows.LatestVersion -ne $ExpectedVersion -or
        $android.LatestVersion -ne $ExpectedVersion) {
        throw "Resolved bootstrap latest versions do not match '$ExpectedVersion'."
    }
    if ((Normalize-Hex -Value ([string]$android.Sha256)) -ne $apkHash) {
        throw "Resolved Android bootstrap SHA-256 does not match the APK."
    }
    $windowsDownloadUrl = [string]$windows.DownloadUrl
    $androidDownloadUrl = [string]$android.DownloadUrl
    Assert-HttpsUri -Value $windowsDownloadUrl `
        -Description "Resolved Windows download URL"
    Assert-HttpsUri -Value ([string]$android.DownloadUrl) `
        -Description "Resolved Android download URL" `
        -RequiredExtension ".apk"
    $windowsDownloadExtension = [IO.Path]::GetExtension(
        ([Uri]$windowsDownloadUrl).AbsolutePath
    ).ToLowerInvariant()
    $expectedWindowsHash = switch ($windowsDownloadExtension) {
        ".msix" { $msixHash }
        ".appinstaller" { $appInstallerHash }
        default {
            throw "Resolved Windows download URL must end in .msix or .appinstaller."
        }
    }
    if ((Normalize-Hex -Value ([string]$windows.Sha256)) -ne $expectedWindowsHash) {
        throw "Resolved Windows bootstrap SHA-256 does not match its download artifact."
    }

    $jwtSecret = [string]$settings.JWT.Secret
    if ($jwtSecret.Length -lt 32 -or $jwtSecret -match "SET_VIA_ENV|CHANGE_ME|PLACEHOLDER") {
        throw "Resolved JWT secret is missing or still a placeholder."
    }
    $rabbitPassword = [string]$settings.RabbitMQ.Password
    if ([string]::IsNullOrWhiteSpace($rabbitPassword) -or
        $rabbitPassword -match "SET_VIA_ENV|CHANGE_ME|PLACEHOLDER") {
        throw "Resolved RabbitMQ password is missing or still a placeholder."
    }
    $databaseConnection = [string]$settings.ConnectionStrings.DefaultConnection
    if ([string]::IsNullOrWhiteSpace($databaseConnection) -or
        $databaseConnection -match "(?i)(Server|Data Source)\s*=\s*(localhost|127\.0\.0\.1)" -or
        $databaseConnection -match "(?i)TrustServerCertificate\s*=\s*True") {
        throw "Resolved production SQL Server connection must use a non-local host and validated TLS."
    }
    if ([string]::IsNullOrWhiteSpace([string]$settings.ConnectionStrings.Redis)) {
        throw "Resolved production settings must configure distributed Redis cache."
    }
    foreach ($origin in @($settings.Cors.AllowedOrigins)) {
        Assert-HttpsUri -Value ([string]$origin) -Description "Resolved CORS origin"
    }
}

if ([string]::IsNullOrWhiteSpace($LatestMigration)) {
    throw "LatestMigration is required."
}
Assert-EvidenceRootUri -Value $EvidenceRootUri
$sbomEvidence = Get-EvidenceFile -Path $SbomPath -Description "SBOM"
$vulnerabilityEvidence = Get-EvidenceFile `
    -Path $VulnerabilityReportPath `
    -Description "Vulnerability report"
$databaseInitializationEvidence = Get-EvidenceFile `
    -Path $DatabaseInitializationArtifactPath `
    -Description "Database initialization artifact"
$sourceGateEvidence = Get-EvidenceFile `
    -Path $SourceGateReportPath `
    -Description "Source gate report"
$sqlIntegrationEvidence = Get-EvidenceFile `
    -Path $SqlIntegrationReportPath `
    -Description "SQL Server integration report"

$releaseManifest = [ordered]@{
    SchemaVersion = 2
    ReleaseVersion = $ExpectedVersion
    GitSha = $GitSha.ToLowerInvariant()
    GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    EvidenceRootUri = $EvidenceRootUri.TrimEnd("/")
    Artifacts = @(
        [ordered]@{
            Kind = "windows-msix"
            FileName = $msix.Name
            Bytes = $msix.Length
            Sha256 = $msixHash
            DownloadUrl = [string]$mainPackage.Uri
            Signer = [ordered]@{
                Type = "Authenticode"
                Identity = $ExpectedWindowsPublisher
                CertificateThumbprint = $authenticode.SignerCertificate.Thumbprint
            }
        },
        [ordered]@{
            Kind = "windows-appinstaller"
            FileName = $appInstaller.Name
            Bytes = $appInstaller.Length
            Sha256 = $appInstallerHash
            DownloadUrl = [string]$appInstallerRoot.Uri
            Signer = [ordered]@{
                Type = "MSIXReference"
                Identity = $ExpectedWindowsPublisher
                CertificateThumbprint = $authenticode.SignerCertificate.Thumbprint
            }
        },
        [ordered]@{
            Kind = "android-apk"
            FileName = $apk.Name
            Bytes = $apk.Length
            Sha256 = $apkHash
            DownloadUrl = $androidDownloadUrl
            Signer = [ordered]@{
                Type = "APK"
                Identity = $androidSignerDn
                CertificateSha256 = $androidSignerDigest
            }
        }
    )
    Images = [ordered]@{
        Api = [ordered]@{
            Repository = $ApiImageRepository
            Digest = $ApiImageDigest.ToLowerInvariant()
        }
        Web = [ordered]@{
            Repository = $WebImageRepository
            Digest = $WebImageDigest.ToLowerInvariant()
        }
    }
    SupplyChain = [ordered]@{
        Sbom = $sbomEvidence
        VulnerabilityReport = $vulnerabilityEvidence
        SourceGateReport = $sourceGateEvidence
        SqlIntegrationReport = $sqlIntegrationEvidence
    }
    Database = [ordered]@{
        LatestMigration = $LatestMigration
        InitializationArtifact = $databaseInitializationEvidence
        MigrationPolicy = "ForwardOnly"
    }
}

$manifestJson = $releaseManifest | ConvertTo-Json -Depth 10
$outputParent = Split-Path -Parent $OutputManifestPath
if (-not [string]::IsNullOrWhiteSpace($outputParent)) {
    [IO.Directory]::CreateDirectory($outputParent) | Out-Null
}
[IO.File]::WriteAllText(
    $OutputManifestPath,
    $manifestJson + [Environment]::NewLine,
    [Text.UTF8Encoding]::new($false)
)

Write-Host "R2 artifact gate passed. Release manifest: $OutputManifestPath"
