using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using CP6.WebApi.OpenApi;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Filters;

/// <summary>
/// Marks the deliberately narrow Published-only API surface that external
/// principals may use. Every other Design V1 contract controller is an
/// internal control-plane surface and fails closed before controller or
/// service code can access Draft, Source, Lease, Validation, AI, or Publish
/// data.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true)]
public sealed class AllowSpaceExternalSubjectAttribute : Attribute;

public sealed class SpaceDesignControlPlaneSubjectFilter(
    CP6.Space.Application.ISpaceExecutionContext execution,
    ISpaceAuditWriter auditWriter) : IAsyncAuthorizationFilter, IOrderedFilter
{
    public const int OrderValue = -900;

    // Run before permission attributes so an accidentally granted external
    // role still receives the stable control-plane denial. Authorization
    // filters also run before model binding, which fences file uploads before
    // ASP.NET reads the request body.
    public int Order => OrderValue;

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (!execution.IsExternal ||
            context.ActionDescriptor is not ControllerActionDescriptor action ||
            !IsDesignV1Controller(action) ||
            AllowsExternalSubject(action))
        {
            return;
        }

        await TryAuditDenialAsync(context, action);

        throw new SpaceProblemException(
            SpaceErrorCodes.ExternalSubjectDenied,
            StatusCodes.Status403Forbidden,
            "External principals cannot access the Space design control plane.",
            "Use the Published-only external portal.",
            recoveryAction: "use-published-portal");
    }

    private async Task TryAuditDenialAsync(
        AuthorizationFilterContext context,
        ControllerActionDescriptor action)
    {
        var request = context.HttpContext.Request;
        try
        {
            await auditWriter.TryAppendAsync(
                new SpaceAuditEventInput(
                    Action: "space.external.control-plane.denied",
                    ResourceType:
                        $"{action.ControllerName}.{action.ActionName}",
                    ResourceId: request.Path.Value,
                    Outcome: SpaceAuditOutcome.Denied,
                    ReasonCode: SpaceErrorCodes.ExternalSubjectDenied,
                    Evidence: new SpaceAuditEvidence(
                        AuthorizationResult: "Denied",
                        OrganizationId: execution.OrganizationContextId),
                    ClientType: "Web",
                    IpAddress: context.HttpContext.Connection
                        .RemoteIpAddress?.ToString(),
                    UserAgent: request.Headers.UserAgent.ToString()),
                context.HttpContext.RequestAborted);
        }
        catch (OperationCanceledException)
            when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            // The external subject remains denied even when the client drops.
        }
        catch
        {
            // Denial is fail-closed. The production writer emits its own safe
            // operational classification when the audit sink is unavailable.
        }
    }

    private static bool IsDesignV1Controller(
        ControllerActionDescriptor action) =>
        action.ControllerTypeInfo.IsDefined(
            typeof(SpaceDesignV1ContractAttribute),
            inherit: true);

    private static bool AllowsExternalSubject(
        ControllerActionDescriptor action) =>
        action.ControllerTypeInfo.IsDefined(
            typeof(AllowSpaceExternalSubjectAttribute),
            inherit: true) ||
        action.MethodInfo.IsDefined(
            typeof(AllowSpaceExternalSubjectAttribute),
            inherit: true);
}
