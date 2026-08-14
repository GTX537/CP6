namespace CP6.Space.Contracts;

public static class SpaceDesignSceneContract
{
    public const int SchemaVersion = 1;
    public const string Authority = "DesignRevision";
}

public sealed record SpaceDesignSceneDto(
    int SchemaVersion,
    string Authority,
    bool RuntimeOverlayIncluded,
    Guid ModelVersionId,
    Guid SiteId,
    string VersionStatus,
    long ContentRevision,
    string? ContentHash,
    SpaceSceneFloorDto Floor,
    IReadOnlyList<SpaceSceneZoneDto> Zones,
    IReadOnlyList<SpaceSceneAisleDto> Aisles,
    IReadOnlyList<SpaceSceneRackDto> Racks,
    IReadOnlyList<SpaceSceneRackLevelDto> RackLevels,
    IReadOnlyList<SpaceSceneLocationDto> Locations,
    IReadOnlyList<SpaceSceneElementDto> Elements,
    IReadOnlyList<SpaceSceneElementAttributeDto> ElementAttributes,
    IReadOnlyList<SpaceSceneLocationExternalBindingDto>?
        LocationExternalBindings = null,
    IReadOnlyList<SpaceSceneDesignAttributeDto>? DesignAttributes = null);

/// <summary>
/// Immutable scene snapshot selected exclusively from a site's current
/// Published production version. Runtime inventory, personnel, and device
/// overlays are intentionally outside this contract.
/// </summary>
public sealed record SpacePublishedViewerSceneDto(
    int SchemaVersion,
    string Authority,
    bool RuntimeOverlayIncluded,
    Guid SiteId,
    Guid PublishedVersionId,
    DateTime? PublishedAtUtc,
    long ContentRevision,
    string? ContentHash,
    IReadOnlyList<SpaceDesignSceneDto> Floors);

public sealed record SpaceSceneRevisionDto(
    Guid RevisionId,
    Guid LogicalId,
    Guid? SourceId,
    string? SourceRef,
    string LifecycleState,
    string RowVersion);

public sealed record SpaceSceneFloorDto(
    SpaceSceneRevisionDto Revision,
    Guid SiteLogicalId,
    int Level,
    string FloorCode,
    string Name,
    int Elevation,
    int Height,
    string BoundaryJson,
    string CoordinateSystem,
    Guid? UnderlaySourceId,
    Guid? UnderlayCalibrationId,
    decimal? UnderlayScale,
    int UnderlayOffsetX,
    int UnderlayOffsetY,
    decimal UnderlayRotationZ,
    long RevisionNumber);

public sealed record SpaceSceneZoneDto(
    SpaceSceneRevisionDto Revision,
    Guid FloorLogicalId,
    string ZoneCode,
    string Name,
    short ZoneType,
    string PolygonJson,
    string? Color,
    string? CapabilityFlags);

public sealed record SpaceSceneAisleDto(
    SpaceSceneRevisionDto Revision,
    Guid ZoneLogicalId,
    string AisleCode,
    string Name,
    string PolygonJson,
    string CenterlineJson,
    short Direction);

public sealed record SpaceSceneRackDto(
    SpaceSceneRevisionDto Revision,
    Guid FloorLogicalId,
    Guid ZoneLogicalId,
    Guid? AisleLogicalId,
    string RackCode,
    string Name,
    string? RackType,
    Guid? TemplateVersionId,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Depth,
    int Height);

public sealed record SpaceSceneRackLevelDto(
    SpaceSceneRevisionDto Revision,
    Guid RackLogicalId,
    int LevelNo,
    int BottomZ,
    int ClearHeight,
    int BinCount,
    int DepthCount,
    int CellWidth,
    int CellDepth,
    int BeamHeight,
    decimal? MaxLoad);

public sealed record SpaceSceneLocationDto(
    SpaceSceneRevisionDto Revision,
    Guid FloorLogicalId,
    Guid? RackLogicalId,
    string? LocationCode,
    int ColumnNo,
    int LevelNo,
    int DepthNo,
    int Width,
    int Height,
    int Depth,
    decimal? MaxLoad,
    string CodeOrigin,
    string ExternalBindingState,
    string? LocationType = null);

public sealed record SpaceSceneLocationExternalBindingDto(
    Guid Id,
    Guid LocationLogicalId,
    string AdapterId,
    string WarehouseCode,
    string ExternalLocationId,
    string BindingMode,
    Guid SourceId,
    string SourceRef);

public sealed record SpaceSceneDesignAttributeDto(
    Guid Id,
    string ObjectType,
    Guid ObjectLogicalId,
    string Namespace,
    string Key,
    string Value,
    string? Unit,
    Guid SourceId,
    string SourceRef);

public sealed record SpaceSceneElementDto(
    SpaceSceneRevisionDto Revision,
    Guid FloorLogicalId,
    Guid? ParentLogicalId,
    string ElementType,
    string GeometryJson,
    Guid? ModelAssetId,
    string? ModelAssetScope,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Height,
    int Depth,
    string? BusinessCode,
    string? LinkedEntityType,
    Guid? LinkedLogicalId);

public sealed record SpaceSceneElementAttributeDto(
    Guid Id,
    Guid ElementRevisionId,
    string Namespace,
    string Key,
    string ValueType,
    string? Value,
    string? Unit);
