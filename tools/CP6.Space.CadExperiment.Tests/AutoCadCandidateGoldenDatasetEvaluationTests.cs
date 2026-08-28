using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.CadExperiment;
using CP6.Space.CadWorker.AutoCadCandidate;

namespace CP6.Space.CadExperiment.Tests;

public sealed class AutoCadCandidateGoldenDatasetEvaluationTests
{
    private static readonly DateTime EvaluatedAtUtc =
        new(2026, 8, 27, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Evaluates_20_files_twice_through_the_sealed_release_worker()
    {
        using var fixture = new TemporaryDirectory();
        var setup = await CreateSetupAsync(fixture.Path);

        var report = await AutoCadCandidateGoldenDatasetEvaluator.EvaluateAsync(
            setup.DatasetRoot,
            setup.WorkRoot,
            setup.Service,
            setup.Release,
            EvaluatedAtUtc);

        Assert.True(report.Passed);
        Assert.Empty(report.BlockingCodes);
        Assert.Equal(20, report.SampleCount);
        Assert.Equal(10, report.DwgCount);
        Assert.Equal(10, report.DxfCount);
        Assert.Equal(10, report.CalibrationCount);
        Assert.Equal(5, report.ValidationCount);
        Assert.Equal(5, report.ReleaseHoldoutCount);
        Assert.Equal(20, report.DeterministicReplayCount);
        Assert.Equal(100, report.SupportedEntityPercent);
        Assert.Equal(0, report.TotalMissingSourceRefCount);
        Assert.Equal(0, report.TotalBlockingIssueCount);
        Assert.Equal(0, report.ResidualAttemptDirectoryCount);
        Assert.Equal(0, report.ResidualRawCadFileCount);
        Assert.Equal(setup.Release.ProviderVersion, report.ProviderVersion);
        Assert.Equal(setup.Release.WorkerReleaseSha256, report.WorkerReleaseSha256);
        Assert.Equal("NotVerifiedAtOsBoundary", report.Environment.OutboundNetworkPolicy);
        Assert.All(report.Results, result =>
        {
            Assert.True(result.Passed);
            Assert.True(result.DeterministicReplay);
            Assert.Empty(result.BlockingCodes);
            Assert.Equal(1, result.EntityCount);
        });

        var output = Path.Combine(fixture.Path, "evidence", "evaluation.json");
        var reportSha256 = await AutoCadCandidateGoldenDatasetEvaluator
            .WriteReportAsync(output, report);
        Assert.Equal(64, reportSha256.Length);
        Assert.True(File.Exists(output));
        await Assert.ThrowsAsync<IOException>(() =>
            AutoCadCandidateGoldenDatasetEvaluator.WriteReportAsync(output, report));
    }

    [Fact]
    public async Task Rejects_a_controlled_source_changed_after_manifest_freeze()
    {
        using var fixture = new TemporaryDirectory();
        var setup = await CreateSetupAsync(fixture.Path);
        await File.AppendAllTextAsync(
            Path.Combine(setup.DatasetRoot, "samples", "L1-C01", "source.dwg"),
            "tampered");

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AutoCadCandidateGoldenDatasetEvaluator.EvaluateAsync(
                setup.DatasetRoot,
                setup.WorkRoot,
                setup.Service,
                setup.Release,
                EvaluatedAtUtc));

        Assert.Contains("source size changed", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejects_an_extra_raw_CAD_file_outside_the_frozen_set()
    {
        using var fixture = new TemporaryDirectory();
        var setup = await CreateSetupAsync(fixture.Path);
        await File.WriteAllTextAsync(
            Path.Combine(setup.DatasetRoot, "samples", "unexpected.dxf"),
            MinimalDxf("L1-C01", "FF"));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            AutoCadCandidateGoldenDatasetEvaluator.EvaluateAsync(
                setup.DatasetRoot,
                setup.WorkRoot,
                setup.Service,
                setup.Release,
                EvaluatedAtUtc));

        Assert.Contains("raw CAD file set", exception.Message, StringComparison.Ordinal);
    }

    private static async Task<EvaluationSetup> CreateSetupAsync(string root)
    {
        var datasetRoot = Path.Combine(root, "dataset");
        var samplesRoot = Path.Combine(datasetRoot, "samples");
        Directory.CreateDirectory(samplesRoot);
        var samples = new List<object>();
        var sourceIdentities = new List<(string SampleRef, string SourceSha256)>();
        var familyIndex = 0;
        foreach (var family in new[] { "L1", "L2", "L3", "L4", "L5" })
        {
            var roleIndex = 0;
            foreach (var role in new[] { "C01", "C02", "V01", "H01" })
            {
                var sampleId = family + "-" + role;
                var sampleRef = "urn:cp6-space-golden-cad:v1.0.0:" +
                                sampleId.ToLowerInvariant();
                var format = (familyIndex + roleIndex) % 2 == 0 ? "DWG" : "DXF";
                var extension = format == "DWG" ? ".dwg" : ".dxf";
                var sourceDirectory = Path.Combine(samplesRoot, sampleId);
                Directory.CreateDirectory(sourceDirectory);
                var sourcePath = Path.Combine(sourceDirectory, "source" + extension);
                var sourceBytes = Encoding.UTF8.GetBytes(MinimalDxf(
                    sampleId,
                    (familyIndex * 4 + roleIndex + 16).ToString("X")));
                await File.WriteAllBytesAsync(sourcePath, sourceBytes);
                var sourceSha256 = Sha256(sourceBytes);
                var split = role.StartsWith("C", StringComparison.Ordinal)
                    ? "Calibration"
                    : role.StartsWith("V", StringComparison.Ordinal)
                        ? "Validation"
                        : "ReleaseHoldout";
                samples.Add(new
                {
                    sampleId,
                    sampleRef,
                    sourceSha256,
                    sourceSizeBytes = sourceBytes.LongLength,
                    sourceFormat = format,
                    cadVersion = "AC1032",
                    split,
                    layoutFamily = family,
                    license = "ApprovedOriginalWork",
                    usedForTuning = split == "Calibration",
                    unit = "Millimeter",
                    coordinateSystem = "FloorLocal-ZUp",
                });
                sourceIdentities.Add((sampleRef, sourceSha256));
                roleIndex++;
            }
            familyIndex++;
        }
        var sourceSetPayload = string.Join(
            "\n",
            sourceIdentities
                .OrderBy(item => item.SampleRef, StringComparer.Ordinal)
                .Select(item => item.SampleRef + ":" + item.SourceSha256));
        var manifest = new
        {
            schemaVersion = 3,
            programId = "CP6_SPACE_STUDIO_V1_CORE_GA",
            deliveryMode = "SoloDeveloper",
            evidenceClass = "AUTHORIZED_GOLDEN_CAD_CANDIDATES",
            conclusion = "Pass",
            dataset = new
            {
                datasetVersion = "1.0.0",
                eligibilityBasis = "ApprovedOriginalWork",
                goldenDatasetSha256 = new string('d', 64),
                sourceSetSha256 = Sha256(Encoding.UTF8.GetBytes(sourceSetPayload)),
                isImmutable = true,
                rawCadCommittedToGit = false,
                samples,
            },
        };
        await File.WriteAllTextAsync(
            Path.Combine(datasetRoot, "controlled-manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            }));

        var payloadRoot = Path.Combine(root, "payload");
        var coreRoot = Path.Combine(root, "autocad");
        Directory.CreateDirectory(payloadRoot);
        Directory.CreateDirectory(coreRoot);
        await File.WriteAllTextAsync(
            Path.Combine(payloadRoot, "CP6.Space.CadWorker.AutoCadCandidate.dll"),
            "worker-entry");
        var corePath = Path.Combine(coreRoot, "accoreconsole.exe");
        await File.WriteAllTextAsync(corePath, "autocad-core-console");
        var release = await AutoCadCandidateReleaseIdentity.CreateAsync(
            payloadRoot,
            "1.0.0",
            new string('a', 40),
            "win-x64",
            corePath,
            "25.0.58.0.0");
        var exporter = new ReleaseBoundAutoCadDwgExporter(
            new CopyDxfExporter(),
            corePath,
            release.Manifest.AutoCadCoreConsoleVersion,
            release.Manifest.AutoCadCoreConsoleSha256);
        var workRoot = Path.Combine(root, "work");
        var service = new AutoCadCandidateConversionService(
            exporter,
            workRoot,
            TimeSpan.FromSeconds(10),
            maximumConcurrency: 1,
            release);
        return new EvaluationSetup(datasetRoot, workRoot, release, service);
    }

    private static string MinimalDxf(string sampleId, string handle) =>
        string.Join(
            "\n",
            "0", "SECTION",
            "2", "HEADER",
            "9", "$ACADVER",
            "1", "AC1032",
            "9", "$INSUNITS",
            "70", "4",
            "0", "ENDSEC",
            "0", "SECTION",
            "2", "ENTITIES",
            "999", "SAMPLE_ID",
            "999", sampleId,
            "0", "LINE",
            "5", handle,
            "8", "A-WALL",
            "10", "0",
            "20", "0",
            "11", "10",
            "21", "10",
            "0", "ENDSEC",
            "0", "EOF") + "\n";

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private sealed record EvaluationSetup(
        string DatasetRoot,
        string WorkRoot,
        AutoCadCandidateReleaseIdentity Release,
        AutoCadCandidateConversionService Service);

    private sealed class CopyDxfExporter : IAutoCadDwgExporter
    {
        public string ProviderVersion => "25.0.58.0.0";

        public Task ExportDxfAsync(
            string inputDwgPath,
            string outputDxfPath,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(inputDwgPath, outputDxfPath);
            return Task.CompletedTask;
        }
    }
}
