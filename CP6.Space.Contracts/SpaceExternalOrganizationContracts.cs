namespace CP6.Space.Contracts;

public sealed record CreateSpaceExternalOrganizationRequest(
    string Type,
    string Code,
    string Name,
    string? BusinessPartnerType = null,
    Guid? BusinessPartnerId = null,
    string Status = "Active");

public sealed record UpdateSpaceExternalOrganizationRequest(
    string Code,
    string Name,
    string? BusinessPartnerType,
    Guid? BusinessPartnerId,
    string Status);

public sealed record SpaceExternalOrganizationDto(
    Guid Id,
    string Type,
    string Code,
    string Name,
    string? BusinessPartnerType,
    Guid? BusinessPartnerId,
    string Status,
    long SecurityStamp,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);

public sealed record CreateSpaceExternalMembershipRequest(
    Guid UserId,
    string Role,
    DateTimeOffset? ValidFromUtc = null,
    DateTimeOffset? ValidToUtc = null,
    string Status = "Invited");

public sealed record UpdateSpaceExternalMembershipRequest(
    string Role,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string Status);

public sealed record SpaceExternalMembershipDto(
    Guid Id,
    Guid OrganizationId,
    Guid UserId,
    string Role,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset? ValidToUtc,
    string Status,
    Guid? InvitedBy,
    DateTimeOffset? AcceptedAtUtc,
    long SecurityStamp,
    DateTime CreatedAtUtc,
    Guid? CreatedBy,
    DateTime? ModifiedAtUtc,
    Guid? ModifiedBy);
