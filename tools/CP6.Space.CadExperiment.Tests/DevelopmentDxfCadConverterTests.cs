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
        long totalSemanticItems = 0;
        long totalSemanticDiagnostics = 0;
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
                request.TenantId,
                inventory,
                mappingProfile);
            Assert.True(mappingPreview.ReadyForSemanticParsing);
            Assert.Equal(
                inventory.Layers.Count + inventory.Blocks.Count,
                mappingPreview.Decisions.Count);
            Assert.Equal(0, mappingPreview.Summary.BlockingCount);
            Assert.Matches("^[0-9a-f]{64}$", mappingPreview.ReuseKeySha256);
            var semanticPreview = SpaceCadSemanticParser.Parse(
                request,
                prepared,
                inventory,
                mappingProfile,
                mappingPreview);
            Assert.Equal(
                prepared.Package.Entities.Count,
                semanticPreview.Summary.SourceEntityCount);
            Assert.Equal(mappingPreview.PreviewSha256, semanticPreview.MappingPreviewSha256);
            Assert.Matches("^[0-9a-f]{64}$", semanticPreview.SemanticPreviewSha256);
            var diagnosticIndex = SpaceCadSemanticDiagnostics.Build(
                request,
                prepared,
                inventory,
                mappingProfile,
                mappingPreview,
                semanticPreview);
            Assert.Equal(semanticPreview.Items.Count, diagnosticIndex.Evidence.Count);
            Assert.Equal(
                mappingPreview.Issues.Count + semanticPreview.Issues.Count,
                diagnosticIndex.Diagnostics.Count);
            Assert.All(
                diagnosticIndex.Diagnostics,
                diagnostic => Assert.Equal(
                    inventory.FloorLogicalId,
                    diagnostic.Location.FloorLogicalId));
            Assert.Matches("^[0-9a-f]{64}$", diagnosticIndex.DiagnosticIndexSha256);
            Assert.NotEmpty(sink.Package.Entities);
            totalEntities += result.Summary.EntityCount;
            unsupportedEntities += result.Summary.UnsupportedEntityCount;
            totalDeclaredLayers += inventory.Summary.LayerCount;
            totalEmptyLayers += inventory.Summary.EmptyLayerCount;
            totalMappingDecisions += mappingPreview.Decisions.Count;
            totalSemanticItems += semanticPreview.Items.Count;
            totalSemanticDiagnostics += diagnosticIndex.Diagnostics.Count;
        }

        Assert.True(totalEntities >= 250);
        Assert.True(unsupportedEntities >= 10);
        Assert.True(totalDeclaredLayers >= 300);
        Assert.True(totalEmptyLayers >= 100);
        Assert.True(totalMappingDecisions >= 320);
        Assert.True(totalSemanticItems >= 100);
        Assert.True(totalSemanticDiagnostics >= 20);
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
    public async Task Ai_feature_command_writes_deterministic_provider_and_local_only_artifacts()
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

        var keyPath = Path.Combine(fixture.Path, "hmac.key");
        await File.WriteAllBytesAsync(
            keyPath,
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var providerPath = Path.Combine(fixture.Path, "provider-input.json");
        var sourceMapPath = Path.Combine(fixture.Path, "source-map.json");
        var arguments = new[]
        {
            "minimize-dev-ai-cad-features",
            "--input", preparedPath,
            "--policy", "StructuredFeatures",
            "--hmac-key-file", keyPath,
            "--tenant-id", "55555555-5555-5555-5555-555555555555",
            "--site-id", "66666666-6666-6666-6666-666666666666",
            "--model-version-id", "77777777-7777-7777-7777-777777777777",
            "--run-id", "88888888-8888-8888-8888-888888888888",
            "--provider-output", providerPath,
            "--source-map-output", sourceMapPath,
        };

        Assert.Equal(0, await Program.Main(arguments));
        var providerJson = await File.ReadAllTextAsync(providerPath);
        await using var sourceMapStream = File.OpenRead(sourceMapPath);
        var sourceMap = await JsonSerializer.DeserializeAsync<
            SpaceAiCadFeatureSourceMapV1>(
            sourceMapStream,
            CadExperimentJson.Options);

        Assert.NotNull(sourceMap);
        Assert.True(sourceMap.IsLocalOnly);
        Assert.Equal(
            sourceMap.ProviderInputSha256,
            await DatasetAuditor.ComputeSha256Async(providerPath));
        Assert.Equal(1, sourceMap.FeatureCount);
        Assert.Equal(1, sourceMap.MappedSourceCount);
        Assert.Single(Assert.Single(sourceMap.Entries).SourceRefs);
        Assert.DoesNotContain(sourceHash, providerJson, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "55555555-5555-5555-5555-555555555555",
            providerJson,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            sourceMap.Entries[0].SourceRefs[0],
            providerJson,
            StringComparison.Ordinal);

        var repeatedProviderPath = Path.Combine(
            fixture.Path,
            "provider-input-repeat.json");
        var repeatedSourceMapPath = Path.Combine(
            fixture.Path,
            "source-map-repeat.json");
        arguments[^3] = repeatedProviderPath;
        arguments[^1] = repeatedSourceMapPath;
        Assert.Equal(0, await Program.Main(arguments));
        Assert.Equal(
            await File.ReadAllBytesAsync(providerPath),
            await File.ReadAllBytesAsync(repeatedProviderPath));
        Assert.Equal(
            await File.ReadAllBytesAsync(sourceMapPath),
            await File.ReadAllBytesAsync(repeatedSourceMapPath));
    }

    [Fact]
    public async Task Ai_provider_command_runs_mock_local_and_deterministic_fallback_without_external_calls()
    {
        using var fixture = new TemporaryDirectory();
        var inputPath = Path.Combine(fixture.Path, "provider-input.json");
        var input = new WarehouseGenerationInput(
            new string('a', 64),
            SpaceAiDataPolicy.StructuredFeatures,
            new WarehouseGenerationLimits(10, 4),
            [
                new WarehouseGenerationFeature(
                    "source-rack",
                    WarehouseCadEntityType.BlockReference,
                    "layer-generic-111111111111111111111111",
                    "block-rack-222222222222222222222222",
                    1,
                    new WarehouseNormalizedBounds(0, 0, 0.5m, 0.5m),
                    0,
                    null,
                    [],
                    [],
                    0),
            ],
            [],
            []);
        await CadExperimentJson.WriteAsync(inputPath, input);

        var mockPath = Path.Combine(fixture.Path, "mock-output.json");
        Assert.Equal(0, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", inputPath,
            "--provider", "mock",
            "--output", mockPath,
        ]));
        var localPath = Path.Combine(fixture.Path, "local-output.json");
        Assert.Equal(0, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", inputPath,
            "--provider", "local",
            "--output", localPath,
        ]));
        var fallbackPath = Path.Combine(fixture.Path, "fallback-output.json");
        Assert.Equal(0, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", inputPath,
            "--provider", "fallback-local",
            "--failure", "timeout",
            "--output", fallbackPath,
        ]));
        var repeatPath = Path.Combine(fixture.Path, "fallback-repeat.json");
        Assert.Equal(0, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", inputPath,
            "--provider", "fallback-local",
            "--failure", "timeout",
            "--output", repeatPath,
        ]));

        var mock = await LoadProviderResultAsync(mockPath);
        var local = await LoadProviderResultAsync(localPath);
        var fallback = await LoadProviderResultAsync(fallbackPath);
        Assert.Equal("cp6-mock-v1", mock.ProviderModel);
        Assert.Equal("cp6-local-heuristic-v1", local.ProviderModel);
        Assert.Equal("cp6-local-heuristic-v1", fallback.ProviderModel);
        Assert.Equal(WarehouseSpaceType.Rack, Assert.Single(local.Suggestions).SuggestedType);
        Assert.Contains(
            fallback.Diagnostics,
            item => item.Code == "AI_PROVIDER_TIMEOUT_FALLBACK");
        Assert.Equal(
            await File.ReadAllBytesAsync(fallbackPath),
            await File.ReadAllBytesAsync(repeatPath));
        var outputs = string.Join(
            '\n',
            await File.ReadAllTextAsync(mockPath),
            await File.ReadAllTextAsync(localPath),
            await File.ReadAllTextAsync(fallbackPath));
        Assert.DoesNotContain("http", outputs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("apiKey", outputs, StringComparison.OrdinalIgnoreCase);

        Assert.Equal(0, await Program.Main(
        [
            "validate-dev-ai-provider-output",
            "--input", inputPath,
            "--provider-output", localPath,
        ]));
        var invalidProviderOutputPath = Path.Combine(
            fixture.Path,
            "invalid-provider-output.json");
        await File.WriteAllTextAsync(
            invalidProviderOutputPath,
            (await File.ReadAllTextAsync(localPath)).Replace(
                "source-rack",
                "source-not-in-input",
                StringComparison.Ordinal));
        Assert.Equal(2, await Program.Main(
        [
            "validate-dev-ai-provider-output",
            "--input", inputPath,
            "--provider-output", invalidProviderOutputPath,
        ]));

        var externalPath = Path.Combine(fixture.Path, "external-output.json");
        Assert.Equal(2, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", inputPath,
            "--provider", "external",
            "--output", externalPath,
        ]));
        Assert.False(File.Exists(externalPath));

        var invalidInputPath = Path.Combine(fixture.Path, "invalid-input.json");
        await File.WriteAllTextAsync(
            invalidInputPath,
            (await File.ReadAllTextAsync(inputPath)).Replace(
                "\"schemaVersion\": \"1.0\"",
                "\"schemaVersion\": \"9.0\"",
                StringComparison.Ordinal));
        var invalidOutputPath = Path.Combine(fixture.Path, "invalid-output.json");
        Assert.Equal(2, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", invalidInputPath,
            "--provider", "local",
            "--output", invalidOutputPath,
        ]));
        Assert.False(File.Exists(invalidOutputPath));
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

        var semanticPath = Path.Combine(fixture.Path, "semantic-preview.json");
        Assert.Equal(0, await Program.Main(
        [
            "parse-dev-semantic", "--prepared", preparedPath,
            "--inventory", inventoryPath,
            "--profile", profilePath,
            "--mapping", previewPath,
            "--output", semanticPath,
        ]));
        await using var semanticStream = File.OpenRead(semanticPath);
        var semantic = await JsonSerializer.DeserializeAsync<SpaceCadSemanticPreviewV1>(
            semanticStream,
            CadExperimentJson.Options);
        Assert.True(semantic!.IsReadOnlyPreview);
        Assert.True(semantic.ReadyForConfirmation);
        Assert.Equal(SpaceCadSemanticTarget.Guide, Assert.Single(semantic.Items).Target);
        SpaceCadSemanticParser.Validate(semantic);

        var synthesisKeyPath = Path.Combine(fixture.Path, "synthesis-hmac.key");
        await File.WriteAllBytesAsync(
            synthesisKeyPath,
            Enumerable.Range(1, 32).Select(value => (byte)value).ToArray());
        var synthesisInputPath = Path.Combine(fixture.Path, "synthesis-input.json");
        var synthesisMapPath = Path.Combine(fixture.Path, "synthesis-map.json");
        Assert.Equal(0, await Program.Main(
        [
            "minimize-dev-ai-cad-features",
            "--input", preparedPath,
            "--policy", "StructuredFeatures",
            "--hmac-key-file", synthesisKeyPath,
            "--tenant-id", "55555555-5555-5555-5555-555555555555",
            "--site-id", "66666666-6666-6666-6666-666666666666",
            "--model-version-id", "77777777-7777-7777-7777-777777777777",
            "--run-id", "88888888-8888-8888-8888-888888888888",
            "--provider-output", synthesisInputPath,
            "--source-map-output", synthesisMapPath,
        ]));
        var synthesisProviderPath = Path.Combine(
            fixture.Path,
            "synthesis-provider.json");
        Assert.Equal(0, await Program.Main(
        [
            "run-dev-ai-provider",
            "--input", synthesisInputPath,
            "--provider", "mock",
            "--output", synthesisProviderPath,
        ]));
        var synthesisPath = Path.Combine(fixture.Path, "synthesis.json");
        Assert.Equal(3, await Program.Main(
        [
            "synthesize-dev-ai-proposals",
            "--input", synthesisInputPath,
            "--source-map", synthesisMapPath,
            "--semantic", semanticPath,
            "--provider-output", synthesisProviderPath,
            "--model-version-id", "77777777-7777-7777-7777-777777777777",
            "--rule-version", "rules-e02-s06-v1",
            "--output", synthesisPath,
        ]));
        await using (var synthesisStream = File.OpenRead(synthesisPath))
        {
            var synthesis = await JsonSerializer.DeserializeAsync<
                WarehouseDraftProposalSetV1>(
                    synthesisStream,
                    CadExperimentJson.Options);
            Assert.NotNull(synthesis);
            Assert.True(synthesis.IsReadOnlyPreview);
            Assert.False(synthesis.DraftWritten);
            Assert.Empty(synthesis.Proposals);
            Assert.Contains(synthesis.Issues, issue =>
                issue.Code == "AI_GEOMETRY_RULE_REQUIRED"
                && issue.Severity == WarehouseProposalIssueSeverity.Blocking);
            _ = WarehouseDraftSynthesizer.Serialize(synthesis);
        }
        var aiBaselineDraftPath = Path.Combine(
            fixture.Path,
            "ai-review-baseline-draft.json");
        var aiBaselinePath = Path.Combine(fixture.Path, "ai-review-baseline.json");
        var aiReviewPath = Path.Combine(fixture.Path, "ai-review-workspace.json");
        var aiReviewPagePath = Path.Combine(fixture.Path, "ai-review-page.json");
        await CadExperimentJson.WriteAsync(
            aiBaselineDraftPath,
            new WarehouseProposalReviewBaselineSnapshotV1(
                WarehouseProposalReviewVersions.SchemaVersion,
                IsReadOnlySnapshot: true,
                IsCompleteFloorProjection: true,
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Guid.Parse("77777777-7777-7777-7777-777777777777"),
                Guid.Parse("44444444-4444-4444-4444-444444444444"),
                ContentRevision: 0,
                ContentHash: null,
                Objects: [],
                SnapshotSha256: string.Empty));
        Assert.Equal(0, await Program.Main(
        [
            "seal-dev-ai-review-baseline",
            "--input", aiBaselineDraftPath,
            "--output", aiBaselinePath,
        ]));
        Assert.Equal(0, await Program.Main(
        [
            "build-dev-ai-review-workspace",
            "--proposals", synthesisPath,
            "--baseline", aiBaselinePath,
            "--output", aiReviewPath,
        ]));
        Assert.Equal(0, await Program.Main(
        [
            "query-dev-ai-review-workspace",
            "--input", aiReviewPath,
            "--cursor-key-file", synthesisKeyPath,
            "--limit", "1",
            "--output", aiReviewPagePath,
        ]));
        var aiReview = await ReadJsonAsync<WarehouseProposalReviewWorkspaceV1>(
            aiReviewPath);
        var aiReviewPage = await ReadJsonAsync<WarehouseProposalReviewPageV1>(
            aiReviewPagePath);
        Assert.True(aiReview.IsReadOnlyWorkspace);
        Assert.False(aiReview.DecisionWritten);
        Assert.False(aiReview.DraftWritten);
        Assert.Empty(aiReview.Items);
        Assert.Equal(0, aiReviewPage.TotalCount);
        Assert.Null(aiReviewPage.NextCursor);

        var diagnosticPath = Path.Combine(fixture.Path, "semantic-diagnostics.json");
        Assert.Equal(0, await Program.Main(
        [
            "build-dev-semantic-diagnostics", "--prepared", preparedPath,
            "--inventory", inventoryPath,
            "--profile", profilePath,
            "--mapping", previewPath,
            "--semantic", semanticPath,
            "--output", diagnosticPath,
        ]));
        var evidencePath = Path.Combine(fixture.Path, "semantic-evidence.json");
        Assert.Equal(0, await Program.Main(
        [
            "query-dev-semantic-diagnostics", "--input", diagnosticPath,
            "--kind", "evidence", "--band", "High",
            "--output", evidencePath,
        ]));
        await using (var diagnosticStream = File.OpenRead(diagnosticPath))
        {
            var diagnostics = await JsonSerializer.DeserializeAsync<
                SpaceCadSemanticDiagnosticIndexV1>(
                diagnosticStream,
                CadExperimentJson.Options);
            Assert.Single(diagnostics!.Evidence);
            Assert.Empty(diagnostics.Diagnostics);
            SpaceCadSemanticDiagnostics.Validate(diagnostics);
        }
        await using var evidenceStream = File.OpenRead(evidencePath);
        var evidence = await JsonSerializer.DeserializeAsync<
            SpaceCadSemanticPageV1<SpaceCadSemanticEvidenceV1>>(
            evidenceStream,
            CadExperimentJson.Options);
        Assert.Equal(1, evidence!.TotalCount);
        Assert.Equal(SpaceCadConfidenceBand.High, Assert.Single(evidence.Items).ConfidenceBand);

        var excelProfilePath = Path.Combine(fixture.Path, "excel-profile.json");
        var workbookPath = Path.Combine(fixture.Path, "excel-workbook.json");
        var editorPath = Path.Combine(fixture.Path, "editor-snapshot.json");
        var matchPath = Path.Combine(fixture.Path, "excel-cad-match.json");
        var unmatchedPath = Path.Combine(fixture.Path, "unmatched.json");
        var reviewPath = Path.Combine(fixture.Path, "cad-review-workspace.json");
        var reviewQueryPath = Path.Combine(fixture.Path, "cad-review-unmatched.json");
        var modelVersionId = Guid.Parse("aaaaaaaa-1111-2222-3333-444444444444");
        await CadExperimentJson.WriteAsync(excelProfilePath, ExcelProfile());
        await CadExperimentJson.WriteAsync(workbookPath, ExcelWorkbook());
        await CadExperimentJson.WriteAsync(
            editorPath,
            SpaceExcelCadMatching.SealEditorSnapshot(
                semantic.TenantId,
                modelVersionId,
                semantic.FloorLogicalId,
                semantic.FloorCode,
                0,
                null,
                []));
        Assert.Equal(3, await Program.Main(
        [
            "match-dev-excel-cad",
            "--mapping", excelProfilePath,
            "--workbook", workbookPath,
            "--semantic", semanticPath,
            "--diagnostics", diagnosticPath,
            "--editor", editorPath,
            "--tenant-id", "55555555-5555-5555-5555-555555555555",
            "--model-version-id", modelVersionId.ToString(),
            "--excel-source-id", "bbbbbbbb-1111-2222-3333-444444444444",
            "--preflight-job-id", "cccccccc-1111-2222-3333-444444444444",
            "--output", matchPath,
        ]));
        Assert.Equal(0, await Program.Main(
        [
            "query-dev-excel-cad-match",
            "--input", matchPath,
            "--disposition", "Unmatched",
            "--output", unmatchedPath,
        ]));
        await using (var matchStream = File.OpenRead(matchPath))
        {
            var match = await JsonSerializer.DeserializeAsync<SpaceExcelCadMatchPreviewV1>(
                matchStream,
                CadExperimentJson.Options);
            SpaceExcelCadMatching.Validate(match!);
            Assert.Equal(1, match!.Summary.UnmatchedCount);
            Assert.False(match.CanConfirm);
        }
        await using var unmatchedStream = File.OpenRead(unmatchedPath);
        var unmatched = await JsonSerializer.DeserializeAsync<SpaceExcelCadMatchPageV1>(
            unmatchedStream,
            CadExperimentJson.Options);
        Assert.Equal(1, unmatched!.TotalCount);
        Assert.Equal("R-CLI-001", Assert.Single(unmatched.Items).Values.RackCode);

        Assert.Equal(0, await Program.Main(
        [
            "build-dev-cad-review-workspace",
            "--diagnostics", diagnosticPath,
            "--editor", editorPath,
            "--matches", matchPath,
            "--output", reviewPath,
        ]));
        Assert.Equal(0, await Program.Main(
        [
            "query-dev-cad-review-workspace",
            "--input", reviewPath,
            "--review-kind", "ExcelUnmatched",
            "--search", "R-CLI-001",
            "--output", reviewQueryPath,
        ]));
        await using (var reviewStream = File.OpenRead(reviewPath))
        {
            var review =
                await JsonSerializer.DeserializeAsync<SpaceCadReviewWorkspaceV1>(
                    reviewStream,
                    CadExperimentJson.Options);
            SpaceCadReviewWorkspace.Validate(review!);
            Assert.Equal(1, review!.Summary.ExcelReviewCount);
        }
        await using var reviewQueryStream = File.OpenRead(reviewQueryPath);
        var reviewQuery =
            await JsonSerializer.DeserializeAsync<SpaceCadReviewWorkspacePageV1>(
                reviewQueryStream,
                CadExperimentJson.Options);
        Assert.Equal(1, reviewQuery!.TotalCount);
        Assert.Equal(
            SpaceCadReviewItemKind.ExcelUnmatched,
            Assert.Single(reviewQuery.Items).Kind);
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
                    ConfidenceWeight: 0.9m,
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
                    ConfidenceWeight: 0.9m,
                    IsRequired: false),
            ]);

    private static SpaceExcelMappingProfileDto ExcelProfile()
    {
        var definition = new SpaceExcelMappingDefinitionDto(
            SpaceExcelTargetCatalog.MappingSchemaVersion,
            "Ignore",
            "Reject",
            "Reject",
            [
                new SpaceExcelSheetMappingDto(
                    "Racks",
                    "Racks",
                    "Exact",
                    1,
                    2,
                    SpaceExcelTargetCatalog.ForSheet("Racks")
                        .Select(field => new SpaceExcelColumnMappingDto(
                            field.Field,
                            field.Field,
                            null,
                            field.DataType,
                            null,
                            null,
                            field.IsBusinessKey,
                            field.ReferenceTarget,
                            [],
                            null))
                        .ToArray()),
            ]);
        return new SpaceExcelMappingProfileDto(
            Guid.Parse("ffffffff-1111-2222-3333-444444444444"),
            "CLI Excel profile",
            "Tenant",
            1,
            false,
            new string('a', 64),
            definition,
            null,
            null,
            null,
            null,
            null);
    }

    private static SpaceExcelWorkbookData ExcelWorkbook()
    {
        var fields = SpaceExcelTargetCatalog.ForSheet("Racks");
        var values = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["FloorCode"] = "F01",
            ["ZoneCode"] = "Z1",
            ["RackCode"] = "R-CLI-001",
            ["XMm"] = "1000",
            ["YMm"] = "2000",
            ["ZMm"] = "0",
            ["WidthMm"] = "1000",
            ["DepthMm"] = "1200",
            ["HeightMm"] = "5000",
            ["RotationZDeg"] = "0",
            ["LifecycleStatus"] = "Active",
        };
        var header = new SpaceExcelWorkbookRow(
            1,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ExcelColumnName(index + 1),
                    field.Field,
                    false))
                .ToDictionary(cell => cell.ColumnIndex));
        var row = new SpaceExcelWorkbookRow(
            2,
            fields.Select((field, index) => new SpaceExcelWorkbookCell(
                    index + 1,
                    ExcelColumnName(index + 1),
                    values.GetValueOrDefault(field.Field),
                    false))
                .ToDictionary(cell => cell.ColumnIndex));
        return new SpaceExcelWorkbookData(
            [new SpaceExcelWorkbookSheet("Racks", [header, row])]);
    }

    private static string ExcelColumnName(int index)
    {
        var value = index;
        var result = string.Empty;
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static async Task<WarehouseGenerationResult> LoadProviderResultAsync(
        string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<WarehouseGenerationResult>(
                   stream,
                   CadExperimentJson.Options)
               ?? throw new InvalidDataException(
                   "The development provider output is empty.");
    }

    private static async Task<T> ReadJsonAsync<T>(string path)
    {
        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(
                   stream,
                   CadExperimentJson.Options)
               ?? throw new InvalidDataException(
                   $"The development artifact '{typeof(T).Name}' is empty.");
    }

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
