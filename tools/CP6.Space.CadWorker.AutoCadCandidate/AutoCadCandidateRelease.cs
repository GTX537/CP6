using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Space.Application;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadWorker.AutoCadCandidate;

public sealed record AutoCadCandidateReleaseFileV1(
    string Path,
    long Length,
    string Sha256);

public sealed record AutoCadCandidateReleaseManifestV1(
    int SchemaVersion,
    string ProviderKey,
    string ReleaseVersion,
    string SourceCommit,
    string RuntimeIdentifier,
    string AutoCadCoreConsoleVersion,
    string AutoCadCoreConsoleSha256,
    string ManagedDxfConverterVersion,
    AutoCadCandidateReleaseFileV1[] Files);

public sealed class AutoCadCandidateReleaseIdentity
{
    public const int SchemaVersion = 1;
    public const string ProviderKey = "cp6-autocad-worker";
    public const string ManifestFileName = "cp6-space-cad-worker-release.json";
    private const int MaximumManifestBytes = 2 * 1024 * 1024;

    private static readonly Regex ReleaseVersionPattern = new(
        @"^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z]+(?:[.-][0-9A-Za-z]+)*)?$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex SourceCommitPattern = new(
        "^[a-f0-9]{40}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex RuntimeIdentifierPattern = new(
        "^[a-z0-9][a-z0-9.-]{0,63}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex ProviderComponentPattern = new(
        "^[0-9A-Za-z][0-9A-Za-z.-]{0,63}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
    };

    private AutoCadCandidateReleaseIdentity(
        AutoCadCandidateReleaseManifestV1 manifest,
        string workerReleaseSha256,
        string providerVersion)
    {
        Manifest = manifest;
        WorkerReleaseSha256 = workerReleaseSha256;
        ProviderVersion = providerVersion;
    }

    public AutoCadCandidateReleaseManifestV1 Manifest { get; }
    public string WorkerReleaseSha256 { get; }
    public string ProviderVersion { get; }

    public static async Task<AutoCadCandidateReleaseIdentity> CreateAsync(
        string payloadRoot,
        string releaseVersion,
        string sourceCommit,
        string runtimeIdentifier,
        string coreConsolePath,
        string autoCadCoreConsoleVersion,
        CancellationToken cancellationToken = default)
    {
        var root = RequireDirectory(payloadRoot, "Worker payload root");
        var manifestPath = Path.Combine(root, ManifestFileName);
        if (File.Exists(manifestPath))
        {
            throw new IOException(
                $"The release Manifest already exists: {manifestPath}");
        }

        var files = new List<AutoCadCandidateReleaseFileV1>();
        foreach (var file in EnumeratePayloadFiles(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = RelativePayloadPath(root, file.FullName);
            files.Add(new AutoCadCandidateReleaseFileV1(
                relativePath,
                file.Length,
                await ComputeSha256Async(file.FullName, cancellationToken)));
        }
        files.Sort((left, right) =>
            StringComparer.Ordinal.Compare(left.Path, right.Path));

        var corePath = RequireFile(coreConsolePath, "AutoCAD Core Console");
        var manifest = new AutoCadCandidateReleaseManifestV1(
            SchemaVersion,
            ProviderKey,
            releaseVersion,
            sourceCommit,
            runtimeIdentifier,
            autoCadCoreConsoleVersion,
            await ComputeSha256Async(corePath, cancellationToken),
            DevelopmentDxfCadConverter.ConverterVersion,
            files.ToArray());
        ValidateManifestShape(
            manifest,
            autoCadCoreConsoleVersion,
            runtimeIdentifier);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);
        await using (var output = new FileStream(
                         manifestPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         64 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await output.WriteAsync(bytes, cancellationToken);
            await output.FlushAsync(cancellationToken);
        }
        var manifestSha256 = Sha256(bytes);
        return await LoadVerifiedAsync(
            manifestPath,
            manifestSha256,
            root,
            corePath,
            autoCadCoreConsoleVersion,
            runtimeIdentifier,
            cancellationToken);
    }

    public static async Task<AutoCadCandidateReleaseIdentity> LoadVerifiedAsync(
        string manifestPath,
        string expectedManifestSha256,
        string payloadRoot,
        string coreConsolePath,
        string autoCadCoreConsoleVersion,
        string runtimeIdentifier,
        CancellationToken cancellationToken = default)
    {
        var manifestFullPath = RequireFile(manifestPath, "Worker release Manifest");
        var root = RequireDirectory(payloadRoot, "Worker payload root");
        var expectedManifestPath = Path.GetFullPath(Path.Combine(root, ManifestFileName));
        if (!manifestFullPath.Equals(
                expectedManifestPath,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Worker release Manifest must be in the payload root.");
        }
        if (new FileInfo(manifestFullPath).Length > MaximumManifestBytes)
            throw new InvalidDataException("The Worker release Manifest is too large.");
        var expectedHash = NormalizeSha256(
            expectedManifestSha256,
            "Worker release Manifest SHA-256");
        var manifestBytes = await File.ReadAllBytesAsync(
            manifestFullPath,
            cancellationToken);
        var actualManifestHash = Sha256(manifestBytes);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualManifestHash),
                Convert.FromHexString(expectedHash)))
        {
            throw new InvalidDataException(
                "The Worker release Manifest hash is invalid.");
        }

