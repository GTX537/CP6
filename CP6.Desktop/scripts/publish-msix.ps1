param(
    [Parameter(Mandatory = $true)][string]$CertificateThumbprint,
    [Parameter(Mandatory = $true)][string]$OutputDirectory,
    [Parameter(Mandatory = $true)][uri]$PackageUri,
    [Parameter(Mandatory = $true)][uri]$AppInstallerUri,
    [ValidatePattern("^\d+\.\d+\.\d+\.\d+$")]
    [string]$PackageVersion = "1.0.0.0",
    [uri]$TimestampServerUrl,
    [string]$AssetsDirectory
)

$ErrorActionPreference = "Stop"

function Invoke-NativeCommand {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$ArgumentList,
        [Parameter(Mandatory = $true)][string]$Description
    )

    & $FilePath @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
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
        throw "$ToolName was not found. Install the Windows 10/11 SDK packaging tools."
    }

    return $resolved.Path
}

function Assert-HttpsUri {
    param(
        [Parameter(Mandatory = $true)][uri]$Uri,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$RequiredExtension
    )

    if (-not $Uri.IsAbsoluteUri -or $Uri.Scheme -ne [uri]::UriSchemeHttps) {
        throw "$Name must be an absolute HTTPS URI."
    }
    if (-not $Uri.AbsolutePath.EndsWith($RequiredExtension, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Name must end with '$RequiredExtension'."
    }
}

function Save-XmlDocument {
    param(
        [Parameter(Mandatory = $true)][xml]$Document,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $settings = [System.Xml.XmlWriterSettings]::new()
    $settings.Encoding = [System.Text.UTF8Encoding]::new($false)
    $settings.Indent = $true
    $settings.NewLineChars = [Environment]::NewLine
    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $Document.Save($writer)
    }
    finally {
        $writer.Dispose()
    }
}

Assert-HttpsUri -Uri $PackageUri -Name "PackageUri" -RequiredExtension ".msix"
Assert-HttpsUri -Uri $AppInstallerUri -Name "AppInstallerUri" -RequiredExtension ".appinstaller"
if ($null -ne $TimestampServerUrl -and
    (-not $TimestampServerUrl.IsAbsoluteUri -or $TimestampServerUrl.Scheme -ne [uri]::UriSchemeHttps)) {
    throw "TimestampServerUrl must be an absolute HTTPS URI."
}

$versionParts = $PackageVersion.Split(".")
if ($versionParts | Where-Object { [int64]$_ -gt 65535 }) {
    throw "Every PackageVersion component must be between 0 and 65535."
}

$normalizedThumbprint = ($CertificateThumbprint -replace "\s", "").ToUpperInvariant()
$certificateLocations = @("Cert:\CurrentUser\My", "Cert:\LocalMachine\My")
$certificate = foreach ($location in $certificateLocations) {
    Get-ChildItem -LiteralPath $location |
        Where-Object { $_.Thumbprint -eq $normalizedThumbprint } |
        Select-Object -First 1
}
$certificate = $certificate | Select-Object -First 1
if ($null -eq $certificate) {
    throw "The signing certificate was not found in CurrentUser\My or LocalMachine\My."
}
if (-not $certificate.HasPrivateKey) {
    throw "The signing certificate does not contain an accessible private key."
}
$now = Get-Date
if ($certificate.NotBefore -gt $now -or $certificate.NotAfter -le $now) {
    throw "The signing certificate is not currently valid."
}
$ekuExtension = $certificate.Extensions |
    Where-Object { $_.Oid.Value -eq "2.5.29.37" } |
    Select-Object -First 1
if ($null -ne $ekuExtension) {
    $codeSigningOid = "1.3.6.1.5.5.7.3.3"
    $supportsCodeSigning = ([System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]$ekuExtension).
        EnhancedKeyUsages |
        Where-Object { $_.Value -eq $codeSigningOid }
    if (-not $supportsCodeSigning) {
        throw "The signing certificate is not valid for code signing."
    }
}

$project = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $project "bin\Release\net8.0-windows10.0.19041.0\win-x64\publish"
$projectFile = Join-Path $project "CP6.Desktop.csproj"
Invoke-NativeCommand -FilePath "dotnet" `
    -ArgumentList @("restore", $projectFile, "-r", "win-x64") `
    -Description "Desktop runtime restore"
Invoke-NativeCommand -FilePath "dotnet" `
    -ArgumentList @(
        "publish", $projectFile, "-c", "Release", "-r", "win-x64",
        "--self-contained", "false", "--no-restore"
    ) `
    -Description "Desktop Release publish"

$manifestPath = Join-Path $publish "AppxManifest.xml"
[xml]$manifest = Get-Content -Raw -LiteralPath (Join-Path $project "Package.appxmanifest")
$identity = $manifest.Package.Identity
$identity.SetAttribute("Publisher", $certificate.Subject)
$identity.SetAttribute("Version", $PackageVersion)
$identity.SetAttribute("ProcessorArchitecture", "x64")
$publisherDisplayName = $certificate.GetNameInfo(
    [System.Security.Cryptography.X509Certificates.X509NameType]::SimpleName,
    $false
)
if (-not [string]::IsNullOrWhiteSpace($publisherDisplayName)) {
    $manifest.Package.Properties.PublisherDisplayName = $publisherDisplayName
}
Save-XmlDocument -Document $manifest -Path $manifestPath

$publishedTemplateManifest = Join-Path $publish "Package.appxmanifest"
if (Test-Path -LiteralPath $publishedTemplateManifest -PathType Leaf) {
    Remove-Item -LiteralPath $publishedTemplateManifest -Force
}

# Transparent 1x1 PNG placeholders keep the template packageable. Replace them
# with approved brand assets before production signing.
$assets = Join-Path $publish "Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$requiredAssets = @("StoreLogo.png", "Square150x150Logo.png", "Square44x44Logo.png")
if (-not [string]::IsNullOrWhiteSpace($AssetsDirectory)) {
    $resolvedAssetsDirectory = (Resolve-Path -LiteralPath $AssetsDirectory -ErrorAction Stop).Path
    foreach ($name in $requiredAssets) {
        $sourceAsset = Join-Path $resolvedAssetsDirectory $name
        if (-not (Test-Path -LiteralPath $sourceAsset -PathType Leaf)) {
            throw "Required package asset was not found: $sourceAsset"
        }
        Copy-Item -LiteralPath $sourceAsset -Destination (Join-Path $assets $name) -Force
    }
}
else {
    Write-Warning "Using placeholder package images. Supply -AssetsDirectory for a production release."
    $png = [Convert]::FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScL1WQAAAABJRU5ErkJggg=="
    )
    foreach ($name in $requiredAssets) {
        [IO.File]::WriteAllBytes((Join-Path $assets $name), $png)
    }
}

