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
        long totalDeclaredLayers = 0;
        long totalEmptyLayers = 0;
        long totalMappingDecisions = 0;
        SpaceCadMappingProfileDraftV1 standardMappingDraft;
        await using (var mappingStream = File.OpenRead(RepositoryFile(
                         "docs",
                         "space",
                         "contracts",
                         "cad",
                         "v1",
                         "examples",
                         "development-mapping-profile-draft.json")))
        {
            standardMappingDraft = await JsonSerializer.DeserializeAsync<
                                       SpaceCadMappingProfileDraftV1>(
                                       mappingStream,
                                       CadExperimentJson.Options)
                                   ?? throw new InvalidDataException(
                                       "The standard development mapping draft is empty.");
        }
        var mappingProfile = SpaceCadMapping.Seal(standardMappingDraft);

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
            var coordinateAnalysis = SpaceCadCoordinatePreparation.Analyze(
                request,
                sink.Package);
            Assert.True(coordinateAnalysis.IsSuggestedExtentPlausible);
            var prepared = SpaceCadCoordinatePreparation.Prepare(
                request,
                sink.Package,
                CoordinateConfirmation(request.SourceSha256));
            Assert.True(prepared.ReadyForParsing);
            Assert.Matches("^[0-9a-f]{64}$", prepared.Metadata.TransformSha256);
            Assert.Equal(
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                prepared.Metadata.TargetFloor.CoordinateSystem);
            var inventory = SpaceCadInventory.Build(request, prepared);
            Assert.Equal(sink.Package.Layers.Count, inventory.Layers.Count);
            Assert.Equal(sink.Package.Entities.Count, inventory.Summary.EntityCount);
            Assert.Equal(
                sink.Package.Entities.Count(entity => entity.Type == SpaceCadIrEntityType.BlockReference),
                inventory.Summary.BlockReferenceCount);
            Assert.All(sink.Package.Layers, layer =>
            {
                Assert.NotNull(layer.Color);
                Assert.Equal("CONTINUOUS", layer.LineType);
            });
            Assert.Contains(inventory.Layers, layer => layer.EntityCount == 0);
            Assert.DoesNotContain(inventory.Blocks, block => !block.IsDefined);
            Assert.Matches("^[0-9a-f]{64}$", inventory.InventorySha256);
            var mappingPreview = SpaceCadMapping.Preview(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                inventory,
                mappingProfile);
            Assert.True(mappingPreview.ReadyForSemanticParsing);
            Assert.Equal(
                inventory.Layers.Count + inventory.Blocks.Count,
                mappingPreview.Decisions.Count);
            Assert.Equal(0, mappingPreview.Summary.BlockingCount);
            Assert.Matches("^[0-9a-f]{64}$", mappingPreview.ReuseKeySha256);
            Assert.NotEmpty(sink.Package.Entities);
            totalEntities += result.Summary.EntityCount;
            unsupportedEntities += result.Summary.UnsupportedEntityCount;
            totalDeclaredLayers += inventory.Summary.LayerCount;
            totalEmptyLayers += inventory.Summary.EmptyLayerCount;
            totalMappingDecisions += mappingPreview.Decisions.Count;
        }

        Assert.True(totalEntities >= 250);
        Assert.True(unsupportedEntities >= 10);
        Assert.True(totalDeclaredLayers >= 300);
        Assert.True(totalEmptyLayers >= 100);
        Assert.True(totalMappingDecisions >= 320);
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

    [Fact]
    public async Task Coordinate_command_writes_a_ready_source_bound_preparation_artifact()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write("sample.dxf", ValidDxf);
        var sourceHash = await DatasetAuditor.ComputeSha256Async(input);
        var request = Request(sourceHash);
        var irPath = Path.Combine(fixture.Path, "sample.cad-ir.json");
        await using (var stream = File.OpenRead(input))
        {
            await new DevelopmentDxfCadConverter().ConvertAsync(
                request,
                stream,
                new DevelopmentCadIrFileSink(request, irPath));
        }
        var confirmationPath = Path.Combine(fixture.Path, "confirmation.json");
        await CadExperimentJson.WriteAsync(
            confirmationPath,
            CoordinateConfirmation(sourceHash));
        var outputPath = Path.Combine(fixture.Path, "prepared.json");

        var exitCode = await Program.Main(
        [
            "prepare-dev-coordinate",
            "--input", irPath,
            "--confirmation", confirmationPath,
            "--output", outputPath,
        ]);

        Assert.Equal(0, exitCode);
        await using var output = File.OpenRead(outputPath);
        var prepared = await JsonSerializer.DeserializeAsync<SpaceCadCoordinatePreparationV1>(
            output,
            CadExperimentJson.Options);
        Assert.True(prepared!.ReadyForParsing);
        Assert.Equal(sourceHash, prepared.Metadata.SourceSha256);
        Assert.Equal("F01", prepared.Metadata.TargetFloor.FloorCode);
    }

    [Fact]
    public async Task Converter_preserves_declared_layer_color_line_type_and_visibility()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write("layer.dxf", LayerMetadataDxf);
        var request = Request(await DatasetAuditor.ComputeSha256Async(input));
        var sink = new DevelopmentCadIrFileSink(
            request,
            Path.Combine(fixture.Path, "layer.json"));
        await using var stream = File.OpenRead(input);

        await new DevelopmentDxfCadConverter().ConvertAsync(request, stream, sink);

        var layer = Assert.Single(sink.Package!.Layers);
        Assert.Equal("WALL", layer.Name);
        Assert.Equal("ACI:2", layer.Color);
        Assert.Equal("DASHED", layer.LineType);
        Assert.False(layer.IsVisible);
        Assert.Equal(1, layer.EntityCount);
        Assert.DoesNotContain(
            sink.Package.Issues,
            issue => issue.Code == "SPACE_CAD_LAYER_METADATA_MISSING");
    }

    [Fact]
    public async Task Inventory_commands_build_and_query_a_coordinate_bound_artifact()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write("sample.dxf", ValidDxf);
        var sourceHash = await DatasetAuditor.ComputeSha256Async(input);
        var request = Request(sourceHash);
        var irPath = Path.Combine(fixture.Path, "sample.cad-ir.json");
        await using (var stream = File.OpenRead(input))
        {
            await new DevelopmentDxfCadConverter().ConvertAsync(
                request,
                stream,
                new DevelopmentCadIrFileSink(request, irPath));
        }
        var confirmationPath = Path.Combine(fixture.Path, "confirmation.json");
        await CadExperimentJson.WriteAsync(
            confirmationPath,
            CoordinateConfirmation(sourceHash));
        var preparedPath = Path.Combine(fixture.Path, "prepared.json");
        Assert.Equal(0, await Program.Main(
        [
            "prepare-dev-coordinate",
            "--input", irPath,
            "--confirmation", confirmationPath,
            "--output", preparedPath,
        ]));

        var inventoryPath = Path.Combine(fixture.Path, "inventory.json");
        Assert.Equal(0, await Program.Main(
        [
            "build-dev-inventory",
            "--input", preparedPath,
            "--output", inventoryPath,
        ]));
        var queryPath = Path.Combine(fixture.Path, "layers.json");
        Assert.Equal(0, await Program.Main(
        [
            "query-dev-inventory",
            "--input", inventoryPath,
            "--kind", "layer",
            "--search", "wall",
            "--exclude-empty",
            "--limit", "10",
            "--output", queryPath,
        ]));

        await using (var inventoryStream = File.OpenRead(inventoryPath))
        {
            var inventory = await JsonSerializer.DeserializeAsync<SpaceCadInventoryV1>(
                inventoryStream,
                CadExperimentJson.Options);
            Assert.Equal(sourceHash, inventory!.SourceSha256);
            SpaceCadInventory.Validate(inventory);
        }
        await using var queryStream = File.OpenRead(queryPath);
        var page = await JsonSerializer.DeserializeAsync<
            SpaceCadInventoryPageV1<SpaceCadLayerInventoryV1>>(
            queryStream,
            CadExperimentJson.Options);
        Assert.Equal(1, page!.TotalCount);
        Assert.Equal("WALL", Assert.Single(page.Items).LayerId);
    }

    [Fact]
    public async Task Mapping_commands_seal_a_profile_and_write_a_ready_preview()
    {
        using var fixture = new TemporaryDirectory();
        var input = fixture.Write("sample.dxf", ValidDxf);
        var sourceHash = await DatasetAuditor.ComputeSha256Async(input);
        var request = Request(sourceHash);
        var irPath = Path.Combine(fixture.Path, "sample.cad-ir.json");
        await using (var stream = File.OpenRead(input))
        {
            await new DevelopmentDxfCadConverter().ConvertAsync(
                request,
                stream,
                new DevelopmentCadIrFileSink(request, irPath));
        }
        var confirmationPath = Path.Combine(fixture.Path, "confirmation.json");
        await CadExperimentJson.WriteAsync(confirmationPath, CoordinateConfirmation(sourceHash));
        var preparedPath = Path.Combine(fixture.Path, "prepared.json");
        Assert.Equal(0, await Program.Main(
        [
            "prepare-dev-coordinate", "--input", irPath,
            "--confirmation", confirmationPath, "--output", preparedPath,
        ]));
        var inventoryPath = Path.Combine(fixture.Path, "inventory.json");
        Assert.Equal(0, await Program.Main(
        [
            "build-dev-inventory", "--input", preparedPath, "--output", inventoryPath,
        ]));

        var draftPath = Path.Combine(fixture.Path, "mapping-draft.json");
        await CadExperimentJson.WriteAsync(draftPath, MappingDraft());
        var profilePath = Path.Combine(fixture.Path, "mapping-profile.json");
        Assert.Equal(0, await Program.Main(
        [
            "seal-dev-mapping-profile", "--input", draftPath, "--output", profilePath,
        ]));
        var previewPath = Path.Combine(fixture.Path, "mapping-preview.json");
        Assert.Equal(0, await Program.Main(
        [
            "preview-dev-mapping", "--inventory", inventoryPath,
            "--profile", profilePath,
            "--tenant-id", "55555555-5555-5555-5555-555555555555",
            "--output", previewPath,
        ]));

        await using var previewStream = File.OpenRead(previewPath);
        var preview = await JsonSerializer.DeserializeAsync<SpaceCadMappingPreviewV1>(
            previewStream,
            CadExperimentJson.Options);
        Assert.True(preview!.ReadyForSemanticParsing);
        Assert.Equal(1, preview.Summary.MappedLayerCount);
        Assert.Equal(0, preview.Summary.BlockingCount);
        SpaceCadMapping.ValidatePreview(preview);
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

    private static SpaceCadCoordinateConfirmationV1 CoordinateConfirmation(
        string sourceSha256) =>
        new(
            sourceSha256,
            UnitConfirmed: true,
            SpaceCadUnit.Millimeter,
            new SpaceCadPointV1(0, 0),
            new SpaceCadMillimeterPointV1(0, 0),
            RotationZDegrees: 0,
            new SpaceCadFloorAssignmentV1(
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                "F01",
                1,
                0,
                SpaceCadCoordinateVersions.TargetCoordinateSystem,
                new SpaceCadBoundsV1(-1_000_000, -1_000_000, 1_000_000, 1_000_000)));

    private static async Task<CadDatasetManifest> LoadManifestAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<CadDatasetManifest>(
                   stream,
                   CadExperimentJson.Options)
               ?? throw new InvalidDataException("Generated manifest is empty.");
    }

    private static SpaceCadMappingProfileDraftV1 MappingDraft() =>
        new(
            SpaceCadMappingVersions.SchemaVersion,
            Guid.Parse("88888888-8888-8888-8888-888888888888"),
            Version: 1,
            "Development catch-all mapping",
            SpaceCadMappingScope.System,
            TenantId: null,
            IsEnabled: true,
            BasedOnProfileId: null,
            BasedOnVersion: null,
            [
                new SpaceCadMappingRuleV1(
                    "BLOCK-ALL",
                    1,
                    SpaceCadMappingSourceKind.Block,
                    SpaceCadMappingMatchKind.Regex,
                    ".*",
                    AttributeName: null,
                    AttributeMatchKind: null,
                    AttributePattern: null,
                    SpaceCadSemanticTarget.Rack,
                    TargetSubtype: null,
                    SpaceCadGeometryRule.InsertionPoint,
                    DefaultHeightMillimeters: null,
                    DefaultThicknessMillimeters: null,
                    ConfidenceWeight: 0.5m,
                    IsRequired: false),
                new SpaceCadMappingRuleV1(
                    "LAYER-ALL",
                    1,
                    SpaceCadMappingSourceKind.Layer,
                    SpaceCadMappingMatchKind.Regex,
                    ".*",
                    AttributeName: null,
                    AttributeMatchKind: null,
                    AttributePattern: null,
                    SpaceCadSemanticTarget.Guide,
                    TargetSubtype: null,
                    SpaceCadGeometryRule.DirectGeometry,
                    DefaultHeightMillimeters: null,
                    DefaultThicknessMillimeters: null,
                    ConfidenceWeight: 0.5m,
                    IsRequired: false),
            ]);

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(
            $"Repository file '{Path.Combine(segments)}' was not found.");
    }

    private const string ValidDxf =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n"
        + "9\n$INSUNITS\n70\n4\n0\nENDSEC\n"
        + "0\nSECTION\n2\nBLOCKS\n0\nENDSEC\n"
        + "0\nSECTION\n2\nENTITIES\n"
        + "0\nLINE\n5\n100\n8\nWALL\n10\n0\n20\n0\n30\n0\n"
        + "11\n1000\n21\n1000\n31\n0\n0\nENDSEC\n0\nEOF\n";

    private const string LayerMetadataDxf =
        "0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1032\n"
        + "9\n$INSUNITS\n70\n4\n0\nENDSEC\n"
        + "0\nSECTION\n2\nTABLES\n0\nTABLE\n2\nLAYER\n70\n1\n"
        + "0\nLAYER\n2\nWALL\n70\n0\n62\n-2\n6\nDASHED\n"
        + "0\nENDTAB\n0\nENDSEC\n"
        + "0\nSECTION\n2\nBLOCKS\n0\nENDSEC\n"
        + "0\nSECTION\n2\nENTITIES\n"
        + "0\nLINE\n5\n100\n8\nWALL\n10\n0\n20\n0\n30\n0\n"
        + "11\n1000\n21\n1000\n31\n0\n0\nENDSEC\n0\nEOF\n";
}
