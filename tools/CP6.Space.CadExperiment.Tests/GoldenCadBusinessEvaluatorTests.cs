using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment.Tests;

public sealed class GoldenCadBusinessEvaluatorTests
{
    [Fact]
    public async Task Evaluates_exact_20_sample_formal_set_and_ignores_structural_noise()
    {
        using var fixture = new TemporaryDirectory();
        var setup = CreateFixture(fixture);

        var result = await GoldenCadBusinessEvaluator.EvaluateAsync(
            setup.DatasetRoot,
            setup.CadIrRoot,
            setup.RulesPath,
            new string('a', 40),
            "1.0.0+formal-test",
            new DateOnly(2026, 8, 28));

        Assert.True(result.Report.Gate.ReleaseEligible);
        Assert.Empty(result.Report.Gate.IssueCodes);
        Assert.Equal(400, result.Report.OverallMetrics.ExpectedTargetCount);
        Assert.Equal(400, result.Report.OverallMetrics.PredictionCount);
        Assert.Equal(400, result.Report.OverallMetrics.CorrectPredictionCount);
        Assert.Equal(1m, result.Report.OutOfSampleMetrics.TargetCoverage);
        Assert.Equal(1m, result.Report.OutOfSampleMetrics.OverallSemanticAccuracy);
        Assert.True(
            result.Report.OutOfSampleMetrics.HighConfidenceWilsonLowerBound >= 0.90m);
        Assert.True(
            result.Report.OutOfSampleMetrics.ManualOperationReduction >= 0.70m);
        Assert.Equal(0, result.HoldoutUnreportedBlockingOmissions);
        Assert.Equal(20, result.Samples.Count);
        Assert.All(result.Samples, item =>
        {
            Assert.Equal(20, item.PredictionCount);
            Assert.Equal(20, item.CorrectPredictionCount);
        });
    }

