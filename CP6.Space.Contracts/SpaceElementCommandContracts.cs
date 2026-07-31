namespace CP6.Space.Contracts;

public static class SpaceElementCommandContract
{
    public const int SchemaVersion = 1;
    public const string UpdateProperties = "UpdateProperties";
    public const string DeleteObject = "DeleteObject";
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

public sealed record SpaceElementCommandDto(
    Guid CommandId,
    string Type,
    Guid TargetLogicalId,
    SpaceUpdateElementPropertiesDto? UpdateProperties);

public sealed record ApplySpaceElementCommandBatchRequest(
    int SchemaVersion,
    Guid CommandBatchId,
    Guid ClientInstanceId,
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
    bool IdempotentReplay);
