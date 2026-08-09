using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Domain;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace CP6.Space.Infrastructure;

public sealed class SpaceExternalGrantService(
    SpaceContext context,
    ISpaceExecutionContext execution,
    ISpaceClock clock) : ISpaceExternalGrantService
{
    private const int MaximumScopeValues = 500;

    public async Task<IReadOnlyList<SpaceExternalGrantDto>> GetGrantsAsync(
        Guid organizationId,
        string? status,
        CancellationToken cancellationToken = default)
    {
        await FindOrganizationAsync(
            organizationId,
            asTracking: false,
            cancellationToken);
        var query = context.ExternalGrants
            .AsNoTracking()
            .Where(item => item.OrganizationId == organizationId);
        if (!string.IsNullOrWhiteSpace(status))
        {
            var parsed = ParseStatus(status);
            query = query.Where(item => item.Status == parsed);
        }

        var grants = await query
            .OrderBy(item => item.SiteId)
            .ThenByDescending(item => item.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        var scopes = await LoadScopesAsync(
            grants.Select(item => item.Id).ToArray(),
            cancellationToken);
        return grants
            .Select(item => ToDto(item, scopes.For(item.Id)))
            .ToArray();
    }

    public async Task<SpaceExternalGrantDto> GetGrantAsync(
        Guid organizationId,
        Guid grantId,
        CancellationToken cancellationToken = default)
    {
        await FindOrganizationAsync(
            organizationId,
            asTracking: false,
            cancellationToken);
        var grant = await FindGrantAsync(
            organizationId,
            grantId,
            asTracking: false,
            cancellationToken);
        var scopes = await LoadScopesAsync([grant.Id], cancellationToken);
        return ToDto(grant, scopes.For(grant.Id));
    }

    public async Task<SpaceExternalGrantDto> CreateGrantAsync(
        Guid organizationId,
        CreateSpaceExternalGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await FindOrganizationAsync(
            organizationId,
            asTracking: true,
            cancellationToken);
        var now = RequireUtcNow();
        var input = await ValidateScopeAsync(
            organization,
            request.SiteId,
            request.FloorLogicalIds,
            request.ZoneLogicalIds,
            request.OwnerIds,
            request.Objects,
            request.FieldPolicyId,
            cancellationToken);
        var grant = SpaceExternalGrant.Create(
            RequireTenant(),
            organizationId,
            request.SiteId,
            request.FieldPolicyId,
            request.CanExport,
            request.ValidFromUtc?.UtcDateTime ?? now,
            request.ValidToUtc?.UtcDateTime,
            ParseStatus(request.Status));
        context.ExternalGrants.Add(grant);
        AddScopes(grant, input);
        organization.TouchAuthorizationSecurityStamp();
        await SaveAsync(cancellationToken);

        var scopes = await LoadScopesAsync([grant.Id], cancellationToken);
        return ToDto(grant, scopes.For(grant.Id));
    }

    public async Task<SpaceExternalGrantDto> UpdateGrantAsync(
        Guid organizationId,
        Guid grantId,
        UpdateSpaceExternalGrantRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var organization = await FindOrganizationAsync(
            organizationId,
            asTracking: true,
            cancellationToken);
        var grant = await FindGrantAsync(
            organizationId,
            grantId,
            asTracking: true,
            cancellationToken);
        var input = await ValidateScopeAsync(
            organization,
            request.SiteId,
            request.FloorLogicalIds,
            request.ZoneLogicalIds,
            request.OwnerIds,
            request.Objects,
            request.FieldPolicyId,
            cancellationToken);

        grant.Update(
            request.SiteId,
            request.FieldPolicyId,
            request.CanExport,
            request.ValidFromUtc.UtcDateTime,
            request.ValidToUtc?.UtcDateTime,
            ParseStatus(request.Status));
        await RetireScopesAsync(grant.Id, cancellationToken);
        AddScopes(grant, input);
        organization.TouchAuthorizationSecurityStamp();
        await SaveAsync(cancellationToken);

        var scopes = await LoadScopesAsync([grant.Id], cancellationToken);
        return ToDto(grant, scopes.For(grant.Id));
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

    private async Task<SpaceExternalGrant> FindGrantAsync(
        Guid organizationId,
        Guid grantId,
        bool asTracking,
        CancellationToken cancellationToken)
    {
        if (grantId == Guid.Empty)
            throw GrantNotFound();
        IQueryable<SpaceExternalGrant> query = context.ExternalGrants;
        if (!asTracking)
            query = query.AsNoTracking();
        return await query.SingleOrDefaultAsync(
                   item =>
                       item.Id == grantId &&
                       item.OrganizationId == organizationId,
                   cancellationToken)
               ?? throw GrantNotFound();
    }

    private async Task<NormalizedGrantScope> ValidateScopeAsync(
        SpaceExternalOrganization organization,
        Guid siteId,
        IEnumerable<Guid>? floorLogicalIds,
        IEnumerable<Guid>? zoneLogicalIds,
        IEnumerable<string>? ownerIds,
        IEnumerable<SpaceExternalGrantObjectRequest>? objects,
        Guid? fieldPolicyId,
        CancellationToken cancellationToken)
    {
        if (siteId == Guid.Empty)
            throw ScopeNotFound();
        if (fieldPolicyId == Guid.Empty)
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.ExternalGrantScopeInvalid,
                422,
                "The external grant scope is invalid.",
                "FieldPolicyId cannot be empty.",
                "select-field-policy");
        }
        if (fieldPolicyId.HasValue &&
            !await context.FieldPolicies.AsNoTracking().AnyAsync(
                item =>
                    item.Id == fieldPolicyId.Value &&
                    item.Status == SpaceFieldPolicyStatus.Active &&
                    item.AudienceType == organization.Type,
                cancellationToken))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.FieldPolicyDenied,
                422,
                "The field policy cannot be used by this grant.",
                "Select an active field policy for the organization's audience.",
                "select-field-policy");
        }

        var floors = NormalizeGuids(floorLogicalIds, "FloorLogicalIds");
        var zones = NormalizeGuids(zoneLogicalIds, "ZoneLogicalIds");
        var owners = NormalizeOwners(ownerIds);
        var objectScopes = NormalizeObjects(objects);

        var model = await context.Models
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.SiteId == siteId, cancellationToken);
        if (model?.CurrentPublishedVersionId is not Guid publishedVersionId)
            throw ScopeNotFound();

        if (floors.Length > 0)
        {
            var found = await context.FloorRevisions
                .AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == publishedVersionId &&
                    item.LifecycleState == SpaceLifecycleState.Active &&
                    floors.Contains(item.LogicalId))
                .Select(item => item.LogicalId)
                .Distinct()
                .CountAsync(cancellationToken);
            if (found != floors.Length)
                throw ScopeNotFound();
        }

        if (zones.Length > 0)
        {
            var found = await context.ZoneRevisions
                .AsNoTracking()
                .Where(item =>
                    item.ModelVersionId == publishedVersionId &&
                    item.LifecycleState == SpaceLifecycleState.Active &&
                    zones.Contains(item.LogicalId))
                .Select(item => new
                {
                    item.LogicalId,
                    item.FloorLogicalId,
                })
                .ToListAsync(cancellationToken);
            if (found.Select(item => item.LogicalId).Distinct().Count() !=
                zones.Length)
            {
                throw ScopeNotFound();
            }
            if (floors.Length > 0 &&
                found.Any(item => !floors.Contains(item.FloorLogicalId)))
            {
                throw new SpaceProblemException(
                    SpaceErrorCodes.ExternalGrantScopeInvalid,
                    422,
                    "The external grant scope is inconsistent.",
                    "Every selected Zone must belong to a selected Floor.",
                    "correct-grant-scope");
            }
        }

        return new NormalizedGrantScope(
            floors,
            zones,
            owners,
            objectScopes);
    }

    private void AddScopes(
        SpaceExternalGrant grant,
        NormalizedGrantScope input)
    {
        context.ExternalGrantFloors.AddRange(input.FloorLogicalIds.Select(
            value => SpaceExternalGrantFloor.Create(
                grant.TenantId,
                grant.Id,
                value)));
        context.ExternalGrantZones.AddRange(input.ZoneLogicalIds.Select(
            value => SpaceExternalGrantZone.Create(
                grant.TenantId,
                grant.Id,
                value)));
        context.ExternalGrantOwners.AddRange(input.Owners.Select(
            value => SpaceExternalGrantOwner.Create(
                grant.TenantId,
                grant.Id,
                value.Value)));
        context.ExternalGrantObjects.AddRange(input.Objects.Select(
            value => SpaceExternalGrantObject.Create(
                grant.TenantId,
                grant.Id,
                value.BusinessObjectType,
                value.BusinessObjectId)));
    }

    private async Task RetireScopesAsync(
        Guid grantId,
        CancellationToken cancellationToken)
    {
        var floors = await context.ExternalGrantFloors
            .Where(item => item.GrantId == grantId)
            .ToListAsync(cancellationToken);
        var zones = await context.ExternalGrantZones
            .Where(item => item.GrantId == grantId)
            .ToListAsync(cancellationToken);
        var owners = await context.ExternalGrantOwners
            .Where(item => item.GrantId == grantId)
            .ToListAsync(cancellationToken);
        var objects = await context.ExternalGrantObjects
            .Where(item => item.GrantId == grantId)
            .ToListAsync(cancellationToken);
        foreach (var scope in floors.Cast<SpaceExternalGrantScope>()
                     .Concat(zones)
                     .Concat(owners)
                     .Concat(objects))
        {
            scope.Retire();
        }
    }

    private async Task<GrantScopeMap> LoadScopesAsync(
        Guid[] grantIds,
        CancellationToken cancellationToken)
    {
        if (grantIds.Length == 0)
            return GrantScopeMap.Empty;
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
        return new GrantScopeMap(floors, zones, owners, objects);
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
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
                SpaceErrorCodes.ExternalGrantConflict,
                409,
                "The external grant scope conflicts with current data.",
                recoveryAction: "reload-current-grants");
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

    private static SpaceExternalGrantStatus ParseStatus(string value)
    {
        var input = value?.Trim();
        if (string.IsNullOrEmpty(input) ||
            long.TryParse(input, out _) ||
            !Enum.TryParse<SpaceExternalGrantStatus>(
                input,
                ignoreCase: true,
                out var parsed) ||
            !Enum.IsDefined(parsed))
        {
            throw new SpaceProblemException(
                SpaceErrorCodes.RequestInvalid,
                400,
                "The request is invalid.",
                "Unsupported grant status.",
                "correct-request");
        }
        return parsed;
    }

    private static Guid[] NormalizeGuids(
        IEnumerable<Guid>? values,
        string field)
    {
        var result = values?.ToArray() ?? [];
        EnsureLimit(result.Length, field);
        if (result.Any(value => value == Guid.Empty) ||
            result.Distinct().Count() != result.Length)
        {
            throw new ArgumentException(
                $"{field} must contain unique non-empty identities.",
                field);
        }
        return result.Order().ToArray();
    }

    private static NormalizedOwner[] NormalizeOwners(
        IEnumerable<string>? values)
    {
        var source = values?.ToArray() ?? [];
        EnsureLimit(source.Length, "OwnerIds");
        var result = source
            .Select(value => NormalizeText(value, 100, "OwnerIds"))
            .Select(value => new NormalizedOwner(
                value,
                value.ToUpperInvariant()))
            .ToArray();
        if (result.Select(item => item.Normalized).Distinct().Count() !=
            result.Length)
        {
            throw new ArgumentException(
                "OwnerIds must be unique after normalization.",
                "OwnerIds");
        }
        return result.OrderBy(item => item.Normalized).ToArray();
    }

    private static NormalizedObject[] NormalizeObjects(
        IEnumerable<SpaceExternalGrantObjectRequest>? values)
    {
        var source = values?.ToArray() ?? [];
        EnsureLimit(source.Length, "Objects");
        var result = source.Select(value =>
        {
            if (value is null)
                throw new ArgumentException("Objects cannot contain null.");
            var type = NormalizeText(
                value.BusinessObjectType,
                50,
                "BusinessObjectType");
            var id = NormalizeText(
                value.BusinessObjectId,
                200,
                "BusinessObjectId");
            return new NormalizedObject(
                type,
                type.ToUpperInvariant(),
                id,
                id.ToUpperInvariant());
        }).ToArray();
        if (result
                .Select(item => $"{item.NormalizedType}\n{item.NormalizedId}")
                .Distinct(StringComparer.Ordinal)
                .Count() != result.Length)
        {
            throw new ArgumentException(
                "Objects must be unique after normalization.",
                "Objects");
        }
        return result
            .OrderBy(item => item.NormalizedType)
            .ThenBy(item => item.NormalizedId)
            .ToArray();
    }

    private static string NormalizeText(
        string? value,
        int maxLength,
        string field)
    {
        var result = value?.Trim() ?? string.Empty;
        if (result.Length == 0 || result.Length > maxLength)
        {
            throw new ArgumentException(
                $"{field} must contain 1 to {maxLength} characters.",
                field);
        }
        return result;
    }

    private static void EnsureLimit(int count, string field)
    {
        if (count > MaximumScopeValues)
        {
            throw new ArgumentException(
                $"{field} cannot contain more than {MaximumScopeValues} values.",
                field);
        }
    }

    private static SpaceExternalGrantDto ToDto(
        SpaceExternalGrant grant,
        GrantScopeSnapshot scope) =>
        new(
            grant.Id,
            grant.OrganizationId,
            grant.SiteId,
            scope.Floors,
            scope.Zones,
            scope.Owners,
            scope.Objects,
            grant.FieldPolicyId,
            grant.CanExport,
            UtcOffset(grant.ValidFromUtc),
            grant.ValidToUtc.HasValue
                ? UtcOffset(grant.ValidToUtc.Value)
                : null,
            grant.Status.ToString(),
            grant.GrantVersion,
            grant.CreatedAtUtc,
            grant.CreatedBy,
            grant.ModifiedAtUtc,
            grant.ModifiedBy);

    private static DateTimeOffset UtcOffset(DateTime value) =>
        new(DateTime.SpecifyKind(value, DateTimeKind.Utc));

    private static SpaceProblemException OrganizationNotFound() =>
        new(
            SpaceErrorCodes.ExternalOrganizationNotFound,
            404,
            "The external organization was not found.",
            recoveryAction: "select-current-tenant-organization");

    private static SpaceProblemException GrantNotFound() =>
        new(
            SpaceErrorCodes.ExternalGrantNotFound,
            404,
            "The external grant was not found.",
            recoveryAction: "select-current-organization-grant");

    private static SpaceProblemException ScopeNotFound() =>
        new(
            SpaceErrorCodes.ExternalGrantScopeInvalid,
            404,
            "The external grant scope was not found.",
            recoveryAction: "select-current-published-scope");

    private sealed record NormalizedOwner(string Value, string Normalized);

    private sealed record NormalizedObject(
        string BusinessObjectType,
        string NormalizedType,
        string BusinessObjectId,
        string NormalizedId);

    private sealed record NormalizedGrantScope(
        Guid[] FloorLogicalIds,
        Guid[] ZoneLogicalIds,
        NormalizedOwner[] Owners,
        NormalizedObject[] Objects);

    private sealed record GrantScopeSnapshot(
        IReadOnlyList<Guid> Floors,
        IReadOnlyList<Guid> Zones,
        IReadOnlyList<string> Owners,
        IReadOnlyList<SpaceExternalGrantObjectDto> Objects);

    private sealed class GrantScopeMap
    {
        public static GrantScopeMap Empty { get; } = new([], [], [], []);

        private readonly IReadOnlyDictionary<Guid, Guid[]> _floors;
        private readonly IReadOnlyDictionary<Guid, Guid[]> _zones;
        private readonly IReadOnlyDictionary<Guid, string[]> _owners;
        private readonly IReadOnlyDictionary<
            Guid,
            SpaceExternalGrantObjectDto[]> _objects;

        public GrantScopeMap(
            IReadOnlyList<SpaceExternalGrantFloor> floors,
            IReadOnlyList<SpaceExternalGrantZone> zones,
            IReadOnlyList<SpaceExternalGrantOwner> owners,
            IReadOnlyList<SpaceExternalGrantObject> objects)
        {
            _floors = floors
                .GroupBy(item => item.GrantId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.FloorLogicalId)
                        .Order()
                        .ToArray());
            _zones = zones
                .GroupBy(item => item.GrantId)
                .ToDictionary(
                    group => group.Key,
                    group => group.Select(item => item.ZoneLogicalId)
                        .Order()
                        .ToArray());
            _owners = owners
                .GroupBy(item => item.GrantId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(item => item.NormalizedOwnerId)
                        .Select(item => item.OwnerId)
                        .ToArray());
            _objects = objects
                .GroupBy(item => item.GrantId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(item => item.NormalizedBusinessObjectType)
                        .ThenBy(item => item.NormalizedBusinessObjectId)
                        .Select(item => new SpaceExternalGrantObjectDto(
                            item.BusinessObjectType,
                            item.BusinessObjectId))
                        .ToArray());
        }

        public GrantScopeSnapshot For(Guid grantId) =>
            new(
                _floors.GetValueOrDefault(grantId) ?? [],
                _zones.GetValueOrDefault(grantId) ?? [],
                _owners.GetValueOrDefault(grantId) ?? [],
                _objects.GetValueOrDefault(grantId) ?? []);
    }
}