    [Fact]
    public async Task Rejects_CAD_IR_not_bound_to_the_controlled_source_hash()
    {
        using var fixture = new TemporaryDirectory();
        var setup = CreateFixture(fixture);
        var path = Path.Combine(setup.CadIrRoot, "L1-C01.json");
        var package = JsonSerializer.Deserialize<SpaceCadIrPackageV1>(
                          File.ReadAllText(path),
                          CadExperimentJson.Options)!;
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(
                package with
                {
                    Document = package.Document with
                    {
                        SourceSha256 = new string('f', 64),
                    },
                },
                CadExperimentJson.Options));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            GoldenCadBusinessEvaluator.EvaluateAsync(
                setup.DatasetRoot,
                setup.CadIrRoot,
                setup.RulesPath,
                new string('a', 40),
                "1.0.0+formal-test",
                new DateOnly(2026, 8, 28)));

        Assert.Contains("frozen source contract", exception.Message, StringComparison.Ordinal);
    }

    private static FixtureSetup CreateFixture(TemporaryDirectory fixture)
    {
        var datasetRoot = Path.Combine(fixture.Path, "dataset");
        var cadIrRoot = Path.Combine(fixture.Path, "ir");
        Directory.CreateDirectory(datasetRoot);
        Directory.CreateDirectory(cadIrRoot);
        var manifestSamples = new List<object>();
        var familyIndex = 0;
        foreach (var family in new[] { "L1", "L2", "L3", "L4", "L5" })
        {
            var roleIndex = 0;
            foreach (var role in new[] { "C01", "C02", "V01", "H01" })
            {
                var sampleId = family + "-" + role;
                var split = role.StartsWith('C')
                    ? "Calibration"
                    : role.StartsWith('V') ? "Validation" : "ReleaseHoldout";
                var sourceFormat = (familyIndex + roleIndex) % 2 == 0
                    ? SpaceCadSourceFormat.Dwg
                    : SpaceCadSourceFormat.Dxf;
                var sourceSha256 = Sha256(sampleId);
                var sampleRoot = Path.Combine(datasetRoot, "samples", sampleId);
                Directory.CreateDirectory(sampleRoot);
                var expectedLines = Enumerable.Range(0, 20).Select(index =>
                {
                    var handle = (0x100 + index).ToString("X");
                    var centerX = 1_000 + (index * 2_000);
                    return JsonSerializer.Serialize(new
                    {
                        expectedId = sampleId + "-RACK-" + (index + 1).ToString("D3"),
                        type = "Rack",
                        layer = "S-RACK",
                        sourceRefs = new[] { new { handle, layer = "S-RACK" } },
                        geometry = new
                        {
                            kind = "OrientedBox2D",
                            center = new[] { centerX, 2_000, 0 },
                            size = new[] { 1_000, 2_000 },
                            rotationDeg = 0,
                        },
                        relationships = new
                        {
                            floorId = "F01",
                            zoneId = "",
                            aisleId = "",
                        },
                    }, new JsonSerializerOptions(CadExperimentJson.Options)
                    {
                        WriteIndented = false,
                    });
                });
                var expectedPath = fixture.Write(
                    Path.GetRelativePath(
                        fixture.Path,
                        Path.Combine(sampleRoot, "expected-elements.jsonl")),
                    string.Join("\n", expectedLines) + "\n");
                var issuesPath = fixture.Write(
                    Path.GetRelativePath(
                        fixture.Path,
                        Path.Combine(sampleRoot, "expected-issues.json")),
                    "{\"issues\":[]}");
                var metadataPath = fixture.Write(
                    Path.GetRelativePath(
                        fixture.Path,
                        Path.Combine(sampleRoot, "metadata.json")),
                    JsonSerializer.Serialize(new { sampleId }, CadExperimentJson.Options));
                var mappingPath = fixture.Write(
                    Path.GetRelativePath(
                        fixture.Path,
                        Path.Combine(sampleRoot, "mapping-profile.json")),
                    "{\"floorResolution\":\"SingleFloorAtZ0\"}");

                var entities = Enumerable.Range(0, 20)
                    .Select(index => RackEntity(index))
                    .Append(new SpaceCadIrEntityV1(
                        "H:999",
                        SpaceCadIrEntityType.Line,
                        "LINE",
                        "S-RACK",
                        BlockName: null,
                        [new SpaceCadPointV1(0, 0), new SpaceCadPointV1(1, 1)],
                        Radius: null,
                        StartAngleDegrees: null,
                        EndAngleDegrees: null,
                        SpaceCadAffineTransformV1.Identity,
                        new SpaceCadBoundsV1(0, 0, 1, 1),
                        IsClosed: false,
                        IsSupported: true,
                        new Dictionary<string, string>()))
                    .ToArray();
                var package = new SpaceCadIrPackageV1(
                    new SpaceCadIrDocumentV1(
                        SpaceCadIrVersions.SchemaVersion,
                        sourceSha256,
                        sourceFormat,
                        "AC1032",
                        SpaceCadUnit.Millimeter,
                        1,
                        SpaceCadIrVersions.CoordinateSystem,
                        new SpaceCadBoundsV1(500, 1_000, 39_500, 3_000),
                        "formal-test",
                        "1.0.0"),
                    [new SpaceCadIrLayerV1("S-RACK", "S-RACK", entities.Length)],
                    [],
                    entities,
                    [],
                    new SpaceCadIrSummaryV1(
                        1,
                        0,
                        entities.Length,
                        entities.Length,
                        0,
                        0,
                        new SpaceCadBoundsV1(0, 0, 39_500, 3_000)));
                fixture.Write(
                    Path.GetRelativePath(
                        fixture.Path,
                        Path.Combine(cadIrRoot, sampleId + ".json")),
                    JsonSerializer.Serialize(package, CadExperimentJson.Options));

                manifestSamples.Add(new
                {
                    sampleId,
                    sampleRef = "urn:cp6-space-golden-cad:test:" + sampleId.ToLowerInvariant(),
                    sourceSha256,
                    sourceFormat = sourceFormat.ToString().ToUpperInvariant(),
                    split,
                    layoutFamily = family,
                    license = "ApprovedOriginalWork",
                    deidentificationEvidence = new
                    {
                        uri = "urn:cp6:test:deidentification:" + sampleId.ToLowerInvariant(),
                    },
                    artifacts = new
                    {
                        metadataSha256 = FileSha256(metadataPath),
                        expectedElementsSha256 = FileSha256(expectedPath),
                        expectedIssuesSha256 = FileSha256(issuesPath),
                        mappingProfileSha256 = FileSha256(mappingPath),
                    },
                });
                roleIndex++;
            }
            familyIndex++;
        }

        fixture.Write(
            Path.GetRelativePath(
                fixture.Path,
                Path.Combine(datasetRoot, "controlled-manifest.json")),
            JsonSerializer.Serialize(new
            {
                dataset = new
                {
                    datasetVersion = "1.0.0",
                    eligibilityBasis = "ApprovedOriginalWork",
                    goldenDatasetSha256 = new string('b', 64),
                    sourceSetSha256 = new string('c', 64),
                    mappingProfileVersion = "cp6-space-cad-original-v1.0.0",
                    ruleSetVersion = "space-v1.0.0-original-cad",
                    expectedAnswerVersion = "1.0.0",
                    integrityAuditPassed = true,
                    integrityAuditEvidence = new
                    {
                        uri = "urn:cp6:test:integrity-audit",
                        sha256 = new string('d', 64),
                    },
                    samples = manifestSamples,
                },
            }, CadExperimentJson.Options));
        var rulesPath = fixture.Write(
            "rules.json",
            JsonSerializer.Serialize(new GoldenCadBusinessRuleSetV1(
                1,
                "test-parser-v1",
                "cp6-space-cad-original-v1.0.0",
                "space-v1.0.0-original-cad",
                "test-rule-only-v1",
                120,
                "Test operation model.",
                [new GoldenCadBusinessRuleV1(
                    "S-RACK",
                    WarehouseSpaceType.Rack,
                    SpaceCadIrEntityType.ClosedPolyline,
                    0.99m,
                    null,
                    false)]),
                CadExperimentJson.Options));
        return new FixtureSetup(datasetRoot, cadIrRoot, rulesPath);
    }

    private static SpaceCadIrEntityV1 RackEntity(int index)
    {
        var handle = (0x100 + index).ToString("X");
        var centerX = 1_000 + (index * 2_000);
        var points = new[]
        {
            new SpaceCadPointV1(centerX - 500, 1_000),
            new SpaceCadPointV1(centerX + 500, 1_000),
            new SpaceCadPointV1(centerX + 500, 3_000),
            new SpaceCadPointV1(centerX - 500, 3_000),
        };
        return new SpaceCadIrEntityV1(
            "H:" + handle,
            SpaceCadIrEntityType.ClosedPolyline,
            "LWPOLYLINE",
            "S-RACK",
            BlockName: null,
            points,
            Radius: null,
            StartAngleDegrees: null,
            EndAngleDegrees: null,
            SpaceCadAffineTransformV1.Identity,
            new SpaceCadBoundsV1(centerX - 500, 1_000, centerX + 500, 3_000),
            IsClosed: true,
            IsSupported: true,
            new Dictionary<string, string>());
    }

    private static string Sha256(string value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string FileSha256(string path) => Convert.ToHexString(
        SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();

    private sealed record FixtureSetup(
        string DatasetRoot,
        string CadIrRoot,
        string RulesPath);
}
