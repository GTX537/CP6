namespace CP6.Space.Contracts;

public static class SpaceElementCommandContract
{
    public const int SchemaVersion = 1;
    public const string UpdateProperties = "UpdateProperties";
    public const string MoveObject = "MoveObject";
    public const string RotateObject = "RotateObject";
    public const string DeleteObject = "DeleteObject";
    public const string RestoreLogicalObject = "RestoreLogicalObject";
    public const string GenerateRackArray = "GenerateRackArray";
}

public sealed record SpaceElementAttributeWriteDto(
    string Namespace,
    string Key,
    string ValueType,
    string? Value,
    string? Unit);

public sealed record SpaceUpdateElementPropertiesDto(
    string GeometryJson,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Height,
    int Depth,
    string? BusinessCode,
    string? LinkedEntityType,
    Guid? LinkedLogicalId,
    IReadOnlyList<SpaceElementAttributeWriteDto> Attributes);

public sealed record SpaceMoveObjectDto(
    int X,
    int Y,
    int Z);

public sealed record SpaceRotateObjectDto(
    decimal RotationZ);

public sealed record SpaceGenerateRackArrayDto(
    int Rows,
    int Columns,
    int RowGap,
    int ColumnGap,
    int StaggerOffset,
    string CodePrefix,
    int StartNumber,
    int CodeDigits);

public sealed record SpaceElementCommandDto(
    Guid CommandId,
    string Type,
    Guid TargetLogicalId,
    SpaceUpdateElementPropertiesDto? UpdateProperties,
    SpaceMoveObjectDto? MoveObject = null,
    SpaceRotateObjectDto? RotateObject = null,
    SpaceGenerateRackArrayDto? GenerateRackArray = null);

public sealed record ApplySpaceElementCommandBatchRequest(
    int SchemaVersion,
    Guid CommandBatchId,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision,
    IReadOnlyList<SpaceElementCommandDto> Commands);

public sealed record SpaceElementCommandResultDto(
    Guid CommandId,
    string Type,
    Guid TargetLogicalId,
    SpaceSceneElementDto Element,
    IReadOnlyList<SpaceSceneElementAttributeDto> Attributes);

public sealed record ApplySpaceElementCommandBatchResponse(
    Guid CommandBatchId,
    long FloorRevision,
    long VersionContentRevision,
    IReadOnlyList<SpaceElementCommandResultDto> AffectedObjects,
    bool IdempotentReplay,
    IReadOnlyList<SpaceSceneRackDto>? AffectedRacks = null,
    IReadOnlyList<SpaceSceneRackLevelDto>? AffectedRackLevels = null,
    IReadOnlyList<SpaceSceneLocationDto>? AffectedLocations = null);
