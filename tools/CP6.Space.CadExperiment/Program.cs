using System.Text.Json;

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
              run --candidate <id> --candidate-version <version> --adapter <path>
                  [--adapter-arg <value>]... --input <path> [--input <path>]...
                  --output <directory> [--runs <n>] [--timeout-seconds <n>]

            Internal calibration adapter:
              inspect --input <dxf> --output <json> --candidate-version <version>
            """);
    }
}
