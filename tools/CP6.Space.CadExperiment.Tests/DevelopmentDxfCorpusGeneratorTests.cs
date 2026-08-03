using System.Text.Json;
using CP6.Space.CadExperiment;

namespace CP6.Space.CadExperiment.Tests;

public sealed class DevelopmentDxfCorpusGeneratorTests
{
    [Fact]
    public async Task Generate_creates_auditable_twenty_sample_development_corpus()
    {
        using var fixture = new TemporaryDirectory();
        var output = Path.Combine(fixture.Path, "corpus");

        var result = await DevelopmentDxfCorpusGenerator.GenerateAsync(output);
        var audit = await DatasetAuditor.AuditAsync(result.ManifestPath);

        Assert.Equal(20, result.SampleCount);
        Assert.Equal(20, Directory.GetFiles(Path.Combine(output, "seeds"), "*.dxf").Length);
        Assert.Equal(4, result.LayoutFamilies["L1"]);
        Assert.Equal(4, result.LayoutFamilies["L5"]);
        Assert.Contains("AC1009", result.CadVersions);
        Assert.Contains("AC1032", result.CadVersions);
        Assert.True(audit.IntegrityPassed);
        Assert.False(audit.CountsTowardReleaseGate);
        Assert.False(audit.E02ReadinessPassed);
        Assert.Empty(audit.Errors);
        Assert.All(audit.Samples, sample => Assert.Equal(0, sample.Dxf!.DuplicateHandleCount));
    }

    [Fact]
    public async Task Generate_is_deterministic_and_covers_edge_entity_types()
    {
        using var fixture = new TemporaryDirectory();
        var first = Path.Combine(fixture.Path, "first");
        var second = Path.Combine(fixture.Path, "second");

        var firstReport = await DevelopmentDxfCorpusGenerator.GenerateAsync(first);
        var secondReport = await DevelopmentDxfCorpusGenerator.GenerateAsync(second);
        var firstManifest = await LoadManifestAsync(firstReport.ManifestPath);
        var secondManifest = await LoadManifestAsync(secondReport.ManifestPath);

        var firstHashes = firstManifest.RootElement.GetProperty("samples")
            .EnumerateArray()
            .Select(sample => sample.GetProperty("sourceSha256").GetString())
            .ToArray();
        var secondHashes = secondManifest.RootElement.GetProperty("samples")
            .EnumerateArray()
            .Select(sample => sample.GetProperty("sourceSha256").GetString())
            .ToArray();
        Assert.Equal(firstHashes, secondHashes);

        var entityTypes = Directory.GetFiles(Path.Combine(first, "seeds"), "*.dxf")
            .Select(DxfProbe.Inspect)
            .SelectMany(probe => probe.EntityTypeCounts.Keys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.Contains("LINE", entityTypes);
        Assert.Contains("LWPOLYLINE", entityTypes);
        Assert.Contains("POLYLINE", entityTypes);
        Assert.Contains("INSERT", entityTypes);
        Assert.Contains("ATTRIB", entityTypes);
        Assert.Contains("HATCH", entityTypes);
        Assert.Contains("SPLINE", entityTypes);
        Assert.Contains("ELLIPSE", entityTypes);
        Assert.Contains("DIMENSION", entityTypes);
        Assert.Contains("MTEXT", entityTypes);
    }

    private static async Task<JsonDocument> LoadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonDocument.ParseAsync(stream);
    }
}
