using System.Diagnostics;
using System.Security.Claims;
using CP6.Core.Services.Common;
using CP6.Core.Services.Space.Observability;
using CP6.WebApi.Localization;
using Microsoft.Extensions.Primitives;

namespace CP6.WebApi.Middleware;

public sealed class SpaceExecutionContextMiddleware
{
    private const string CorrelationHeader = "X-Correlation-ID";
    private const string TraceHeader = "X-Trace-ID";
    private const string SpacePath = "/api/space";
    private const string PortalPath = "/api/space/portal/v1";
    private const string PortalOrganizationsPath =
        "/api/space/portal/v1/organizations";

    private readonly RequestDelegate _next;
    private readonly ILogger<SpaceExecutionContextMiddleware> _logger;

    public SpaceExecutionContextMiddleware(
        RequestDelegate next,
        ILogger<SpaceExecutionContextMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        ITenantContext tenantContext,
        ISpaceExecutionContextManager manager)
    {
        if (!context.Request.Path.StartsWithSegments(SpacePath))
        {
            await _next(context);
            return;
        }

        var (correlationId, invalidCorrelation) =
            ReadCorrelation(context.Request.Headers[CorrelationHeader]);
        context.Response.Headers[CorrelationHeader] = correlationId.ToString();
        if (invalidCorrelation)
            throw new BizException(
                "SPACE_CORRELATION_ID_INVALID",
                StatusCodes.Status400BadRequest);

        var inboundActivity = Activity.Current;
        using var ownedActivity =
            IsUsableW3C(inboundActivity) ? null : StartActivity("Space.Http");
        var traceActivity = ownedActivity ?? inboundActivity;
        if (!IsUsableW3C(traceActivity))
            throw new BizException(
                "SPACE_TRACE_CONTEXT_REQUIRED",
                StatusCodes.Status500InternalServerError);
        var traceId = traceActivity!.TraceId.ToHexString();
        context.Response.Headers[TraceHeader] = traceId;

        var identities = context.User.Identities
            .Where(identity => identity.IsAuthenticated)
            .ToArray();
        if (identities.Length == 0)
            throw new BizException(
                "SPACE_AUTHENTICATION_REQUIRED",
                StatusCodes.Status401Unauthorized);

        var tenantClaims = ClaimValues(identities, "tenant_id");
        if (tenantClaims.Length != 1
            || !Guid.TryParse(tenantClaims[0], out var tenantId)
            || tenantId == Guid.Empty
            || tenantContext.CurrentTenantId != tenantId)
        {
            throw new BizException(
                "SPACE_TENANT_CONTEXT_REQUIRED",
                StatusCodes.Status403Forbidden);
        }

        var actorClaims = ClaimValues(identities, ClaimTypes.NameIdentifier);
        if (actorClaims.Length != 1
            || !Guid.TryParse(actorClaims[0], out var actorId)
            || actorId == Guid.Empty)
        {
            throw new BizException(
                "SPACE_ACTOR_CONTEXT_REQUIRED",
                StatusCodes.Status403Forbidden);
        }

        var subjectTypes = ClaimValues(identities, "subject_type");
        var organizations = ClaimValues(identities, "organization_context_id");
        if (subjectTypes.Length > 1 || organizations.Length > 1)
            throw new BizException(
                "SPACE_EXTERNAL_SUBJECT_DENIED",
                StatusCodes.Status403Forbidden);

        var subjectType = subjectTypes.SingleOrDefault();
        var organizationValue = organizations.SingleOrDefault();
        var organization = string.IsNullOrWhiteSpace(organizationValue)
            ? null
            : organizationValue;
        if (subjectTypes.Length == 1 &&
            !string.Equals(
                subjectType,
                "internal",
                StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(
                subjectType,
                "external",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BizException(
                "SPACE_EXTERNAL_SUBJECT_DENIED",
                StatusCodes.Status403Forbidden);
        }
        var external =
            string.Equals(
                subjectType,
                "external",
                StringComparison.OrdinalIgnoreCase)
            || (!string.IsNullOrWhiteSpace(organization)
                && !string.Equals(
                    subjectType,
                    "internal",
                    StringComparison.OrdinalIgnoreCase));
        var portal = context.Request.Path.StartsWithSegments(PortalPath);
        if (external && !portal)
            throw new BizException(
                "SPACE_EXTERNAL_SUBJECT_DENIED",
                StatusCodes.Status403Forbidden);
        if (portal && !string.Equals(
                subjectType,
                "external",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new BizException(
                "SPACE_EXTERNAL_PORTAL_SUBJECT_REQUIRED",
                StatusCodes.Status403Forbidden);
        }
        if (external && context.Request.Method is not ("GET" or "HEAD"))
        {
            throw new BizException(
                "SPACE_EXTERNAL_PORTAL_READ_ONLY",
                StatusCodes.Status403Forbidden);
        }
        if (external && !string.IsNullOrWhiteSpace(organization) &&
            (!Guid.TryParse(organization, out var organizationId) ||
             organizationId == Guid.Empty))
        {
            throw new BizException(
                "SPACE_ORGANIZATION_CONTEXT_REQUIRED",
                StatusCodes.Status403Forbidden);
        }
        if (external &&
            !context.Request.Path.Equals(new PathString(PortalOrganizationsPath)) &&
            string.IsNullOrWhiteSpace(organization))
        {
            throw new BizException(
                "SPACE_ORGANIZATION_CONTEXT_REQUIRED",
                StatusCodes.Status403Forbidden);
        }

        var actorNames = ClaimValues(identities, ClaimTypes.Name);
        var actorName = actorNames.Length == 1
            && !string.IsNullOrWhiteSpace(actorNames[0])
                ? actorNames[0]
                : null;
        var snapshot = SpaceExecutionContext.ForUser(
            tenantId,
            actorId.ToString(),
            actorName,
            correlationId,
            traceId,
            organization);

        using var execution = manager.Push(snapshot);
        using var logScope = _logger.BeginScope(new Dictionary<string, object?>
        {
            ["TenantId"] = snapshot.TenantId,
            ["ActorType"] = snapshot.ActorType,
            ["ActorId"] = snapshot.ActorId,
            ["CorrelationId"] = snapshot.CorrelationId,
            ["TraceId"] = snapshot.TraceId,
        });
        await _next(context);
    }

    private static (Guid CorrelationId, bool Invalid) ReadCorrelation(
        StringValues values)
    {
        if (values.Count == 0)
            return (Guid.NewGuid(), false);

        if (values.Count == 1
            && Guid.TryParse(values[0], out var parsed)
            && parsed != Guid.Empty)
        {
            return (parsed, false);
        }

        return (Guid.NewGuid(), true);
    }

    private static string[] ClaimValues(
        IEnumerable<ClaimsIdentity> identities,
        string claimType)
        => identities
            .SelectMany(identity => identity.FindAll(claimType))
            .Select(claim => claim.Value)
            .ToArray();

    private static bool IsUsableW3C(Activity? activity)
        => activity is not null
           && activity.IdFormat == ActivityIdFormat.W3C
           && activity.TraceId != default;

    private static Activity StartActivity(string name)
        => new Activity(name)
            .SetIdFormat(ActivityIdFormat.W3C)
            .Start();
}
