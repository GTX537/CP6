using CP6.Core.EFDbContext;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class Cp6SpaceExternalReferenceValidator(
    CP6Context legacy,
    ISpaceExecutionContext execution) : ISpaceExternalReferenceValidator
{
    public async Task EnsureUserAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        if (userId == Guid.Empty ||
            !await legacy.Sys_Users.AnyAsync(
                user =>
                    user.Id == userId &&
                    user.TenantId == tenantId &&
                    user.Enable,
                cancellationToken))
        {
            throw ReferenceNotFound();
        }
    }

    public async Task EnsureBusinessPartnerAsync(
        Guid tenantId,
        SpaceExternalOrganizationType organizationType,
        string businessPartnerType,
        Guid businessPartnerId,
        CancellationToken cancellationToken = default)
    {
        EnsureTenant(tenantId);
        if (!string.Equals(
                businessPartnerType,
                SpaceExternalBusinessPartnerTypes.ErpBusinessPartner,
                StringComparison.Ordinal) ||
            businessPartnerId == Guid.Empty)
        {
            throw ReferenceNotFound();
        }

        var query = legacy.BusinessPartners.Where(partner =>
            partner.Id == businessPartnerId &&
            partner.TenantId == tenantId &&
            partner.Status != 9 &&
            !partner.IsDeleted);
        query = organizationType switch
        {
            SpaceExternalOrganizationType.Customer =>
                query.Where(partner => partner.CustomerFlg),
            SpaceExternalOrganizationType.Supplier =>
                query.Where(partner => partner.SupplierFlg),
            SpaceExternalOrganizationType.ThirdPartyLogistics => query,
            _ => query.Where(_ => false),
        };

        if (!await query.AnyAsync(cancellationToken))
            throw ReferenceNotFound();
    }

    private void EnsureTenant(Guid tenantId)
    {
        if (tenantId == Guid.Empty ||
            execution.TenantId != tenantId ||
            legacy.CurrentTenantId != tenantId)
        {
            throw new SpaceTenantScopeException(
                "A cross-tenant external reference was rejected.");
        }
    }

    private static SpaceProblemException ReferenceNotFound() =>
        new(
            SpaceErrorCodes.ExternalReferenceNotFound,
            404,
            "The external organization reference was not found.",
            recoveryAction: "select-current-tenant-reference");
}

