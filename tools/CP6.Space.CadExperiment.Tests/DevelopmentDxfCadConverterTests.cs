using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment.Tests;

public sealed class DevelopmentDxfCadConverterTests
{
    [Fact]
    public async Task Converter_emits_valid_ir_for_all_twenty_synthetic_drawings()
    {
        using var fixture = new TemporaryDirectory();
        var corpus = Path.Combine(fixture.Path, "corpus");
        var generated = await DevelopmentDxfCorpusGenerator.GenerateAsync(corpus);
        var manifest = await LoadManifestAsync(generated.ManifestPath);
        var converter = new DevelopmentDxfCadConverter();
        long totalEntities = 0;
        long unsupportedEntities = 0;

        foreach (var sample in manifest.Samples)
        {
            var input = Path.Combine(corpus, sample.SourceFile.Replace('/', Path.DirectorySeparatorChar));
            var output = Path.Combine(fixture.Path, "ir", sample.SampleId + ".json");
            var request = Request(sample.SourceSha256);
            var sink = new DevelopmentCadIrFileSink(request, output);
            await using var stream = File.OpenRead(input);

            var result = await converter.ConvertAsync(request, stream, sink);

            Assert.NotNull(sink.Package);
            SpaceCadConversionContract.ValidatePackage(request, sink.Package!);
            Assert.Equal(SpaceCadUnit.Millimeter, sink.Package!.Document.Unit);
            Assert.Equal(1m, sink.Package.Document.ScaleToMillimeters);
            Assert.Equal(sample.SourceSha256, result.SourceSha256);
            Assert.Equal(
                await DatasetAuditor.ComputeSha256Async(output),
                result.CadIrSha256);
            Assert.Equal(
                sink.Package.Entities.Count,
                sink.Package.Entities.Select(entity => entity.SourceRef).Distinct().Count());
            var unsupportedIssueRefs = sink.Package.Issues
                .Where(issue => issue.Code == "SPACE_CAD_ENTITY_UNSUPPORTED")
                .Select(issue => issue.SourceRef)
                .ToHashSet(StringComparer.Ordinal);
            Assert.All(
                sink.Package.Entities.Where(entity => !entity.IsSupported),
                entity => Assert.Contains(entity.SourceRef, unsupportedIssueRefs));
            Assert.NotEmpty(sink.Package.Entities);
            totalEntities += result.Summary.EntityCount;
            unsupportedEntities += result.Summary.UnsupportedEntityCount;
        }

        Assert.True(totalEntities >= 250);
        Assert.True(unsupportedEntities >= 10);
    }

    [Fact]
    public async Task Converter_rejects_source_bytes_that_do_not_match_the_request_hash()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write("sample.dxf", ValidDxf);
        var request = Request(new string('a', 64));
        var sink = new DevelopmentCadIrFileSink(request, Path.Combine(fixture.Path, "ir.json"));
        await using var stream = File.OpenRead(input);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new DevelopmentDxfCadConverter().ConvertAsync(request, stream, sink));
    }

    [Fact]
    public async Task Converter_is_deterministic_for_the_same_dxf_bytes()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write("sample.dxf", ValidDxf);
        var hash = await DatasetAuditor.ComputeSha256Async(input);
        var request = Request(hash);
        var firstOutput = Path.Combine(fixture.Path, "first.json");
        var secondOutput = Path.Combine(fixture.Path, "second.json");
        var converter = new DevelopmentDxfCadConverter();

        await using (var firstStream = File.OpenRead(input))
        {
            await converter.ConvertAsync(
                request,
                firstStream,
                new DevelopmentCadIrFileSink(request, firstOutput));
        }
        await using (var secondStream = File.OpenRead(input))
        {
            await converter.ConvertAsync(
                request,
                secondStream,
                new DevelopmentCadIrFileSink(request, secondOutput));
        }

        Assert.Equal(
            await DatasetAuditor.ComputeSha256Async(firstOutput),
            await DatasetAuditor.ComputeSha256Async(secondOutput));
    }

    [Theory]
    [InlineData(1, SpaceCadUnit.Inch, "25.4", "25400")]
    [InlineData(2, SpaceCadUnit.Foot, "304.8", "304800")]
    [InlineData(5, SpaceCadUnit.Centimeter, "10", "10000")]
    [InlineData(6, SpaceCadUnit.Meter, "1000", "1000000")]
    public async Task Converter_normalizes_known_source_units_to_millimeters(
        int unitCode,
        SpaceCadUnit expectedUnit,
        string expectedScale,
        string expectedX)
    {
        using var fixture = new TemporaryDirectory();
        var dxf = ValidDxf.Replace("70\n4\n", $"70\n{unitCode}\n", StringComparison.Ordinal);
        var input = fixture.Write("sample.dxf", dxf);
        var request = Request(await DatasetAuditor.ComputeSha256Async(input));
        var sink = new DevelopmentCadIrFileSink(
            request,
            Path.Combine(fixture.Path, "ir.json"));
        await using var stream = File.OpenRead(input);

        await new DevelopmentDxfCadConverter().ConvertAsync(request, stream, sink);

        Assert.Equal(expectedUnit, sink.Package!.Document.Unit);
        Assert.Equal(decimal.Parse(expectedScale), sink.Package.Document.ScaleToMillimeters);
        Assert.Equal(decimal.Parse(expectedX), sink.Package.Entities[0].Points[1].X);
    }

    private static SpaceCadConversionRequest Request(string sourceSha256) =>
        new(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            sourceSha256,
            SpaceCadSourceFormat.Dxf,
            DevelopmentDxfCadConverter.ConverterId,
            DevelopmentDxfCadConverter.ConverterVersion);

    private static async Task<CadDatasetManifest> LoadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CadDatasetManifest>(
                   stream,
                   CadExperimentJson.Options)
               ?? throw new InvalidDataException("Generated manifest is empty.");
    }

    private const string ValidDxf =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n"
        + "9\n$INSUNITS\n70\n4\n0\nENDSEC\n"
        + "0\nSECTION\n2\nBLOCKS\n0\nENDSEC\n"
        + "0\nSECTION\n2\nENTITIES\n"
        + "0\nLINE\n5\n100\n8\nWALL\n10\n0\n20\n0\n30\n0\n"
        + "11\n1000\n21\n1000\n31\n0\n0\nENDSEC\n0\nEOF\n";
}
