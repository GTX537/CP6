using System.Text.Json.Serialization;

namespace CP6.Space.Contracts;

public sealed record SpacePortalOrganizationDto(
    Guid OrganizationId,
    string Type,
    string Code,
    string Name,
    string Role,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    long OrganizationSecurityStamp,
    long MembershipSecurityStamp);

public sealed record SpacePortalSiteDto(
    Guid SiteId,
    Guid PublishedVersionId,
    bool CanViewScene,
    bool CanViewStock,
    bool CanViewTasks,
    bool CanExport,
    string AuthorizationVersion);

public sealed record SpacePortalPublishedSceneDto(
    Guid SiteId,
    Guid PublishedVersionId,
    string AuthorizationVersion,
    IReadOnlyList<SpacePortalFloorDto> Floors);

public sealed record SpacePortalFloorDto(
    Guid LogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Level,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Elevation,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Height,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BoundaryJson,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CoordinateSystem,
    IReadOnlyList<SpacePortalZoneDto> Zones,
    IReadOnlyList<SpacePortalAisleDto> Aisles,
    IReadOnlyList<SpacePortalRackDto> Racks,
    IReadOnlyList<SpacePortalRackLevelDto> RackLevels,
    IReadOnlyList<SpacePortalLocationDto> Locations,
    IReadOnlyList<SpacePortalElementDto> Elements);

public sealed record SpacePortalZoneDto(
    Guid LogicalId,
    Guid FloorLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    short? Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PolygonJson,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Color,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CapabilityFlags);

public sealed record SpacePortalAisleDto(
    Guid LogicalId,
    Guid ZoneLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? PolygonJson,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? CenterlineJson,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    short? Direction);

public sealed record SpacePortalRackDto(
    Guid LogicalId,
    Guid FloorLogicalId,
    Guid ZoneLogicalId,
    Guid? AisleLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? TemplateVersionId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? X,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Y,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Z,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? RotationZ,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Width,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Depth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Height);

public sealed record SpacePortalRackLevelDto(
    Guid LogicalId,
    Guid RackLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? LevelNo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BottomZ,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ClearHeight,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BinCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DepthCount,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? CellWidth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? CellDepth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? BeamHeight,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? MaxLoad);

public sealed record SpacePortalLocationDto(
    Guid LogicalId,
    Guid FloorLogicalId,
    Guid? RackLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Code,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? ColumnNo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? LevelNo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? DepthNo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Width,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Height,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? Depth,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? MaxLoad,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ExternalBindingState);

public sealed record SpacePortalElementDto(
    Guid LogicalId,
    Guid FloorLogicalId,
    Guid? ParentLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? GeometryJson,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? ModelAssetId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ModelAssetScope,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? BusinessCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LinkedEntityType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? LinkedLogicalId);

public sealed record SpacePortalRuntimeSourceDto(
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ReceivedAtUtc,
    long DelayMilliseconds,
    bool IsAvailable);

public sealed record SpacePortalStockResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string AuthorizationVersion,
    SpacePortalRuntimeSourceDto Source,
    IReadOnlyList<SpacePortalStockItemDto> Items);

public sealed record SpacePortalStockItemDto(
    Guid LocationLogicalId,
    Guid FloorLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SpaceLocationCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? WmsLocationCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FloorCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FloorName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? FloorLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? PhysicalQuantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? AllocatedQuantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MaterialNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LotNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ContainerNumber,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? OwnerId);

public sealed record SpacePortalTaskResponse(
    Guid SiteId,
    Guid PublishedVersionId,
    string AuthorizationVersion,
    SpacePortalRuntimeSourceDto Source,
    IReadOnlyList<SpacePortalTaskItemDto> Items);

public sealed record SpacePortalTaskItemDto(
    Guid LocationLogicalId,
    Guid FloorLogicalId,
    Guid? ZoneLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TaskId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? TaskType,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? SequenceNo,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? SpaceLocationCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? WmsLocationCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FloorCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? FloorName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? FloorLevel,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ZoneCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    Guid? RackLogicalId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? RackCode,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? AnchorXMillimeters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? AnchorYMillimeters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    double? AnchorZMillimeters,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    decimal? Quantity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? MaterialNumber);