public sealed class SpaceExternalOrganizationService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock,
    ISpaceExternalReferenceValidator references) :
    ISpaceExternalOrganizationService
{
    public async Task<IReadOnlyList<SpaceExternalOrganizationDto>>
        GetOrganizationsAsync(
            string? type,
            string? status,
            CancellationToken cancellationToken = default)
    {
        RequireTenant();
        var query = context.ExternalOrganizations.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(type))
        {
            var parsed = ParseEnum<SpaceExternalOrganizationType>(
                type,
                "organization type");
            query = query.Where(item => item.Type == parsed);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseEnum<SpaceExternalOrganizationStatus>(
                status,
                "organization status");
            query = query.Where(item => item.Status == parsed);
        }

        return await query
            .OrderBy(item => item.Type)
            .ThenBy(item => item.NormalizedCode)
            .Select(item => ToOrganizationDto(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<SpaceExternalOrganizationDto> GetOrganizationAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var organization = await FindOrganizationAsync(
            organizationId,
            asTracking: false,
            cancellationToken);
        return ToOrganizationDto(organization);
    }

    public async Task<SpaceExternalOrganizationDto> CreateOrganizationAsync(
        CreateSpaceExternalOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenantId = RequireTenant();
        var type = ParseEnum<SpaceExternalOrganizationType>(
            request.Type,
            "organization type");
        var status = ParseEnum<SpaceExternalOrganizationStatus>(
            request.Status,
            "organization status");
        var businessPartnerType = CanonicalBusinessPartnerType(
            request.BusinessPartnerType,
            request.BusinessPartnerId);
        var organization = SpaceExternalOrganization.Create(
            tenantId,
            type,
            request.Code,
            request.Name,
            businessPartnerType,
            request.BusinessPartnerId,
            status);

        await EnsureBusinessPartnerAsync(organization, cancellationToken);
        await EnsureOrganizationIdentityAvailableAsync(
            organization.Type,
            organization.NormalizedCode,
            organization.BusinessPartnerType,
            organization.BusinessPartnerId,
            exceptOrganizationId: null,
            cancellationToken);

        context.ExternalOrganizations.Add(organization);
        await SaveAsync(
            SpaceErrorCodes.ExternalOrganizationConflict,
            "An external organization with the same identity already exists.",
            cancellationToken);
        return ToOrganizationDto(organization);
    }

    public async Task<SpaceExternalOrganizationDto> UpdateOrganizationAsync(
        Guid organizationId,
        UpdateSpaceExternalOrganizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await FindOrganizationAsync(
            organizationId,
            asTracking: true,
            cancellationToken);
        var status = ParseEnum<SpaceExternalOrganizationStatus>(
            request.Status,
            "organization status");
        var businessPartnerType = CanonicalBusinessPartnerType(
            request.BusinessPartnerType,
            request.BusinessPartnerId);

        organization.Update(
            request.Code,
            request.Name,
            businessPartnerType,
            request.BusinessPartnerId,
            status);
        await EnsureBusinessPartnerAsync(organization, cancellationToken);
        await EnsureOrganizationIdentityAvailableAsync(
            organization.Type,
            organization.NormalizedCode,
            organization.BusinessPartnerType,
            organization.BusinessPartnerId,
            organization.Id,
            cancellationToken);

        await SaveAsync(
            SpaceErrorCodes.ExternalOrganizationConflict,
            "An external organization with the same identity already exists.",
            cancellationToken);
        return ToOrganizationDto(organization);
    }

    public async Task<IReadOnlyList<SpaceExternalMembershipDto>>
        GetMembershipsAsync(
            Guid organizationId,
            string? status,
            CancellationToken cancellationToken = default)
    {
        await FindOrganizationAsync(
            organizationId,
            asTracking: false,
            cancellationToken);
        var query = context.ExternalMemberships
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseEnum<SpaceExternalMembershipStatus>(
                status,
                "membership status");
            query = query.Where(item => item.Status == parsed);
        }

        return await query
            .OrderBy(item => item.UserId)
            .ThenByDescending(item => item.CreatedAtUtc)
            .Select(item => ToMembershipDto(item))
            .ToListAsync(cancellationToken);
    }

    public async Task<SpaceExternalMembershipDto> CreateMembershipAsync(
        Guid organizationId,
        CreateSpaceExternalMembershipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await FindOrganizationAsync(
            organizationId,
            asTracking: true,
            cancellationToken);
        var tenantId = RequireTenant();
        await references.EnsureUserAsync(
            tenantId,
            request.UserId,
            cancellationToken);
        await EnsureMembershipAvailableAsync(
            organizationId,
            request.UserId,
            cancellationToken);

        var now = RequireUtcNow();
        var membership = SpaceExternalMembership.Create(
            tenantId,
            organizationId,
            request.UserId,
            ParseEnum<SpaceExternalMembershipRole>(request.Role, "membership role"),
            request.ValidFromUtc?.UtcDateTime ?? now,
            request.ValidToUtc?.UtcDateTime,
            ParseEnum<SpaceExternalMembershipStatus>(
                request.Status,
                "membership status"),
            execution.ActorId == Guid.Empty ? null : execution.ActorId,
            now);
        organization.TouchMembershipSecurityStamp();
        context.ExternalMemberships.Add(membership);

        await SaveAsync(
            SpaceErrorCodes.ExternalMembershipConflict,
            "The user already has a current membership in this organization.",
            cancellationToken);
        return ToMembershipDto(membership);
    }

    public async Task<SpaceExternalMembershipDto> UpdateMembershipAsync(
        Guid organizationId,
        Guid membershipId,
        UpdateSpaceExternalMembershipRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await FindOrganizationAsync(
            organizationId,
            asTracking: true,
            cancellationToken);
        var membership = await context.ExternalMemberships
            .SingleOrDefaultAsync(
                item =>
                    item.Id == membershipId &&
                    item.OrganizationId == organizationId,
                cancellationToken)
            ?? throw MembershipNotFound();

        membership.Update(
            ParseEnum<SpaceExternalMembershipRole>(request.Role, "membership role"),
            request.ValidFromUtc.UtcDateTime,
            request.ValidToUtc?.UtcDateTime,
            ParseEnum<SpaceExternalMembershipStatus>(
                request.Status,
                "membership status"),
            RequireUtcNow());
        organization.TouchMembershipSecurityStamp();
        await SaveAsync(
            SpaceErrorCodes.ExternalMembershipConflict,
            "The membership could not be updated because its identity conflicts.",
            cancellationToken);
        return ToMembershipDto(membership);
    }

    private async Task<SpaceExternalOrganization> FindOrganizationAsync(
        Guid organizationId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        RequireTenant();
        if (organizationId == Guid.Empty)
            throw OrganizationNotFound();
        IQueryable<SpaceExternalOrganization> query =
            context.ExternalOrganizations;
        if (!asTracking)
            query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(
                   item => item.Id == organizationId,
                   cancellationToken)
               ?? throw OrganizationNotFound();
    }

    private async Task EnsureBusinessPartnerAsync(
        SpaceExternalOrganization organization,
        CancellationToken cancellationToken)
    {
        if (!organization.BusinessPartnerId.HasValue)
            return;
        await references.EnsureBusinessPartnerAsync(
            organization.TenantId,
            organization.Type,
            organization.BusinessPartnerType!,
            organization.BusinessPartnerId.Value,
            cancellationToken);
    }

    private async Task EnsureOrganizationIdentityAvailableAsync(
        SpaceExternalOrganizationType type,
        string normalizedCode,
        string? businessPartnerType,
        Guid? businessPartnerId,
        Guid? exceptOrganizationId,
        CancellationToken cancellationToken)
    {
        if (await context.ExternalOrganizations.AnyAsync(
                item =>
                    item.Type == type &&
                    item.NormalizedCode == normalizedCode &&
                    (!exceptOrganizationId.HasValue ||
                     item.Id != exceptOrganizationId.Value),
                cancellationToken))
        {
            throw OrganizationConflict();
        }

        if (businessPartnerId.HasValue &&
            await context.ExternalOrganizations.AnyAsync(
                item =>
                    item.Type == type &&
                    item.BusinessPartnerType == businessPartnerType &&
                    item.BusinessPartnerId == businessPartnerId &&
                    (!exceptOrganizationId.HasValue ||
                     item.Id != exceptOrganizationId.Value),
                cancellationToken))
        {
            throw OrganizationConflict();
        }
    }

    private async Task EnsureMembershipAvailableAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        if (await context.ExternalMemberships.AnyAsync(
                item =>
                    item.OrganizationId == organizationId &&
                    item.UserId == userId &&
                    item.Status != SpaceExternalMembershipStatus.Revoked,
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalMembershipConflict,
                409,
                "The user already has a current membership in this organization.",
                recoveryAction: "update-existing-membership");
        }
    }

    private async Task SaveAsync(
        string conflictCode,
        string conflictTitle,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.GetBaseException() is SqlException
                  {
                      Number: 2601 or 2627,
                  })
        {
            throw new SpaceProblemException(
                conflictCode,
                409,
                conflictTitle,
                recoveryAction: "reload-current-resource");
        }
        catch (SpaceExternalAccessStateException exception)
        {
            throw InvalidState(exception.Message);
        }
    }

    private Guid RequireTenant()
    {
        if (execution.TenantId == Guid.Empty ||
            context.CurrentTenantId != execution.TenantId)
        {
            throw new SpaceTenantScopeException(
                "A verified Space tenant context is required.");
        }

        return execution.TenantId;
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }

    private static string? CanonicalBusinessPartnerType(
        string? businessPartnerType,
        Guid? businessPartnerId)
    {
        if (string.IsNullOrWhiteSpace(businessPartnerType) &&
            !businessPartnerId.HasValue)
        {
            return null;
        }
        if (!businessPartnerId.HasValue ||
            !string.Equals(
                businessPartnerType?.Trim(),
                SpaceExternalBusinessPartnerTypes.ErpBusinessPartner,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalReferenceNotFound,
                404,
                "The external organization reference was not found.",
                recoveryAction: "select-current-tenant-reference");
        }

        return SpaceExternalBusinessPartnerTypes.ErpBusinessPartner;
    }

    private static T ParseEnum<T>(string value, string field)
        where T : struct, Enum
    {
        var input = value?.Trim();
        if (string.IsNullOrEmpty(input) ||
            long.TryParse(input, out _) ||
            !Enum.TryParse<T>(input, ignoreCase: true, out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.RequestInvalid,
                400,
                "The request is invalid.",
                $"Unsupported {field}.",
                "correct-request");
        }

        return parsed;
    }

    private static SpaceExternalOrganizationDto ToOrganizationDto(
        SpaceExternalOrganization item) =>
        new(
            item.Id,
            item.Type.ToString(),
            item.Code,
            item.Name,
            item.BusinessPartnerType,
            item.BusinessPartnerId,
            item.Status.ToString(),
            item.SecurityStamp,
            item.CreatedAtUtc,
            item.CreatedBy,
            item.ModifiedAtUtc,
            item.ModifiedBy);

    private static SpaceExternalMembershipDto ToMembershipDto(
        SpaceExternalMembership item) =>
        new(
            item.Id,
            item.OrganizationId,
            item.UserId,
            item.Role.ToString(),
            UtcOffset(item.ValidFromUtc),
            item.ValidToUtc.HasValue
                ? UtcOffset(item.ValidToUtc.Value)
                : null,
            item.Status.ToString(),
            item.InvitedBy,
            item.AcceptedAtUtc.HasValue
                ? UtcOffset(item.AcceptedAtUtc.Value)
                : null,
            item.SecurityStamp,
            item.CreatedAtUtc,
            item.CreatedBy,
            item.ModifiedAtUtc,
            item.ModifiedBy);

    private static DateTimeOffset UtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException OrganizationNotFound() =>
        new(
            SpaceErrorCodes.ExternalOrganizationNotFound,
            404,
            "The external organization was not found.",
            recoveryAction: "select-current-tenant-organization");

    private static SpaceProblemException MembershipNotFound() =>
        new(
            SpaceErrorCodes.ExternalMembershipNotFound,
            404,
            "The external membership was not found.",
            recoveryAction: "select-current-organization-membership");

    private static SpaceProblemException OrganizationConflict() =>
        new(
            SpaceErrorCodes.ExternalOrganizationConflict,
            409,
            "An external organization with the same identity already exists.",
            recoveryAction: "use-unique-organization-identity");

    private static SpaceProblemException InvalidState(string detail) =>
        new(
            SpaceErrorCodes.ExternalAccessStateInvalid,
            409,
            "The external access state does not allow this operation.",
            detail,
            "reload-current-resource");
}
