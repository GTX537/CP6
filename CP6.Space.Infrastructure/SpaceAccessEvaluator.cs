using System.Security.Cryptography;
using System.Text;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceAccessEvaluator(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock) : ISpaceAccessEvaluator
{
    public async Task<SpaceAccessDecision> EvaluateAsync(
        SpacePrincipal principal,
        SpaceAccessAction action,
        SpaceResource resource,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(resource);
        var organization = principal.OrganizationContextId.HasValue
            ? new SpaceOrganizationContext(
                principal.OrganizationContextId.Value)
            : null;
        var scope = await BuildQueryScopeAsync(
            principal,
            resource.ResourceType,
            organization,
            cancellationToken);
        if (!scope.Allowed)
            return Denied(scope.ReasonCode, scope);
        if (resource.TenantId != scope.TenantId ||
            resource.SiteId == Guid.Empty ||
            resource.ResourceType != scope.ResourceType ||
            !Enum.IsDefined(action))
        {
            return Denied(SpaceErrorCodes.ExternalScopeDenied, scope);
        }
        if (scope.IsInternal)
        {
            return new SpaceAccessDecision(
                true,
                SpaceErrorCodes.InternalScopeAllowed,
                [],
                [],
                scope);
        }
        if (string.IsNullOrWhiteSpace(resource.BusinessObjectType) !=
            string.IsNullOrWhiteSpace(resource.BusinessObjectId))
        {
            return Denied(SpaceErrorCodes.ExternalScopeDenied, scope);
        }

        var matched = scope.Clauses
            .Where(clause => Matches(clause, action, resource))
            .ToArray();
        if (matched.Length == 0)
            return Denied(SpaceErrorCodes.ExternalScopeDenied, scope);

        return new SpaceAccessDecision(
            true,
            SpaceErrorCodes.ExternalScopeAllowed,
            matched.Select(item => item.GrantId).Distinct().ToArray(),
            matched
                .Where(item => item.FieldPolicyId.HasValue)
                .Select(item => item.FieldPolicyId!.Value)
                .Distinct()
                .ToArray(),
            scope);
    }

    public async Task<SpaceQueryScope> BuildQueryScopeAsync(
        SpacePrincipal principal,
        SpaceResourceType resourceType,
        SpaceOrganizationContext? organization,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (!Enum.IsDefined(resourceType) ||
            principal.TenantId == Guid.Empty ||
            principal.UserId == Guid.Empty ||
            principal.TenantId != execution.TenantId ||
            principal.UserId != execution.ActorId ||
            context.CurrentTenantId != execution.TenantId)
        {
            return DeniedScope(
                SpaceErrorCodes.ExternalScopeDenied,
                principal,
                resourceType);
        }

        if (!principal.IsExternal)
        {
            if (organization is not null ||
                principal.OrganizationContextId.HasValue)
            {
                return DeniedScope(
                    SpaceErrorCodes.ExternalOrganizationContextRequired,
                    principal,
                    resourceType);
            }
            return new SpaceQueryScope(
                true,
                SpaceErrorCodes.InternalScopeAllowed,
                true,
                resourceType,
                principal.TenantId,
                principal.UserId,
                null,
                0,
                0,
                "internal-v1",
                []);
        }

        if (organization is null ||
            organization.OrganizationId == Guid.Empty ||
            principal.OrganizationContextId != organization.OrganizationId)
        {
            return DeniedScope(
                SpaceErrorCodes.ExternalOrganizationContextRequired,
                principal,
                resourceType);
        }

        var now = RequireUtcNow();
        var organizationRow = await context.ExternalOrganizations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.Id == organization.OrganizationId &&
                    item.Status == SpaceExternalOrganizationStatus.Active,
                cancellationToken);
        if (organizationRow is null)
        {
            return DeniedScope(
                SpaceErrorCodes.ExternalOrganizationContextRequired,
                principal,
                resourceType);
        }

        var membership = await context.ExternalMemberships
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item =>
                    item.OrganizationId == organization.OrganizationId &&
                    item.UserId == principal.UserId &&
                    item.Status == SpaceExternalMembershipStatus.Active &&
                    item.ValidFromUtc <= now &&
                    (!item.ValidToUtc.HasValue || item.ValidToUtc > now),
                cancellationToken);
        if (membership is null)
        {
            return DeniedScope(
                SpaceErrorCodes.ExternalMembershipInactive,
                principal,
                resourceType,
                organization.OrganizationId,
                organizationRow.SecurityStamp);
        }

        var grants = await context.ExternalGrants
            .AsNoTracking()
            .Where(item =>
                item.OrganizationId == organization.OrganizationId &&
                item.Status == SpaceExternalGrantStatus.Active &&
                item.ValidFromUtc <= now &&
                (!item.ValidToUtc.HasValue || item.ValidToUtc > now))
            .OrderBy(item => item.Id)
            .ToListAsync(cancellationToken);
        if (grants.Count == 0)
        {
            return DeniedScope(
                SpaceErrorCodes.ExternalGrantInactive,
                principal,
                resourceType,
                organization.OrganizationId,
                organizationRow.SecurityStamp,
                membership.SecurityStamp);
        }

        var grantIds = grants.Select(item => item.Id).ToArray();
        var floors = await context.ExternalGrantFloors
            .AsNoTracking()
            .Where(item => grantIds.Contains(item.GrantId))
            .ToListAsync(cancellationToken);
        var zones = await context.ExternalGrantZones
            .AsNoTracking()
            .Where(item => grantIds.Contains(item.GrantId))
            .ToListAsync(cancellationToken);
        var owners = await context.ExternalGrantOwners
            .AsNoTracking()
            .Where(item => grantIds.Contains(item.GrantId))
            .ToListAsync(cancellationToken);
        var objects = await context.ExternalGrantObjects
            .AsNoTracking()
            .Where(item => grantIds.Contains(item.GrantId))
            .ToListAsync(cancellationToken);

        var clauses = grants.Select(grant => new SpaceGrantClause(
                grant.Id,
                grant.GrantVersion,
                grant.SiteId,
                floors
                    .Where(item => item.GrantId == grant.Id)
                    .Select(item => item.FloorLogicalId)
                    .Order()
                    .ToArray(),
                zones
                    .Where(item => item.GrantId == grant.Id)
                    .Select(item => item.ZoneLogicalId)
                    .Order()
                    .ToArray(),
                owners
                    .Where(item => item.GrantId == grant.Id)
                    .OrderBy(item => item.NormalizedOwnerId)
                    .Select(item => item.NormalizedOwnerId)
                    .ToArray(),
                objects
                    .Where(item => item.GrantId == grant.Id)
                    .OrderBy(item => item.NormalizedBusinessObjectType)
                    .ThenBy(item => item.NormalizedBusinessObjectId)
                    .Select(item => new SpaceGrantObjectScope(
                        item.NormalizedBusinessObjectType,
                        item.NormalizedBusinessObjectId))
                    .ToArray(),
                grant.FieldPolicyId,
                grant.CanExport))
            .ToArray();

        return new SpaceQueryScope(
            true,
            SpaceErrorCodes.ExternalScopeAllowed,
            false,
            resourceType,
            principal.TenantId,
            principal.UserId,
            organization.OrganizationId,
            organizationRow.SecurityStamp,
            membership.SecurityStamp,
            AuthorizationVersion(
                organizationRow.SecurityStamp,
                membership.SecurityStamp,
                grants),
            clauses);
    }

    private static bool Matches(
        SpaceGrantClause clause,
        SpaceAccessAction action,
        SpaceResource resource)
    {
        if (clause.SiteId != resource.SiteId ||
            (action == SpaceAccessAction.Export && !clause.CanExport) ||
            !Matches(clause.FloorLogicalIds, resource.FloorLogicalId) ||
            !Matches(clause.ZoneLogicalIds, resource.ZoneLogicalId) ||
            !Matches(clause.OwnerIds, resource.OwnerId))
        {
            return false;
        }

        if (clause.Objects.Count == 0)
            return true;
        if (string.IsNullOrWhiteSpace(resource.BusinessObjectType) ||
            string.IsNullOrWhiteSpace(resource.BusinessObjectId))
        {
            return false;
        }
        var type = resource.BusinessObjectType.Trim().ToUpperInvariant();
        var id = resource.BusinessObjectId.Trim().ToUpperInvariant();
        return clause.Objects.Any(item =>
            item.BusinessObjectType == type &&
            item.BusinessObjectId == id);
    }

    private static bool Matches(
        IReadOnlyList<Guid> allowed,
        Guid? value) =>
        allowed.Count == 0 ||
        (value.HasValue && allowed.Contains(value.Value));

    private static bool Matches(
        IReadOnlyList<string> allowed,
        string? value) =>
        allowed.Count == 0 ||
        (!string.IsNullOrWhiteSpace(value) &&
         allowed.Contains(value.Trim().ToUpperInvariant()));

    private static SpaceAccessDecision Denied(
        string reasonCode,
        SpaceQueryScope scope) =>
        new(false, reasonCode, [], [], scope);

    private static SpaceQueryScope DeniedScope(
        string reasonCode,
        SpacePrincipal principal,
        SpaceResourceType resourceType,
        Guid? organizationId = null,
        long organizationSecurityStamp = 0,
        long membershipSecurityStamp = 0) =>
        new(
            false,
            reasonCode,
            false,
            resourceType,
            principal.TenantId,
            principal.UserId,
            organizationId,
            organizationSecurityStamp,
            membershipSecurityStamp,
            "denied",
            []);

    private static string AuthorizationVersion(
        long organizationSecurityStamp,
        long membershipSecurityStamp,
        IReadOnlyList<SpaceExternalGrant> grants)
    {
        var material = new StringBuilder()
            .Append(organizationSecurityStamp)
            .Append(':')
            .Append(membershipSecurityStamp);
        foreach (var grant in grants.OrderBy(item => item.Id))
        {
            material
                .Append(':')
                .Append(grant.Id.ToString("N"))
                .Append('@')
                .Append(grant.GrantVersion);
        }
        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(material.ToString())));
    }

    private DateTime RequireUtcNow()
    {
        var now = clock.UtcNow;
        if (now.Kind != DateTimeKind.Utc)
            throw new InvalidOperationException("The Space clock must return UTC.");
        return now;
    }
}
