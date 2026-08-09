namespace CP6.Space.Contracts;

public sealed record SpaceRackGenerationProfileLevelDto(
    int LevelNo,
    int BottomZMillimeters,
    int ClearHeightMillimeters,
    int BinCount,
    int DepthCount,
    int CellWidthMillimeters,
    int CellDepthMillimeters,
    int BeamHeightMillimeters = 0,
    decimal? MaxLoadKilograms = null);

public sealed record SpaceRackGenerationProfileVersionDto(
    Guid Id,
    Guid ProfileId,
    string Scope,
    long VersionNo,
    int RackWidthMillimeters,
    int RackDepthMillimeters,
    int RackHeightMillimeters,
    IReadOnlyList<SpaceRackGenerationProfileLevelDto> Levels,
    long LocationCount,
    string ContentHash,
    string Status,
    string RowVersion);

public sealed record SpaceRackGenerationProfileDto(
    Guid Id,
    string Scope,
    string ProfileCode,
    string Name,
    string? Description,
    string Status,
    SpaceRackGenerationProfileVersionDto LatestVersion,
    string RowVersion);

public sealed record CreateSpaceRackGenerationProfileRequest(
    string ProfileCode,
    string Name,
    int RackWidthMillimeters,
    int RackDepthMillimeters,
    int RackHeightMillimeters,
    IReadOnlyList<SpaceRackGenerationProfileLevelDto> Levels,
    string? Description = null,
    string Scope = "Tenant");

public sealed record CreateSpaceRackGenerationProfileResponse(
    SpaceRackGenerationProfileDto Profile,
    bool IdempotentReplay);
