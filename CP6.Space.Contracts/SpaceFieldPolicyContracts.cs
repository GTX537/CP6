namespace CP6.Space.Contracts;

public sealed record SpaceFieldPolicyFieldRequest(
    string ResourceType,
    string FieldName,
    string MaskingRule = "None");

public sealed record CreateSpaceFieldPolicyRequest(
    string Name,
    string AudienceType,
    IReadOnlyList<SpaceFieldPolicyFieldRequest> Fields,
    bool CanExport = false);

public sealed record UpdateSpaceFieldPolicyRequest(
    string Name,
    IReadOnlyList<SpaceFieldPolicyFieldRequest> Fields,
    bool CanExport,
    string Status);

public sealed record SpaceFieldPolicyFieldDto(
    string ResourceType,
    string FieldName,
    string MaskingRule);

public sealed record SpaceFieldPolicyDto(
    Guid Id,
    string Name,
    string AudienceType,
    bool CanExport,
    string Status,
    long PolicyVersion,
    IReadOnlyList<SpaceFieldPolicyFieldDto> Fields,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);
