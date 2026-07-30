using System.Security.Cryptography;
using System.Text.Json;

namespace CP6.Space.CadExperiment;

public sealed record CadDatasetManifest(
    string DatasetName,
    string DatasetVersion,
    int SchemaVersion,
    string Purpose,
    bool CountsTowardReleaseGate,
    string Unit,
    string CoordinateSystem,
    IReadOnlyList<CadDatasetSample> Samples,
    IReadOnlyDictionary<string, string> Files);

public sealed record CadDatasetSample(
    string SampleId,
    string LayoutFamily,
    string Split,
    string SourceFile,
    string SourceSha256,
    int ExpectedTargetCount);

public sealed record DatasetAuditGate(
    string Id,
    bool Passed,
    string Evidence);

public sealed record DatasetSampleAudit(
    string SampleId,
    string SourceFile,
    long SizeBytes,
    string ActualSha256,
    bool HashMatches,
    int ExpectedTargetCount,
    int ActualExpectedTargetCount,
    DxfProbeResult? Dxf,
    DwgHeaderProbeResult? DwgHeader);

public sealed record DatasetStressAssetAudit(
    string Role,
    string SourcePath,
    long SizeBytes,
    string ActualSha256,
    DxfProbeResult? Dxf);

public sealed record DatasetAuditReport(
    int SchemaVersion,
    DateTimeOffset GeneratedAtUtc,
    string ManifestPath,
    string DatasetName,
    string DatasetVersion,
    string Purpose,
    bool CountsTowardReleaseGate,
    bool IntegrityPassed,
    bool E02ReadinessPassed,
    IReadOnlyList<DatasetAuditGate> Gates,
    IReadOnlyList<DatasetSampleAudit> Samples,
    IReadOnlyList<DatasetStressAssetAudit> StressAssets,
    IReadOnlyList<string> Errors);

public static class DatasetAuditor
{
    private static readonly string[] RequiredDxfVersions =
    [
        "AC1009",
        "AC1015",
        "AC1021",
        "AC1027",
        "AC1032"
    ];

    private static readonly string[] RequiredDwgVersions =
    [
        "AC1015",
        "AC1021",
        "AC1027",
        "AC1032"
    ];

