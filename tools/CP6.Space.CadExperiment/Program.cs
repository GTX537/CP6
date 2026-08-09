using System.Security.Cryptography;
using System.Text;
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
                "minimize-dev-ai-cad-features" =>
                    await MinimizeDevelopmentAiCadFeaturesAsync(
                        commandLine,
                        cancellation.Token),
                "run-dev-ai-provider" => await RunDevelopmentAiProviderAsync(
                    commandLine,
                    cancellation.Token),
                "validate-dev-ai-provider-output" =>
                    await ValidateDevelopmentAiProviderOutputAsync(
                        commandLine,
                        cancellation.Token),
                "synthesize-dev-ai-proposals" =>
                    await SynthesizeDevelopmentAiProposalsAsync(
                        commandLine,
                        cancellation.Token),
                "evaluate-ai-offline" => await EvaluateAiOfflineAsync(
                    commandLine,
                    cancellation.Token),
                "seal-dev-ai-review-baseline" =>
                    await SealDevelopmentAiReviewBaselineAsync(
                        commandLine,
                        cancellation.Token),
                "build-dev-ai-review-workspace" =>
                    await BuildDevelopmentAiReviewWorkspaceAsync(
                        commandLine,
                        cancellation.Token),
                "query-dev-ai-review-workspace" =>
                    await QueryDevelopmentAiReviewWorkspaceAsync(
                        commandLine,
                        cancellation.Token),
                "preview-dev-ai-review-batch" =>
                    await PreviewDevelopmentAiReviewBatchAsync(
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
                "match-dev-excel-cad" => await MatchDevelopmentExcelCadAsync(
                    commandLine,
                    cancellation.Token),
                "seal-dev-editor-rack-snapshot" =>
                    await SealDevelopmentEditorRackSnapshotAsync(
                        commandLine,
                        cancellation.Token),
                "query-dev-excel-cad-match" =>
                    await QueryDevelopmentExcelCadMatchAsync(
                        commandLine,
                        cancellation.Token),
                "build-dev-cad-review-workspace" =>
                    await BuildDevelopmentCadReviewWorkspaceAsync(
                        commandLine,
                        cancellation.Token),
                "query-dev-cad-review-workspace" =>
                    await QueryDevelopmentCadReviewWorkspaceAsync(
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
                or JsonException
                or SpaceProblemException)
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

    private static async Task<int> MinimizeDevelopmentAiCadFeaturesAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var input = Path.GetFullPath(commandLine.Required("--input"));
        var keyPath = Path.GetFullPath(commandLine.Required("--hmac-key-file"));
        var providerOutput = Path.GetFullPath(
            commandLine.Required("--provider-output"));
        var sourceMapOutput = Path.GetFullPath(
            commandLine.Required("--source-map-output"));
        EnsureDistinctPaths(
            input,
            keyPath,
            providerOutput,
            sourceMapOutput);

        SpaceCadCoordinatePreparationV1 preparation;
        await using (var inputStream = File.OpenRead(input))
        {
            preparation = await JsonSerializer.DeserializeAsync<
                              SpaceCadCoordinatePreparationV1>(
                              inputStream,
                              CadExperimentJson.Options,
                              cancellationToken)
                          ?? throw new InvalidDataException(
                              "The prepared CAD IR package is empty.");
        }

        if (!Enum.TryParse<SpaceAiDataPolicy>(
                commandLine.Required("--policy"),
                ignoreCase: false,
                out var policy)
            || policy == SpaceAiDataPolicy.Disabled)
        {
            throw new ArgumentException(
                "Option '--policy' must be MetadataOnly or StructuredFeatures.");
        }
        var package = preparation.Package;
        var request = new SpaceCadConversionRequest(
            ParseRequiredGuid(commandLine, "--tenant-id"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            package.Document.SourceSha256,
            package.Document.SourceFormat,
            package.Document.ConverterId,
            package.Document.ConverterVersion);
        var siteId = ParseRequiredGuid(commandLine, "--site-id");
        var modelVersionId = ParseRequiredGuid(commandLine, "--model-version-id");
        var runId = ParseRequiredGuid(commandLine, "--run-id");
        var hmacKey = await File.ReadAllBytesAsync(keyPath, cancellationToken);
        try
        {
            var result = SpaceAiCadFeatureMinimizer.Minimize(
                request,
                preparation,
                policy,
                hmacKey,
                siteId,
                modelVersionId,
                runId,
                new WarehouseGenerationLimits(
                    commandLine.Integer("--max-suggestions", 1_000),
                    commandLine.Integer("--max-relations", 8)));
            await WriteCanonicalJsonAsync(
                providerOutput,
                SpaceAiCadFeatureMinimizer.SerializeProviderInput(result),
                cancellationToken);
            await WriteCanonicalJsonAsync(
                sourceMapOutput,
                SpaceAiCadFeatureMinimizer.SerializeLocalSourceMap(result),
                cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    result.ProviderInput.Policy,
                    FeatureCount = result.ProviderInput.Features.Count,
                    result.LocalSourceMap.MappedSourceCount,
                    result.LocalSourceMap.ProviderInputSha256,
                    result.LocalSourceMap.SourceMapSha256,
                    ExternalProviderInvoked = false,
                    DraftWritten = false,
                },
                CadExperimentJson.Options));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(hmacKey);
        }
        return 0;
    }

    private static Guid ParseRequiredGuid(
        CommandLine commandLine,
        string option)
    {
        var value = commandLine.Required(option);
        return Guid.TryParseExact(value, "D", out var parsed)
            && parsed != Guid.Empty
                ? parsed
                : throw new ArgumentException(
                    $"Option '{option}' must be a non-empty D-format GUID.");
    }

    private static async Task<int> RunDevelopmentAiProviderAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(commandLine.Required("--input"));
        var outputPath = Path.GetFullPath(commandLine.Required("--output"));
        if (inputPath.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Provider input and output paths must differ.");
        }
        var input = await LoadDevelopmentAiProviderInputAsync(
            inputPath,
            cancellationToken);

        var providerName = commandLine.Required("--provider");
        IWarehouseGenerationProvider provider = providerName switch
        {
            "mock" => new MockWarehouseGenerationProvider(),
            "local" => new LocalHeuristicWarehouseGenerationProvider(),
            "fallback-local" => new FallbackWarehouseGenerationProvider(
                new DevelopmentFailureProvider(ParseDevelopmentFailure(
                    commandLine.Optional("--failure") ?? "unavailable")),
                new LocalHeuristicWarehouseGenerationProvider()),
            _ => throw new ArgumentException(
                "Option '--provider' must be mock, local or fallback-local."),
        };
        var result = await provider.GenerateAsync(input, cancellationToken);
        var validated = new WarehouseGenerationOutputValidator()
            .Validate(input, result);
        result = validated.Output;
        await CadExperimentJson.WriteAsync(outputPath, result, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                Provider = providerName,
                result.ProviderModel,
                SuggestionCount = result.Suggestions.Count,
                DiagnosticCount = result.Diagnostics.Count,
                validated.CanonicalSha256,
                Degraded = result.Diagnostics.Any(item =>
                    item.Code.EndsWith("_FALLBACK", StringComparison.Ordinal)),
                ExternalProviderInvoked = false,
                DraftWritten = false,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> ValidateDevelopmentAiProviderOutputAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(commandLine.Required("--input"));
        var providerOutputPath = Path.GetFullPath(
            commandLine.Required("--provider-output"));
        if (inputPath.Equals(
                providerOutputPath,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Provider input and output paths must differ.");
        }

        var input = await LoadDevelopmentAiProviderInputAsync(
            inputPath,
            cancellationToken);
        var limits = new WarehouseGenerationOutputValidationLimits().Validate();
        var file = new FileInfo(providerOutputPath);
        if (!file.Exists
            || file.Length is < 1
            || file.Length > limits.MaxCanonicalJsonBytes)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.AiOutputInvalid,
                502,
                "The warehouse generation provider output is invalid.",
                "Provider output failed validation (OUTPUT_JSON_SIZE_INVALID).",
                "change-ai-provider-or-model");
        }

        var bytes = await File.ReadAllBytesAsync(
            providerOutputPath,
            cancellationToken);
        try
        {
            var validated = new WarehouseGenerationOutputValidator(limits)
                .ValidateJson(input, bytes);
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    Validated = true,
                    validated.Output.SchemaVersion,
                    validated.Output.ProviderModel,
                    SuggestionCount = validated.Output.Suggestions.Count,
                    DiagnosticCount = validated.Output.Diagnostics.Count,
                    validated.CanonicalSha256,
                    ExternalProviderInvoked = false,
                    DraftWritten = false,
                },
                CadExperimentJson.Options));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
        return 0;
    }

    private static async Task<WarehouseGenerationInput>
        LoadDevelopmentAiProviderInputAsync(
            string inputPath,
            CancellationToken cancellationToken)
    {
        await using var inputStream = File.OpenRead(inputPath);
        using var document = await JsonDocument.ParseAsync(
            inputStream,
            cancellationToken: cancellationToken);
        if (!document.RootElement.TryGetProperty(
                "schemaVersion",
                out var schemaVersion)
            || schemaVersion.GetString()
                != WarehouseGenerationInput.CurrentSchemaVersion
            || !document.RootElement.TryGetProperty(
                "warehouseKind",
                out var warehouseKind)
            || warehouseKind.GetString()
                != WarehouseGenerationInput.GeneralRackWarehouse)
        {
            throw new InvalidDataException(
                "The minimized AI provider input schema is unsupported.");
        }
        return document.RootElement.Deserialize<WarehouseGenerationInput>(
                   CadExperimentJson.Options)
               ?? throw new InvalidDataException(
                   "The minimized AI provider input is empty.");
    }

    private static async Task<int> SynthesizeDevelopmentAiProposalsAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(commandLine.Required("--input"));
        var sourceMapPath = Path.GetFullPath(commandLine.Required("--source-map"));
        var semanticPath = Path.GetFullPath(commandLine.Required("--semantic"));
        var providerOutputPath = Path.GetFullPath(
            commandLine.Required("--provider-output"));
        var outputPath = Path.GetFullPath(commandLine.Required("--output"));
        EnsureDistinctPaths(
            inputPath,
            sourceMapPath,
            semanticPath,
            providerOutputPath,
            outputPath);

        var input = await LoadDevelopmentAiProviderInputAsync(
            inputPath,
            cancellationToken);
        var sourceMap = await ReadRequiredJsonAsync<
            SpaceAiCadFeatureSourceMapV1>(
                sourceMapPath,
                "The local CAD feature source map is empty.",
                cancellationToken);
        var semantic = await ReadRequiredJsonAsync<SpaceCadSemanticPreviewV1>(
            semanticPath,
            "The CAD semantic preview is empty.",
            cancellationToken);
        var lockedFacts = commandLine.Optional("--locked-facts") is { } lockedPath
            ? await ReadRequiredJsonAsync<SpaceAiCadLockedFactV1[]>(
                lockedPath,
                "The locked fact artifact is empty.",
                cancellationToken)
            : [];
        var defaults = commandLine.Optional("--template-defaults") is { } defaultPath
            ? await ReadRequiredJsonAsync<WarehouseTemplateDefaultFactV1[]>(
                defaultPath,
                "The template default artifact is empty.",
                cancellationToken)
            : [];
        var rackProfiles = commandLine.Optional("--rack-profiles") is { } profilePath
            ? await ReadRequiredJsonAsync<WarehouseRackProfileBindingV1[]>(
                profilePath,
                "The rack profile binding artifact is empty.",
                cancellationToken)
            : [];

        var limits = new WarehouseGenerationOutputValidationLimits().Validate();
        var providerFile = new FileInfo(providerOutputPath);
        if (!providerFile.Exists
            || providerFile.Length is < 1
            || providerFile.Length > limits.MaxCanonicalJsonBytes)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.AiOutputInvalid,
                502,
                "The warehouse generation provider output is invalid.",
                "Provider output failed validation (OUTPUT_JSON_SIZE_INVALID).",
                "change-ai-provider-or-model");
        }
        var bytes = await File.ReadAllBytesAsync(
            providerOutputPath,
            cancellationToken);
        try
        {
            var validated = new WarehouseGenerationOutputValidator(limits)
                .ValidateJson(input, bytes);
            var proposalSet = await new WarehouseDraftSynthesizer()
                .SynthesizeAsync(
                    new WarehouseDraftSynthesisRequestV1(
                        ParseRequiredGuid(commandLine, "--model-version-id"),
                        commandLine.Required("--rule-version"),
                        new SpaceAiCadFeatureMinimizationV1(input, sourceMap),
                        semantic,
                        validated,
                        lockedFacts,
                        defaults,
                        rackProfiles),
                    cancellationToken);
            await WriteCanonicalJsonAsync(
                outputPath,
                WarehouseDraftSynthesizer.Serialize(proposalSet),
                cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(
                new
                {
                    proposalSet.ModelVersionId,
                    proposalSet.FloorLogicalId,
                    proposalSet.ProposalSetSha256,
                    proposalSet.Summary,
                    ExternalProviderInvoked = false,
                    proposalSet.DraftWritten,
                },
                CadExperimentJson.Options));
            return proposalSet.Summary.BlockingCount == 0 ? 0 : 3;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    private static async Task<int> SealDevelopmentAiReviewBaselineAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var draft = await ReadRequiredJsonAsync<
            WarehouseProposalReviewBaselineSnapshotV1>(
                commandLine.Required("--input"),
                "The AI review baseline draft is empty.",
                cancellationToken);
        var baseline = WarehouseProposalReviewWorkbench.SealBaseline(
            draft.TenantId,
            draft.ModelVersionId,
            draft.FloorLogicalId,
            draft.ContentRevision,
            draft.ContentHash,
            draft.Objects);
        await WriteCanonicalJsonAsync(
            Path.GetFullPath(commandLine.Required("--output")),
            WarehouseProposalReviewWorkbench.SerializeBaseline(baseline),
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                baseline.ModelVersionId,
                baseline.FloorLogicalId,
                baseline.ContentRevision,
                ObjectCount = baseline.Objects.Count,
                baseline.SnapshotSha256,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> EvaluateAiOfflineAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var inputPath = Path.GetFullPath(commandLine.Required("--input"));
        var outputPath = Path.GetFullPath(commandLine.Required("--output"));
        if (inputPath.Equals(outputPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Offline evaluation input and output paths must differ.");
        }

        var request = await ReadRequiredJsonAsync<
            SpaceAiOfflineEvaluationRequestV1>(
                inputPath,
                "The offline AI evaluation request is empty.",
                cancellationToken);
        var report = new SpaceAiOfflineEvaluator().Evaluate(request);
        await WriteCanonicalJsonAsync(
            outputPath,
            SpaceAiOfflineEvaluator.Serialize(report),
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                report.DatasetVersion,
                report.DatasetPurpose,
                report.AppliedHighConfidenceThreshold,
                report.OverallMetrics,
                report.OutOfSampleMetrics,
                report.Calibration.DecisionCode,
                report.Gate,
                report.ReportSha256,
                ExternalProviderInvoked = false,
                DraftWritten = false,
            },
            CadExperimentJson.Options));
        if (!report.Gate.EvaluationDataValid)
            return 3;
        return commandLine.HasFlag("--require-release-eligible")
               && !report.Gate.ReleaseEligible
            ? 4
            : 0;
    }

    private static async Task<int> BuildDevelopmentAiReviewWorkspaceAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var proposalSet = await ReadRequiredJsonAsync<WarehouseDraftProposalSetV1>(
            commandLine.Required("--proposals"),
            "The warehouse proposal set is empty.",
            cancellationToken);
        var baseline = await ReadRequiredJsonAsync<
            WarehouseProposalReviewBaselineSnapshotV1>(
                commandLine.Required("--baseline"),
                "The AI review baseline is empty.",
                cancellationToken);
        var workspace = WarehouseProposalReviewWorkbench.Build(proposalSet, baseline);
        await WriteCanonicalJsonAsync(
            Path.GetFullPath(commandLine.Required("--output")),
            WarehouseProposalReviewWorkbench.Serialize(workspace),
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                workspace.ModelVersionId,
                workspace.FloorLogicalId,
                workspace.ProposalSetSha256,
                workspace.BaselineSnapshotSha256,
                workspace.ReviewEtag,
                workspace.WorkspaceSha256,
                workspace.Summary,
                workspace.DecisionWritten,
                workspace.DraftWritten,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> QueryDevelopmentAiReviewWorkspaceAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var workspace = await ReadRequiredJsonAsync<
            WarehouseProposalReviewWorkspaceV1>(
                commandLine.Required("--input"),
                "The AI review workspace is empty.",
                cancellationToken);
        var key = await ReadDevelopmentCursorKeyAsync(commandLine, cancellationToken);
        try
        {
            using var codec = new HmacDevelopmentCursorCodec(key);
            var page = WarehouseProposalReviewWorkbench.Query(
                workspace,
                new WarehouseProposalReviewQueryV1(
                    DevelopmentReviewFilter(commandLine),
                    commandLine.Optional("--cursor"),
                    commandLine.Integer(
                        "--limit",
                        WarehouseProposalReviewVersions.DefaultPageSize)),
                codec);
            var output = commandLine.Optional("--output");
            if (output is not null)
                await CadExperimentJson.WriteAsync(output, page, cancellationToken);
            Console.WriteLine(JsonSerializer.Serialize(page, CadExperimentJson.Options));
            return 0;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private static async Task<int> PreviewDevelopmentAiReviewBatchAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var workspace = await ReadRequiredJsonAsync<
            WarehouseProposalReviewWorkspaceV1>(
                commandLine.Required("--input"),
                "The AI review workspace is empty.",
                cancellationToken);
        var ids = commandLine.All("--review-id");
        var preview = WarehouseProposalReviewWorkbench.PreviewBatchSelection(
            workspace,
            new WarehouseProposalBatchSelectionRequestV1(
                Enum.TryParse<WarehouseProposalBatchAction>(
                    commandLine.Required("--action"),
                    ignoreCase: true,
                    out var action)
                    ? action
                    : throw new ArgumentException(
                        "Option '--action' must be Accept or Reject."),
                workspace.ReviewEtag,
                ids.Count > 0 ? ids : null,
                ids.Count == 0 ? DevelopmentReviewFilter(commandLine) : null));
        var output = commandLine.Optional("--output");
        if (output is not null)
            await CadExperimentJson.WriteAsync(output, preview, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(preview, CadExperimentJson.Options));
        return 0;
    }

    private static WarehouseProposalReviewFilterV1 DevelopmentReviewFilter(
        CommandLine commandLine) => new(
        OptionalEnum<WarehouseFusionConfidenceBand>(commandLine, "--band"),
        OptionalEnum<WarehouseSpaceType>(commandLine, "--object-type"),
        OptionalEnum<WarehouseProposalReviewReadiness>(commandLine, "--readiness"),
        OptionalEnum<WarehouseProposalDifferenceKind>(commandLine, "--difference"),
        OptionalEnum<WarehouseProposalIssueSeverity>(commandLine, "--issue-severity"),
        commandLine.Optional("--issue-code"),
        OptionalEnum<WarehouseFusionSource>(commandLine, "--winning-source"),
        commandLine.Optional("--evidence-code"),
        commandLine.Optional("--source"),
        commandLine.Optional("--search"),
        commandLine.HasFlag("--locatable"));

    private static async Task<byte[]> ReadDevelopmentCursorKeyAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var key = await File.ReadAllBytesAsync(
            Path.GetFullPath(commandLine.Required("--cursor-key-file")),
            cancellationToken);
        if (key.Length is < 32 or > 128)
        {
            CryptographicOperations.ZeroMemory(key);
            throw new ArgumentException(
                "The development cursor key must contain 32 to 128 binary bytes.");
        }
        return key;
    }

    private static WarehouseGenerationProviderFailureKind ParseDevelopmentFailure(
        string value) =>
        value switch
        {
            "unavailable" => WarehouseGenerationProviderFailureKind.Unavailable,
            "timeout" => WarehouseGenerationProviderFailureKind.Timeout,
            "rate-limited" => WarehouseGenerationProviderFailureKind.RateLimited,
            _ => throw new ArgumentException(
                "Option '--failure' must be unavailable, timeout or rate-limited."),
        };

    private static void EnsureDistinctPaths(params string[] paths)
    {
        if (paths.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            != paths.Length)
        {
            throw new ArgumentException(
                "Input, HMAC key, provider output and local source-map paths must differ.");
        }
    }

    private static async Task WriteCanonicalJsonAsync(
        string path,
        string json,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException(
                "The output directory is invalid."));
        await File.WriteAllTextAsync(
            path,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
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

    private static async Task<int> MatchDevelopmentExcelCadAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var mappingProfile = await ReadRequiredJsonAsync<SpaceExcelMappingProfileDto>(
            commandLine.Required("--mapping"),
            "The Excel mapping profile is empty.",
            cancellationToken);
        var workbook = await ReadRequiredJsonAsync<SpaceExcelWorkbookData>(
            commandLine.Required("--workbook"),
            "The canonical Excel workbook input is empty.",
            cancellationToken);
        var semanticPreview = await ReadRequiredJsonAsync<SpaceCadSemanticPreviewV1>(
            commandLine.Required("--semantic"),
            "The CAD semantic preview is empty.",
            cancellationToken);
        var diagnosticIndex =
            await ReadRequiredJsonAsync<SpaceCadSemanticDiagnosticIndexV1>(
                commandLine.Required("--diagnostics"),
                "The CAD semantic diagnostic index is empty.",
                cancellationToken);
        var editorSnapshot = await ReadRequiredJsonAsync<SpaceExcelEditorSnapshotV1>(
            commandLine.Required("--editor"),
            "The editor rack snapshot is empty.",
            cancellationToken);
        var preview = SpaceExcelCadMatching.Build(
            RequiredGuid(commandLine, "--tenant-id"),
            RequiredGuid(commandLine, "--model-version-id"),
            RequiredGuid(commandLine, "--excel-source-id"),
            RequiredGuid(commandLine, "--preflight-job-id"),
            mappingProfile,
            workbook,
            semanticPreview,
            diagnosticIndex,
            editorSnapshot);
        await CadExperimentJson.WriteAsync(
            commandLine.Required("--output"),
            preview,
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                preview.TenantId,
                preview.ModelVersionId,
                preview.ExcelSourceId,
                preview.FloorLogicalId,
                preview.WorkbookProjectionSha256,
                preview.SemanticPreviewSha256,
                preview.DiagnosticIndexSha256,
                preview.EditorSnapshotSha256,
                preview.MatchPreviewSha256,
                preview.Summary,
                preview.CanConfirm,
            },
            CadExperimentJson.Options));
        return preview.CanConfirm ? 0 : 3;
    }

    private static async Task<int> SealDevelopmentEditorRackSnapshotAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var draft = await ReadRequiredJsonAsync<SpaceExcelEditorSnapshotV1>(
            commandLine.Required("--input"),
            "The editor rack snapshot draft is empty.",
            cancellationToken);
        var snapshot = SpaceExcelCadMatching.SealEditorSnapshot(
            draft.TenantId,
            draft.ModelVersionId,
            draft.FloorLogicalId,
            draft.FloorCode,
            draft.ContentRevision,
            draft.ContentHash,
            draft.Racks);
        await CadExperimentJson.WriteAsync(
            commandLine.Required("--output"),
            snapshot,
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                snapshot.ModelVersionId,
                snapshot.FloorLogicalId,
                snapshot.FloorCode,
                snapshot.ContentRevision,
                RackCount = snapshot.Racks.Count,
                snapshot.SnapshotSha256,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> QueryDevelopmentExcelCadMatchAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var preview = await ReadRequiredJsonAsync<SpaceExcelCadMatchPreviewV1>(
            commandLine.Required("--input"),
            "The Excel/CAD match preview is empty.",
            cancellationToken);
        var page = SpaceExcelCadMatching.Query(
            preview,
            new SpaceExcelCadMatchQueryV1(
                OptionalEnum<SpaceExcelCadMatchDisposition>(
                    commandLine,
                    "--disposition"),
                commandLine.Optional("--rack-code"),
                commandLine.Optional("--source"),
                commandLine.HasFlag("--locatable"),
                commandLine.Integer("--offset", 0),
                commandLine.Integer(
                    "--limit",
                    SpaceExcelCadMatchVersions.DefaultPageSize)));
        var output = commandLine.Optional("--output");
        if (output is not null)
            await CadExperimentJson.WriteAsync(output, page, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(page, CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> BuildDevelopmentCadReviewWorkspaceAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var diagnostics =
            await ReadRequiredJsonAsync<SpaceCadSemanticDiagnosticIndexV1>(
                commandLine.Required("--diagnostics"),
                "The CAD semantic diagnostic index is empty.",
                cancellationToken);
        var editor = await ReadRequiredJsonAsync<SpaceExcelEditorSnapshotV1>(
            commandLine.Required("--editor"),
            "The editor rack snapshot is empty.",
            cancellationToken);
        SpaceExcelCadMatchPreviewV1? matches = null;
        if (commandLine.Optional("--matches") is { } matchPath)
        {
            matches = await ReadRequiredJsonAsync<SpaceExcelCadMatchPreviewV1>(
                matchPath,
                "The Excel/CAD match preview is empty.",
                cancellationToken);
        }
        SpaceCadReviewWorkspaceV1? previous = null;
        if (commandLine.Optional("--previous") is { } previousPath)
        {
            previous = await ReadRequiredJsonAsync<SpaceCadReviewWorkspaceV1>(
                previousPath,
                "The previous CAD review workspace is empty.",
                cancellationToken);
        }
        var workspace = SpaceCadReviewWorkspace.Build(
            diagnostics,
            editor,
            matches,
            previous);
        await CadExperimentJson.WriteAsync(
            commandLine.Required("--output"),
            workspace,
            cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(
            new
            {
                workspace.TenantId,
                workspace.ModelVersionId,
                workspace.FloorLogicalId,
                workspace.DiagnosticIndexSha256,
                workspace.MatchPreviewSha256,
                workspace.EditorContentRevision,
                workspace.PreviousWorkspaceSha256,
                workspace.WorkspaceSha256,
                workspace.Summary,
            },
            CadExperimentJson.Options));
        return 0;
    }

    private static async Task<int> QueryDevelopmentCadReviewWorkspaceAsync(
        CommandLine commandLine,
        CancellationToken cancellationToken)
    {
        var workspace = await ReadRequiredJsonAsync<SpaceCadReviewWorkspaceV1>(
            commandLine.Required("--input"),
            "The CAD review workspace is empty.",
            cancellationToken);
        var page = SpaceCadReviewWorkspace.Query(
            workspace,
            new SpaceCadReviewWorkspaceQueryV1(
                OptionalEnum<SpaceCadReviewItemStatus>(commandLine, "--status"),
                OptionalEnum<SpaceCadIssueSeverity>(commandLine, "--severity"),
                OptionalEnum<SpaceCadReviewItemKind>(commandLine, "--review-kind"),
                commandLine.Optional("--source"),
                commandLine.Optional("--search"),
                commandLine.HasFlag("--locatable"),
                commandLine.Integer("--offset", 0),
                commandLine.Integer(
                    "--limit",
                    SpaceCadReviewWorkspaceVersions.DefaultPageSize)));
        var output = commandLine.Optional("--output");
        if (output is not null)
            await CadExperimentJson.WriteAsync(output, page, cancellationToken);
        Console.WriteLine(JsonSerializer.Serialize(page, CadExperimentJson.Options));
        return 0;
    }

    private static async Task<T> ReadRequiredJsonAsync<T>(
        string path,
        string emptyMessage,
        CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(Path.GetFullPath(path));
        return await JsonSerializer.DeserializeAsync<T>(
                   stream,
                   CadExperimentJson.Options,
                   cancellationToken)
               ?? throw new InvalidDataException(emptyMessage);
    }

    private static Guid RequiredGuid(CommandLine commandLine, string name) =>
        Guid.TryParse(commandLine.Required(name), out var value) && value != Guid.Empty
            ? value
            : throw new ArgumentException($"Option '{name}' must be a non-empty GUID.");

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
              minimize-dev-ai-cad-features --input <prepared-cad-ir-json-path>
                  --policy <MetadataOnly|StructuredFeatures>
                  --hmac-key-file <32-to-128-byte-binary-key-path>
                  --tenant-id <guid> --site-id <guid>
                  --model-version-id <guid> --run-id <guid>
                  --provider-output <provider-input-json-path>
                  --source-map-output <local-only-source-map-json-path>
                  [--max-suggestions <n>] [--max-relations <n>]
              run-dev-ai-provider --input <provider-input-json-path>
                  --provider <mock|local|fallback-local> --output <json-path>
                  [--failure <unavailable|timeout|rate-limited>]
              validate-dev-ai-provider-output --input <provider-input-json-path>
                  --provider-output <canonical-provider-output-json-path>
              synthesize-dev-ai-proposals --input <provider-input-json-path>
                  --source-map <local-only-source-map-json-path>
                  --semantic <semantic-preview-json-path>
                  --provider-output <canonical-provider-output-json-path>
                  --model-version-id <guid> --rule-version <token>
                  --output <read-only-proposal-set-json-path>
                  [--locked-facts <json-path>]
                  [--template-defaults <json-path>]
                  [--rack-profiles <json-path>]
              evaluate-ai-offline --input <normalized-evaluation-request-json-path>
                  --output <canonical-evaluation-report-json-path>
                  [--require-release-eligible]
              seal-dev-ai-review-baseline --input <baseline-draft-json-path>
                  --output <sealed-baseline-json-path>
              build-dev-ai-review-workspace --proposals <proposal-set-json-path>
                  --baseline <sealed-baseline-json-path>
                  --output <read-only-review-workspace-json-path>
              query-dev-ai-review-workspace --input <review-workspace-json-path>
                  --cursor-key-file <32-to-128-byte-binary-key-path>
                  [--band <band>] [--object-type <type>]
                  [--readiness <readiness>] [--difference <kind>]
                  [--issue-severity <severity>] [--issue-code <code>]
                  [--winning-source <source>] [--evidence-code <code>]
                  [--source <source-ref>] [--search <text>] [--locatable]
                  [--cursor <opaque-cursor>] [--limit <n>]
                  [--output <json-path>]
              preview-dev-ai-review-batch --input <review-workspace-json-path>
                  --action <Accept|Reject>
                  [--review-id <id>]...
                  [--band <band>] [--object-type <type>]
                  [--readiness <readiness>] [--difference <kind>]
                  [--issue-severity <severity>] [--issue-code <code>]
                  [--winning-source <source>] [--evidence-code <code>]
                  [--source <source-ref>] [--search <text>] [--locatable]
                  [--output <json-path>]
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
              match-dev-excel-cad --mapping <excel-mapping-profile-json-path>
                  --workbook <canonical-workbook-json-path>
                  --semantic <semantic-preview-json-path>
                  --diagnostics <diagnostic-index-json-path>
                  --editor <editor-snapshot-json-path> --tenant-id <guid>
                  --model-version-id <guid> --excel-source-id <guid>
                  --preflight-job-id <guid> --output <match-preview-json-path>
              seal-dev-editor-rack-snapshot --input <snapshot-draft-json-path>
                  --output <sealed-editor-snapshot-json-path>
              query-dev-excel-cad-match --input <match-preview-json-path>
                  [--disposition <disposition>] [--rack-code <code>]
                  [--source <source-ref>] [--locatable]
                  [--offset <n>] [--limit <n>] [--output <json-path>]
              build-dev-cad-review-workspace --diagnostics <diagnostic-index-json-path>
                  --editor <editor-snapshot-json-path>
                  [--matches <match-preview-json-path>]
                  [--previous <previous-workspace-json-path>]
                  --output <review-workspace-json-path>
              query-dev-cad-review-workspace --input <review-workspace-json-path>
                  [--status <status>] [--severity <severity>]
                  [--review-kind <kind>] [--source <source-ref>]
                  [--search <text>] [--locatable]
                  [--offset <n>] [--limit <n>] [--output <json-path>]
              run --candidate <id> --candidate-version <version> --adapter <path>
                  [--adapter-arg <value>]... --input <path> [--input <path>]...
                  --output <directory> [--runs <n>] [--timeout-seconds <n>]

            Internal calibration adapter:
              inspect --input <dxf> --output <json> --candidate-version <version>
            """);
    }

    private sealed class HmacDevelopmentCursorCodec : ISpaceCursorCodec, IDisposable
    {
        private readonly byte[] _key;

        public HmacDevelopmentCursorCodec(byte[] key)
        {
            ArgumentNullException.ThrowIfNull(key);
            _key = key.ToArray();
        }

        public string Encode(SpaceCursorState state)
        {
            ArgumentNullException.ThrowIfNull(state);
            var payload = JsonSerializer.SerializeToUtf8Bytes(
                state,
                CadExperimentJson.Options);
            var signature = HMACSHA256.HashData(_key, payload);
            try
            {
                return $"{Base64Url(payload)}.{Base64Url(signature)}";
            }
            finally
            {
                CryptographicOperations.ZeroMemory(payload);
                CryptographicOperations.ZeroMemory(signature);
            }
        }

        public SpaceCursorState Decode(
            string cursor,
            string expectedResource,
            string expectedFilterHash)
        {
            try
            {
                var parts = cursor.Split('.');
                if (parts.Length != 2)
                    throw new InvalidDataException();
                var payload = FromBase64Url(parts[0]);
                var provided = FromBase64Url(parts[1]);
                var expected = HMACSHA256.HashData(_key, payload);
                try
                {
                    if (!CryptographicOperations.FixedTimeEquals(provided, expected))
                        throw new InvalidDataException();
                    var state = JsonSerializer.Deserialize<SpaceCursorState>(
                                    payload,
                                    CadExperimentJson.Options)
                                ?? throw new InvalidDataException();
                    if (!state.Resource.Equals(expectedResource, StringComparison.Ordinal)
                        || !state.FilterHash.Equals(
                            expectedFilterHash,
                            StringComparison.Ordinal)
                        || state.Offset < 0)
                    {
                        throw new InvalidDataException();
                    }
                    return state;
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(payload);
                    CryptographicOperations.ZeroMemory(provided);
                    CryptographicOperations.ZeroMemory(expected);
                }
            }
            catch (Exception exception) when (
                exception is FormatException or JsonException)
            {
                throw new InvalidDataException(
                    "The development AI review cursor is invalid.",
                    exception);
            }
        }

        public void Dispose() => CryptographicOperations.ZeroMemory(_key);

        private static string Base64Url(byte[] value) =>
            Convert.ToBase64String(value)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');

        private static byte[] FromBase64Url(string value)
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - padded.Length % 4) % 4);
            return Convert.FromBase64String(padded);
        }
    }

    private sealed class DevelopmentFailureProvider(
        WarehouseGenerationProviderFailureKind failureKind) :
        IWarehouseGenerationProvider
    {
        public Task<WarehouseGenerationResult> GenerateAsync(
            WarehouseGenerationInput input,
            CancellationToken cancellationToken) =>
            throw new WarehouseGenerationProviderException(failureKind);
    }
}
