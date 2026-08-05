using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CP6.Space.Application;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseGenerationProviderFailureKind
{
    Unavailable = 0,
    Timeout = 1,
    RateLimited = 2,
    ContractViolation = 3,
}

public sealed class WarehouseGenerationProviderException : Exception
{
    public WarehouseGenerationProviderException(
        WarehouseGenerationProviderFailureKind failureKind)
        : base(SafeMessage(failureKind))
    {
        if (!Enum.IsDefined(failureKind))
            throw new ArgumentOutOfRangeException(nameof(failureKind));
        FailureKind = failureKind;
    }

    public WarehouseGenerationProviderFailureKind FailureKind { get; }

    public bool CanFallback => FailureKind is
        WarehouseGenerationProviderFailureKind.Unavailable
        or WarehouseGenerationProviderFailureKind.Timeout
        or WarehouseGenerationProviderFailureKind.RateLimited;

    public string StableCode => FailureKind switch
    {
        WarehouseGenerationProviderFailureKind.Unavailable =>
            "AI_PROVIDER_UNAVAILABLE",
        WarehouseGenerationProviderFailureKind.Timeout =>
            "AI_PROVIDER_TIMEOUT",
        WarehouseGenerationProviderFailureKind.RateLimited =>
            "AI_PROVIDER_RATE_LIMITED",
        WarehouseGenerationProviderFailureKind.ContractViolation =>
            "AI_PROVIDER_CONTRACT_VIOLATION",
        _ => throw new ArgumentOutOfRangeException(nameof(FailureKind)),
    };

    private static string SafeMessage(
        WarehouseGenerationProviderFailureKind failureKind) =>
        failureKind switch
        {
            WarehouseGenerationProviderFailureKind.Unavailable =>
                "The warehouse generation provider is unavailable.",
            WarehouseGenerationProviderFailureKind.Timeout =>
                "The warehouse generation provider timed out.",
            WarehouseGenerationProviderFailureKind.RateLimited =>
                "The warehouse generation provider is rate limited.",
            WarehouseGenerationProviderFailureKind.ContractViolation =>
                "The warehouse generation provider contract was violated.",
            _ => throw new ArgumentOutOfRangeException(nameof(failureKind)),
        };
}

public sealed class FallbackWarehouseGenerationProvider(
    IWarehouseGenerationProvider primary,
    IWarehouseGenerationProvider fallback) : IWarehouseGenerationProvider
{
    private readonly IWarehouseGenerationProvider _primary =
        primary ?? throw new ArgumentNullException(nameof(primary));
    private readonly IWarehouseGenerationProvider _fallback =
        fallback ?? throw new ArgumentNullException(nameof(fallback));

    public async Task<WarehouseGenerationResult> GenerateAsync(
        WarehouseGenerationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        try
        {
            return await _primary.GenerateAsync(input, cancellationToken);
        }
        catch (WarehouseGenerationProviderException exception)
            when (exception.CanFallback && !cancellationToken.IsCancellationRequested)
        {
            var result = await _fallback.GenerateAsync(input, cancellationToken);
            ArgumentNullException.ThrowIfNull(result);
            ArgumentNullException.ThrowIfNull(result.Diagnostics);
            var diagnostics = result.Diagnostics
                .Take(999)
                .Append(new WarehouseGenerationDiagnostic(
                    $"{exception.StableCode}_FALLBACK",
                    WarehouseDiagnosticSeverity.Warning))
                .ToArray();
            return result with
            {
                Usage = result.Usage with
                {
                    OutputUnits = checked(result.Usage.OutputUnits + 1),
                },
                Diagnostics = diagnostics,
            };
        }
    }
}

