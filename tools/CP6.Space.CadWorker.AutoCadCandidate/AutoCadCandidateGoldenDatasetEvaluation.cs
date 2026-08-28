using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CP6.Space.Application;
using CP6.Space.Contracts;

namespace CP6.Space.CadWorker.AutoCadCandidate;

public sealed record AutoCadCandidateGoldenDatasetSampleResultV1(
    string SampleId,
    string SampleRef,
    string Split,
    string LayoutFamily,
    string SourceFormat,
    string SourceSha256,
    long SourceSizeBytes,
    double FirstRunSeconds,
    double ReplayRunSeconds,
    string PackageSha256,
    long LayerCount,
    long BlockCount,
    long EntityCount,
    long SupportedEntityCount,
    long UnsupportedEntityCount,
    long MissingSourceRefCount,
    int IssueCount,
    int BlockingIssueCount,
    bool DeterministicReplay,
    bool Passed,
    IReadOnlyList<string> BlockingCodes);

public sealed record AutoCadCandidateGoldenDatasetEnvironmentV1(
    string RuntimeIdentifier,
    string OperatingSystem,
    string Framework,
    string OsArchitecture,
    string ProcessArchitecture,
    int ProcessorCount,
    int ConversionTimeoutSeconds,
    int MaximumConcurrency,
    string ExecutionMode,
    bool NetworkListenerStarted,
    string OutboundNetworkPolicy,
    bool BusinessIdentityExcludedByProtocol,
    string RawCadRetentionMode);

public sealed record AutoCadCandidateGoldenDatasetEvaluationReportV1(
    int SchemaVersion,
    string EvidenceClass,
    DateTime EvaluatedAtUtc,
    string DatasetVersion,
    string DatasetManifestSha256,
    string SourceSetSha256,
    string GoldenDatasetSha256,
    string ProviderKey,
    string ProviderVersion,
    string WorkerReleaseSha256,
    string SourceCommit,
    string AutoCadCoreConsoleVersion,
    string AutoCadCoreConsoleSha256,
    string FrozenEnvironmentSha256,
    AutoCadCandidateGoldenDatasetEnvironmentV1 Environment,
    int SampleCount,
    int DwgCount,
    int DxfCount,
    int CalibrationCount,
    int ValidationCount,
    int ReleaseHoldoutCount,
    long TotalEntityCount,
    long TotalSupportedEntityCount,
    long TotalUnsupportedEntityCount,
    long TotalMissingSourceRefCount,
    int TotalIssueCount,
    int TotalBlockingIssueCount,
    double SupportedEntityPercent,
    double FirstRunP95Seconds,
    double FirstRunMaximumSeconds,
    int DeterministicReplayCount,
    int ResidualAttemptDirectoryCount,
    int ResidualRawCadFileCount,
    bool Passed,
    IReadOnlyList<string> BlockingCodes,
    IReadOnlyList<AutoCadCandidateGoldenDatasetSampleResultV1> Results);

public static class AutoCadCandidateGoldenDatasetEvaluator
{
    public const int SchemaVersion = 1;
    public const string EvidenceClass = "CP6_SPACE_AUTOCAD_PRIMARY_GOLDEN_EVALUATION";
    public const int ExpectedSampleCount = 20;
    public const double MinimumSupportedEntityPercent = 99.0;
    public const double MaximumPerSampleSeconds = 120.0;
    private const int MaximumManifestBytes = 2 * 1024 * 1024;
    private const int EvaluationTimeoutSeconds = 120;
    private const int EvaluationConcurrency = 1;

