using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.CadWorker.AutoCadCandidate;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment.Tests;

public sealed class AutoCadCandidateWorkerTests
{
    [Fact]
    public async Task Worker_verifies_hash_runs_contract_and_deletes_attempt()
    {
        using var directory = new TemporaryDirectory();
        var exporter = new FakeExporter();
        var service = new AutoCadCandidateConversionService(
            exporter,
            directory.Path,
            TimeSpan.FromMinutes(1),
            maximumConcurrency: 1);
        var bytes = Encoding.UTF8.GetBytes("candidate-worker-dwg");
        var request = Request(service, Sha256(bytes));
        await using var source = new MemoryStream(bytes, writable: false);

        var response = await service.ConvertAsync(request, source);

        SpaceCadWorkerProtocol.ValidateResponse(request, response);
        Assert.Equal(1, exporter.CallCount);
        Assert.Equal(bytes, exporter.LastSourceBytes);
        Assert.Equal(SpaceCadSourceFormat.Dwg, response.Package.Document.SourceFormat);
        Assert.Empty(Directory.GetFileSystemEntries(
            Path.Combine(directory.Path, "attempts")));
    }

    [Fact]
    public async Task Worker_rejects_hash_mismatch_before_starting_converter()
    {
        using var directory = new TemporaryDirectory();
        var exporter = new FakeExporter();
        var service = new AutoCadCandidateConversionService(
            exporter,
            directory.Path,
            TimeSpan.FromMinutes(1),
            maximumConcurrency: 1);
        await using var source = new MemoryStream(
            Encoding.UTF8.GetBytes("different"),
            writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ConvertAsync(request: Request(service, new string('a', 64)), source));

        Assert.Equal(0, exporter.CallCount);
        Assert.Empty(Directory.GetFileSystemEntries(
            Path.Combine(directory.Path, "attempts")));
    }

    [Fact]
    public async Task Worker_accepts_native_DXF_without_starting_AutoCAD()
    {
        using var directory = new TemporaryDirectory();
        var exporter = new FakeExporter();
        var service = new AutoCadCandidateConversionService(
            exporter,
            directory.Path,
            TimeSpan.FromMinutes(1),
            maximumConcurrency: 1);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(ValidDxf);
        var request = Request(service, Sha256(bytes), SpaceCadSourceFormat.Dxf);
        await using var source = new MemoryStream(bytes, writable: false);

        var response = await service.ConvertAsync(request, source);

        SpaceCadWorkerProtocol.ValidateResponse(request, response);
        Assert.Equal(0, exporter.CallCount);
        Assert.Equal(SpaceCadSourceFormat.Dxf, response.Package.Document.SourceFormat);
        Assert.Equal(service.ProviderKey, response.Package.Document.ConverterId);
        Assert.Equal(service.ProviderVersion, response.Package.Document.ConverterVersion);
        Assert.Contains("cp6-dxf-1.0.0", service.ProviderVersion, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(
            Path.Combine(directory.Path, "attempts")));
    }

    [Fact]
    public async Task Worker_rejects_stale_composite_version_before_staging()
    {
        using var directory = new TemporaryDirectory();
        var exporter = new FakeExporter();
        var service = new AutoCadCandidateConversionService(
            exporter,
            directory.Path,
            TimeSpan.FromMinutes(1),
            maximumConcurrency: 1);
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            .GetBytes(ValidDxf);
        var request = Request(
            service,
            Sha256(bytes),
            SpaceCadSourceFormat.Dxf) with
        {
            ProviderVersion = exporter.ProviderVersion,
        };
        await using var source = new MemoryStream(bytes, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.ConvertAsync(request, source));

        Assert.Equal(0, exporter.CallCount);
        Assert.Empty(Directory.GetFileSystemEntries(
            Path.Combine(directory.Path, "attempts")));
    }

    [AutoCadCoreConsoleFact]
    public async Task Installed_core_console_runs_through_candidate_Worker_boundary()
    {
        var executable = Environment.GetEnvironmentVariable(
            AutoCadCoreConsoleFactAttribute.ExecutableEnvVar)!;
        var sample = Environment.GetEnvironmentVariable(
            AutoCadCoreConsoleFactAttribute.SampleEnvVar)!;
        var configuredRoot = Environment.GetEnvironmentVariable(
            AutoCadCoreConsoleFactAttribute.WorkRootEnvVar)!;
        var root = Path.Combine(
            Path.GetFullPath(configuredRoot),
            "autocad-worker-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var exporter = new AutoCadCoreConsoleDwgExporter(
                executable,
                Path.Combine(
                    Path.GetFullPath(configuredRoot),
                    "_autodesk-runtime-cache"),
                TimeSpan.FromMinutes(2));
            var service = new AutoCadCandidateConversionService(
                exporter,
                root,
                TimeSpan.FromMinutes(2),
                maximumConcurrency: 1);
            var hash = await DatasetAuditor.ComputeSha256Async(sample);
            var request = Request(service, hash);
            await using var source = File.OpenRead(sample);

            var response = await service.ConvertAsync(request, source);

            Assert.Equal(29, response.Package.Summary.LayerCount);
            Assert.Equal(19, response.Package.Summary.BlockCount);
            Assert.Equal(4_424, response.Package.Summary.EntityCount);
            Assert.Equal(4_422, response.Package.Summary.SupportedEntityCount);
            Assert.Equal(hash, response.Package.Document.SourceSha256);
            Assert.Equal(service.ProviderKey, response.Package.Document.ConverterId);
            Assert.Equal(service.ProviderVersion, response.Package.Document.ConverterVersion);
            Assert.Empty(Directory.GetFileSystemEntries(
                Path.Combine(root, "attempts")));
            Assert.DoesNotContain(
                Directory.EnumerateFiles(
                    root,
                    "*.*",
                    SearchOption.AllDirectories),
                path =>
                Path.GetExtension(path).Equals(".dwg", StringComparison.OrdinalIgnoreCase) ||
                Path.GetExtension(path).Equals(".dxf", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static SpaceCadWorkerConversionRequestV1 Request(
        AutoCadCandidateConversionService service,
        string sha256,
        SpaceCadSourceFormat sourceFormat = SpaceCadSourceFormat.Dwg) =>
        new(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            Guid.NewGuid(),
            sha256,
            sourceFormat,
            service.ProviderKey,
            service.ProviderVersion);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class FakeExporter : IAutoCadDwgExporter
    {
        public string ProviderVersion => "25.0-worker-test";
        public int CallCount { get; private set; }
        public byte[]? LastSourceBytes { get; private set; }

        public async Task ExportDxfAsync(
            string inputDwgPath,
            string outputDxfPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSourceBytes = await File.ReadAllBytesAsync(
                inputDwgPath,
                cancellationToken);
            await File.WriteAllTextAsync(
                outputDxfPath,
                ValidDxf,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                cancellationToken);
        }
    }

    private const string ValidDxf =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n"
        + "9\n$INSUNITS\n70\n4\n0\nENDSEC\n"
        + "0\nSECTION\n2\nBLOCKS\n0\nENDSEC\n"
        + "0\nSECTION\n2\nENTITIES\n"
        + "0\nLINE\n5\n100\n8\nWALL\n10\n0\n20\n0\n30\n0\n"
        + "11\n1000\n21\n1000\n31\n0\n0\nENDSEC\n0\nEOF\n";
}
