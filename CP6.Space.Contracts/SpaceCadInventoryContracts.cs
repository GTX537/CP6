namespace CP6.Space.Contracts;

public static class SpaceCadInventoryVersions
{
    public const int SchemaVersion = 1;
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;
}

public sealed record SpaceCadLayerInventoryV1(
    string LayerId,
    string Name,
    string? Color,
    string? LineType,
    bool IsVisible,
    long EntityCount,
    long SupportedEntityCount,
    long UnsupportedEntityCount,
    long BlockReferenceCount,
    long AttributedEntityCount,
    IReadOnlyDictionary<string, long> EntityTypeCounts,
    SpaceCadBoundsV1? Bounds);

public sealed record SpaceCadBlockAttributeInventoryV1(
    string Name,
    long ReferenceCount,
    long DistinctValueCount);

public sealed record SpaceCadBlockInventoryV1(
    string BlockId,
    string Name,
    bool IsDefined,
    bool IsExternalReference,
    string? ExternalReferenceToken,
    long DefinitionEntityCount,
    long ReferenceCount,
    long AttributedReferenceCount,
    IReadOnlyList<SpaceCadBlockAttributeInventoryV1> Attributes,
    SpaceCadBoundsV1? ReferenceBounds);

public sealed record SpaceCadBlockReferenceInventoryV1(
    string SourceRef,
    string BlockName,
    string LayerId,
    bool IsSupported,
    IReadOnlyDictionary<string, string> Attributes,
    SpaceCadBoundsV1? Bounds);

public sealed record SpaceCadInventorySummaryV1(
    long LayerCount,
    long EmptyLayerCount,
    long BlockCount,
    long UndefinedBlockCount,
    long BlockReferenceCount,
    long AttributedBlockReferenceCount,
    long EntityCount,
    long SupportedEntityCount,
    long UnsupportedEntityCount,
    SpaceCadBoundsV1? Bounds);

public sealed record SpaceCadInventoryV1(
    int SchemaVersion,
    string SourceSha256,
    string CoordinateTransformSha256,
    Guid FloorLogicalId,
    string FloorCode,
    IReadOnlyList<SpaceCadLayerInventoryV1> Layers,
    IReadOnlyList<SpaceCadBlockInventoryV1> Blocks,
    IReadOnlyList<SpaceCadBlockReferenceInventoryV1> BlockReferences,
    SpaceCadInventorySummaryV1 Summary,
    string InventorySha256);

public sealed record SpaceCadLayerInventoryQueryV1(
    string? Search = null,
    bool? IsVisible = null,
    SpaceCadIrEntityType? EntityType = null,
    bool IncludeEmpty = true,
    int Offset = 0,
    int Limit = SpaceCadInventoryVersions.DefaultPageSize);

public sealed record SpaceCadBlockInventoryQueryV1(
    string? Search = null,
    bool? IsExternalReference = null,
    string? AttributeName = null,
    int Offset = 0,
    int Limit = SpaceCadInventoryVersions.DefaultPageSize);

public sealed record SpaceCadBlockReferenceInventoryQueryV1(
    string? LayerId = null,
    string? BlockName = null,
    string? AttributeName = null,
    string? AttributeValue = null,
    int Offset = 0,
    int Limit = SpaceCadInventoryVersions.DefaultPageSize);

public sealed record SpaceCadInventoryPageV1<T>(
    int Offset,
    int Limit,
    long TotalCount,
    IReadOnlyList<T> Items);