    private static readonly Regex SampleIdPattern = new(
        "^L[1-5]-(?:C0[12]|V01|H01)$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly Regex SampleRefPattern = new(
        "^urn:cp6-space-golden-cad:[A-Za-z0-9][A-Za-z0-9:._-]{0,200}$",
        RegexOptions.CultureInvariant,
        TimeSpan.FromSeconds(1));
    private static readonly JsonSerializerOptions ReportJsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static async Task<AutoCadCandidateGoldenDatasetEvaluationReportV1>
        EvaluateAsync(
            string datasetRoot,
            string workRoot,
            AutoCadCandidateConversionService service,
            AutoCadCandidateReleaseIdentity releaseIdentity,
            DateTime evaluatedAtUtc,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(releaseIdentity);
        if (evaluatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("The evaluation timestamp must be UTC.");
        if (!service.ProviderKey.Equals(
                AutoCadCandidateReleaseIdentity.ProviderKey,
                StringComparison.Ordinal) ||
            !service.ProviderVersion.Equals(
                releaseIdentity.ProviderVersion,
                StringComparison.Ordinal) ||
            !service.WorkerReleaseSha256.Equals(
                releaseIdentity.WorkerReleaseSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "The evaluator requires the verified release Worker identity.");
        }

        var root = RequireDirectory(datasetRoot, "Golden dataset root");
        var work = Path.GetFullPath(workRoot);
        var manifestPath = RequireContainedFile(
            root,
            Path.Combine(root, "controlled-manifest.json"),
            "Golden dataset Manifest");
        var manifestInfo = new FileInfo(manifestPath);
        if (manifestInfo.Length is <= 0 or > MaximumManifestBytes)
            throw new InvalidDataException("The golden dataset Manifest size is invalid.");
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath, cancellationToken);
        using var manifestDocument = JsonDocument.Parse(
            manifestBytes,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 64,
            });
        RejectDuplicateProperties(manifestDocument.RootElement, "$", 0);
        var manifest = ParseManifest(manifestDocument.RootElement);
        ValidateManifest(manifest);

        var results = new List<AutoCadCandidateGoldenDatasetSampleResultV1>(
            ExpectedSampleCount);
        foreach (var sample in manifest.Samples.OrderBy(
                     item => item.SampleId,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await EvaluateSampleAsync(
                root,
                sample,
                service,
                cancellationToken));
        }

        var totalEntities = results.Sum(item => item.EntityCount);
        var totalSupported = results.Sum(item => item.SupportedEntityCount);
        var totalUnsupported = results.Sum(item => item.UnsupportedEntityCount);
        var totalMissingSourceRefs = results.Sum(item => item.MissingSourceRefCount);
        var totalIssues = results.Sum(item => item.IssueCount);
        var totalBlockingIssues = results.Sum(item => item.BlockingIssueCount);
        var supportedPercent = totalEntities == 0
            ? 0
            : Math.Round(totalSupported * 100.0 / totalEntities, 6);
        var firstRunSeconds = results.Select(item => item.FirstRunSeconds).ToArray();
        var residualAttemptDirectories = CountAttemptDirectories(work);
        var residualRawCadFiles = CountResidualRawCadFiles(work);

        var blockers = new List<string>();
        if (results.Any(item => !item.Passed))
            blockers.Add("CAD_PRIMARY_SAMPLE_EVALUATION_FAILED");
        if (supportedPercent < MinimumSupportedEntityPercent)
            blockers.Add("CAD_PRIMARY_SUPPORTED_ENTITY_COVERAGE_BELOW_THRESHOLD");
        if (totalMissingSourceRefs != 0)
            blockers.Add("CAD_PRIMARY_SOURCE_REFERENCE_MISSING");
        if (totalBlockingIssues != 0)
            blockers.Add("CAD_PRIMARY_BLOCKING_ISSUE_PRESENT");
        if (results.Any(item => item.FirstRunSeconds > MaximumPerSampleSeconds))
            blockers.Add("CAD_PRIMARY_CONVERSION_TIMEOUT_THRESHOLD_EXCEEDED");
        if (results.Any(item => !item.DeterministicReplay))
            blockers.Add("CAD_PRIMARY_REPLAY_NOT_DETERMINISTIC");
        if (residualAttemptDirectories != 0)
            blockers.Add("CAD_PRIMARY_ATTEMPT_DIRECTORY_RESIDUAL");
        if (residualRawCadFiles != 0)
            blockers.Add("CAD_PRIMARY_RAW_CAD_RESIDUAL");
        var orderedBlockers = blockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();