        AutoCadCandidateReleaseManifestV1 manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<AutoCadCandidateReleaseManifestV1>(
                           manifestBytes,
                           JsonOptions)
                       ?? throw new InvalidDataException(
                           "The Worker release Manifest is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                "The Worker release Manifest is invalid JSON.",
                exception);
        }
        ValidateManifestShape(
            manifest,
            autoCadCoreConsoleVersion,
            runtimeIdentifier);

        var corePath = RequireFile(coreConsolePath, "AutoCAD Core Console");
        var coreHash = await ComputeSha256Async(corePath, cancellationToken);
        if (!FixedTimeSha256Equals(coreHash, manifest.AutoCadCoreConsoleSha256))
        {
            throw new InvalidDataException(
                "The AutoCAD Core Console hash does not match the frozen release.");
        }

        var actualFiles = EnumeratePayloadFiles(root)
            .ToDictionary(
                file => RelativePayloadPath(root, file.FullName),
                StringComparer.Ordinal);
        if (actualFiles.Count != manifest.Files.Length)
        {
            throw new InvalidDataException(
                "The Worker payload file set does not match the release Manifest.");
        }
        foreach (var expectedFile in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!actualFiles.TryGetValue(expectedFile.Path, out var actualFile))
            {
                throw new InvalidDataException(
                    $"The Worker release file is missing: {expectedFile.Path}");
            }
            if (actualFile.Length != expectedFile.Length)
            {
                throw new InvalidDataException(
                    $"The Worker release file length changed: {expectedFile.Path}");
            }
            var actualFileHash = await ComputeSha256Async(
                actualFile.FullName,
                cancellationToken);
            if (!FixedTimeSha256Equals(actualFileHash, expectedFile.Sha256))
            {
                throw new InvalidDataException(
                    $"The Worker release file hash changed: {expectedFile.Path}");
            }
        }

        var providerVersion = BuildProviderVersion(
            manifest.ReleaseVersion,
            actualManifestHash,
            autoCadCoreConsoleVersion,
            manifest.ManagedDxfConverterVersion);
        return new AutoCadCandidateReleaseIdentity(
            manifest,
            actualManifestHash,
            providerVersion);
    }

    public static string ReadValidatedAutoCadProviderVersion(string coreConsolePath)
    {
        var path = RequireFile(coreConsolePath, "AutoCAD Core Console");
        if (!Path.GetFileName(path).Equals(
                "accoreconsole.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The AutoCAD Core Console file name is invalid.");
        }
        var versionInfo = FileVersionInfo.GetVersionInfo(path);
        if (!string.Equals(
                versionInfo.CompanyName,
                "Autodesk, Inc.",
                StringComparison.Ordinal)
            || !string.Equals(
                versionInfo.ProductName,
                "AcCoreConsole",
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The configured executable does not identify as Autodesk AcCoreConsole.");
        }
        return RequireProviderComponent(
            versionInfo.FileVersion,
            "AutoCAD Core Console version");
    }

    public static string CurrentRuntimeIdentifier() =>
        RequireRuntimeIdentifier(RuntimeInformation.RuntimeIdentifier);

    private static void ValidateManifestShape(
        AutoCadCandidateReleaseManifestV1 manifest,
        string expectedAutoCadVersion,
        string expectedRuntimeIdentifier)
    {
        if (manifest.SchemaVersion != SchemaVersion)
            throw new InvalidDataException("The Worker release schema is unsupported.");
        if (!manifest.ProviderKey.Equals(ProviderKey, StringComparison.Ordinal))
            throw new InvalidDataException("The Worker release Provider key is invalid.");
        if (!ReleaseVersionPattern.IsMatch(manifest.ReleaseVersion))
            throw new InvalidDataException("The Worker release version is not valid SemVer.");
        if (!SourceCommitPattern.IsMatch(manifest.SourceCommit))
            throw new InvalidDataException("The Worker release source commit is invalid.");
        var runtime = RequireRuntimeIdentifier(manifest.RuntimeIdentifier);
        if (!runtime.Equals(
                RequireRuntimeIdentifier(expectedRuntimeIdentifier),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The Worker runtime does not match the frozen release.");
        }
        var autoCadVersion = RequireProviderComponent(
            manifest.AutoCadCoreConsoleVersion,
            "AutoCAD Core Console version");
        if (!autoCadVersion.Equals(
                RequireProviderComponent(
                    expectedAutoCadVersion,
                    "Expected AutoCAD Core Console version"),
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The AutoCAD Core Console version does not match the frozen release.");
        }
        _ = NormalizeSha256(
            manifest.AutoCadCoreConsoleSha256,
            "AutoCAD Core Console SHA-256");
        if (!manifest.ManagedDxfConverterVersion.Equals(
                DevelopmentDxfCadConverter.ConverterVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The managed DXF converter version does not match the Worker release.");
        }
        if (manifest.Files is null || manifest.Files.Length == 0)
            throw new InvalidDataException("The Worker release contains no payload files.");
        if (manifest.Files.Length > 5000)
            throw new InvalidDataException("The Worker release contains too many payload files.");

        string? previousPath = null;
        foreach (var file in manifest.Files)
        {
            var path = RequireRelativePayloadPath(file.Path);
            if (previousPath is not null &&
                StringComparer.Ordinal.Compare(previousPath, path) >= 0)
            {
                throw new InvalidDataException(
                    "Worker release files must be unique and ordinally sorted.");
            }
            if (file.Length < 0)
                throw new InvalidDataException("A Worker release file length is invalid.");
            _ = NormalizeSha256(file.Sha256, $"Worker release file {path} SHA-256");
            previousPath = path;
        }
        if (!manifest.Files.Any(file => file.Path.Equals(
                "CP6.Space.CadWorker.AutoCadCandidate.dll",
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "The Worker release entry assembly is not in the payload.");
        }
    }

    private static string BuildProviderVersion(
        string releaseVersion,
        string manifestSha256,
        string autoCadVersion,
        string dxfVersion)
    {
        var version = releaseVersion
                      + "+worker."
                      + manifestSha256[..12]
                      + ".autocad."
                      + RequireProviderComponent(autoCadVersion, "AutoCAD version")
                      + ".dxf."
                      + RequireProviderComponent(dxfVersion, "DXF converter version");
        if (version.Length > SpaceCadConversionContract.MaximumIdentifierLength)
            throw new InvalidDataException("The Worker Provider version is too long.");
        return version;
    }

    private static IReadOnlyList<FileInfo> EnumeratePayloadFiles(string root)
    {
        var result = new List<FileInfo>();
        var directories = new Stack<DirectoryInfo>();
        directories.Push(new DirectoryInfo(root));
        while (directories.TryPop(out var directory))
        {
            directory.Refresh();
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Worker payload directories cannot be reparse points.");
            foreach (var entry in directory.EnumerateFileSystemInfos())
            {
                entry.Refresh();
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Worker payload entries cannot be reparse points.");
                if (entry is DirectoryInfo child)
                {
                    directories.Push(child);
                    continue;
                }
                if (entry is not FileInfo file)
                    throw new InvalidDataException("The Worker payload contains an unknown entry.");
                var relativePath = Path.GetRelativePath(root, file.FullName)
                    .Replace('\\', '/');
                if (relativePath.Equals(ManifestFileName, StringComparison.Ordinal))
                    continue;
                _ = RequireRelativePayloadPath(relativePath);
                result.Add(file);
            }
        }
        return result;
    }

    private static string RelativePayloadPath(string root, string filePath) =>
        RequireRelativePayloadPath(
            Path.GetRelativePath(root, filePath).Replace('\\', '/'));

    private static string RequireRelativePayloadPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 500
            || value.Contains('\\')
            || value.StartsWith('/')
            || Path.IsPathRooted(value))
        {
            throw new InvalidDataException("A Worker release file path is invalid.");
        }
        var segments = value.Split('/');
        if (segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment)
                || segment is "." or ".."
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
        {
            throw new InvalidDataException("A Worker release file path is invalid.");
        }
        if (value.Equals(ManifestFileName, StringComparison.Ordinal))
            throw new InvalidDataException("The release Manifest cannot list itself.");
        return value;
    }

    private static string RequireRuntimeIdentifier(string value)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !RuntimeIdentifierPattern.IsMatch(normalized))
        {
            throw new InvalidDataException("The Worker runtime identifier is invalid.");
        }
        return normalized;
    }

    private static string RequireProviderComponent(string? value, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || !ProviderComponentPattern.IsMatch(normalized))
        {
            throw new InvalidDataException($"{label} is invalid.");
        }
        return normalized;
    }

    private static string RequireDirectory(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{label} is required.");
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"{label} does not exist: {fullPath}");
        return fullPath;
    }

    private static string RequireFile(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{label} is required.");
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"{label} does not exist.", fullPath);
        var attributes = File.GetAttributes(fullPath);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label} cannot be a reparse point.");
        return fullPath;
    }

    private static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string NormalizeSha256(string value, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized)
            || normalized.Length != 64
            || normalized.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException($"{label} is invalid.");
        }
        return normalized;
    }

    private static bool FixedTimeSha256Equals(string actual, string expected) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(NormalizeSha256(expected, "Expected SHA-256")));

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