$makeAppx = Resolve-WindowsSdkTool -ToolName "MakeAppx.exe"
$signTool = Resolve-WindowsSdkTool -ToolName "SignTool.exe"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$resolvedOutputDirectory = (Resolve-Path -LiteralPath $OutputDirectory).Path
$msix = Join-Path $resolvedOutputDirectory "CP6.Desktop.msix"
Invoke-NativeCommand -FilePath $makeAppx `
    -ArgumentList @("pack", "/d", $publish, "/p", $msix, "/o") `
    -Description "MSIX packaging"

$signArguments = @("sign", "/sha1", $normalizedThumbprint, "/fd", "SHA256")
if ($certificate.PSParentPath -like "*LocalMachine*") {
    $signArguments += "/sm"
}
if ($null -ne $TimestampServerUrl) {
    $signArguments += @("/tr", $TimestampServerUrl.AbsoluteUri, "/td", "SHA256")
}
$signArguments += $msix
Invoke-NativeCommand -FilePath $signTool `
    -ArgumentList $signArguments `
    -Description "MSIX signing"
Invoke-NativeCommand -FilePath $signTool `
    -ArgumentList @("verify", "/pa", "/v", $msix) `
    -Description "MSIX signature verification"

$appInstallerPath = Join-Path $resolvedOutputDirectory "CP6.Desktop.appinstaller"
$appInstaller = [xml]::new()
$declaration = $appInstaller.CreateXmlDeclaration("1.0", "utf-8", $null)
[void]$appInstaller.AppendChild($declaration)
$namespace = "http://schemas.microsoft.com/appx/appinstaller/2018"
$root = $appInstaller.CreateElement("AppInstaller", $namespace)
$root.SetAttribute("Uri", $AppInstallerUri.AbsoluteUri)
$root.SetAttribute("Version", $PackageVersion)
[void]$appInstaller.AppendChild($root)
$mainPackage = $appInstaller.CreateElement("MainPackage", $namespace)
$mainPackage.SetAttribute("Name", "CP6.Desktop")
$mainPackage.SetAttribute("Publisher", $certificate.Subject)
$mainPackage.SetAttribute("Version", $PackageVersion)
$mainPackage.SetAttribute("Uri", $PackageUri.AbsoluteUri)
$mainPackage.SetAttribute("ProcessorArchitecture", "x64")
[void]$root.AppendChild($mainPackage)
$updateSettings = $appInstaller.CreateElement("UpdateSettings", $namespace)
$onLaunch = $appInstaller.CreateElement("OnLaunch", $namespace)
$onLaunch.SetAttribute("HoursBetweenUpdateChecks", "0")
$onLaunch.SetAttribute("ShowPrompt", "true")
$onLaunch.SetAttribute("UpdateBlocksActivation", "true")
[void]$updateSettings.AppendChild($onLaunch)
[void]$root.AppendChild($updateSettings)
Save-XmlDocument -Document $appInstaller -Path $appInstallerPath

Get-FileHash -Algorithm SHA256 $msix, $appInstallerPath
