namespace CP6.Space.Contracts;

public sealed record SpaceAssetVersionDto(
    Guid Id,
    long VersionNo,
    string Format,
    string ParameterSchemaJson,
    string? PreviewRef,
    string? RenderArtifactRef,
    string ContentHash,
    string Status,
    string RowVersion);

public sealed record SpaceAssetDto(
    Guid Id,
    string Scope,
    string AssetCode,
    string Name,
    string Category,
    string? Description,
    string Status,
    SpaceAssetVersionDto LatestVersion,
    string RowVersion);

public sealed record CreateSpaceAssetRequest(
    string AssetCode,
    string Name,
    string Category,
    string Format,
    string ParameterSchemaJson,
    string ContentHash,
    string? Description = null,
    string? PreviewRef = null,
    string? RenderArtifactRef = null,
    string Scope = "Tenant");

public sealed record CreateSpaceAssetResponse(
    SpaceAssetDto Asset,
    bool IdempotentReplay);