    private static readonly IReadOnlyDictionary<string, int> RequiredGoldenSplits =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Calibration"] = 10,
            ["Validation"] = 5,
            ["Release Holdout"] = 5
        };

    private static readonly string[] RequiredLayoutFamilies =
    [
        "L1",
        "L2",
        "L3",
        "L4",
        "L5"
    ];

    public static Task<DatasetAuditReport> AuditAsync(
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        return AuditAsync(
            manifestPath,
            stress50MiBPath: null,
            stressOneMillionPath: null,
            cancellationToken);
    }

    public static async Task<DatasetAuditReport> AuditAsync(
        string manifestPath,
        string? stress50MiBPath,
        string? stressOneMillionPath,
        CancellationToken cancellationToken = default)
    {
        var fullManifestPath = Path.GetFullPath(manifestPath);
        var packageDirectory = Path.GetDirectoryName(fullManifestPath)
            ?? throw new InvalidOperationException("The manifest directory is invalid.");
        var errors = new List<string>();

        CadDatasetManifest manifest;
        await using (var stream = File.OpenRead(fullManifestPath))
        {
            manifest = await JsonSerializer.DeserializeAsync<CadDatasetManifest>(
                    stream,
                    CadExperimentJson.Options,
                    cancellationToken)
                ?? throw new InvalidDataException("The manifest is empty.");
        }

        var expectedElements = await LoadExpectedCountsAsync(
            ResolveWithinPackage(packageDirectory, manifest.Files["expectedElements"]),
            cancellationToken);
        ValidateCompanionFiles(packageDirectory, manifest.Files, errors);

        var sampleAudits = new List<DatasetSampleAudit>();
        foreach (var sample in manifest.Samples)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourcePath = ResolveWithinPackage(packageDirectory, sample.SourceFile);
            if (!File.Exists(sourcePath))
            {
                errors.Add($"{sample.SampleId}: source file is missing.");
                continue;
            }

            var actualHash = await ComputeSha256Async(sourcePath, cancellationToken);
            var hashMatches = actualHash.Equals(
                sample.SourceSha256,
                StringComparison.OrdinalIgnoreCase);
            if (!hashMatches)
            {
                errors.Add($"{sample.SampleId}: SHA-256 does not match the manifest.");
            }

            var actualExpectedCount = expectedElements.TryGetValue(
                sample.SampleId,
                out var count)
                ? count
                : 0;
            if (actualExpectedCount != sample.ExpectedTargetCount)
            {
                errors.Add(
                    $"{sample.SampleId}: expected target count is "
                    + $"{sample.ExpectedTargetCount}, but JSONL contains {actualExpectedCount}.");
            }

            DxfProbeResult? dxf = null;
            DwgHeaderProbeResult? dwgHeader = null;
            if (Path.GetExtension(sourcePath).Equals(".dxf", StringComparison.OrdinalIgnoreCase))
            {
                dxf = DxfProbe.Inspect(sourcePath);
                foreach (var error in dxf.Errors)
                {
                    errors.Add($"{sample.SampleId}: {error}");
                }
            }
            else if (Path.GetExtension(sourcePath).Equals(".dwg", StringComparison.OrdinalIgnoreCase))
            {
                dwgHeader = DwgHeaderProbe.Inspect(sourcePath);
                foreach (var error in dwgHeader.Errors)
                {
                    errors.Add($"{sample.SampleId}: {error}");
                }
            }

            sampleAudits.Add(new DatasetSampleAudit(
                sample.SampleId,
                sample.SourceFile,
                new FileInfo(sourcePath).Length,
                actualHash,
                hashMatches,
                sample.ExpectedTargetCount,
                actualExpectedCount,
                dxf,
                dwgHeader));
        }

        var stressAssets = new List<DatasetStressAssetAudit>();
        await AuditStressAssetAsync(
            "50MiB",
            stress50MiBPath,
            stressAssets,
            errors,
            cancellationToken);
        await AuditStressAssetAsync(
            "OneMillionEntities",
            stressOneMillionPath,
            stressAssets,
            errors,
            cancellationToken);

        var integrityPassed = errors.Count == 0
            && sampleAudits.Count == manifest.Samples.Count;
        var extensions = manifest.Samples
            .Select(sample => Path.GetExtension(sample.SourceFile).ToLowerInvariant())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dxfVersions = sampleAudits
            .Select(sample => sample.Dxf?.CadVersion)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var dwgVersions = sampleAudits
            .Select(sample => sample.DwgHeader?.CadVersion)
            .Where(version => !string.IsNullOrWhiteSpace(version))
            .Cast<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var formalGoldenSamples = manifest.Samples
            .Where(sample => RequiredGoldenSplits.ContainsKey(sample.Split))
            .ToArray();
        var layoutFamilyCounts = formalGoldenSamples
            .GroupBy(
                sample => LayoutFamilyId(sample.LayoutFamily),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var splitCounts = manifest.Samples
            .GroupBy(sample => sample.Split, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Count(),
                StringComparer.OrdinalIgnoreCase);
        var largestSourceBytes = sampleAudits
            .Select(sample => sample.SizeBytes)
            .Concat(stressAssets.Select(asset => asset.SizeBytes))
            .DefaultIfEmpty()
            .Max();
        var largestEntityCount = sampleAudits
            .Select(sample => sample.Dxf?.EntityCount ?? 0)
            .Concat(stressAssets.Select(asset => asset.Dxf?.EntityCount ?? 0))
            .DefaultIfEmpty()
            .Max();
        var gates = new List<DatasetAuditGate>
        {
            new(
                "package-integrity",
                integrityPassed,
                integrityPassed
                    ? "Manifest, companion files, hashes, DXF framing, and expected counts passed."
                    : $"{errors.Count} integrity error(s) found."),
            new(
                "five-layout-families",
                RequiredLayoutFamilies.All(family => manifest.Samples.Any(
                    sample => LayoutFamilyId(sample.LayoutFamily).Equals(
                        family,
                        StringComparison.OrdinalIgnoreCase))),
                FormatCounts(
                    RequiredLayoutFamilies,
                    manifest.Samples
                        .GroupBy(
                            sample => LayoutFamilyId(sample.LayoutFamily),
                            StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(
                            group => group.Key,
                            group => group.Count(),
                            StringComparer.OrdinalIgnoreCase))),
            new(
                "formal-golden-20",
                manifest.CountsTowardReleaseGate && formalGoldenSamples.Length >= 20,
                $"{formalGoldenSamples.Length} formal golden sample(s); "
                + $"purpose={manifest.Purpose}; "
                + $"countsTowardReleaseGate={manifest.CountsTowardReleaseGate}."),
            new(
                "golden-split-distribution",
                RequiredGoldenSplits.All(required =>
                    splitCounts.TryGetValue(required.Key, out var count)
                    && count >= required.Value),
                FormatCounts(RequiredGoldenSplits.Keys, splitCounts)),
            new(
                "four-per-layout-family",
                RequiredLayoutFamilies.All(family =>
                    layoutFamilyCounts.TryGetValue(family, out var count)
                    && count >= 4),
                FormatCounts(RequiredLayoutFamilies, layoutFamilyCounts)),
            new(
                "dwg-present",
                extensions.Contains(".dwg"),
                extensions.Count == 0
                    ? "No source format found."
                    : $"Formats: {string.Join(", ", extensions.Order())}."),
            new(
                "dwg-version-header-matrix",
                RequiredDwgVersions.All(dwgVersions.Contains),
                dwgVersions.Count == 0
                    ? "No DWG version-header evidence."
                    : $"Observed headers: {string.Join(", ", dwgVersions.Order())}; "
                    + $"required: {string.Join(", ", RequiredDwgVersions)}."),
            new(
                "dxf-version-matrix",
                RequiredDxfVersions.All(dxfVersions.Contains),
                dxfVersions.Count == 0
                    ? "No DXF version evidence."
                    : $"Observed: {string.Join(", ", dxfVersions.Order())}; "
                    + $"required: {string.Join(", ", RequiredDxfVersions)}."),
            new(
                "sample-50mb",
                largestSourceBytes >= 50L * 1024 * 1024,
                $"Largest source or explicit stress asset: {largestSourceBytes} bytes."),
            new(
                "sample-1m-entities",
                largestEntityCount >= 1_000_000,
                $"Largest probed DXF entity count: {largestEntityCount}.")
        };

        return new DatasetAuditReport(
            2,
            DateTimeOffset.UtcNow,
            fullManifestPath,
            manifest.DatasetName,
            manifest.DatasetVersion,
            manifest.Purpose,
            manifest.CountsTowardReleaseGate,
            integrityPassed,
            gates.All(gate => gate.Passed),
            gates,
            sampleAudits,
            stressAssets,
            errors);
    }

    private static async Task AuditStressAssetAsync(
        string role,
        string? path,
        ICollection<DatasetStressAssetAudit> results,
        ICollection<string> errors,
        CancellationToken cancellationToken)
    {
        if (path is null)
        {
            return;
        }

        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            errors.Add($"{role} stress asset is missing: {fullPath}");
            return;
        }

        DxfProbeResult? dxf = null;
        if (Path.GetExtension(fullPath).Equals(".dxf", StringComparison.OrdinalIgnoreCase))
        {
            dxf = DxfProbe.Inspect(fullPath);
            foreach (var error in dxf.Errors)
            {
                errors.Add($"{role} stress asset: {error}");
            }
        }

        results.Add(new DatasetStressAssetAudit(
            role,
            fullPath,
            new FileInfo(fullPath).Length,
            await ComputeSha256Async(fullPath, cancellationToken),
            dxf));
    }

    private static string LayoutFamilyId(string layoutFamily)
    {
        var separatorIndex = layoutFamily.IndexOfAny(['-', '_', ' ']);
        return separatorIndex < 0
            ? layoutFamily
            : layoutFamily[..separatorIndex];
    }

    private static string FormatCounts(
        IEnumerable<string> requiredKeys,
        IReadOnlyDictionary<string, int> counts)
    {
        return string.Join(
            ", ",
            requiredKeys.Select(
                key => $"{key}={counts.GetValueOrDefault(key)}"));
    }

    private static void ValidateCompanionFiles(
        string packageDirectory,
        IReadOnlyDictionary<string, string> files,
        ICollection<string> errors)
    {
        foreach (var (id, path) in files)
        {
            try
            {
                var fullPath = ResolveWithinPackage(packageDirectory, path);
                if (!File.Exists(fullPath))
                {
                    errors.Add($"Companion file '{id}' is missing.");
                    continue;
                }

                if (Path.GetExtension(fullPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
                {
                    using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
                }
                else if (Path.GetExtension(fullPath).Equals(".jsonl", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var line in File.ReadLines(fullPath)
                                 .Where(line => !string.IsNullOrWhiteSpace(line)))
                    {
                        using var document = JsonDocument.Parse(line);
                    }
                }
            }
            catch (Exception exception) when (
                exception is InvalidDataException
                    or JsonException
                    or UnauthorizedAccessException
                    or IOException)
            {
                errors.Add($"Companion file '{id}' is invalid: {exception.Message}");
            }
        }
    }

    private static async Task<IReadOnlyDictionary<string, int>> LoadExpectedCountsAsync(
        string path,
        CancellationToken cancellationToken)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in await File.ReadAllLinesAsync(path, cancellationToken))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            using var document = JsonDocument.Parse(line);
            var sampleId = document.RootElement.GetProperty("sampleId").GetString()
                ?? throw new InvalidDataException("Expected element has no sampleId.");
            counts[sampleId] = counts.TryGetValue(sampleId, out var count)
                ? count + 1
                : 1;
        }

        return counts;
    }

    private static string ResolveWithinPackage(string packageDirectory, string relativePath)
    {
        var packageRoot = Path.GetFullPath(packageDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        var candidate = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
        if (!candidate.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Path '{relativePath}' escapes the dataset package.");
        }

        return candidate;
    }

    public static async Task<string> ComputeSha256Async(
        string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
