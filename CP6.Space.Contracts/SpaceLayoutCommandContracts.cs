namespace CP6.Space.Contracts;

public static class SpaceLayoutCommandContract
{
    public const int SchemaVersion = 1;
    public const string CreateZone = "CreateZone";
    public const string CreateAisle = "CreateAisle";
    public const string CreateRack = "CreateRack";
}

public sealed record SpaceCreateLayoutZoneDto(
    string ZoneCode,
    string? Name,
    short ZoneType,
    string PolygonJson,
    string? Color,
    string? CapabilityFlags);

public sealed record SpaceCreateLayoutAisleDto(
    Guid ZoneLogicalId,
    string AisleCode,
    string? Name,
    short Direction,
    string PolygonJson,
    string CenterlineJson);

public sealed record SpaceCreateLayoutRackLevelDto(
    int LevelNo,
    int BottomZ,
    int ClearHeight,
    int BinCount,
    int DepthCount,
    int CellWidth,
    int CellDepth,
    int BeamHeight,
    decimal? MaxLoad,
    string? LocationCodePrefix = null);

public sealed record SpaceCreateLayoutRackDto(
    Guid ZoneLogicalId,
    Guid? AisleLogicalId,
    string RackCode,
    string? Name,
    string? RackType,
    Guid? TemplateVersionId,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Depth,
    int Height,
    IReadOnlyList<SpaceCreateLayoutRackLevelDto> Levels);

public sealed record SpaceLayoutCommandDto(
    Guid CommandId,
    string Type,
    Guid TargetLogicalId,
    SpaceCreateLayoutZoneDto? CreateZone = null,
    SpaceCreateLayoutAisleDto? CreateAisle = null,
    SpaceCreateLayoutRackDto? CreateRack = null);

public sealed record ApplySpaceLayoutCommandBatchRequest(
    int SchemaVersion,
    Guid CommandBatchId,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision,
    IReadOnlyList<SpaceLayoutCommandDto> Commands);

public sealed record SpaceLayoutCommandResultDto(
    Guid CommandId,
    string Type,
    Guid TargetLogicalId);

public sealed record ApplySpaceLayoutCommandBatchResponse(
    Guid CommandBatchId,
    long FloorRevision,
    long VersionContentRevision,
    IReadOnlyList<SpaceLayoutCommandResultDto> AppliedCommands,
    IReadOnlyList<SpaceSceneZoneDto> AffectedZones,
    IReadOnlyList<SpaceSceneAisleDto> AffectedAisles,
    IReadOnlyList<SpaceSceneRackDto> AffectedRacks,
    IReadOnlyList<SpaceSceneRackLevelDto> AffectedRackLevels,
    IReadOnlyList<SpaceSceneLocationDto> AffectedLocations,
    bool IdempotentReplay);
