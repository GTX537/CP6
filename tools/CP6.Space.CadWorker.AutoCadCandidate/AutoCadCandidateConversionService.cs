using System.Security.Cryptography;
using CP6.Space.Application;
using CP6.Space.CadExperiment;
using CP6.Space.Contracts;

namespace CP6.Space.CadWorker.AutoCadCandidate;

public sealed class AutoCadCandidateConversionService
{
    private readonly IAutoCadDwgExporter _exporter;
    private readonly string _attemptRoot;
    private readonly TimeSpan _waitLimit;
    private readonly SemaphoreSlim _capacity;

    public AutoCadCandidateConversionService(
        string coreConsolePath,
        string workRoot,
        TimeSpan conversionTimeout,
        int maximumConcurrency) :
        this(
            CreateExporter(coreConsolePath, workRoot, conversionTimeout),
            workRoot,
            conversionTimeout,
            maximumConcurrency)
    {
    }

    public AutoCadCandidateConversionService(
        IAutoCadDwgExporter exporter,
        string workRoot,
        TimeSpan conversionTimeout,
        int maximumConcurrency)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        if (maximumConcurrency is < 1 or > 4)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrency));
        var root = Path.GetFullPath(workRoot);
        _attemptRoot = Path.Combine(root, "attempts");
        Directory.CreateDirectory(_attemptRoot);
        _waitLimit = conversionTimeout + TimeSpan.FromSeconds(30);
        _capacity = new SemaphoreSlim(maximumConcurrency, maximumConcurrency);
    }

    public string ProviderKey => AutoCadCoreConsoleDevelopmentConverter.ConverterId;
    public string ProviderVersion => _exporter.ProviderVersion;

    public async Task<SpaceCadWorkerConversionResponseV1> ConvertAsync(
        SpaceCadWorkerConversionRequestV1 request,
        Stream source,
        CancellationToken cancellationToken = default)
    {
        SpaceCadWorkerProtocol.ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(source);
        if (!source.CanRead)
            throw new ArgumentException("The CAD source stream must be readable.", nameof(source));
        if (request.SourceFormat != SpaceCadSourceFormat.Dwg ||
            request.ProviderKey != ProviderKey ||
            request.ProviderVersion != ProviderVersion)
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

    private async Task<SpaceCadWorkerConversionResponseV1> ConvertInAttemptAsync(
        SpaceCadWorkerConversionRequestV1 request,
        Stream source,
        CancellationToken cancellationToken)
    {
        var attempt = Path.Combine(_attemptRoot, request.AttemptId.ToString("N"));
        if (Directory.Exists(attempt))
            throw new InvalidDataException("The CAD Worker attempt identity was already used.");
        Directory.CreateDirectory(attempt);
        try
        {
            var input = Path.Combine(attempt, "source.dwg");
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
            var converter = new AutoCadCoreConsoleDevelopmentConverter(
                _exporter,
                Path.Combine(attempt, "engine-attempts"));
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
            var response = new SpaceCadWorkerConversionResponseV1(
                SpaceCadWorkerProtocolVersions.SchemaVersion,
                request.AttemptId,
                request.SourceSha256,
                request.SourceFormat,
                request.ProviderKey,
                request.ProviderVersion,
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

    private static AutoCadCoreConsoleDwgExporter CreateExporter(
        string coreConsolePath,
        string workRoot,
        TimeSpan conversionTimeout)
    {
        var runtimeRoot = Path.Combine(
            Path.GetFullPath(workRoot),
            "autodesk-runtime-cache");
        Directory.CreateDirectory(runtimeRoot);
        return new AutoCadCoreConsoleDwgExporter(
            coreConsolePath,
            runtimeRoot,
            conversionTimeout);
    }
}
