using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment;

public interface IAutoCadDwgExporter
{
    string ProviderVersion { get; }

    Task ExportDxfAsync(
        string inputDwgPath,
        string outputDxfPath,
        CancellationToken cancellationToken = default);
}

public sealed class AutoCadCoreConsoleDwgExporter : IAutoCadDwgExporter
{
    private const int MaximumDiagnosticCharacters = 32 * 1024;
    private readonly string _executablePath;
    private readonly string _runtimeCacheRoot;
    private readonly TimeSpan _timeout;

    public AutoCadCoreConsoleDwgExporter(
        string executablePath,
        string runtimeCacheRoot,
        TimeSpan timeout)
    {
        _executablePath = Path.GetFullPath(executablePath);
        if (!File.Exists(_executablePath)
            || !Path.GetFileName(_executablePath).Equals(
                "accoreconsole.exe",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException(
                "A valid accoreconsole.exe path is required.",
                _executablePath);
        }
        if (timeout < TimeSpan.FromSeconds(1)
            || timeout > TimeSpan.FromMinutes(30))
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                "AutoCAD conversion timeout must be between 1 second and 30 minutes.");
        }

        _timeout = timeout;
        if (string.IsNullOrWhiteSpace(runtimeCacheRoot))
        {
            throw new ArgumentException(
                "An AutoCAD runtime cache root is required.",
                nameof(runtimeCacheRoot));
        }
        _runtimeCacheRoot = Path.GetFullPath(runtimeCacheRoot);
        Directory.CreateDirectory(_runtimeCacheRoot);
        var versionInfo = FileVersionInfo.GetVersionInfo(_executablePath);
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
        ProviderVersion = versionInfo.FileVersion
            ?? throw new InvalidDataException(
                "AutoCAD Core Console does not expose a file version.");
        if (string.IsNullOrWhiteSpace(ProviderVersion)
            || ProviderVersion.Length > SpaceCadConversionContract.MaximumIdentifierLength)
        {
            throw new InvalidDataException(
                "AutoCAD Core Console exposes an invalid provider version.");
        }
    }

    public string ProviderVersion { get; }

    public async Task ExportDxfAsync(
        string inputDwgPath,
        string outputDxfPath,
        CancellationToken cancellationToken = default)
    {
        var input = Path.GetFullPath(inputDwgPath);
        var output = Path.GetFullPath(outputDxfPath);
        if (!File.Exists(input)
            || !Path.GetExtension(input).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
        {
            throw new FileNotFoundException("A staged DWG source is required.", input);
        }
        if (!Path.GetExtension(output).Equals(".dxf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("AutoCAD output must use the .dxf extension.");
        }
        if (File.Exists(output))
        {
            throw new IOException("AutoCAD DXF output already exists.");
        }

        var workingDirectory = Path.GetDirectoryName(output)
            ?? throw new InvalidOperationException("AutoCAD output has no directory.");
        Directory.CreateDirectory(workingDirectory);
        var scriptPath = Path.Combine(workingDirectory, "convert-dwg-to-dxf.scr");
        var script = BuildScript(output);
        await File.WriteAllTextAsync(
            scriptPath,
            script,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                WorkingDirectory = _runtimeCacheRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.Unicode,
                StandardErrorEncoding = Encoding.Unicode
            }
        };
        process.StartInfo.ArgumentList.Add("/i");
        process.StartInfo.ArgumentList.Add(input);
        process.StartInfo.ArgumentList.Add("/s");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("/l");
        process.StartInfo.ArgumentList.Add("en-US");
        EnsureRuntimeCacheContainsNoCadSource();
        process.StartInfo.Environment["TEMP"] = _runtimeCacheRoot;
        process.StartInfo.Environment["TMP"] = _runtimeCacheRoot;

        if (!process.Start())
            throw new InvalidOperationException("AutoCAD Core Console did not start.");

        var standardOutputTask = ReadBoundedAsync(process.StandardOutput);
        var standardErrorTask = ReadBoundedAsync(process.StandardError);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw new InvalidOperationException(
                $"AutoCAD Core Console exceeded the {_timeout.TotalSeconds:0}-second timeout.");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(standardOutputTask, standardErrorTask);
            throw;
        }

        var standardOutput = await standardOutputTask;
        var standardError = await standardErrorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"AutoCAD Core Console exited {process.ExitCode}. " +
                $"stdout={Diagnostic(standardOutput)} stderr={Diagnostic(standardError)}");
        }
        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            throw new InvalidDataException(
                "AutoCAD Core Console completed without a non-empty DXF output. " +
                $"stdout={Diagnostic(standardOutput)} stderr={Diagnostic(standardError)}");
        }
    }

    private void EnsureRuntimeCacheContainsNoCadSource()
    {
        var cadSource = Directory.EnumerateFiles(
                _runtimeCacheRoot,
                "*",
                SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase));
        if (cadSource is not null)
        {
            throw new InvalidDataException(
                "AutoCAD runtime cache must not contain DWG or DXF source data.");
        }
    }

    private static string BuildScript(string outputPath)
    {
        var normalized = outputPath.Replace('\\', '/');
        if (normalized.IndexOfAny(['\r', '\n']) >= 0)
            throw new ArgumentException("AutoCAD output path contains a newline.");
        return string.Join(
                   "\r\n",
                   "FILEDIA",
                   "0",
                   "_.DXFOUT",
                   normalized,
                   "16",
                   "_.QUIT",
                   "_Y")
               + "\r\n";
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder(MaximumDiagnosticCharacters);
        var buffer = new char[4096];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None);
            if (count == 0)
                return result.ToString();
            if (count >= MaximumDiagnosticCharacters)
            {
                result.Clear();
                result.Append(buffer, count - MaximumDiagnosticCharacters, MaximumDiagnosticCharacters);
                continue;
            }
            var overflow = result.Length + count - MaximumDiagnosticCharacters;
            if (overflow > 0)
                result.Remove(0, overflow);
            result.Append(buffer, 0, count);
        }
    }

    private static string Diagnostic(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? "<empty>"
            : value.Replace('\0', ' ').ReplaceLineEndings(" ").Trim();

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Process exited between the state check and termination.
        }
    }
}

