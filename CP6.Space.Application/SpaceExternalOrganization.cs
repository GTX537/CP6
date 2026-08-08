using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public static class SpaceExternalBusinessPartnerTypes
{
    public const string ErpBusinessPartner = "ErpBusinessPartner";
}

public interface ISpaceExternalReferenceValidator
{
    Task EnsureUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task EnsureBusinessPartnerAsync(
        Guid tenantId,
        SpaceExternalOrganizationType organizationType,
        string businessPartnerType,
        Guid businessPartnerId,
        CancellationToken cancellationToken = default);
}

public interface ISpaceExternalOrganizationService
{
    Task<IReadOnlyList<SpaceExternalOrganizationDto>> GetOrganizationsAsync(
        string? type,
        string? status,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalOrganizationDto> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalOrganizationDto> CreateOrganizationAsync(
        CreateSpaceExternalOrganizationRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalOrganizationDto> UpdateOrganizationAsync(
        Guid organizationId,
        UpdateSpaceExternalOrganizationRequest request,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SpaceExternalMembershipDto>> GetMembershipsAsync(
        Guid organizationId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalMembershipDto> CreateMembershipAsync(
        Guid organizationId,
        CreateSpaceExternalMembershipRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalMembershipDto> UpdateMembershipAsync(
        Guid organizationId,
        Guid membershipId,
        UpdateSpaceExternalMembershipRequest request,
        CancellationToken cancellationToken = default);
}
