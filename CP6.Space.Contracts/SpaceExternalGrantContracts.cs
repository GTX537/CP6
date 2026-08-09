namespace CP6.Space.Contracts;

public sealed record SpaceExternalGrantObjectRequest(
    string BusinessObjectType,
    string BusinessObjectId);

public sealed record CreateSpaceExternalGrantRequest(
    Guid SiteId,
    IReadOnlyList<Guid>? FloorLogicalIds = null,
    IReadOnlyList<Guid>? ZoneLogicalIds = null,
    IReadOnlyList<string>? OwnerIds = null,
    IReadOnlyList<SpaceExternalGrantObjectRequest>? Objects = null,
    Guid? FieldPolicyId = null,
    bool CanExport = false,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null,
    string Status = "Active");

public sealed record UpdateSpaceExternalGrantRequest(
    Guid SiteId,
    IReadOnlyList<Guid> FloorLogicalIds,
    IReadOnlyList<Guid> ZoneLogicalIds,
    IReadOnlyList<string> OwnerIds,
    IReadOnlyList<SpaceExternalGrantObjectRequest> Objects,
    Guid? FieldPolicyId,
    bool CanExport,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string Status);

public sealed record SpaceExternalGrantObjectDto(
    string BusinessObjectType,
    string BusinessObjectId);

public sealed record SpaceExternalGrantDto(
    Guid Id,
    Guid OrganizationId,
    Guid SiteId,
    IReadOnlyList<Guid> FloorLogicalIds,
    IReadOnlyList<Guid> ZoneLogicalIds,
    IReadOnlyList<string> OwnerIds,
    IReadOnlyList<SpaceExternalGrantObjectDto> Objects,
    Guid? FieldPolicyId,
    bool CanExport,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string Status,
    long GrantVersion,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);