public sealed class MockWarehouseGenerationProvider :
    IWarehouseGenerationProvider
{
    public Task<WarehouseGenerationResult> GenerateAsync(
        WarehouseGenerationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var suggestions = input.Features
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal)
            .Take(input.Limits.MaxSuggestions)
            .Select(item => new WarehouseGenerationSuggestion(
                item.SourceKey,
                MockType(item.CadEntityType),
                0.5m,
                new WarehouseSuggestionAttributes(),
                Relations(item, input.Limits.MaxRelationsPerSuggestion, 0.5m),
                Evidence(item)))
            .ToArray();
        return Task.FromResult(Result(
            input,
            "mock",
            "cp6-mock-v1",
            suggestions,
            [
                new WarehouseGenerationDiagnostic(
                    "MOCK_PROVIDER_ACTIVE",
                    WarehouseDiagnosticSeverity.Info),
            ]));
    }

    private static WarehouseSpaceType MockType(WarehouseCadEntityType type) =>
        type switch
        {
            WarehouseCadEntityType.Line => WarehouseSpaceType.Wall,
            WarehouseCadEntityType.Polyline => WarehouseSpaceType.Aisle,
            WarehouseCadEntityType.ClosedPolyline => WarehouseSpaceType.Zone,
            WarehouseCadEntityType.Circle => WarehouseSpaceType.Column,
            WarehouseCadEntityType.Arc => WarehouseSpaceType.Door,
            WarehouseCadEntityType.BlockReference => WarehouseSpaceType.Rack,
            WarehouseCadEntityType.TextToken => WarehouseSpaceType.Ignore,
            WarehouseCadEntityType.Unknown => WarehouseSpaceType.Unknown,
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

    internal static WarehouseGenerationResult Result(
        WarehouseGenerationInput input,
        string requestPrefix,
        string model,
        IReadOnlyList<WarehouseGenerationSuggestion> suggestions,
        IReadOnlyList<WarehouseGenerationDiagnostic> diagnostics)
    {
        var inputHash = ComputeInputHash(input);
        return new WarehouseGenerationResult(
            WarehouseGenerationInput.CurrentSchemaVersion,
            $"{requestPrefix}-{inputHash[..32]}",
            model,
            new WarehouseGenerationUsage(
                input.Features.Count
                + input.MappingHints.Count
                + input.LockedFacts.Count,
                suggestions.Count + diagnostics.Count),
            suggestions,
            diagnostics);
    }

    internal static WarehouseSuggestionRelation[] Relations(
        WarehouseGenerationFeature feature,
        int maximum,
        decimal confidence) =>
        feature.RelationSourceKeys
            .Order(StringComparer.Ordinal)
            .Take(maximum)
            .Select(target => new WarehouseSuggestionRelation(
                WarehouseRelationType.AdjacentTo,
                target,
                confidence))
            .ToArray();

    internal static WarehouseEvidenceCode[] Evidence(
        WarehouseGenerationFeature feature)
    {
        var evidence = new List<WarehouseEvidenceCode>
        {
            feature.BlockToken is null
                ? WarehouseEvidenceCode.LAYER_NAME
                : WarehouseEvidenceCode.BLOCK_NAME,
        };
        if (feature.AttributeKeyTokens.Count > 0)
            evidence.Add(WarehouseEvidenceCode.ATTRIBUTE_KEY);
        if (feature.RepetitionGroup is not null)
            evidence.Add(WarehouseEvidenceCode.REPETITION_PATTERN);
        if (feature.RelationSourceKeys.Count > 0)
            evidence.Add(WarehouseEvidenceCode.ADJACENCY);
        return evidence.Distinct().Order().ToArray();
    }

    private static string ComputeInputHash(WarehouseGenerationInput input)
    {
        var json = JsonSerializer.Serialize(input);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }
}