        var environment = EnvironmentEvidence();
        var frozenEnvironmentSha256 = FrozenEnvironmentSha256(
            environment,
            releaseIdentity);
        return new AutoCadCandidateGoldenDatasetEvaluationReportV1(
            SchemaVersion,
            EvidenceClass,
            evaluatedAtUtc,
            manifest.DatasetVersion,
            Sha256(manifestBytes),
            manifest.SourceSetSha256,
            manifest.GoldenDatasetSha256,
            service.ProviderKey,
            service.ProviderVersion,
            service.WorkerReleaseSha256,
            releaseIdentity.Manifest.SourceCommit,
            releaseIdentity.Manifest.AutoCadCoreConsoleVersion,
            releaseIdentity.Manifest.AutoCadCoreConsoleSha256,
            frozenEnvironmentSha256,
            environment,
            results.Count,
            results.Count(item => item.SourceFormat == "DWG"),
            results.Count(item => item.SourceFormat == "DXF"),
            results.Count(item => item.Split == "Calibration"),
            results.Count(item => item.Split == "Validation"),
            results.Count(item => item.Split == "ReleaseHoldout"),
            totalEntities,
            totalSupported,
            totalUnsupported,
            totalMissingSourceRefs,
            totalIssues,
            totalBlockingIssues,
            supportedPercent,
            Percentile(firstRunSeconds, 0.95),
            firstRunSeconds.Length == 0 ? 0 : firstRunSeconds.Max(),
            results.Count(item => item.DeterministicReplay),
            residualAttemptDirectories,
            residualRawCadFiles,
            orderedBlockers.Length == 0,
            orderedBlockers,
            results);
    }

    public static async Task<string> WriteReportAsync(
        string outputPath,
        AutoCadCandidateGoldenDatasetEvaluationReportV1 report,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        var path = Path.GetFullPath(outputPath);
        var parent = Path.GetDirectoryName(path) ?? throw new InvalidDataException(
            "The evaluation report path has no parent directory.");
        Directory.CreateDirectory(parent);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(report, ReportJsonOptions);
        await using var output = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.WriteThrough);
        await output.WriteAsync(bytes, cancellationToken);
        await output.FlushAsync(cancellationToken);
        return Sha256(bytes);
    }

    private static async Task<AutoCadCandidateGoldenDatasetSampleResultV1>
        EvaluateSampleAsync(
            string datasetRoot,
            ControlledSample sample,
            AutoCadCandidateConversionService service,
            CancellationToken cancellationToken)
    {
        var extension = sample.SourceFormat == SpaceCadSourceFormat.Dwg
            ? ".dwg"
            : ".dxf";
        var sourcePath = RequireContainedFile(
            datasetRoot,
            Path.Combine(datasetRoot, "samples", sample.SampleId, "source" + extension),
            $"Source CAD for {sample.SampleId}");
        var sourceInfo = new FileInfo(sourcePath);
        if (sourceInfo.Length != sample.SourceSizeBytes)
            throw new InvalidDataException($"{sample.SampleId} source size changed.");
        var sourceSha256 = await FileSha256Async(sourcePath, cancellationToken);
        if (!FixedTimeSha256Equals(sourceSha256, sample.SourceSha256))
            throw new InvalidDataException($"{sample.SampleId} source hash changed.");

        var first = await ConvertAsync(
            sourcePath,
            sample,
            service,
            cancellationToken);
        var replay = await ConvertAsync(
            sourcePath,
            sample,
            service,
            cancellationToken);
        var package = first.Response.Package;
        var sampleBlockers = new List<string>();
        if (!first.Response.PackageSha256.Equals(
                replay.Response.PackageSha256,
                StringComparison.Ordinal))
        {
            sampleBlockers.Add("CAD_PRIMARY_PACKAGE_HASH_REPLAY_MISMATCH");
        }
        if (!package.Document.CadVersion.Equals("AC1032", StringComparison.Ordinal))
            sampleBlockers.Add("CAD_PRIMARY_CAD_VERSION_MISMATCH");
        if (package.Document.Unit != SpaceCadUnit.Millimeter ||
            package.Document.ScaleToMillimeters != 1m)
        {
            sampleBlockers.Add("CAD_PRIMARY_UNIT_MISMATCH");
        }
        if (!package.Document.CoordinateSystem.Equals(
                SpaceCadIrVersions.CoordinateSystem,
                StringComparison.Ordinal))
        {
            sampleBlockers.Add("CAD_PRIMARY_COORDINATE_SYSTEM_MISMATCH");
        }
        if (package.Summary.MissingSourceRefCount != 0)
            sampleBlockers.Add("CAD_PRIMARY_SOURCE_REFERENCE_MISSING");
        var blockingIssues = package.Issues.Count(
            issue => issue.Severity == SpaceCadIssueSeverity.Blocking);
        if (blockingIssues != 0)
            sampleBlockers.Add("CAD_PRIMARY_BLOCKING_ISSUE_PRESENT");
        if (first.ElapsedSeconds > MaximumPerSampleSeconds)
            sampleBlockers.Add("CAD_PRIMARY_CONVERSION_TIMEOUT_THRESHOLD_EXCEEDED");
        var orderedBlockers = sampleBlockers.Distinct(StringComparer.Ordinal)
            .OrderBy(item => item, StringComparer.Ordinal)
            .ToArray();
        return new AutoCadCandidateGoldenDatasetSampleResultV1(
            sample.SampleId,
            sample.SampleRef,
            sample.Split,
            sample.LayoutFamily,
            sample.SourceFormat == SpaceCadSourceFormat.Dwg ? "DWG" : "DXF",
            sample.SourceSha256,
            sample.SourceSizeBytes,
            first.ElapsedSeconds,
            replay.ElapsedSeconds,
            first.Response.PackageSha256,
            package.Summary.LayerCount,
            package.Summary.BlockCount,
            package.Summary.EntityCount,
            package.Summary.SupportedEntityCount,
            package.Summary.UnsupportedEntityCount,
            package.Summary.MissingSourceRefCount,
            package.Issues.Count,
            blockingIssues,
            first.Response.PackageSha256.Equals(
                replay.Response.PackageSha256,
                StringComparison.Ordinal),
            orderedBlockers.Length == 0,
            orderedBlockers);
    }

    private static async Task<ConversionResult> ConvertAsync(
        string sourcePath,
        ControlledSample sample,
        AutoCadCandidateConversionService service,
        CancellationToken cancellationToken)
    {
        var request = new SpaceCadWorkerConversionRequestV2(
            SpaceCadWorkerProtocolVersions.SchemaVersion,
            Guid.NewGuid(),
            sample.SourceSha256,
            sample.SourceFormat,
            service.ProviderKey,
            service.ProviderVersion,
            service.WorkerReleaseSha256);
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var stopwatch = Stopwatch.StartNew();
        var response = await service.ConvertAsync(request, source, cancellationToken);
        stopwatch.Stop();
        SpaceCadWorkerProtocol.ValidateResponse(request, response);
        return new ConversionResult(
            response,
            Math.Round(stopwatch.Elapsed.TotalSeconds, 3));
    }

    private static ControlledManifest ParseManifest(JsonElement root)
    {
        RequireObject(root, "$");
        var schemaVersion = RequiredInt32(root, "schemaVersion", "$");
        var programId = RequiredString(root, "programId", "$");
        var deliveryMode = RequiredString(root, "deliveryMode", "$");
        var evidenceClass = RequiredString(root, "evidenceClass", "$");
        var conclusion = RequiredString(root, "conclusion", "$");
        var dataset = RequiredProperty(root, "dataset", "$");
        RequireObject(dataset, "$.dataset");
        var datasetVersion = RequiredString(dataset, "datasetVersion", "$.dataset");
        var eligibilityBasis = RequiredString(
            dataset,
            "eligibilityBasis",
            "$.dataset");
        var goldenDatasetSha256 = NormalizeSha256(
            RequiredString(dataset, "goldenDatasetSha256", "$.dataset"),
            "Golden dataset SHA-256");
        var sourceSetSha256 = NormalizeSha256(
            RequiredString(dataset, "sourceSetSha256", "$.dataset"),
            "Source set SHA-256");
        var immutable = RequiredBoolean(dataset, "isImmutable", "$.dataset");
        var rawCadCommitted = RequiredBoolean(
            dataset,
            "rawCadCommittedToGit",
            "$.dataset");
        var samplesElement = RequiredProperty(dataset, "samples", "$.dataset");
        if (samplesElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("$.dataset.samples must be an array.");
        var samples = new List<ControlledSample>();
        var index = 0;
        foreach (var item in samplesElement.EnumerateArray())
        {
            var path = $"$.dataset.samples[{index}]";
            RequireObject(item, path);
            var formatText = RequiredString(item, "sourceFormat", path);
            var sourceFormat = formatText switch
            {
                "DWG" => SpaceCadSourceFormat.Dwg,
                "DXF" => SpaceCadSourceFormat.Dxf,
                _ => throw new InvalidDataException(
                    $"{path}.sourceFormat must be DWG or DXF."),
            };
            samples.Add(new ControlledSample(
                RequiredString(item, "sampleId", path),
                RequiredString(item, "sampleRef", path),
                NormalizeSha256(
                    RequiredString(item, "sourceSha256", path),
                    $"{path} source SHA-256"),
                RequiredInt64(item, "sourceSizeBytes", path),
                sourceFormat,
                RequiredString(item, "cadVersion", path),
                RequiredString(item, "split", path),
                RequiredString(item, "layoutFamily", path),
                RequiredString(item, "license", path),
                RequiredBoolean(item, "usedForTuning", path),
                RequiredString(item, "unit", path),
                RequiredString(item, "coordinateSystem", path)));
            index++;
        }
        return new ControlledManifest(
            schemaVersion,
            programId,
            deliveryMode,
            evidenceClass,
            conclusion,
            datasetVersion,
            eligibilityBasis,
            goldenDatasetSha256,
            sourceSetSha256,
            immutable,
            rawCadCommitted,
            samples);
    }

    private static void ValidateManifest(ControlledManifest manifest)
    {
        if (manifest.SchemaVersion != 3 ||
            manifest.ProgramId != "CP6_SPACE_STUDIO_V1_CORE_GA" ||
            manifest.DeliveryMode != "SoloDeveloper" ||
            manifest.EvidenceClass != "AUTHORIZED_GOLDEN_CAD_CANDIDATES" ||
            manifest.Conclusion != "Pass" ||
            manifest.EligibilityBasis != "ApprovedOriginalWork" ||
            !manifest.IsImmutable ||
            manifest.RawCadCommittedToGit)
        {
            throw new InvalidDataException(
                "The controlled golden dataset identity is not release eligible.");
        }
        if (manifest.Samples.Count != ExpectedSampleCount)
            throw new InvalidDataException("Exactly 20 controlled CAD samples are required.");
        if (manifest.Samples.Select(item => item.SampleId)
                .Distinct(StringComparer.Ordinal).Count() != ExpectedSampleCount ||
            manifest.Samples.Select(item => item.SampleRef)
                .Distinct(StringComparer.Ordinal).Count() != ExpectedSampleCount ||
            manifest.Samples.Select(item => item.SourceSha256)
                .Distinct(StringComparer.Ordinal).Count() != ExpectedSampleCount)
        {
            throw new InvalidDataException(
                "Controlled sample IDs, references and source hashes must be unique.");
        }
        foreach (var sample in manifest.Samples)
        {
            if (!SampleIdPattern.IsMatch(sample.SampleId) ||
                !SampleRefPattern.IsMatch(sample.SampleRef) ||
                sample.SourceSizeBytes <= 0 ||
                sample.CadVersion != "AC1032" ||
                sample.License != "ApprovedOriginalWork" ||
                sample.Unit != "Millimeter" ||
                sample.CoordinateSystem != SpaceCadIrVersions.CoordinateSystem ||
                sample.LayoutFamily is not ("L1" or "L2" or "L3" or "L4" or "L5") ||
                sample.Split is not ("Calibration" or "Validation" or "ReleaseHoldout") ||
                (sample.Split == "ReleaseHoldout" && sample.UsedForTuning))
            {
                throw new InvalidDataException(
                    $"Controlled sample {sample.SampleId} is not release eligible.");
            }
        }
        if (manifest.Samples.Count(item => item.SourceFormat == SpaceCadSourceFormat.Dwg) != 10 ||
            manifest.Samples.Count(item => item.SourceFormat == SpaceCadSourceFormat.Dxf) != 10 ||
            manifest.Samples.Count(item => item.Split == "Calibration") != 10 ||
            manifest.Samples.Count(item => item.Split == "Validation") != 5 ||
            manifest.Samples.Count(item => item.Split == "ReleaseHoldout") != 5 ||
            new[] { "L1", "L2", "L3", "L4", "L5" }.Any(
                family => manifest.Samples.Count(item => item.LayoutFamily == family) != 4))
        {
            throw new InvalidDataException(
                "The controlled dataset must be 10 DWG/10 DXF, 10/5/5 and L1-L5 x4.");
        }
        var sourceSetPayload = string.Join(
            "\n",
            manifest.Samples
                .OrderBy(item => item.SampleRef, StringComparer.Ordinal)
                .Select(item => item.SampleRef + ":" + item.SourceSha256));
        var sourceSetSha256 = Sha256(Encoding.UTF8.GetBytes(sourceSetPayload));
        if (!FixedTimeSha256Equals(sourceSetSha256, manifest.SourceSetSha256))
            throw new InvalidDataException("The controlled source-set hash is invalid.");
    }

    private static AutoCadCandidateGoldenDatasetEnvironmentV1 EnvironmentEvidence() =>
        new(
            AutoCadCandidateReleaseIdentity.CurrentRuntimeIdentifier(),
            Environment.OSVersion.VersionString,
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSArchitecture.ToString(),
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            EvaluationTimeoutSeconds,
            EvaluationConcurrency,
            "SealedWorkerDirectContractEvaluation",
            NetworkListenerStarted: false,
            "NotVerifiedAtOsBoundary",
            BusinessIdentityExcludedByProtocol: true,
            "EphemeralDeleteInFinally");

    private static string FrozenEnvironmentSha256(
        AutoCadCandidateGoldenDatasetEnvironmentV1 environment,
        AutoCadCandidateReleaseIdentity releaseIdentity)
    {
        var payload = string.Join(
            "\n",
            environment.RuntimeIdentifier,
            environment.OperatingSystem,
            environment.Framework,
            environment.OsArchitecture,
            environment.ProcessArchitecture,
            environment.ProcessorCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            environment.ConversionTimeoutSeconds.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            environment.MaximumConcurrency.ToString(
                System.Globalization.CultureInfo.InvariantCulture),
            environment.ExecutionMode,
            environment.NetworkListenerStarted.ToString(),
            environment.OutboundNetworkPolicy,
            environment.BusinessIdentityExcludedByProtocol.ToString(),
            environment.RawCadRetentionMode,
            releaseIdentity.WorkerReleaseSha256,
            releaseIdentity.Manifest.AutoCadCoreConsoleSha256,
            releaseIdentity.Manifest.SourceCommit);
        return Sha256(Encoding.UTF8.GetBytes(payload));
    }

    private static double Percentile(double[] values, double percentile)
    {
        if (values.Length == 0)
            return 0;
        var ordered = values.OrderBy(item => item).ToArray();
        var index = Math.Clamp(
            (int)Math.Ceiling(percentile * ordered.Length) - 1,
            0,
            ordered.Length - 1);
        return Math.Round(ordered[index], 3);
    }

    private static int CountAttemptDirectories(string workRoot)
    {
        var attempts = Path.Combine(workRoot, "attempts");
        return Directory.Exists(attempts)
            ? Directory.EnumerateDirectories(attempts).Count()
            : 0;
    }

    private static int CountResidualRawCadFiles(string workRoot)
    {
        if (!Directory.Exists(workRoot))
            return 0;
        return Directory.EnumerateFiles(workRoot, "*", SearchOption.AllDirectories)
            .Count(path => Path.GetExtension(path) is ".dwg" or ".dxf" or ".DWG" or ".DXF");
    }

    private static string RequireDirectory(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException($"{label} is required.");
        var fullPath = Path.GetFullPath(path);
        if (!Directory.Exists(fullPath))
            throw new DirectoryNotFoundException($"{label} does not exist: {fullPath}");
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label} cannot be a reparse point.");
        return fullPath;
    }

    private static string RequireContainedFile(
        string root,
        string path,
        string label)
    {
        var fullPath = Path.GetFullPath(path);
        var prefix = root.TrimEnd(
                         Path.DirectorySeparatorChar,
                         Path.AltDirectorySeparatorChar)
                     + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(
                prefix,
                OperatingSystem.IsWindows()
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{label} escapes the controlled root.");
        }
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"{label} does not exist.", fullPath);
        if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException($"{label} cannot be a reparse point.");
        var parent = new DirectoryInfo(Path.GetDirectoryName(fullPath)!);
        while (parent.FullName.Length >= root.Length)
        {
            if ((parent.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException($"{label} has a reparse-point parent.");
            if (parent.FullName.Equals(
                    root,
                    OperatingSystem.IsWindows()
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                break;
            }
            parent = parent.Parent ?? throw new InvalidDataException(
                $"{label} has no controlled parent.");
        }
        return fullPath;
    }

    private static void RejectDuplicateProperties(
        JsonElement element,
        string path,
        int depth)
    {
        if (depth > 64)
            throw new InvalidDataException("The controlled Manifest is too deeply nested.");
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidDataException(
                        $"Duplicate JSON property at {path}: {property.Name}");
                RejectDuplicateProperties(
                    property.Value,
                    path + "." + property.Name,
                    depth + 1);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in element.EnumerateArray())
            {
                RejectDuplicateProperties(item, $"{path}[{index}]", depth + 1);
                index++;
            }
        }
    }

    private static JsonElement RequiredProperty(
        JsonElement element,
        string name,
        string path) =>
        element.TryGetProperty(name, out var value)
            ? value
            : throw new InvalidDataException($"{path}.{name} is required.");

    private static string RequiredString(
        JsonElement element,
        string name,
        string path)
    {
        var value = RequiredProperty(element, name, path);
        if (value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
        {
            throw new InvalidDataException($"{path}.{name} must be a string.");
        }
        return value.GetString()!.Trim();
    }

    private static int RequiredInt32(
        JsonElement element,
        string name,
        string path)
    {
        var value = RequiredProperty(element, name, path);
        if (!value.TryGetInt32(out var result))
            throw new InvalidDataException($"{path}.{name} must be an integer.");
        return result;
    }

    private static long RequiredInt64(
        JsonElement element,
        string name,
        string path)
    {
        var value = RequiredProperty(element, name, path);
        if (!value.TryGetInt64(out var result))
            throw new InvalidDataException($"{path}.{name} must be an integer.");
        return result;
    }

    private static bool RequiredBoolean(
        JsonElement element,
        string name,
        string path)
    {
        var value = RequiredProperty(element, name, path);
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidDataException($"{path}.{name} must be a Boolean.");
        return value.GetBoolean();
    }

    private static void RequireObject(JsonElement element, string path)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidDataException($"{path} must be an object.");
    }

    private static string NormalizeSha256(string value, string label)
    {
        var normalized = value.Trim();
        if (normalized.Length != 64 ||
            normalized.Any(character => character is not (>= '0' and <= '9')
                and not (>= 'a' and <= 'f')))
        {
            throw new InvalidDataException($"{label} must be lowercase SHA-256.");
        }
        return normalized;
    }

    private static async Task<string> FileSha256Async(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var algorithm = SHA256.Create();
        var bytes = await algorithm.ComputeHashAsync(stream, cancellationToken);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string Sha256(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static bool FixedTimeSha256Equals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(left),
            Convert.FromHexString(right));

    private sealed record ControlledManifest(
        int SchemaVersion,
        string ProgramId,
        string DeliveryMode,
        string EvidenceClass,
        string Conclusion,
        string DatasetVersion,
        string EligibilityBasis,
        string GoldenDatasetSha256,
        string SourceSetSha256,
        bool IsImmutable,
        bool RawCadCommittedToGit,
        IReadOnlyList<ControlledSample> Samples);

    private sealed record ControlledSample(
        string SampleId,
        string SampleRef,
        string SourceSha256,
        long SourceSizeBytes,
        SpaceCadSourceFormat SourceFormat,
        string CadVersion,
        string Split,
        string LayoutFamily,
        string License,
        bool UsedForTuning,
        string Unit,
        string CoordinateSystem);

    private sealed record ConversionResult(
        SpaceCadWorkerConversionResponseV2 Response,
        double ElapsedSeconds);
}
