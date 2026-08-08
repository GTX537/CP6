using System.Text.Json.Serialization;

namespace CP6.Space.Application;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SpaceAiDataPolicy
{
    Disabled = 0,
    MetadataOnly = 1,
    StructuredFeatures = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseGenerationProviderKind
{
    Mock = 0,
    Local = 1,
    External = 2,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseCadEntityType
{
    Line = 0,
    Polyline = 1,
    ClosedPolyline = 2,
    Circle = 3,
    Arc = 4,
    BlockReference = 5,
    TextToken = 6,
    Unknown = 7,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseSpaceType
{
    Floor = 0,
    Zone = 1,
    Aisle = 2,
    Rack = 3,
    Wall = 4,
    Column = 5,
    Door = 6,
    Dock = 7,
    StaticEquipment = 8,
    Ignore = 9,
    Unknown = 10,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseZonePurpose
{
    Receiving = 0,
    Storage = 1,
    Picking = 2,
    Packing = 3,
    Shipping = 4,
    Staging = 5,
    Passage = 6,
    Unknown = 7,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseRackType
{
    Selective = 0,
    DriveIn = 1,
    Cantilever = 2,
    Flow = 3,
    Mobile = 4,
    Unknown = 5,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseDoorType
{
    Personnel = 0,
    Rolling = 1,
    Fire = 2,
    Dock = 3,
    Unknown = 4,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseDockType
{
    Inbound = 0,
    Outbound = 1,
    Shared = 2,
    Unknown = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseEquipmentType
{
    Conveyor = 0,
    Agv = 1,
    Forklift = 2,
    Workstation = 3,
    Scale = 4,
    Charger = 5,
    Unknown = 6,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseRelationType
{
    ParentCandidate = 0,
    AdjacentTo = 1,
    ContainedBy = 2,
    ServedByAisle = 3,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseEvidenceCode
{
    LAYER_NAME = 0,
    BLOCK_NAME = 1,
    ATTRIBUTE_KEY = 2,
    REPETITION_PATTERN = 3,
    ADJACENCY = 4,
    MAPPING_HINT = 5,
    RULE_CONFLICT = 6,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WarehouseDiagnosticSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

public sealed record WarehouseGenerationLimits(
    [property: JsonPropertyName("maxSuggestions")]
    int MaxSuggestions,
    [property: JsonPropertyName("maxRelationsPerSuggestion")]
    int MaxRelationsPerSuggestion);

public sealed record WarehouseNormalizedBounds(
    [property: JsonPropertyName("x")] decimal X,
    [property: JsonPropertyName("y")] decimal Y,
    [property: JsonPropertyName("width")] decimal Width,
    [property: JsonPropertyName("height")] decimal Height);

public sealed record WarehouseGenerationFeature(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("cadEntityType")]
    WarehouseCadEntityType CadEntityType,
    [property: JsonPropertyName("layerToken")] string LayerToken,
    [property: JsonPropertyName("blockToken")] string? BlockToken,
    [property: JsonPropertyName("entityCount")] int EntityCount,
    [property: JsonPropertyName("normalizedBounds")]
    WarehouseNormalizedBounds? NormalizedBounds,
    [property: JsonPropertyName("angleBucket")] int AngleBucket,
    [property: JsonPropertyName("repetitionGroup")]
    string? RepetitionGroup,
    [property: JsonPropertyName("attributeKeyTokens")]
    IReadOnlyList<string> AttributeKeyTokens,
    [property: JsonPropertyName("relationSourceKeys")]
    IReadOnlyList<string> RelationSourceKeys,
    [property: JsonPropertyName("aspectRatioBucket")]
    int? AspectRatioBucket = null);

public sealed record WarehouseGenerationMappingHint(
    [property: JsonPropertyName("token")] string Token,
    [property: JsonPropertyName("targetType")]
    WarehouseSpaceType TargetType,
    [property: JsonPropertyName("strength")] decimal Strength);

public sealed record WarehouseGenerationLockedFact(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("fieldPath")] string FieldPath,
    [property: JsonPropertyName("valueToken")] string ValueToken);

public sealed class WarehouseGenerationInput
{
    public const string CurrentSchemaVersion = "1.0";
    public const string GeneralRackWarehouse = "GeneralRackWarehouse";

    public WarehouseGenerationInput(
        string runCorrelationKey,
        SpaceAiDataPolicy policy,
        WarehouseGenerationLimits limits,
        IReadOnlyList<WarehouseGenerationFeature> features,
        IReadOnlyList<WarehouseGenerationMappingHint> mappingHints,
        IReadOnlyList<WarehouseGenerationLockedFact> lockedFacts)
    {
        if (string.IsNullOrWhiteSpace(runCorrelationKey) ||
            runCorrelationKey.Length is < 32 or > 128 ||
            Guid.TryParse(runCorrelationKey, out _))
        {
            throw new ArgumentException(
                "Run correlation key must be an opaque 32-128 character value.",
                nameof(runCorrelationKey));
        }
        if (!Enum.IsDefined(policy) || policy == SpaceAiDataPolicy.Disabled)
        {
            throw new ArgumentOutOfRangeException(
                nameof(policy),
                "Disabled policy must never reach a provider.");
        }
        ArgumentNullException.ThrowIfNull(limits);
        if (limits.MaxSuggestions is < 1 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limits),
                "Suggestion limit must be between 1 and 1,000,000.");
        }
        if (limits.MaxRelationsPerSuggestion is < 0 or > 32)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limits),
                "Relation limit must be between 0 and 32.");
        }

        SchemaVersion = CurrentSchemaVersion;
        RunCorrelationKey = runCorrelationKey;
        Policy = policy;
        WarehouseKind = GeneralRackWarehouse;
        Limits = limits;
        Features = ValidateFeatures(features, policy);
        MappingHints = ValidateMappingHints(mappingHints);
        LockedFacts = ValidateLockedFacts(lockedFacts, Features, policy);
    }

    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; }

    [JsonPropertyName("runCorrelationKey")]
    public string RunCorrelationKey { get; }

    [JsonPropertyName("policy")]
    public SpaceAiDataPolicy Policy { get; }

    [JsonPropertyName("warehouseKind")]
    public string WarehouseKind { get; }

    [JsonPropertyName("limits")]
    public WarehouseGenerationLimits Limits { get; }

    [JsonPropertyName("features")]
    public IReadOnlyList<WarehouseGenerationFeature> Features { get; }

    [JsonPropertyName("mappingHints")]
    public IReadOnlyList<WarehouseGenerationMappingHint> MappingHints { get; }

    [JsonPropertyName("lockedFacts")]
    public IReadOnlyList<WarehouseGenerationLockedFact> LockedFacts { get; }

    private static IReadOnlyList<WarehouseGenerationFeature> ValidateFeatures(
        IReadOnlyList<WarehouseGenerationFeature> source,
        SpaceAiDataPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(source));

        var items = source.ToArray();
        var sourceKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            ArgumentNullException.ThrowIfNull(item.AttributeKeyTokens);
            ArgumentNullException.ThrowIfNull(item.RelationSourceKeys);
            if (!IsToken(item.SourceKey)
                || !sourceKeys.Add(item.SourceKey)
                || !Enum.IsDefined(item.CadEntityType)
                || !IsToken(item.LayerToken)
                || item.BlockToken is not null && !IsToken(item.BlockToken)
                || item.EntityCount is < 1 or > 1_000_000
                || item.NormalizedBounds is { } bounds && !IsBounds(bounds)
                || item.AngleBucket is < 0 or > 35
                || item.AspectRatioBucket is < 0 or > 8
                || item.RepetitionGroup is not null
                    && !IsToken(item.RepetitionGroup)
                || item.AttributeKeyTokens.Count > 64
                || !IsUniqueTokens(item.AttributeKeyTokens)
                || item.RelationSourceKeys.Count > 32
                || !IsUniqueTokens(item.RelationSourceKeys)
                || policy == SpaceAiDataPolicy.MetadataOnly
                    && (item.NormalizedBounds is not null
                        || item.RelationSourceKeys.Count > 0))
            {
                throw new ArgumentException(
                    "Provider feature shape is invalid.",
                    nameof(source));
            }
        }
        foreach (var item in items)
        {
            if (item.RelationSourceKeys.Any(key =>
                    key.Equals(item.SourceKey, StringComparison.Ordinal)
                    || !sourceKeys.Contains(key)))
            {
                throw new ArgumentException(
                    "Provider feature relations must reference another input feature.",
                    nameof(source));
            }
        }
        return items;
    }

    private static IReadOnlyList<WarehouseGenerationMappingHint>
        ValidateMappingHints(IReadOnlyList<WarehouseGenerationMappingHint> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Count > 10_000)
            throw new ArgumentOutOfRangeException(nameof(source));
        var items = source.ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!IsToken(item.Token)
                || !Enum.IsDefined(item.TargetType)
                || item.Strength is < 0 or > 1
                || !identities.Add($"{item.Token}\n{item.TargetType}"))
            {
                throw new ArgumentException(
                    "Provider mapping hint shape is invalid.",
                    nameof(source));
            }
        }
        return items;
    }

    private static IReadOnlyList<WarehouseGenerationLockedFact>
        ValidateLockedFacts(
            IReadOnlyList<WarehouseGenerationLockedFact> source,
            IReadOnlyList<WarehouseGenerationFeature> features,
            SpaceAiDataPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (policy == SpaceAiDataPolicy.MetadataOnly && source.Count > 0)
        {
            throw new ArgumentException(
                "Metadata-only input cannot carry object-level locked facts.",
                nameof(source));
        }
        if (source.Count > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(source));
        var sourceKeys = features
            .Select(item => item.SourceKey)
            .ToHashSet(StringComparer.Ordinal);
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var items = source.ToArray();
        foreach (var item in items)
        {
            ArgumentNullException.ThrowIfNull(item);
            if (!sourceKeys.Contains(item.SourceKey)
                || !IsToken(item.FieldPath)
                || !IsToken(item.ValueToken)
                || !identities.Add($"{item.SourceKey}\n{item.FieldPath}"))
            {
                throw new ArgumentException(
                    "Provider locked fact shape is invalid.",
                    nameof(source));
            }
        }
        return items;
    }

    private static bool IsBounds(WarehouseNormalizedBounds bounds) =>
        bounds.X is >= 0 and <= 1
        && bounds.Y is >= 0 and <= 1
        && bounds.Width is > 0 and <= 1
        && bounds.Height is > 0 and <= 1
        && bounds.X + bounds.Width <= 1
        && bounds.Y + bounds.Height <= 1;

    private static bool IsUniqueTokens(IReadOnlyList<string> values)
    {
        var unique = new HashSet<string>(StringComparer.Ordinal);
        return values.All(value => IsToken(value) && unique.Add(value));
    }

    private static bool IsToken(string? value) =>
        value is { Length: > 0 and <= 256 }
        && value.Equals(value.Trim(), StringComparison.Ordinal)
        && value.All(character => character >= ' ' && character != '\u007f');
}

