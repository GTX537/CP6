namespace CP6.Space.Contracts;

public static class SpaceWarehouseTemplateContract
{
    public const int SchemaVersion = 1;
}

public sealed record SpaceWarehouseTemplateCountsDto(
    int Floors,
    int Zones,
    int Aisles,
    int Racks,
    int Locations);

public sealed record SpaceWarehouseTemplateVersionDto(
    Guid Id,
    long VersionNo,
    int SchemaVersion,
    string ContentHash,
    SpaceWarehouseTemplateCountsDto Counts,
    string Status);

public sealed record SpaceWarehouseTemplateDto(
    Guid Id,
    string Scope,
    string TemplateCode,
    string Name,
    string? Description,
    string Status,
    SpaceWarehouseTemplateVersionDto LatestVersion);

public sealed record CreateTenantSpaceWarehouseTemplateRequest(
    string TemplateCode,
    string Name,
    string? Description,
    int SchemaVersion,
    IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto> Floors,
    IReadOnlyList<SpaceWarehouseTemplateZonePlanDto> Zones,
    IReadOnlyList<SpaceWarehouseTemplateAislePlanDto> Aisles,
    IReadOnlyList<SpaceWarehouseTemplateRackPlanDto> Racks);

public sealed record CreateTenantSpaceWarehouseTemplateResponse(
    SpaceWarehouseTemplateDto Template,
    bool IdempotentReplay);

public sealed record PreviewTenantSpaceWarehouseTemplateFromDraftRequest(
    long ExpectedContentRevision);

public sealed record SpaceDraftWarehouseTemplatePreviewDto(
    int SchemaVersion,
    Guid ModelVersionId,
    long ContentRevision,
    string TemplateContentHash,
    string ProposalHash,
    SpaceWarehouseTemplateCountsDto Counts,
    IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto> Floors,
    bool WritesTemplate);

public sealed record CreateTenantSpaceWarehouseTemplateFromDraftRequest(
    string TemplateCode,
    string Name,
    string? Description,
    long ExpectedContentRevision,
    string ProposalHash);

public sealed record PreviewSpaceWarehouseTemplateRequest(
    Guid TemplateVersionId);

public sealed record SpaceWarehouseTemplateFloorPlanDto(
    string Key,
    string FloorCode,
    string Name,
    int Level,
    int Elevation,
    int Width,
    int Depth,
    int Height);

public sealed record SpaceWarehouseTemplateZonePlanDto(
    string Key,
    string FloorKey,
    string ZoneCode,
    string ZoneType,
    int MinX,
    int MinY,
    int MaxX,
    int MaxY);

public sealed record SpaceWarehouseTemplateAislePlanDto(
    string Key,
    string FloorKey,
    string ZoneKey,
    string AisleCode,
    int StartX,
    int StartY,
    int EndX,
    int EndY);

public sealed record SpaceWarehouseTemplateRackPlanDto(
    string Key,
    string FloorKey,
    string ZoneKey,
    string AisleKey,
    string RackCode,
    int X,
    int Y,
    int Z,
    decimal RotationZ,
    int Width,
    int Depth,
    int Height,
    int Columns,
    int Levels,
    int Depths);

public sealed record SpaceWarehouseTemplateInstantiationPreviewDto(
    int SchemaVersion,
    Guid TemplateId,
    Guid TemplateVersionId,
    string TemplateContentHash,
    string ProposalHash,
    SpaceWarehouseTemplateCountsDto Counts,
    IReadOnlyList<SpaceWarehouseTemplateFloorPlanDto> Floors,
    IReadOnlyList<SpaceWarehouseTemplateZonePlanDto> Zones,
    IReadOnlyList<SpaceWarehouseTemplateAislePlanDto> Aisles,
    IReadOnlyList<SpaceWarehouseTemplateRackPlanDto> Racks,
    bool WritesDraft);

public sealed record ApplySpaceWarehouseTemplateFloorRequest(
    int SchemaVersion,
    Guid SiteId,
    Guid TemplateVersionId,
    string ProposalHash,
    string TemplateFloorKey,
    Guid CommandBatchId,
    Guid ClientInstanceId,
    Guid LeaseId,
    long ExpectedFloorRevision,
    long ExpectedContentRevision);

public sealed record ApplySpaceWarehouseTemplateFloorResponse(
    int SchemaVersion,
    Guid TemplateId,
    Guid TemplateVersionId,
    string TemplateContentHash,
    string ProposalHash,
    string TemplateFloorKey,
    Guid ModelVersionId,
    Guid FloorLogicalId,
    long FloorRevision,
    long VersionContentRevision,
    SpaceWarehouseTemplateCountsDto AppliedCounts,
    Guid CommandBatchId,
    bool IdempotentReplay);