public sealed class AutoCadCoreConsoleDevelopmentConverter : ICadConverter
{
    public const string ConverterId = "autodesk-autocad-core-console-development";
    private readonly IAutoCadDwgExporter _exporter;
    private readonly string _workingRoot;

    public AutoCadCoreConsoleDevelopmentConverter(
        IAutoCadDwgExporter exporter,
        string workingRoot)
    {
        _exporter = exporter ?? throw new ArgumentNullException(nameof(exporter));
        if (string.IsNullOrWhiteSpace(workingRoot))
            throw new ArgumentException("A development conversion working root is required.");
        _workingRoot = Path.GetFullPath(workingRoot);
    }

    public async Task<SpaceCadConversionResult> ConvertAsync(
        SpaceCadConversionRequest request,
        Stream source,
        ISpaceCadIrSink sink,
        CancellationToken cancellationToken = default)
    {
        SpaceCadConversionContract.ValidateRequest(request);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sink);
        if (request.SourceFormat != SpaceCadSourceFormat.Dwg)
            throw new InvalidDataException("AutoCAD Core Console development conversion accepts DWG only.");
        if (!request.ConverterId.Equals(ConverterId, StringComparison.Ordinal)
            || !request.ConverterVersion.Equals(
                _exporter.ProviderVersion,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "AutoCAD converter identity does not match the configured executable.");
        }

