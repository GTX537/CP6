using System.Security.Cryptography;
using CP6.Space.Application;
using CP6.Space.CadExperiment;
using CP6.Space.Contracts;

namespace CP6.Space.CadWorker.AutoCadCandidate;

public sealed class AutoCadCandidateConversionService
{
    public const int MaximumWorkRootPathLength = 120;

    private readonly IAutoCadDwgExporter _exporter;
    private readonly string _attemptRoot;
    private readonly TimeSpan _waitLimit;
    private readonly SemaphoreSlim _capacity;
    private readonly string _providerKey;
    private readonly string _providerVersion;
    private readonly string _workerReleaseSha256;

    public AutoCadCandidateConversionService(
        IAutoCadDwgExporter exporter,
        string workRoot,
        TimeSpan conversionTimeout,
        int maximumConcurrency) :
        this(
            exporter ?? throw new ArgumentNullException(nameof(exporter)),
            workRoot,
            conversionTimeout,
            maximumConcurrency,
            AutoCadCandidateConverter.DevelopmentConverterId,
            AutoCadCandidateConverter.VersionFor(exporter.ProviderVersion),
            DevelopmentWorkerReleaseSha256(exporter.ProviderVersion))
    {
    }

    public AutoCadCandidateConversionService(
        IAutoCadDwgExporter exporter,
        string workRoot,
        TimeSpan conversionTimeout,
        int maximumConcurrency,
        AutoCadCandidateReleaseIdentity releaseIdentity) :
        this(
            exporter,
            workRoot,
            conversionTimeout,
            maximumConcurrency,
            AutoCadCandidateReleaseIdentity.ProviderKey,
            RequireReleaseProviderVersion(exporter, releaseIdentity),
            releaseIdentity.WorkerReleaseSha256)
    {
    }

    private AutoCadCandidateConversionService(
        IAutoCadDwgExporter exporter,
        string workRoot,
        TimeSpan conversionTimeout,
        int maximumConcurrency,
        string providerKey,
        string providerVersion,
        string workerReleaseSha256)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        if (maximumConcurrency is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        var root = Path.GetFullPath(workRoot);
        if (root.Length > MaximumWorkRootPathLength)
        {
            throw new ArgumentException(
                $"The CAD Worker root must not exceed {MaximumWorkRootPathLength} characters.",
                nameof(workRoot));
        }
        _attemptRoot = Path.Combine(root, "attempts");
        Directory.CreateDirectory(_attemptRoot);
        _waitLimit = conversionTimeout + TimeSpan.FromSeconds(30);
        _capacity = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
        if (string.IsNullOrWhiteSpace(providerKey)
            || providerKey.Length > SpaceCadConversionContract.MaximumIdentifierLength)
        {
            throw new ArgumentException("A bounded Worker Provider key is required.");
        }
        if (string.IsNullOrWhiteSpace(providerVersion)
            || providerVersion.Length > SpaceCadConversionContract.MaximumIdentifierLength)
        {
            throw new ArgumentException("A bounded Worker Provider version is required.");
        }
        _providerKey = providerKey;
        _providerVersion = providerVersion;
        if (workerReleaseSha256.Length != 64 ||
            workerReleaseSha256.Any(character =>
                character is not (>= '0' and <= '9')
                    and not (>= 'a' and <= 'f')))
        {
            throw new ArgumentException("A valid Worker Release SHA-256 is required.");
        }
        _workerReleaseSha256 = workerReleaseSha256;
    }

    public string ProviderKey => _providerKey;
    public string ProviderVersion => _providerVersion;
    public string WorkerReleaseSha256 => _workerReleaseSha256;

