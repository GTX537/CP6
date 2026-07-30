using System.Diagnostics;
using System.Text.Json;

namespace CP6.Space.CadExperiment;

public sealed record CadAdapterObservation(
    int SchemaVersion,
    string CandidateVersion,
    string SourceSha256,
    string Format,
    string? CadVersion,
    string? Unit,
    string? CoordinateSystem,
    long EntityCount,
    long HandleCount,
    long DuplicateHandleCount,
    IReadOnlyDictionary<string, long> EntityTypeCounts,
    IReadOnlyDictionary<string, long> LayerCounts,
    IReadOnlyDictionary<string, long> UnsupportedEntityCounts,
    IReadOnlyList<string> Issues);

public sealed record AdapterRunOptions(
    string CandidateId,
    string CandidateVersion,
    string AdapterPath,
    IReadOnlyList<string> AdapterPrefixArguments,
    IReadOnlyList<string> InputPaths,
    string OutputDirectory,
    int Runs,
    TimeSpan Timeout,
    string? AdapterWorkingDirectory = null);

public sealed record AdapterAttemptEvidence(
    int SchemaVersion,
    string CandidateId,
    string CandidateVersion,
    string InputPath,
    string SourceSha256,
    int Iteration,
    DateTimeOffset StartedAtUtc,
    double ElapsedMilliseconds,
    long PeakWorkingSetBytes,
    string Outcome,
    int? ExitCode,
    string? ObservationPath,
    string? ObservationSha256,
    string StandardOutput,
    string StandardError);

public sealed record AdapterRunReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string CandidateId,
    string CandidateVersion,
    string OperatingSystem,
    string ProcessArchitecture,
    string Framework,
    string AdapterWorkingDirectory,
    int RunsPerInput,
    int TimeoutSeconds,
    IReadOnlyList<AdapterAttemptEvidence> Attempts);

public static class AdapterRunner
{
    public static async Task<AdapterRunReport> RunAsync(
        AdapterRunOptions options,
        CancellationToken cancellationToken = default)
    {
        if (options.Runs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Runs must be positive.");
        }

        var adapterWorkingDirectory = Path.GetFullPath(
            options.AdapterWorkingDirectory ?? Environment.CurrentDirectory);
        if (!Directory.Exists(adapterWorkingDirectory))
        {
            throw new DirectoryNotFoundException(
                $"Adapter working directory does not exist: {adapterWorkingDirectory}");
        }

        var attempts = new List<AdapterAttemptEvidence>();
        foreach (var input in options.InputPaths)
        {
            var fullInputPath = Path.GetFullPath(input);
            var sourceHash = await DatasetAuditor.ComputeSha256Async(
                fullInputPath,
                cancellationToken);
            var sampleDirectoryName = Path.GetFileNameWithoutExtension(fullInputPath);

            for (var iteration = 1; iteration <= options.Runs; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var attemptDirectory = Path.GetFullPath(Path.Combine(
                    options.OutputDirectory,
                    Sanitize(options.CandidateId),
                    Sanitize(sampleDirectoryName),
                    $"run-{iteration:000}"));
                Directory.CreateDirectory(attemptDirectory);
                var observationPath = Path.Combine(attemptDirectory, "adapter-observation.json");
                var evidencePath = Path.Combine(attemptDirectory, "run-evidence.json");

                var evidence = await RunAttemptAsync(
                    options,
                    fullInputPath,
                    sourceHash,
                    iteration,
                    observationPath,
                    adapterWorkingDirectory,
                    cancellationToken);
                attempts.Add(evidence);
                await CadExperimentJson.WriteAsync(
                    evidencePath,
                    evidence,
                    CancellationToken.None);
            }
        }

        var report = new AdapterRunReport(
            1,
            DateTimeOffset.UtcNow,
            options.CandidateId,
            options.CandidateVersion,
            Environment.OSVersion.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            adapterWorkingDirectory,
            options.Runs,
            checked((int)options.Timeout.TotalSeconds),
            attempts);
        await CadExperimentJson.WriteAsync(
            Path.Combine(options.OutputDirectory, $"{Sanitize(options.CandidateId)}-run-report.json"),
            report,
            CancellationToken.None);
        return report;
    }