public sealed class LocalHeuristicWarehouseGenerationProvider :
    IWarehouseGenerationProvider
{
    public Task<WarehouseGenerationResult> GenerateAsync(
        WarehouseGenerationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        var suggestions = new List<WarehouseGenerationSuggestion>();
        var diagnostics = new List<WarehouseGenerationDiagnostic>();
        foreach (var feature in input.Features
            .OrderBy(item => item.SourceKey, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryInfer(feature, out var type, out var attributes))
            {
                if (diagnostics.Count < 1_000)
                {
                    diagnostics.Add(new WarehouseGenerationDiagnostic(
                        "LOCAL_HEURISTIC_NO_MATCH",
                        WarehouseDiagnosticSeverity.Info,
                        feature.SourceKey));
                }
                continue;
            }
            if (suggestions.Count >= input.Limits.MaxSuggestions)
                continue;
            suggestions.Add(new WarehouseGenerationSuggestion(
                feature.SourceKey,
                type,
                0.96m,
                attributes,
                MockWarehouseGenerationProvider.Relations(
                    feature,
                    input.Limits.MaxRelationsPerSuggestion,
                    0.75m),
                MockWarehouseGenerationProvider.Evidence(feature)));
        }
        return Task.FromResult(MockWarehouseGenerationProvider.Result(
            input,
            "local",
            "cp6-local-heuristic-v1",
            suggestions,
            diagnostics));
    }

    private static bool TryInfer(
        WarehouseGenerationFeature feature,
        out WarehouseSpaceType type,
        out WarehouseSuggestionAttributes attributes)
    {
        var categories = Categories(feature.LayerToken)
            .Concat(Categories(feature.BlockToken))
            .ToHashSet(StringComparer.Ordinal);
        var zonePurpose = ZonePurpose(categories);
        var equipmentType = EquipmentType(categories);
        type = categories switch
        {
            _ when categories.Overlaps(["rack", "shelf"]) =>
                WarehouseSpaceType.Rack,
            _ when categories.Contains("aisle")
                || categories.Contains("passage") => WarehouseSpaceType.Aisle,
            _ when categories.Contains("wall") => WarehouseSpaceType.Wall,
            _ when categories.Contains("column") => WarehouseSpaceType.Column,
            _ when categories.Contains("door") => WarehouseSpaceType.Door,
            _ when categories.Contains("dock") => WarehouseSpaceType.Dock,
            _ when equipmentType is not null => WarehouseSpaceType.StaticEquipment,
            _ when categories.Contains("zone") || zonePurpose is not null =>
                WarehouseSpaceType.Zone,
            _ when categories.Contains("floor") => WarehouseSpaceType.Floor,
            _ => WarehouseSpaceType.Unknown,
        };
        attributes = new WarehouseSuggestionAttributes(
            ZonePurpose: type == WarehouseSpaceType.Zone ? zonePurpose : null,
            RackType: type == WarehouseSpaceType.Rack
                ? WarehouseRackType.Unknown
                : null,
            DoorType: type == WarehouseSpaceType.Door
                ? DoorType(categories)
                : null,
            DockType: type == WarehouseSpaceType.Dock
                ? DockType(categories)
                : null,
            EquipmentType: type == WarehouseSpaceType.StaticEquipment
                ? equipmentType
                : null);
        return type != WarehouseSpaceType.Unknown;
    }

    private static IEnumerable<string> Categories(string? token)
    {
        if (token is null) return [];
        var firstSeparator = token.IndexOf('-');
        var lastSeparator = token.LastIndexOf('-');
        if (firstSeparator < 0 || lastSeparator <= firstSeparator)
            return [];
        return token[(firstSeparator + 1)..lastSeparator]
            .Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Where(item => item.Length <= 32);
    }

    private static WarehouseZonePurpose? ZonePurpose(
        IReadOnlySet<string> categories)
    {
        if (categories.Contains("receiving"))
            return WarehouseZonePurpose.Receiving;
        if (categories.Contains("storage"))
            return WarehouseZonePurpose.Storage;
        if (categories.Contains("picking"))
            return WarehouseZonePurpose.Picking;
        if (categories.Contains("packing"))
            return WarehouseZonePurpose.Packing;
        if (categories.Contains("shipping"))
            return WarehouseZonePurpose.Shipping;
        if (categories.Contains("staging"))
            return WarehouseZonePurpose.Staging;
        if (categories.Contains("passage"))
            return WarehouseZonePurpose.Passage;
        return null;
    }

    private static WarehouseEquipmentType? EquipmentType(
        IReadOnlySet<string> categories)
    {
        if (categories.Contains("conveyor"))
            return WarehouseEquipmentType.Conveyor;
        if (categories.Contains("agv")) return WarehouseEquipmentType.Agv;
        if (categories.Contains("forklift"))
            return WarehouseEquipmentType.Forklift;
        if (categories.Contains("workstation"))
            return WarehouseEquipmentType.Workstation;
        if (categories.Contains("scale")) return WarehouseEquipmentType.Scale;
        if (categories.Contains("charger"))
            return WarehouseEquipmentType.Charger;
        if (categories.Contains("equipment"))
            return WarehouseEquipmentType.Unknown;
        return null;
    }

    private static WarehouseDoorType DoorType(IReadOnlySet<string> categories)
    {
        if (categories.Contains("dock")) return WarehouseDoorType.Dock;
        return WarehouseDoorType.Unknown;
    }

    private static WarehouseDockType DockType(IReadOnlySet<string> categories)
    {
        if (categories.Contains("receiving")) return WarehouseDockType.Inbound;
        if (categories.Contains("shipping")) return WarehouseDockType.Outbound;
        return WarehouseDockType.Unknown;
    }
}