    public async Task<SpaceCadWorkerConversionResponseV2> ConvertAsync(
        SpaceCadWorkerConversionRequestV2 request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        SpaceCadWorkerProtocol.ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The CAD source stream must be readable.", nameof(source));
        if (request.ProviderKey != ProviderKey ||
            request.ProviderVersion != ProviderVersion ||
            request.WorkerReleaseSha256 != WorkerReleaseSha256)
        {
            throw new InvalidDataException(
                "The AutoCAD candidate Worker request does not match its frozen Provider.");
        }

        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        wait.CancelAfter(_waitLimit);
        try
        {
            await _capacity.WaitAsync(wait.Token);
        }
        catch (OperationCanceledException exception)
            when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "The AutoCAD candidate Worker capacity wait timed out.",
                exception);
        }
        try
        {
            return await ConvertInAttemptAsync(request, source, cancellationToken);
        }
        finally
        {
            _capacity.Release();
        }
    }

    private async Task<SpaceCadWorkerConversionResponseV2> ConvertInAttemptAsync(
        SpaceCadWorkerConversionRequestV2 request,
        Stream source,
        CancellationToken cancellationToken)
    {
        var attempt = Path.Combine(_attemptRoot, request.AttemptId.ToString("N"));
        if (Directory.Exists(attempt))
            throw new InvalidDataException("The CAD Worker attempt identity was already used.");
        Directory.CreateDirectory(attempt);
        try
        {
            var input = Path.Combine(
                attempt,
                request.SourceFormat == SpaceCadSourceFormat.Dwg
                    ? "source.dwg"
                    : "source.dxf");
            await StageAndVerifyAsync(
                source,
                input,
                request.SourceSha256,
                cancellationToken);
            File.SetAttributes(input, File.GetAttributes(input) | FileAttributes.ReadOnly);

            var output = Path.Combine(attempt, "cad-ir.json");
            var conversion = new SpaceCadConversionRequest(
                request.AttemptId,
                request.AttemptId,
                request.AttemptId,
                request.SourceSha256,
                request.SourceFormat,
                request.ProviderKey,
                request.ProviderVersion);
            var converter = new AutoCadCandidateConverter(
                _exporter,
                Path.Combine(attempt, "engine-attempts"),
                ProviderKey,
                ProviderVersion);
            var sink = new DevelopmentCadIrFileSink(conversion, output);
            await using var staged = new FileStream(
                input,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            _ = await SpaceCadConverterContractRunner.ConvertAsync(
                converter,
                conversion,
                staged,
                sink,
                cancellationToken);
            var package = sink.Package ?? throw new InvalidDataException(
                "The AutoCAD candidate Worker did not complete a CAD IR package.");
            SpaceCadConversionContract.ValidatePackage(conversion, package);
            var response = new SpaceCadWorkerConversionResponseV2(
                SpaceCadWorkerProtocolVersions.SchemaVersion,
                request.AttemptId,
                request.SourceSha256,
                request.SourceFormat,
                request.ProviderKey,
                request.ProviderVersion,
                request.WorkerReleaseSha256,
                SpaceCadWorkerProtocol.ComputePackageSha256(package),
                package);
            SpaceCadWorkerProtocol.ValidateResponse(request, response);
            return response;
        }
        finally
        {
            DeleteAttempt(attempt);
        }
    }

    private static async Task StageAndVerifyAsync(
        Stream source,
        string path,
        string expectedSha256,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[64 * 1024];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
                break;
            total = checked(total + read);
            if (total > SpaceCadWorkerProtocolVersions.MaximumSourceBytes)
                throw new InvalidDataException("The CAD source exceeds the Worker limit.");
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        await output.FlushAsync(cancellationToken);
        var actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!actual.Equals(expectedSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The CAD source SHA-256 is invalid.");
    }

    private static void DeleteAttempt(string attempt)
    {
        if (!Directory.Exists(attempt))
            return;
        DeleteDirectoryWithoutFollowingReparsePoints(new DirectoryInfo(attempt));
    }

    private static void DeleteDirectoryWithoutFollowingReparsePoints(
        DirectoryInfo directory)
    {
        foreach (var entry in directory.EnumerateFileSystemInfos())
        {
            entry.Refresh();
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                    Directory.Delete(entry.FullName);
                else
                    File.Delete(entry.FullName);
                continue;
            }
            if (entry is DirectoryInfo child)
            {
                DeleteDirectoryWithoutFollowingReparsePoints(child);
                continue;
            }
            entry.Attributes = FileAttributes.Normal;
            File.Delete(entry.FullName);
        }
        directory.Attributes = FileAttributes.Directory;
        directory.Delete();
    }

    private static string RequireReleaseProviderVersion(
        IAutoCadDwgExporter exporter,
        AutoCadCandidateReleaseIdentity releaseIdentity)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(releaseIdentity);
        if (exporter is not ReleaseBoundAutoCadDwgExporter releaseBound)
        {
            throw new ArgumentException(
                "A release Worker requires a release-bound AutoCAD exporter.",
                nameof(exporter));
        }
        if (!exporter.ProviderVersion.Equals(
                releaseIdentity.Manifest.AutoCadCoreConsoleVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The release-bound exporter version does not match the Worker release.");
        }
        if (!releaseBound.CoreConsoleSha256.Equals(
                releaseIdentity.Manifest.AutoCadCoreConsoleSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The release-bound exporter hash does not match the Worker release.");
        }
        return releaseIdentity.ProviderVersion;
    }

    private static string DevelopmentWorkerReleaseSha256(
        string autoCadProviderVersion)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(
            AutoCadCandidateConverter.DevelopmentConverterId
            + "\n"
            + AutoCadCandidateConverter.VersionFor(autoCadProviderVersion));
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

}