    private static async Task<AdapterAttemptEvidence> RunAttemptAsync(
        AdapterRunOptions options,
        string inputPath,
        string sourceHash,
        int iteration,
        string observationPath,
        string adapterWorkingDirectory,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        long peakWorkingSet = 0;
        int? exitCode = null;
        string outcome;
        string standardOutput = string.Empty;
        string standardError = string.Empty;

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = options.AdapterPath,
                WorkingDirectory = adapterWorkingDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        foreach (var argument in options.AdapterPrefixArguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.StartInfo.ArgumentList.Add("inspect");
        process.StartInfo.ArgumentList.Add("--input");
        process.StartInfo.ArgumentList.Add(inputPath);
        process.StartInfo.ArgumentList.Add("--output");
        process.StartInfo.ArgumentList.Add(observationPath);
        process.StartInfo.ArgumentList.Add("--candidate-version");
        process.StartInfo.ArgumentList.Add(options.CandidateVersion);

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("The adapter process did not start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            var timedOut = false;
            while (!process.HasExited)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    outcome = "Cancelled";
                    await process.WaitForExitAsync(CancellationToken.None);
                    standardOutput = await stdoutTask;
                    standardError = await stderrTask;
                    stopwatch.Stop();
                    return BuildEvidence();
                }

                if (stopwatch.Elapsed >= options.Timeout)
                {
                    timedOut = true;
                    TryKill(process);
                    break;
                }

                try
                {
                    process.Refresh();
                    peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
                }
                catch (InvalidOperationException)
                {
                    // Process exited between HasExited and Refresh.
                }

                await Task.Delay(25, CancellationToken.None);
            }

            await process.WaitForExitAsync(CancellationToken.None);
            exitCode = process.ExitCode;
            standardOutput = await stdoutTask;
            standardError = await stderrTask;
            outcome = timedOut
                ? "Timeout"
                : await ClassifyOutcomeAsync(
                    process.ExitCode,
                    observationPath,
                    sourceHash);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException
                or System.ComponentModel.Win32Exception
                or IOException
                or JsonException)
        {
            outcome = "RunnerError";
            standardError = exception.ToString();
        }
        finally
        {
            stopwatch.Stop();
        }

        return BuildEvidence();

        AdapterAttemptEvidence BuildEvidence()
        {
            string? observationHash = null;
            if (File.Exists(observationPath))
            {
                observationHash = DatasetAuditor
                    .ComputeSha256Async(observationPath, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }

            return new AdapterAttemptEvidence(
                1,
                options.CandidateId,
                options.CandidateVersion,
                inputPath,
                sourceHash,
                iteration,
                startedAt,
                stopwatch.Elapsed.TotalMilliseconds,
                peakWorkingSet,
                outcome,
                exitCode,
                File.Exists(observationPath) ? observationPath : null,
                observationHash,
                Truncate(standardOutput),
                Truncate(standardError));
        }
    }

    private static async Task<string> ClassifyOutcomeAsync(
        int exitCode,
        string observationPath,
        string expectedSourceHash)
    {
        if (exitCode != 0)
        {
            return "Crash";
        }

        if (!File.Exists(observationPath))
        {
            return "MissingOutput";
        }

        await using var stream = File.OpenRead(observationPath);
        var observation = await JsonSerializer.DeserializeAsync<CadAdapterObservation>(
            stream,
            CadExperimentJson.Options);
        if (observation is null
            || observation.SchemaVersion != 1
            || string.IsNullOrWhiteSpace(observation.CandidateVersion)
            || !observation.SourceSha256.Equals(
                expectedSourceHash,
                StringComparison.OrdinalIgnoreCase))
        {
            return "InvalidOutput";
        }

        return "Success";
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process already exited.
        }
    }

    private static string Sanitize(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        return string.Concat(value.Select(character => invalid.Contains(character) ? '-' : character));
    }

    private static string Truncate(string value)
    {
        const int maximumLength = 16_384;
        return value.Length <= maximumLength
            ? value
            : value[..maximumLength] + "\n...[truncated]";
    }
}
