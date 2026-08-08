using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment.Tests;

public sealed class OfflineEvaluationCommandTests
{
    [Fact]
    public async Task Development_evaluation_writes_report_but_release_flag_fails_closed()
    {
        using var fixture = new TemporaryDirectory();
        var requestPath = fixture.Write(
            "request.json",
            JsonSerializer.Serialize(CreateDevelopmentRequest(), CadExperimentJson.Options));
        var reportPath = Path.Combine(fixture.Path, "report.json");

        var exitCode = await Program.Main(
        [
            "evaluate-ai-offline",
            "--input", requestPath,
            "--output", reportPath,
        ]);
        var releaseExitCode = await Program.Main(
        [
            "evaluate-ai-offline",
            "--input", requestPath,
            "--output", Path.Combine(fixture.Path, "release-report.json"),
            "--require-release-eligible",
        ]);

        Assert.Equal(0, exitCode);
        Assert.Equal(4, releaseExitCode);
        using var report = JsonDocument.Parse(await File.ReadAllTextAsync(reportPath));
        Assert.Equal(
            "DevelopmentSeed",
            report.RootElement.GetProperty("datasetPurpose").GetString());
        Assert.False(report.RootElement
            .GetProperty("gate")
            .GetProperty("releaseEligible")
            .GetBoolean());
        Assert.Equal(
            64,
            report.RootElement.GetProperty("reportSha256").GetString()!.Length);
    }

    private static SpaceAiOfflineEvaluationRequestV1 CreateDevelopmentRequest()
    {
        var samples = Enumerable.Range(0, 20)
            .Select(index => new SpaceAiEvaluationSampleV1(
                $"L{(index / 4) + 1}-DEV-{index + 1:D3}",
                $"L{(index / 4) + 1}-Family",
                SpaceAiEvaluationSplit.DevelopmentSeed,
                $"seeds/{index + 1:D2}.dxf",
                string.Concat(Enumerable.Repeat($"{index + 1:x2}", 32)),
                1))
            .ToArray();
        var expected = samples.Select(sample => new SpaceAiExpectedTargetV1(
                sample.SampleId,
                $"{sample.SampleId}-EXPECTED",
                $"{sample.SampleId}-SOURCE",
                WarehouseSpaceType.Floor,
                new Dictionary<string, string>(),
                []))
            .ToArray();
        var predictions = expected.Select(item => new SpaceAiEvaluationPredictionV1(
                item.SampleId,
                item.ExpectedId.Replace("EXPECTED", "PROPOSAL", StringComparison.Ordinal),
                item.MatchKey,
                item.ObjectType,
                item.KeyAttributes,
                item.Relations,
                0.95m))
            .ToArray();
        var effort = samples.Select(sample => new SpaceAiEvaluationEffortV1(
                sample.SampleId,
                10,
                2))
            .ToArray();
        return new SpaceAiOfflineEvaluationRequestV1(
            new SpaceAiEvaluationManifestV1(
                SpaceAiOfflineEvaluationVersions.SchemaVersion,
                "development-v2.0.0",
                SpaceAiEvaluationDatasetPurpose.DevelopmentSeed,
                false,
                "Millimeter",
                "FloorLocal-ZUp",
                "space-cad-mapping-v1",
                "space-v1",
                "2.0.0",
                "CP6-Synthetic-Development-Only",
                samples),
            expected,
            predictions,
            effort);
    }
}
