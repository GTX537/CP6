[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$temporaryRoot = Join-Path `
    ([IO.Path]::GetTempPath()) `
    "cp6-r2-deployment-$([Guid]::NewGuid().ToString('N'))"
$server = $null

function Write-Utf8NoBom {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    [IO.File]::WriteAllText($Path, $Content, [Text.UTF8Encoding]::new($false))
}

if ($null -eq ("Cp6R2ContractServer" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

public sealed class Cp6R2ContractServer : IDisposable
{
    private readonly HttpListener _listener = new HttpListener();
    private readonly string _baseUrl;
    private readonly string _msixPath;
    private readonly string _appInstallerPath;
    private readonly string _apkPath;
    private readonly string _windowsHash;
    private readonly string _androidHash;
    private readonly Task _loop;

    public Cp6R2ContractServer(
        string baseUrl,
        string msixPath,
        string appInstallerPath,
        string apkPath,
        string windowsHash,
        string androidHash)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _msixPath = msixPath;
        _appInstallerPath = appInstallerPath;
        _apkPath = apkPath;
        _windowsHash = windowsHash;
        _androidHash = androidHash;
        _listener.Prefixes.Add(_baseUrl + "/");
        _listener.Start();
        _loop = Task.Run((Action)ListenLoop);
    }

    private void ListenLoop()
    {
        while (_listener.IsListening)
        {
            try
            {
                Handle(_listener.GetContext());
            }
            catch (HttpListenerException)
            {
                if (!_listener.IsListening) return;
                throw;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
        }
    }

    private void Handle(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        string path = request.Url.AbsolutePath;
        byte[] body;

        if (path == "/health/live")
        {
            response.ContentType = "application/json";
            response.Headers["Cache-Control"] = "no-store";
            body = Utf8("{\"status\":\"Healthy\",\"checks\":[{\"name\":\"self\",\"status\":\"Healthy\"}]}");
        }
        else if (path == "/health/ready")
        {
            response.ContentType = "application/json";
            response.Headers["Cache-Control"] = "no-store";
            body = Utf8("{\"status\":\"Healthy\",\"checks\":[{\"name\":\"redis\",\"status\":\"Healthy\"},{\"name\":\"sqlserver\",\"status\":\"Healthy\"}]}");
        }
        else if (path == "/health/release")
        {
            response.ContentType = "application/json";
            response.Headers["Cache-Control"] = "no-store";
            body = Utf8(
                "{\"version\":\"1.0.0\"," +
                "\"gitSha\":\"1111111111111111111111111111111111111111\"," +
                "\"apiImageDigest\":\"sha256:" + new string('a', 64) + "\"," +
                "\"webImageDigest\":\"sha256:" + new string('b', 64) + "\"," +
                "\"latestMigration\":\"ContractMigration\"}");
        }
        else if (path == "/release.json")
        {
            response.ContentType = "application/json";
            response.Headers["Cache-Control"] = "no-store";
            body = Utf8(
                "{\"version\":\"1.0.0\"," +
                "\"gitSha\":\"1111111111111111111111111111111111111111\"," +
                "\"generatedAtUtc\":\"2026-07-28T00:00:00Z\"}");
        }
        else if (path == "/api/client/bootstrap")
        {
            string platform = request.QueryString["platform"] ?? "";
            string currentVersion = request.QueryString["currentVersion"] ?? "";
            bool windows = platform == "windows";
            string downloadUrl = windows
                ? _baseUrl + "/downloads/CP6.Desktop.appinstaller"
                : _baseUrl + "/downloads/CP6.Mobile.apk";
            string hash = windows ? _windowsHash : _androidHash;
            string payload =
                "{\"apiVersion\":\"1\",\"serverUtc\":\"" +
                DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture) +
                "\",\"platform\":\"" + platform +
                "\",\"currentVersion\":\"" + currentVersion +
                "\",\"latestVersion\":\"1.0.0\",\"minimumVersion\":\"1.0.0\"," +
                "\"upgradeRequired\":false,\"downloadUrl\":\"" + downloadUrl +
                "\",\"sha256\":\"" + hash +
                "\",\"languageManifestVersion\":\"contract-test\"}";
            response.ContentType = "application/json";
            body = Utf8(payload);
        }
        else if (path == "/downloads/CP6.Desktop.msix")
        {
            response.ContentType = "application/octet-stream";
            body = File.ReadAllBytes(_msixPath);
        }
        else if (path == "/downloads/CP6.Desktop.appinstaller")
        {
            response.ContentType = "application/appinstaller";
            body = File.ReadAllBytes(_appInstallerPath);
        }
        else if (path == "/downloads/CP6.Mobile.apk")
        {
            response.ContentType = "application/vnd.android.package-archive";
            body = File.ReadAllBytes(_apkPath);
        }
        else
        {
            response.StatusCode = 404;
            body = Utf8("not found");
        }

        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
        response.OutputStream.Close();
    }

    private static byte[] Utf8(string value)
    {
        return Encoding.UTF8.GetBytes(value);
    }

    public void Dispose()
    {
        if (_listener.IsListening) _listener.Stop();
        _listener.Close();
        _loop.Wait(TimeSpan.FromSeconds(5));
    }
}
'@
}

try {
    [IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
    $portProbe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $portProbe.Start()
    try {
        $port = ([Net.IPEndPoint]$portProbe.LocalEndpoint).Port
    }
    finally {
        $portProbe.Stop()
    }
    $baseUrl = "http://127.0.0.1:$port"

    $msixPath = Join-Path $temporaryRoot "CP6.Desktop.msix"
    $appInstallerPath = Join-Path $temporaryRoot "CP6.Desktop.appinstaller"
    $apkPath = Join-Path $temporaryRoot "CP6.Mobile.apk"
    Write-Utf8NoBom -Path $msixPath -Content "contract-test-msix"
    Write-Utf8NoBom -Path $apkPath -Content "contract-test-apk"
    Write-Utf8NoBom -Path $appInstallerPath -Content @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
  xmlns="http://schemas.microsoft.com/appx/appinstaller/2018"
  Uri="$baseUrl/downloads/CP6.Desktop.appinstaller"
  Version="1.0.0.0">
  <MainPackage
    Name="CP6.Desktop"
    Publisher="CN=CP6 Contract Test"
    Version="1.0.0.0"
    ProcessorArchitecture="x64"
    Uri="$baseUrl/downloads/CP6.Desktop.msix" />
</AppInstaller>
"@

    $artifacts = @(
        [ordered]@{
            Kind = "windows-msix"
            FileName = "CP6.Desktop.msix"
            Bytes = (Get-Item -LiteralPath $msixPath).Length
            Sha256 = (Get-FileHash -LiteralPath $msixPath -Algorithm SHA256).Hash
            DownloadUrl = "$baseUrl/downloads/CP6.Desktop.msix"
        },
        [ordered]@{
            Kind = "windows-appinstaller"
            FileName = "CP6.Desktop.appinstaller"
            Bytes = (Get-Item -LiteralPath $appInstallerPath).Length
            Sha256 = (Get-FileHash -LiteralPath $appInstallerPath -Algorithm SHA256).Hash
            DownloadUrl = "$baseUrl/downloads/CP6.Desktop.appinstaller"
        },
        [ordered]@{
            Kind = "android-apk"
            FileName = "CP6.Mobile.apk"
            Bytes = (Get-Item -LiteralPath $apkPath).Length
            Sha256 = (Get-FileHash -LiteralPath $apkPath -Algorithm SHA256).Hash
            DownloadUrl = "$baseUrl/downloads/CP6.Mobile.apk"
        }
    )
    $executionSpecPath = Join-Path $temporaryRoot "candidate.yaml"
    Write-Utf8NoBom -Path $executionSpecPath -Content @"
schemaVersion: 1
release:
  version: 1.0.0
  tag: v1.0.0
"@
    $executionSpecHash = (
        Get-FileHash -LiteralPath $executionSpecPath -Algorithm SHA256
    ).Hash
    $freezeSnapshotPath = Join-Path $temporaryRoot "release-freeze.json"
    $freezeSnapshot = [ordered]@{
        SchemaVersion = 1
        Status = "Approved"
        ReleaseVersion = "1.0.0"
        Tag = "v1.0.0"
        GitSha = "1" * 40
        RepositoryPath = "docs/client/r2/releases/v1.0.0/candidate.yaml"
        SpecSha256 = $executionSpecHash
        ChangeTicket = "CHG-CONTRACT"
        ApprovedAt = "2026-07-28T00:00:00Z"
        ApprovalExpiresAt = "2099-01-01T00:00:00Z"
        EvidenceRootUri = "s3://cp6-contract/r2/1.0.0"
    }
    Write-Utf8NoBom `
        -Path $freezeSnapshotPath `
        -Content (($freezeSnapshot | ConvertTo-Json -Depth 5) + [Environment]::NewLine)
    $freezeSnapshotHash = (
        Get-FileHash -LiteralPath $freezeSnapshotPath -Algorithm SHA256
    ).Hash
    $manifestPath = Join-Path $temporaryRoot "release-manifest.json"
    $manifest = [ordered]@{
        SchemaVersion = 2
        ReleaseVersion = "1.0.0"
        GitSha = "1" * 40
        GeneratedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        EvidenceRootUri = "s3://cp6-contract/r2/1.0.0"
        ExecutionSpec = [ordered]@{
            Version = 1
            RepositoryPath = "docs/client/r2/releases/v1.0.0/candidate.yaml"
            SpecSha256 = $executionSpecHash
            FreezeSnapshotUri = "s3://cp6-contract/r2/1.0.0/release-freeze.json"
            FreezeSnapshotSha256 = $freezeSnapshotHash
            ChangeTicket = "CHG-CONTRACT"
            ApprovedAt = "2026-07-28T00:00:00Z"
        }
        Artifacts = $artifacts
        Images = [ordered]@{
            Api = [ordered]@{
                Repository = "registry.example/cp6-api"
                Digest = "sha256:" + ("a" * 64)
            }
            Web = [ordered]@{
                Repository = "registry.example/cp6-web"
                Digest = "sha256:" + ("b" * 64)
            }
        }
        Database = [ordered]@{
            LatestMigration = "ContractMigration"
        }
    }
    Write-Utf8NoBom `
        -Path $manifestPath `
        -Content (($manifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
    $manifestHash = (
        Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256
    ).Hash
    $candidateResultPath = Join-Path $temporaryRoot "candidate-result.json"
    $candidateResult = [ordered]@{
        SchemaVersion = 1
        ReleaseVersion = "1.0.0"
        Tag = "v1.0.0"
        GitSha = "1" * 40
        GeneratedAtUtc = "2026-07-28T00:00:00Z"
        ManifestUri = "s3://cp6-contract/r2/1.0.0/release-manifest.json"
        ManifestSha256 = $manifestHash
        FreezeSnapshotUri = "s3://cp6-contract/r2/1.0.0/release-freeze.json"
        FreezeSnapshotSha256 = $freezeSnapshotHash
        ExecutionSpecPath = "docs/client/r2/releases/v1.0.0/candidate.yaml"
        ExecutionSpecSha256 = $executionSpecHash
    }
    Write-Utf8NoBom `
        -Path $candidateResultPath `
        -Content (($candidateResult | ConvertTo-Json -Depth 5) + [Environment]::NewLine)

    $server = [Cp6R2ContractServer]::new(
        $baseUrl,
        $msixPath,
        $appInstallerPath,
        $apkPath,
        $artifacts[1].Sha256,
        $artifacts[2].Sha256
    )

    $evidencePath = Join-Path $temporaryRoot "deployment-evidence.json"
    $smokeOutput = & (Join-Path $repoRoot "scripts\test-r2-deployment.ps1") `
        -BaseUrl $baseUrl `
        -ReleaseManifestPath $manifestPath `
        -ExecutionSpecPath $executionSpecPath `
        -FreezeSnapshotPath $freezeSnapshotPath `
        -CandidateResultPath $candidateResultPath `
        -AllowLoopbackHttp `
        -OutputEvidencePath $evidencePath
    if (-not (Test-Path -LiteralPath $evidencePath -PathType Leaf)) {
        throw "Deployment smoke test did not write evidence."
    }
    $evidence = [IO.File]::ReadAllText($evidencePath, [Text.Encoding]::UTF8) |
        ConvertFrom-Json
    if ($evidence.LiveStatus -ne "Healthy" -or
        $evidence.ReadyStatus -ne "Healthy" -or
        $evidence.WebReleaseIdentity.version -ne "1.0.0" -or
        $evidence.ExecutionSpecSha256 -ne $executionSpecHash -or
        $evidence.FreezeSnapshotSha256 -ne $freezeSnapshotHash -or
        -not [bool]$evidence.ArtifactDownloadsVerified -or
        @($evidence.Clients).Count -ne 2) {
        throw "Deployment smoke evidence is incomplete."
    }
    if (($smokeOutput | Out-String) -notmatch '"ReleaseVersion":\s*"1\.0\.0"') {
        throw "Deployment smoke output is missing its release evidence."
    }

    $badManifestPath = Join-Path $temporaryRoot "release-manifest-bad-hash.json"
    $badManifest = ($manifest | ConvertTo-Json -Depth 8) | ConvertFrom-Json
    ($badManifest.Artifacts |
        Where-Object { $_.Kind -eq "windows-appinstaller" }).Sha256 = "0" * 64
    Write-Utf8NoBom `
        -Path $badManifestPath `
        -Content (($badManifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine)
    $badHashRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\test-r2-deployment.ps1") `
            -BaseUrl $baseUrl `
            -ReleaseManifestPath $badManifestPath `
            -ExecutionSpecPath $executionSpecPath `
            -FreezeSnapshotPath $freezeSnapshotPath `
            -CandidateResultPath $candidateResultPath `
            -AllowLoopbackHttp `
            -SkipArtifactDownload | Out-Null
    }
    catch {
        $badHashRejected = $_.Exception.Message -match "SHA-256"
    }
    if (-not $badHashRejected) {
        throw "Deployment smoke test accepted mismatched bootstrap release metadata."
    }

    $nonLoopbackRejected = $false
    try {
        & (Join-Path $repoRoot "scripts\test-r2-deployment.ps1") `
            -BaseUrl "http://example.com" `
            -ReleaseManifestPath $manifestPath `
            -ExecutionSpecPath $executionSpecPath `
            -FreezeSnapshotPath $freezeSnapshotPath `
            -CandidateResultPath $candidateResultPath `
            -AllowLoopbackHttp `
            -SkipArtifactDownload | Out-Null
    }
    catch {
        $nonLoopbackRejected = $_.Exception.Message -match "must use HTTPS"
    }
    if (-not $nonLoopbackRejected) {
        throw "Deployment smoke test accepted non-loopback HTTP."
    }

    Write-Host "R2 deployment smoke contract test passed."
}
finally {
    if ($null -ne $server) {
        $server.Dispose()
    }
    $resolvedTemporaryRoot = [IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTemporaryRoot.StartsWith(
        $resolvedSystemTemp,
        [StringComparison]::OrdinalIgnoreCase
    ) -and
        (Test-Path -LiteralPath $resolvedTemporaryRoot)) {
        Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
    }
}
