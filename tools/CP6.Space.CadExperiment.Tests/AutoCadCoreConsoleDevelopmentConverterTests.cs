using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment.Tests;

public sealed class AutoCadCoreConsoleDevelopmentConverterTests
{
    [Fact]
    public async Task Converter_stages_dwg_and_emits_source_bound_cad_ir()
    {
        using var fixture = new TemporaryDirectory();
        var sourceBytes = Encoding.UTF8.GetBytes("development-dwg-source");
        var request = Request(Sha256(sourceBytes));
        var exporter = new FakeExporter();
        var output = Path.Combine(fixture.Path, "output", "sample.cad-ir.json");
        var workRoot = Path.Combine(fixture.Path, "isolated");
        var sink = new DevelopmentCadIrFileSink(request, output);
        await using var source = new MemoryStream(sourceBytes, writable: false);

        var result = await SpaceCadConverterContractRunner.ConvertAsync(
            new AutoCadCoreConsoleDevelopmentConverter(exporter, workRoot),
            request,
            source,
            sink);

        Assert.Equal(1, exporter.CallCount);
        Assert.Equal(sourceBytes, exporter.LastSourceBytes);
        Assert.Equal(request.SourceSha256, result.SourceSha256);
        Assert.Equal(AutoCadCoreConsoleDevelopmentConverter.ConverterId, result.ConverterId);
        Assert.Equal(exporter.ProviderVersion, result.ConverterVersion);
        Assert.Equal(1, result.Summary.EntityCount);
        Assert.Equal(SpaceCadSourceFormat.Dwg, sink.Package!.Document.SourceFormat);
        Assert.Equal(request.SourceSha256, sink.Package.Document.SourceSha256);
        Assert.Equal(request.ConverterId, sink.Package.Document.ConverterId);
        Assert.Equal(request.ConverterVersion, sink.Package.Document.ConverterVersion);
        Assert.Empty(Directory.GetFileSystemEntries(workRoot));
    }

    [Fact]
    public async Task Converter_rejects_wrong_source_hash_before_export()
    {
        using var fixture = new TemporaryDirectory();
        var exporter = new FakeExporter();
        var request = Request(new string('a', 64));
        var sink = new DevelopmentCadIrFileSink(
            request,
            Path.Combine(fixture.Path, "output.json"));
        await using var source = new MemoryStream(
            Encoding.UTF8.GetBytes("different-source"),
            writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                new AutoCadCoreConsoleDevelopmentConverter(
                    exporter,
                    Path.Combine(fixture.Path, "isolated")),
                request,
                source,
                sink));