        Directory.CreateDirectory(_workingRoot);
        var attemptDirectory = Path.Combine(
            _workingRoot,
            "autocad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(attemptDirectory);
        SpaceCadIrPackageV1 package;
        try
        {
            var stagedDwg = Path.Combine(attemptDirectory, "source.dwg");
            var actualSourceSha256 = await CopyAndHashAsync(
                source,
                stagedDwg,
                cancellationToken);
            if (!actualSourceSha256.Equals(
                    request.SourceSha256,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "DWG source bytes do not match the conversion request hash.");
            }

            var dxfPath = Path.Combine(attemptDirectory, "source.dxf");
            await _exporter.ExportDxfAsync(stagedDwg, dxfPath, cancellationToken);
            var dxfSha256 = await DatasetAuditor.ComputeSha256Async(
                dxfPath,
                cancellationToken);
            var dxfRequest = new SpaceCadConversionRequest(
                request.TenantId,
                request.FileId,
                request.SourceId,
                dxfSha256,
                SpaceCadSourceFormat.Dxf,
                DevelopmentDxfCadConverter.ConverterId,
                DevelopmentDxfCadConverter.ConverterVersion);
            var collectingSink = new CollectingCadIrSink(dxfRequest);
            await using (var dxf = File.OpenRead(dxfPath))
            {
                await SpaceCadConverterContractRunner.ConvertAsync(
                    new DevelopmentDxfCadConverter(),
                    dxfRequest,
                    dxf,
                    collectingSink,
                    cancellationToken);
            }

            package = collectingSink.Package
                ?? throw new InvalidDataException("DXF conversion did not produce CAD IR.");
        }
        finally
        {
            await DeleteAttemptDirectoryAsync(attemptDirectory);
        }

        var document = package.Document with
        {
            SourceSha256 = request.SourceSha256,
            SourceFormat = SpaceCadSourceFormat.Dwg,
            ConverterId = request.ConverterId,
            ConverterVersion = request.ConverterVersion
        };
        await sink.WriteDocumentAsync(document, cancellationToken);
        foreach (var layer in package.Layers)
            await sink.WriteLayerAsync(layer, cancellationToken);
        foreach (var block in package.Blocks)
            await sink.WriteBlockAsync(block, cancellationToken);
        foreach (var entity in package.Entities)
            await sink.WriteEntityAsync(entity, cancellationToken);
        var cadIrSha256 = await sink.CompleteAsync(
            package.Issues,
            package.Summary,
            cancellationToken);
        return new SpaceCadConversionResult(
            request.SourceSha256,
            cadIrSha256,
            request.ConverterId,
            request.ConverterVersion,
            package.Summary,
            package.Issues);
    }

    private static async Task DeleteAttemptDirectoryAsync(string attemptDirectory)
    {
        Exception? lastError = null;
        for (var attempt = 1; attempt <= 25; attempt++)
        {
            if (!Directory.Exists(attemptDirectory))
                return;
            try
            {
                Directory.Delete(attemptDirectory, recursive: true);
                return;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                lastError = exception;
                if (attempt < 25)
                    await Task.Delay(TimeSpan.FromMilliseconds(200));
            }
        }

        throw new InvalidOperationException(
            "AutoCAD isolated working directory could not be removed.",
            lastError);
    }

    private static async Task<string> CopyAndHashAsync(
        Stream source,
        string destination,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[81920];
        try
        {
            while (true)
            {
                var count = await source.ReadAsync(buffer.AsMemory(), cancellationToken);
                if (count == 0)
                    break;
                hash.AppendData(buffer, 0, count);
                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            }
            await output.FlushAsync(cancellationToken);
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(buffer);
        }
    }

    private sealed class CollectingCadIrSink : ISpaceCadIrSink
    {
        private readonly SpaceCadConversionRequest _request;
        private readonly List<SpaceCadIrLayerV1> _layers = [];
        private readonly List<SpaceCadIrBlockV1> _blocks = [];
        private readonly List<SpaceCadIrEntityV1> _entities = [];
        private SpaceCadIrDocumentV1? _document;

        public CollectingCadIrSink(SpaceCadConversionRequest request) =>
            _request = request;

        public SpaceCadIrPackageV1? Package { get; private set; }

        public ValueTask WriteDocumentAsync(
            SpaceCadIrDocumentV1 document,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _document = document;
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteLayerAsync(
            SpaceCadIrLayerV1 layer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _layers.Add(layer);
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteBlockAsync(
            SpaceCadIrBlockV1 block,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _blocks.Add(block);
            return ValueTask.CompletedTask;
        }

        public ValueTask WriteEntityAsync(
            SpaceCadIrEntityV1 entity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _entities.Add(entity);
            return ValueTask.CompletedTask;
        }

        public ValueTask<string> CompleteAsync(
            IReadOnlyList<SpaceCadConversionIssueV1> issues,
            SpaceCadIrSummaryV1 summary,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Package = new SpaceCadIrPackageV1(
                _document ?? throw new InvalidOperationException(
                    "CAD IR document was not written."),
                _layers.ToArray(),
                _blocks.ToArray(),
                _entities.ToArray(),
                issues.ToArray(),
                summary);
            SpaceCadConversionContract.ValidatePackage(_request, Package);
            var bytes = JsonSerializer.SerializeToUtf8Bytes(
                Package,
                CadExperimentJson.Options);
            try
            {
                return ValueTask.FromResult(
                    Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
            }
            finally
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }
}
