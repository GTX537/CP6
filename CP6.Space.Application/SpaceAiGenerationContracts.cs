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
    IReadOnlyList<string> RelationSourceKeys);

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
        if (policy == SpaceAiDataPolicy.Disabled)
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
        Features = Copy(features, nameof(features));
        MappingHints = Copy(mappingHints, nameof(mappingHints));
        LockedFacts = Copy(lockedFacts, nameof(lockedFacts));
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

    private static IReadOnlyList<T> Copy<T>(
        IReadOnlyList<T> source,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(source, parameterName);
        return source.ToArray();
    }
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