        Assert.Equal(0, exporter.CallCount);
        Assert.Empty(Directory.GetFileSystemEntries(
            Path.Combine(fixture.Path, "isolated")));
    }

    [Fact]
    public async Task Converter_cleans_staged_source_when_export_fails()
    {
        using var fixture = new TemporaryDirectory();
        var sourceBytes = Encoding.UTF8.GetBytes("development-dwg-source");
        var exporter = new FakeExporter { Failure = new IOException("fixture failure") };
        var request = Request(Sha256(sourceBytes));
        var workRoot = Path.Combine(fixture.Path, "isolated");
        var sink = new DevelopmentCadIrFileSink(
            request,
            Path.Combine(fixture.Path, "output.json"));
        await using var source = new MemoryStream(sourceBytes, writable: false);

        await Assert.ThrowsAsync<IOException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                new AutoCadCoreConsoleDevelopmentConverter(exporter, workRoot),
                request,
                source,
                sink));

        Assert.Equal(1, exporter.CallCount);
        Assert.Empty(Directory.GetFileSystemEntries(workRoot));
    }

    [Fact]
    public async Task Converter_rejects_identity_that_does_not_bind_executable_version()
    {
        using var fixture = new TemporaryDirectory();
        var sourceBytes = Encoding.UTF8.GetBytes("development-dwg-source");
        var request = Request(Sha256(sourceBytes)) with { ConverterVersion = "different" };
        var exporter = new FakeExporter();
        var sink = new DevelopmentCadIrFileSink(
            request,
            Path.Combine(fixture.Path, "output.json"));
        await using var source = new MemoryStream(sourceBytes, writable: false);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            SpaceCadConverterContractRunner.ConvertAsync(
                new AutoCadCoreConsoleDevelopmentConverter(
                    exporter,
                    Path.Combine(fixture.Path, "isolated")),
                request,
                source,
                sink));

        Assert.Equal(0, exporter.CallCount);
    }

    [Fact]
    public void Process_exporter_rejects_a_non_core_console_executable()
    {
        var exception = Assert.Throws<FileNotFoundException>(() =>
            new AutoCadCoreConsoleDwgExporter(
                typeof(AutoCadCoreConsoleDevelopmentConverterTests).Assembly.Location,
                Path.Combine(Path.GetTempPath(), "cp6-invalid-autocad-cache"),
                TimeSpan.FromMinutes(1)));

        Assert.Contains("accoreconsole.exe", exception.Message, StringComparison.Ordinal);
    }

    [AutoCadCoreConsoleFact]
    public async Task Installed_core_console_converts_a_real_dwg_through_contract_runner()
    {
        var executable = Environment.GetEnvironmentVariable(
            AutoCadCoreConsoleFactAttribute.ExecutableEnvVar)!;
        var sample = Environment.GetEnvironmentVariable(
            AutoCadCoreConsoleFactAttribute.SampleEnvVar)!;
        var configuredWorkRoot = Environment.GetEnvironmentVariable(
            AutoCadCoreConsoleFactAttribute.WorkRootEnvVar)!;
        var testRoot = Path.Combine(
            Path.GetFullPath(configuredWorkRoot),
            "autocad-contract-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(testRoot);
        try
        {
            var exporter = new AutoCadCoreConsoleDwgExporter(
                executable,
                Path.Combine(
                    Path.GetFullPath(configuredWorkRoot),
                    "_autodesk-runtime-cache"),
                TimeSpan.FromMinutes(2));
            var sourceHash = await DatasetAuditor.ComputeSha256Async(sample);
            var request = Request(sourceHash, exporter.ProviderVersion);
            var output = Path.Combine(testRoot, "sample.cad-ir.json");
            var sink = new DevelopmentCadIrFileSink(request, output);
            await using var source = File.OpenRead(sample);

            var result = await SpaceCadConverterContractRunner.ConvertAsync(
                new AutoCadCoreConsoleDevelopmentConverter(
                    exporter,
                    Path.Combine(testRoot, "isolated")),
                request,
                source,
                sink);

            Assert.True(result.Summary.EntityCount > 0);
            Assert.True(File.Exists(output));
            Assert.Equal(SpaceCadSourceFormat.Dwg, sink.Package!.Document.SourceFormat);
            Assert.Equal(sourceHash, sink.Package.Document.SourceSha256);
            Assert.Equal(exporter.ProviderVersion, result.ConverterVersion);
        }
        finally
        {
            if (Directory.Exists(testRoot))
                Directory.Delete(testRoot, recursive: true);
        }
    }

    private static SpaceCadConversionRequest Request(
        string sourceSha256,
        string converterVersion = FakeExporter.Version) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            sourceSha256,
            SpaceCadSourceFormat.Dwg,
            AutoCadCoreConsoleDevelopmentConverter.ConverterId,
            converterVersion);

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed class FakeExporter : IAutoCadDwgExporter
    {
        public const string Version = "25.0-test";

        public string ProviderVersion => Version;

        public int CallCount { get; private set; }

        public byte[]? LastSourceBytes { get; private set; }

        public Exception? Failure { get; init; }

        public async Task ExportDxfAsync(
            string inputDwgPath,
            string outputDxfPath,
            CancellationToken cancellationToken = default)
        {
            CallCount++;
            LastSourceBytes = await File.ReadAllBytesAsync(
                inputDwgPath,
                cancellationToken);
            if (Failure is not null)
                throw Failure;
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

[AttributeUsage(AttributeTargets.Method)]
public sealed class AutoCadCoreConsoleFactAttribute : FactAttribute
{
    public const string ExecutableEnvVar = "CP6_TEST_AUTOCAD_CORE_CONSOLE";
    public const string SampleEnvVar = "CP6_TEST_AUTOCAD_DWG";
    public const string WorkRootEnvVar = "CP6_TEST_AUTOCAD_WORK_ROOT";

    public AutoCadCoreConsoleFactAttribute()
    {
        var executable = Environment.GetEnvironmentVariable(ExecutableEnvVar);
        var sample = Environment.GetEnvironmentVariable(SampleEnvVar);
        var workRoot = Environment.GetEnvironmentVariable(WorkRootEnvVar);
        if (string.IsNullOrWhiteSpace(executable)
            || string.IsNullOrWhiteSpace(sample)
            || string.IsNullOrWhiteSpace(workRoot)
            || !File.Exists(executable)
            || !File.Exists(sample))
        {
            Skip = $"Set {ExecutableEnvVar}, {SampleEnvVar}, and {WorkRootEnvVar} " +
                   "to run the installed AutoCAD Core Console contract test.";
        }
    }
}
