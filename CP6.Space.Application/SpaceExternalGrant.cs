using CP6.Space.Contracts;

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
    bool CanExport);

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