public sealed record WarehouseGenerationUsage(
    [property: JsonPropertyName("inputUnits")] long InputUnits,
    [property: JsonPropertyName("outputUnits")] long OutputUnits);

public sealed record WarehouseSuggestionAttributes(
    [property: JsonPropertyName("zonePurpose")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WarehouseZonePurpose? ZonePurpose = null,
    [property: JsonPropertyName("rackType")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WarehouseRackType? RackType = null,
    [property: JsonPropertyName("doorType")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WarehouseDoorType? DoorType = null,
    [property: JsonPropertyName("dockType")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WarehouseDockType? DockType = null,
    [property: JsonPropertyName("equipmentType")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    WarehouseEquipmentType? EquipmentType = null,
    [property: JsonPropertyName("semanticLabel")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SemanticLabel = null);

public sealed record WarehouseSuggestionRelation(
    [property: JsonPropertyName("relationType")]
    WarehouseRelationType RelationType,
    [property: JsonPropertyName("targetSourceKey")]
    string TargetSourceKey,
    [property: JsonPropertyName("confidence")] decimal Confidence);

public sealed record WarehouseGenerationSuggestion(
    [property: JsonPropertyName("sourceKey")] string SourceKey,
    [property: JsonPropertyName("suggestedType")]
    WarehouseSpaceType SuggestedType,
    [property: JsonPropertyName("confidence")] decimal Confidence,
    [property: JsonPropertyName("attributes")]
    WarehouseSuggestionAttributes Attributes,
    [property: JsonPropertyName("relations")]
    IReadOnlyList<WarehouseSuggestionRelation> Relations,
    [property: JsonPropertyName("evidenceCodes")]
    IReadOnlyList<WarehouseEvidenceCode> EvidenceCodes);

public sealed record WarehouseGenerationDiagnostic(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("severity")]
    WarehouseDiagnosticSeverity Severity,
    [property: JsonPropertyName("sourceKey")] string? SourceKey = null);

public sealed record WarehouseGenerationResult(
    [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
    [property: JsonPropertyName("providerRequestId")]
    string ProviderRequestId,
    [property: JsonPropertyName("providerModel")] string ProviderModel,
    [property: JsonPropertyName("usage")] WarehouseGenerationUsage Usage,
    [property: JsonPropertyName("suggestions")]
    IReadOnlyList<WarehouseGenerationSuggestion> Suggestions,
    [property: JsonPropertyName("diagnostics")]
    IReadOnlyList<WarehouseGenerationDiagnostic> Diagnostics);

public interface IWarehouseGenerationProvider
{
    Task<WarehouseGenerationResult> GenerateAsync(
        WarehouseGenerationInput input,
        CancellationToken cancellationToken);
}

/// <summary>
/// The deterministic rule path remains callable without AI policy or
/// AI permissions. E13-S07 supplies the production implementation.
/// </summary>
public interface IDeterministicWarehouseGenerationPort
{
    Task<WarehouseGenerationResult> GenerateAsync(
        WarehouseGenerationInput input,
        CancellationToken cancellationToken);
}
