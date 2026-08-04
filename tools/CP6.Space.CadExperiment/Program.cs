using System.Text.Json;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadExperiment;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var commandLine = new CommandLine(args);
            return commandLine.Command switch
            {
                "audit" => await AuditAsync(commandLine, cancellation.Token),
                "preflight" => await PreflightAsync(commandLine, cancellation.Token),
                "generate-stress" => await GenerateStressAsync(commandLine, cancellation.Token),
                "generate-dev-corpus" => await GenerateDevelopmentCorpusAsync(
                    commandLine,
                    cancellation.Token),
                "convert-dev-ir" => await ConvertDevelopmentIrAsync(
                    commandLine,
                    cancellation.Token),
                "prepare-dev-coordinate" => await PrepareDevelopmentCoordinateAsync(
                    commandLine,
                    cancellation.Token),
                "build-dev-inventory" => await BuildDevelopmentInventoryAsync(
                    commandLine,
                    cancellation.Token),
                "query-dev-inventory" => await QueryDevelopmentInventoryAsync(
                    commandLine,
                    cancellation.Token),
                "seal-dev-mapping-profile" => await SealDevelopmentMappingProfileAsync(
                    commandLine,
                    cancellation.Token),
                "preview-dev-mapping" => await PreviewDevelopmentMappingAsync(
                    commandLine,
                    cancellation.Token),
                "parse-dev-semantic" => await ParseDevelopmentSemanticAsync(
                    commandLine,
                    cancellation.Token),
                "build-dev-semantic-diagnostics" =>
                    await BuildDevelopmentSemanticDiagnosticsAsync(
                        commandLine,
                        cancellation.Token),
                "query-dev-semantic-diagnostics" =>
                    await QueryDevelopmentSemanticDiagnosticsAsync(
                        commandLine,
                        cancellation.Token),
                "run" => await RunAsync(commandLine, cancellation.Token),
                "inspect" => await ProbeAdapterAsync(commandLine, cancellation.Token),
                "probe-adapter" => await ProbeAdapterAsync(commandLine, cancellation.Token),
                "fixture-crash-adapter" => 17,
                "fixture-timeout-adapter" => await FixtureTimeoutAsync(cancellation.Token),
                _ => throw new ArgumentException($"Unknown command '{commandLine.Command}'.")
            };
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled.");
            return 130;
        }
        catch (Exception exception) when (
            exception is ArgumentException
                or InvalidDataException
                or InvalidOperationException
                or IOException
                or UnauthorizedAccessException
                or JsonException)
        {
            Console.Error.WriteLine(exception.Message);
            PrintUsage();
            return 2;
        }
    }

    private static async Task<int> PreflightAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var report = await CadTrialPreflight.AuditAsync(
            commandLine.Required("--config"),
            cancellationToken);
        var output = commandLine.Optional("--output");
        if (output is not null)
        {
            await CadExperimentJson.WriteAsync(output, report, cancellationToken);
        }

        Console.WriteLine(JsonSerializer.Serialize(report, CadExperimentJson.Options));
        return report.Passed ? 0 : 4;
    }

    private static async Task<int> AuditAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var report = await DatasetAuditor.AuditAsync(
            commandLine.Required("--manifest"),
            commandLine.Optional("--stress-50mb"),
            commandLine.Optional("--stress-million"),
            cancellationToken);
        var output = commandLine.Optional("--output");
        if (output is not null)
        {
            await CadExperimentJson.WriteAsync(output, report, cancellationToken);
        }

        Console.WriteLine(JsonSerializer.Serialize(report, CadExperimentJson.Options));
        if (!report.IntegrityPassed)
        {
            return 1;
        }

        return commandLine.HasFlag("--require-e02-ready") && !report.E02ReadinessPassed
            ? 3
            : 0;
    }

    private static async Task<int> GenerateStressAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var report = await StressDxfGenerator.GenerateAsync(
            commandLine.Required("--kind"),
            commandLine.Required("--output"),
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(report, CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> GenerateDevelopmentCorpusAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var report = await DevelopmentDxfCorpusGenerator.GenerateAsync(
            commandLine.Required("--output"),
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(report, CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> ConvertDevelopmentIrAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        if (!Path.GetExtension(input).Equals(".dxf", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The development CAD IR converter accepts .dxf files only.");
        }
        var output = Path.GetFullPath(commandLine.Required("--output"));
        var sourceHash = await DatasetAuditor.ComputeSha256Async(input, cancellationToken);
        var request = new CP6.Space.Application.SpaceCadConversionRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            sourceHash,
            CP6.Space.Contracts.SpaceCadSourceFormat.Dxf,
            DevelopmentDxfCadConverter.ConverterId,
            DevelopmentDxfCadConverter.ConverterVersion);
        await using var source = File.OpenRead(input);
        var sink = new DevelopmentCadIrFileSink(request, output);
        var converter = new DevelopmentDxfCadConverter();
        var result = await converter.ConvertAsync(
            request,
            source,
            sink,
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(result, CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> PrepareDevelopmentCoordinateAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        var confirmationPath = Path.GetFullPath(commandLine.Required("--confirmation"));
        var output = Path.GetFullPath(commandLine.Required("--output"));
        SpaceCadIrPackageV1 package;
        SpaceCadCoordinateConfirmationV1 confirmation;
        await using (var inputStream = File.OpenRead(input))
        {
            package = await JsonSerializer.DeserializeAsync<SpaceCadIrPackageV1>(
                          inputStream,
                          CadExperimentJson.Options,
                          cancellationToken)
                      ?? throw new InvalidDataException("The CAD IR package is empty.");
        }
        await using (var confirmationStream = File.OpenRead(confirmationPath))
        {
            confirmation = await JsonSerializer.DeserializeAsync<SpaceCadCoordinateConfirmationV1>(
                               confirmationStream,
                               CadExperimentJson.Options,
                               cancellationToken)
                           ?? throw new InvalidDataException(
                               "The CAD coordinate confirmation is empty.");
        }

        var request = new SpaceCadConversionRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            package.Document.SourceSha256,
            package.Document.SourceFormat,
            package.Document.ConverterId,
            package.Document.ConverterVersion);
        var analysis = SpaceCadCoordinatePreparation.Analyze(request, package);
        var prepared = SpaceCadCoordinatePreparation.Prepare(
            request,
            package,
            confirmation);
        await CadExperimentJson.WriteAsync(output, prepared, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                analysis.SuggestedUnit,
                analysis.SuggestedScaleToMillimeters,
                analysis.IsSuggestedExtentPlausible,
                prepared.ReadyForParsing,
                prepared.Metadata.TargetFloor.FloorLogicalId,
                prepared.Metadata.TargetFloor.FloorCode,
                prepared.Metadata.TransformSha256,
                prepared.Metadata.PreparedBounds,
                IssueCount = prepared.Issues.Count,
            },
            CadExperimentJson.Options));
        return prepared.ReadyForParsing ? 0 : 3;
    }

    private static async Task<int> BuildDevelopmentInventoryAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        var output = Path.GetFullPath(commandLine.Required("--output"));
        SpaceCadCoordinatePreparationV1 preparation;
        await using (var inputStream = File.OpenRead(input))
        {
            preparation = await JsonSerializer.DeserializeAsync<SpaceCadCoordinatePreparationV1>(
                              inputStream,
                              CadExperimentJson.Options,
                              cancellationToken)
                          ?? throw new InvalidDataException(
                              "The prepared CAD IR package is empty.");
        }

        var package = preparation.Package;
        var request = new SpaceCadConversionRequest(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            package.Document.SourceSha256,
            package.Document.SourceFormat,
            package.Document.ConverterId,
            package.Document.ConverterVersion);
        var inventory = SpaceCadInventory.Build(request, preparation);
        await CadExperimentJson.WriteAsync(output, inventory, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                inventory.SourceSha256,
                inventory.CoordinateTransformSha256,
                inventory.FloorLogicalId,
                inventory.FloorCode,
                inventory.InventorySha256,
                inventory.Summary,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> QueryDevelopmentInventoryAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        SpaceCadInventoryV1 inventory;
        await using (var inputStream = File.OpenRead(input))
        {
            inventory = await JsonSerializer.DeserializeAsync<SpaceCadInventoryV1>(
                            inputStream,
                            CadExperimentJson.Options,
                            cancellationToken)
                        ?? throw new InvalidDataException("The CAD inventory is empty.");
        }
        SpaceCadInventory.Validate(inventory);

        var offset = commandLine.Integer("--offset", 0);
        var limit = commandLine.Integer(
            "--limit",
            SpaceCadInventoryVersions.DefaultPageSize);
        object page = commandLine.Required("--kind").ToLowerInvariant() switch
        {
            "layer" or "layers" => SpaceCadInventory.QueryLayers(
                inventory,
                new SpaceCadLayerInventoryQueryV1(
                    commandLine.Optional("--search"),
                    OptionalBoolean(commandLine, "--visible"),
                    OptionalEnum<SpaceCadIrEntityType>(commandLine, "--entity-type"),
                    IncludeEmpty: !commandLine.HasFlag("--exclude-empty"),
                    offset,
                    limit)),
            "block" or "blocks" => SpaceCadInventory.QueryBlocks(
                inventory,
                new SpaceCadBlockInventoryQueryV1(
                    commandLine.Optional("--search"),
                    OptionalBoolean(commandLine, "--external"),
                    commandLine.Optional("--attribute"),
                    offset,
                    limit)),
            "reference" or "references" => SpaceCadInventory.QueryBlockReferences(
                inventory,
                new SpaceCadBlockReferenceInventoryQueryV1(
                    commandLine.Optional("--layer"),
                    commandLine.Optional("--block"),
                    commandLine.Optional("--attribute"),
                    commandLine.Optional("--value"),
                    offset,
                    limit)),
            var kind => throw new ArgumentException(
                $"Unknown CAD inventory query kind '{kind}'.")
        };

        var output = commandLine.Optional("--output");
        if (output is not null)
            await CadExperimentJson.WriteAsync(output, page, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(page, CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> SealDevelopmentMappingProfileAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        var output = Path.GetFullPath(commandLine.Required("--output"));
        SpaceCadMappingProfileDraftV1 draft;
        await using (var inputStream = File.OpenRead(input))
        {
            draft = await JsonSerializer.DeserializeAsync<SpaceCadMappingProfileDraftV1>(
                        inputStream,
                        CadExperimentJson.Options,
                        cancellationToken)
                    ?? throw new InvalidDataException("The CAD mapping profile draft is empty.");
        }
        var profile = SpaceCadMapping.Seal(draft);
        await CadExperimentJson.WriteAsync(output, profile, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                profile.ProfileId,
                profile.Version,
                profile.Scope,
                profile.TenantId,
                RuleCount = profile.Rules.Count,
                profile.DefinitionSha256,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> PreviewDevelopmentMappingAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var inventoryPath = Path.GetFullPath(commandLine.Required("--inventory"));
        var profilePath = Path.GetFullPath(commandLine.Required("--profile"));
        var output = Path.GetFullPath(commandLine.Required("--output"));
        var tenantId = Guid.TryParse(commandLine.Required("--tenant-id"), out var parsedTenantId)
                       && parsedTenantId != Guid.Empty
            ? parsedTenantId
            : throw new ArgumentException("Option '--tenant-id' must be a non-empty GUID.");
        SpaceCadInventoryV1 inventory;
        SpaceCadMappingProfileV1 profile;
        await using (var inventoryStream = File.OpenRead(inventoryPath))
        {
            inventory = await JsonSerializer.DeserializeAsync<SpaceCadInventoryV1>(
                            inventoryStream,
                            CadExperimentJson.Options,
                            cancellationToken)
                        ?? throw new InvalidDataException("The CAD inventory is empty.");
        }
        await using (var profileStream = File.OpenRead(profilePath))
        {
            profile = await JsonSerializer.DeserializeAsync<SpaceCadMappingProfileV1>(
                          profileStream,
                          CadExperimentJson.Options,
                          cancellationToken)
                      ?? throw new InvalidDataException("The CAD mapping profile is empty.");
        }
        IReadOnlyList<SpaceCadLayerMappingOverrideV1> overrides = [];
        if (commandLine.Optional("--overrides") is { } overridePath)
        {
            await using var overrideStream = File.OpenRead(Path.GetFullPath(overridePath));
            overrides = await JsonSerializer.DeserializeAsync<SpaceCadLayerMappingOverrideV1[]>(
                            overrideStream,
                            CadExperimentJson.Options,
                            cancellationToken)
                        ?? throw new InvalidDataException("The CAD layer override list is empty.");
        }

        var preview = SpaceCadMapping.Preview(tenantId, inventory, profile, overrides);
        await CadExperimentJson.WriteAsync(output, preview, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                preview.ProfileId,
                preview.ProfileVersion,
                preview.ProfileDefinitionSha256,
                preview.SourceSha256,
                preview.InventorySha256,
                preview.SourceStructureSha256,
                preview.ReuseKeySha256,
                preview.PreviewSha256,
                preview.ReadyForSemanticParsing,
                preview.Summary,
            },
            CadExperimentJson.Options));
        return preview.ReadyForSemanticParsing ? 0 : 3;
    }

    private static async Task<int> ParseDevelopmentSemanticAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var preparedPath = Path.GetFullPath(commandLine.Required("--prepared"));
        var inventoryPath = Path.GetFullPath(commandLine.Required("--inventory"));
        var profilePath = Path.GetFullPath(commandLine.Required("--profile"));
        var mappingPath = Path.GetFullPath(commandLine.Required("--mapping"));
        var output = Path.GetFullPath(commandLine.Required("--output"));
        SpaceCadCoordinatePreparationV1 preparation;
        SpaceCadInventoryV1 inventory;
        SpaceCadMappingProfileV1 profile;
        SpaceCadMappingPreviewV1 mappingPreview;
        await using (var stream = File.OpenRead(preparedPath))
        {
            preparation = await JsonSerializer.DeserializeAsync<SpaceCadCoordinatePreparationV1>(
                              stream,
                              CadExperimentJson.Options,
                              cancellationToken)
                          ?? throw new InvalidDataException(
                              "The prepared CAD IR package is empty.");
        }
        await using (var stream = File.OpenRead(inventoryPath))
        {
            inventory = await JsonSerializer.DeserializeAsync<SpaceCadInventoryV1>(
                            stream,
                            CadExperimentJson.Options,
                            cancellationToken)
                        ?? throw new InvalidDataException("The CAD inventory is empty.");
        }
        await using (var stream = File.OpenRead(profilePath))
        {
            profile = await JsonSerializer.DeserializeAsync<SpaceCadMappingProfileV1>(
                          stream,
                          CadExperimentJson.Options,
                          cancellationToken)
                      ?? throw new InvalidDataException(
                          "The sealed CAD mapping profile is empty.");
        }
        await using (var stream = File.OpenRead(mappingPath))
        {
            mappingPreview = await JsonSerializer.DeserializeAsync<SpaceCadMappingPreviewV1>(
                                 stream,
                                 CadExperimentJson.Options,
                                 cancellationToken)
                             ?? throw new InvalidDataException(
                                 "The CAD mapping preview is empty.");
        }

        var package = preparation.Package;
        var request = new SpaceCadConversionRequest(
            mappingPreview.TenantId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            package.Document.SourceSha256,
            package.Document.SourceFormat,
            package.Document.ConverterId,
            package.Document.ConverterVersion);
        var semanticPreview = SpaceCadSemanticParser.Parse(
            request,
            preparation,
            inventory,
            profile,
            mappingPreview);
        await CadExperimentJson.WriteAsync(output, semanticPreview, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                semanticPreview.TenantId,
                semanticPreview.FloorLogicalId,
                semanticPreview.FloorCode,
                semanticPreview.SourceSha256,
                semanticPreview.MappingPreviewSha256,
                semanticPreview.SemanticPreviewSha256,
                semanticPreview.ReadyForConfirmation,
                semanticPreview.Summary,
            },
            CadExperimentJson.Options));
        return semanticPreview.ReadyForConfirmation ? 0 : 3;
    }

    private static async Task<int> BuildDevelopmentSemanticDiagnosticsAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var preparedPath = Path.GetFullPath(commandLine.Required("--prepared"));
        var inventoryPath = Path.GetFullPath(commandLine.Required("--inventory"));
        var profilePath = Path.GetFullPath(commandLine.Required("--profile"));
        var mappingPath = Path.GetFullPath(commandLine.Required("--mapping"));
        var semanticPath = Path.GetFullPath(commandLine.Required("--semantic"));
        var output = Path.GetFullPath(commandLine.Required("--output"));
        SpaceCadCoordinatePreparationV1 preparation;
        SpaceCadInventoryV1 inventory;
        SpaceCadMappingProfileV1 profile;
        SpaceCadMappingPreviewV1 mappingPreview;
        SpaceCadSemanticPreviewV1 semanticPreview;
        await using (var stream = File.OpenRead(preparedPath))
        {
            preparation = await JsonSerializer.DeserializeAsync<SpaceCadCoordinatePreparationV1>(
                              stream,
                              CadExperimentJson.Options,
                              cancellationToken)
                          ?? throw new InvalidDataException(
                              "The prepared CAD IR package is empty.");
        }
        await using (var stream = File.OpenRead(inventoryPath))
        {
            inventory = await JsonSerializer.DeserializeAsync<SpaceCadInventoryV1>(
                            stream,
                            CadExperimentJson.Options,
                            cancellationToken)
                        ?? throw new InvalidDataException("The CAD inventory is empty.");
        }
        await using (var stream = File.OpenRead(profilePath))
        {
            profile = await JsonSerializer.DeserializeAsync<SpaceCadMappingProfileV1>(
                          stream,
                          CadExperimentJson.Options,
                          cancellationToken)
                      ?? throw new InvalidDataException(
                          "The sealed CAD mapping profile is empty.");
        }
        await using (var stream = File.OpenRead(mappingPath))
        {
            mappingPreview = await JsonSerializer.DeserializeAsync<SpaceCadMappingPreviewV1>(
                                 stream,
                                 CadExperimentJson.Options,
                                 cancellationToken)
                             ?? throw new InvalidDataException(
                                 "The CAD mapping preview is empty.");
        }
        await using (var stream = File.OpenRead(semanticPath))
        {
            semanticPreview = await JsonSerializer.DeserializeAsync<SpaceCadSemanticPreviewV1>(
                                  stream,
                                  CadExperimentJson.Options,
                                  cancellationToken)
                              ?? throw new InvalidDataException(
                                  "The CAD semantic preview is empty.");
        }

        var package = preparation.Package;
        var request = new SpaceCadConversionRequest(
            mappingPreview.TenantId,
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            package.Document.SourceSha256,
            package.Document.SourceFormat,
            package.Document.ConverterId,
            package.Document.ConverterVersion);
        var index = SpaceCadSemanticDiagnostics.Build(
            request,
            preparation,
            inventory,
            profile,
            mappingPreview,
            semanticPreview);
        await CadExperimentJson.WriteAsync(output, index, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                index.TenantId,
                index.FloorLogicalId,
                index.FloorCode,
                index.SourceSha256,
                index.SemanticPreviewSha256,
                index.DiagnosticIndexSha256,
                index.Summary,
            },
            CadExperimentJson.Options));
        return index.Summary.BlockingCount == 0 ? 0 : 3;
    }

    private static async Task<int> QueryDevelopmentSemanticDiagnosticsAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        SpaceCadSemanticDiagnosticIndexV1 index;
        await using (var stream = File.OpenRead(input))
        {
            index = await JsonSerializer.DeserializeAsync<SpaceCadSemanticDiagnosticIndexV1>(
                        stream,
                        CadExperimentJson.Options,
                        cancellationToken)
                    ?? throw new InvalidDataException(
                        "The CAD semantic diagnostic index is empty.");
        }
        SpaceCadSemanticDiagnostics.Validate(index);
        var offset = commandLine.Integer("--offset", 0);
        var limit = commandLine.Integer(
            "--limit",
            SpaceCadSemanticDiagnosticVersions.DefaultPageSize);
        object page = commandLine.Required("--kind").ToLowerInvariant() switch
        {
            "evidence" or "proposal" or "proposals" =>
                SpaceCadSemanticDiagnostics.QueryEvidence(
                    index,
                    new SpaceCadSemanticEvidenceQueryV1(
                        OptionalEnum<SpaceCadConfidenceBand>(commandLine, "--band"),
                        OptionalEnum<SpaceCadSemanticTarget>(commandLine, "--target"),
                        commandLine.Optional("--layer"),
                        commandLine.Optional("--source"),
                        commandLine.HasFlag("--with-diagnostics"),
                        offset,
                        limit)),
            "diagnostic" or "diagnostics" or "issue" or "issues" =>
                SpaceCadSemanticDiagnostics.QueryDiagnostics(
                    index,
                    new SpaceCadSemanticDiagnosticQueryV1(
                        OptionalEnum<SpaceCadIssueSeverity>(commandLine, "--severity"),
                        OptionalEnum<SpaceCadDiagnosticOrigin>(commandLine, "--origin"),
                        commandLine.Optional("--code"),
                        commandLine.Optional("--layer"),
                        commandLine.Optional("--source"),
                        commandLine.HasFlag("--locatable"),
                        offset,
                        limit)),
            var kind => throw new ArgumentException(
                $"Unknown CAD semantic diagnostic query kind '{kind}'.")
        };
        var output = commandLine.Optional("--output");
        if (output is not null)
            await CadExperimentJson.WriteAsync(output, page, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(page, CadExperimentJson.Options));
        return 0;
    }

    private static bool? OptionalBoolean(CommandLine commandLine, string name)
    {
        var value = commandLine.Optional(name);
        if (value is null)
            return null;
        return bool.TryParse(value, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option '{name}' must be true or false.");
    }

    private static T? OptionalEnum<T>(CommandLine commandLine, string name)
        where T : struct, Enum
    {
        var value = commandLine.Optional(name);
        if (value is null)
            return null;
        return Enum.TryParse<T>(value, ignoreCase: true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option '{name}' is invalid.");
    }

    private static async Task<int> RunAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var inputs = commandLine.All("--input");
        if (inputs.Count == 0)
        {
            throw new ArgumentException("At least one '--input' option is required.");
        }

        var options = new AdapterRunOptions(
            commandLine.Required("--candidate"),
            commandLine.Required("--candidate-version"),
            commandLine.Required("--adapter"),
            commandLine.All("--adapter-arg"),
            inputs,
            commandLine.Required("--output"),
            commandLine.Integer("--runs", 5),
            TimeSpan.FromSeconds(commandLine.Integer("--timeout-seconds", 300)));
        var report = await AdapterRunner.RunAsync(options, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(report, CadExperimentJson.Options));
        return report.Attempts.All(attempt => attempt.Outcome == "Success") ? 0 : 1;
    }

    private static async Task<int> ProbeAdapterAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        var output = commandLine.Required("--output");
        var probe = DxfProbe.Inspect(input);
        var sourceHash = await DatasetAuditor.ComputeSha256Async(input, cancellationToken);
        var observation = new CadAdapterObservation(
            1,
            commandLine.Required("--candidate-version"),
            sourceHash,
            "DXF",
            probe.CadVersion,
            probe.InsertionUnitsCode == 4 ? "Millimeter" : null,
            null,
            probe.EntityCount,
            probe.HandleCount,
            probe.DuplicateHandleCount,
            probe.EntityTypeCounts,
            probe.LayerCounts,
            new Dictionary<string, long>(),
            probe.Errors);
        await CadExperimentJson.WriteAsync(output, observation, cancellationToken);
        return probe.Errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> FixtureTimeoutAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine(
            """
            Usage:
              audit --manifest <path> [--stress-50mb <path>]
                    [--stress-million <path>] [--output <path>]
                    [--require-e02-ready]
              preflight --config <path> [--output <path>]
              generate-stress --kind <50mb|million> --output <path>
              generate-dev-corpus --output <directory>
              convert-dev-ir --input <dxf-path> --output <cad-ir-json-path>
              prepare-dev-coordinate --input <cad-ir-json-path>
                  --confirmation <confirmation-json-path>
                  --output <prepared-cad-ir-json-path>
              build-dev-inventory --input <prepared-cad-ir-json-path>
                  --output <inventory-json-path>
              query-dev-inventory --input <inventory-json-path>
                  --kind <layer|block|reference> [--search <text>]
                  [--visible <true|false>] [--entity-type <type>]
                  [--external <true|false>] [--layer <id>] [--block <name>]
                  [--attribute <name>] [--value <value>] [--exclude-empty]
                  [--offset <n>] [--limit <n>] [--output <json-path>]
              seal-dev-mapping-profile --input <profile-draft-json-path>
                  --output <sealed-profile-json-path>
              preview-dev-mapping --inventory <inventory-json-path>
                  --profile <sealed-profile-json-path> --tenant-id <guid>
                  [--overrides <override-json-path>] --output <preview-json-path>
              parse-dev-semantic --prepared <prepared-cad-ir-json-path>
                  --inventory <inventory-json-path>
                  --profile <sealed-profile-json-path>
                  --mapping <mapping-preview-json-path>
                  --output <semantic-preview-json-path>
              build-dev-semantic-diagnostics --prepared <prepared-cad-ir-json-path>
                  --inventory <inventory-json-path>
                  --profile <sealed-profile-json-path>
                  --mapping <mapping-preview-json-path>
                  --semantic <semantic-preview-json-path>
                  --output <diagnostic-index-json-path>
              query-dev-semantic-diagnostics --input <diagnostic-index-json-path>
                  --kind <evidence|diagnostic> [--band <band>] [--target <target>]
                  [--severity <severity>] [--origin <origin>] [--code <code>]
                  [--layer <id>] [--source <source-ref>] [--with-diagnostics]
                  [--locatable] [--offset <n>] [--limit <n>] [--output <json-path>]
              run --candidate <id> --candidate-version <version> --adapter <path>
                  [--adapter-arg <value>]... --input <path> [--input <path>]...
                  --output <directory> [--runs <n>] [--timeout-seconds <n>]

            Internal calibration adapter:
              inspect --input <dxf> --output <json> --candidate-version <version>
            """);
    }
}
