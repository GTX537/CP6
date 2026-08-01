using CP6.Space.Contracts;
using CP6.Space.Domain;

namespace CP6.Space.Application;

public enum SpaceAccessAction
{
    Read = 0,
    Export = 1,
}

public enum SpaceResourceType
{
    PublishedScene = 0,
    Stock = 1,
    Task = 2,
}

public sealed record SpacePrincipal(
    Guid TenantId,
    Guid UserId,
    bool IsExternal,
    Guid? OrganizationContextId);

public sealed record SpaceOrganizationContext(Guid OrganizationId);

public sealed record SpaceResource(
    Guid TenantId,
    SpaceResourceType ResourceType,
    Guid SiteId,
    Guid? FloorLogicalId = null,
    Guid? ZoneLogicalId = null,
    string? OwnerId = null,
    string? BusinessObjectType = null,
    string? BusinessObjectId = null);

public sealed record SpaceGrantObjectScope(
    string BusinessObjectType,
    string BusinessObjectId);

public sealed record SpaceGrantClause(
    Guid GrantId,
    long GrantVersion,
    Guid SiteId,
    IReadOnlyList<Guid> FloorLogicalIds,
    IReadOnlyList<Guid> ZoneLogicalIds,
    IReadOnlyList<string> OwnerIds,
    IReadOnlyList<SpaceGrantObjectScope> Objects,
    Guid? FieldPolicyId,
    long FieldPolicyVersion,
    bool FieldPolicyCanExport,
    IReadOnlyList<SpaceGrantFieldRule> FieldRules,
    bool CanExport);

public sealed record SpaceGrantFieldRule(
    SpaceResourceType ResourceType,
    string FieldName,
    SpaceFieldMaskingRule MaskingRule);

public sealed record SpaceQueryScope(
    bool Allowed,
    string ReasonCode,
    bool IsInternal,
    SpaceResourceType ResourceType,
    Guid TenantId,
    Guid PrincipalId,
    Guid? OrganizationId,
    long OrganizationSecurityStamp,
    long MembershipSecurityStamp,
    string AuthorizationVersion,
    IReadOnlyList<SpaceGrantClause> Clauses);

public sealed record SpaceAccessDecision(
    bool Allowed,
    string ReasonCode,
    IReadOnlyList<Guid> MatchedGrantIds,
    IReadOnlyList<Guid> FieldPolicyIds,
    SpaceQueryScope Scope);

public interface ISpaceAccessEvaluator
{
    Task<SpaceAccessDecision> EvaluateAsync(
        SpacePrincipal principal,
        SpaceAccessAction action,
        SpaceResource resource,
        CancellationToken cancellationToken = default);

    Task<SpaceQueryScope> BuildQueryScopeAsync(
        SpacePrincipal principal,
        SpaceResourceType resourceType,
        SpaceOrganizationContext? organization,
        CancellationToken cancellationToken = default);
}

public interface ISpaceExternalGrantService
{
    Task<IReadOnlyList<SpaceExternalGrantDto>> GetGrantsAsync(
        Guid organizationId,
        string? status,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalGrantDto> GetGrantAsync(
        Guid organizationId,
        Guid grantId,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalGrantDto> CreateGrantAsync(
        Guid organizationId,
        CreateSpaceExternalGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<SpaceExternalGrantDto> UpdateGrantAsync(
        Guid organizationId,
        Guid grantId,
        UpdateSpaceExternalGrantRequest request,
        CancellationToken cancellationToken = default);
}

public static class SpaceAccessScopeMatcher
{
    public static SpaceAccessDecision Evaluate(
        SpaceQueryScope scope,
        SpaceAccessAction action,
        SpaceResource resource)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(resource);
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

    private static bool Matches(
        SpaceGrantClause clause,
        SpaceAccessAction action,
        SpaceResource resource)
    {
        if (clause.SiteId != resource.SiteId ||
            (action == SpaceAccessAction.Export &&
             (!clause.CanExport || !clause.FieldPolicyCanExport)) ||
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
}
