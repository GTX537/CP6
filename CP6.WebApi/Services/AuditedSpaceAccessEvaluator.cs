using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.Space.Infrastructure;

namespace CP6.WebApi.Services;

public sealed class AuditedSpaceAccessEvaluator(
    SpaceAccessEvaluator inner,
    ISpaceAuditWriter audit,
    IHttpContextAccessor httpContextAccessor) : ISpaceAccessEvaluator
{
    public async Task<SpaceAccessDecision> EvaluateAsync(
        SpacePrincipal principal,
        SpaceAccessAction action,
        SpaceResource resource,
        CancellationToken cancellationToken = default)
    {
        var decision = await inner.EvaluateAsync(
            principal,
            action,
            resource,
            cancellationToken);
        if (action != SpaceAccessAction.Export || !principal.IsExternal)
            return decision;

        var request = httpContextAccessor.HttpContext?.Request;
        var appended = false;
        try
        {
            appended = await audit.TryAppendAsync(
                new SpaceAuditEventInput(
                    Action: "space.external.export.attempt",
                    ResourceType: resource.ResourceType.ToString(),
                    ResourceId: resource.SiteId.ToString(),
                    Outcome: decision.Allowed
                        ? SpaceAuditOutcome.Succeeded
                        : SpaceAuditOutcome.Denied,
                    ReasonCode: decision.Allowed ? null : decision.ReasonCode,
                    SiteId: resource.SiteId,
                    Evidence: new SpaceAuditEvidence(
                        PermissionCode: "space:external:export",
                        AuthorizationResult: decision.Allowed
                            ? "Allowed"
                            : "Denied",
                        OrganizationId: decision.Scope.OrganizationId,
                        OrganizationSecurityStamp:
                            decision.Scope.OrganizationSecurityStamp,
                        MembershipSecurityStamp:
                            decision.Scope.MembershipSecurityStamp,
                        AuthorizationVersion:
                            decision.Scope.AuthorizationVersion,
                        GrantIds: decision.MatchedGrantIds,
                        FieldPolicyIds: decision.FieldPolicyIds),
                    ClientType: "Web",
                    IpAddress: httpContextAccessor.HttpContext?.Connection
                        .RemoteIpAddress?.ToString(),
                    UserAgent: request?.Headers.UserAgent.ToString()),
                cancellationToken);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Export authorization is fail-closed when its mandatory audit
            // evidence cannot be persisted.
        }

        return appended
            ? decision
            : decision with
            {
                Allowed = false,
                ReasonCode = SpaceErrorCodes.AuditUnavailable,
                MatchedGrantIds = [],
                FieldPolicyIds = [],
            };
    }

    public Task<SpaceQueryScope> BuildQueryScopeAsync(
        SpacePrincipal principal,
        SpaceResourceType resourceType,
        SpaceOrganizationContext? organization,
        CancellationToken cancellationToken = default) =>
        inner.BuildQueryScopeAsync(
            principal,
            resourceType,
            organization,
            cancellationToken);
}
