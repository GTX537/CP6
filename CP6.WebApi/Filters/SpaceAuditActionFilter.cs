using CP6.Core.Services.Space.Observability;
using CP6.Space.Application;
using CP6.Space.Contracts;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace CP6.WebApi.Filters;

public sealed class SpaceAuditActionFilter : IAsyncActionFilter
{
    private const string SpaceControllerNamespace =
        "CP6.WebApi.Controllers.Space";

    private readonly ISpaceAuditWriter _writer;

    public SpaceAuditActionFilter(ISpaceAuditWriter writer) => _writer = writer;

    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var operation = ResolveOperation(context);
        if (!IsSpaceMutation(context, method))
        {
            if (operation?.AuditRead == true &&
                (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)))
            {
                await AuditReadAsync(context, next, operation);
                return;
            }
            await next();
            return;
        }

        var controller =
            context.ActionDescriptor.RouteValues["controller"] ?? "Space";
        var action =
            context.ActionDescriptor.RouteValues["action"] ?? "Unknown";
        var resourceType = operation?.ResourceType ?? $"{controller}.{action}";
        var request = context.HttpContext.Request;
        var auditAction = operation?.Action ??
            $"space.http.{method.ToLowerInvariant()}";
        var resourceId = ResourceId(context, operation) ?? request.Path.Value;
        var siteId = GuidArgument(context, operation?.SiteIdArgument);

        var started = await _writer.TryAppendAsync(
            new SpaceAuditEventInput(
                Action: auditAction,
                ResourceType: resourceType,
                ResourceId: resourceId,
                Outcome: SpaceAuditOutcome.Started,
                SiteId: siteId,
                Evidence: operation?.PermissionCode is null
                    ? null
                    : new SpaceAuditEvidence(
                        PermissionCode: operation.PermissionCode,
                        AuthorizationResult: "Allowed"),
                ClientType: "Web",
                IpAddress:
                    context.HttpContext.Connection.RemoteIpAddress?.ToString(),
                UserAgent: request.Headers.UserAgent.ToString()),
            context.HttpContext.RequestAborted);
        if (!started)
        {
            context.Result = Error(
                StatusCodes.Status503ServiceUnavailable,
                "SPACE_AUDIT_UNAVAILABLE");
            return;
        }

        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch (Exception exception)
        {
            var safe = SpaceErrorSanitizer.Classify(
                exception,
                "SPACE_ACTION_FAILED");
            await _writer.TryAppendAsync(
                new SpaceAuditEventInput(
                    Action: auditAction,
                    ResourceType: resourceType,
                    ResourceId: resourceId,
                    Outcome: SpaceAuditOutcome.Failed,
                    ReasonCode: safe.ReasonCode,
                    SiteId: siteId,
                    Evidence: new SpaceAuditEvidence(
                        PermissionCode: operation?.PermissionCode,
                        AuthorizationResult: "Failed",
                        ExceptionType: safe.ExceptionType,
                        ErrorFingerprint: safe.Fingerprint),
                    ClientType: "Web"),
                CancellationToken.None);
            throw;
        }

        var status = StatusCodeOf(executed);
        var outcome = status is
            StatusCodes.Status401Unauthorized or
            StatusCodes.Status403Forbidden
                ? SpaceAuditOutcome.Denied
                : executed.Exception is not null ||
                  status >= StatusCodes.Status400BadRequest
                    ? SpaceAuditOutcome.Failed
                    : SpaceAuditOutcome.Succeeded;
        var safeError = executed.Exception is null
            ? null
            : SpaceErrorSanitizer.Classify(
                executed.Exception,
                "SPACE_ACTION_FAILED");

        // The action may already have committed side effects. Do not let a
        // disconnected client cancel the mandatory outcome append.
        var appended = await _writer.TryAppendAsync(
            new SpaceAuditEventInput(
                Action: auditAction,
                ResourceType: resourceType,
                ResourceId: resourceId,
                Outcome: outcome,
                ReasonCode: safeError?.ReasonCode,
                SiteId: siteId,
                Evidence: safeError is null
                    ? new SpaceAuditEvidence(
                        PermissionCode: operation?.PermissionCode,
                        AuthorizationResult: AuthorizationResult(outcome),
                        Status: status.ToString())
                    : new SpaceAuditEvidence(
                        PermissionCode: operation?.PermissionCode,
                        AuthorizationResult: AuthorizationResult(outcome),
                        Status: status.ToString(),
                        ExceptionType: safeError.ExceptionType,
                        ErrorFingerprint: safeError.Fingerprint),
                ClientType: "Web"),
            CancellationToken.None);

        if (!appended &&
            outcome == SpaceAuditOutcome.Succeeded &&
            executed.Exception is null)
        {
            executed.Result = Error(
                StatusCodes.Status503ServiceUnavailable,
                "SPACE_OPERATION_OUTCOME_UNKNOWN");
        }
    }

    private async Task AuditReadAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next,
        SpaceAuditOperationAttribute operation)
    {
        var resourceId = ResourceId(context, operation) ??
            context.HttpContext.Request.Path.Value;
        var siteId = GuidArgument(context, operation.SiteIdArgument);
        ActionExecutedContext executed;
        try
        {
            executed = await next();
        }
        catch (Exception exception)
        {
            await TryAppendReadAsync(
                ReadInput(
                    context,
                    operation,
                    resourceId,
                    siteId,
                    exception is SpaceProblemException problem &&
                    problem.StatusCode is 401 or 403 or 404
                        ? SpaceAuditOutcome.Denied
                        : SpaceAuditOutcome.Failed,
                    exception is SpaceProblemException known
                        ? known.Code
                        : SpaceErrorSanitizer.Classify(
                            exception,
                            "SPACE_EXTERNAL_READ_FAILED").ReasonCode,
                    exception: exception),
                CancellationToken.None);
            throw;
        }

        var status = StatusCodeOf(executed);
        var outcome = status is 401 or 403 or 404
            ? SpaceAuditOutcome.Denied
            : executed.Exception is not null || status >= 400
                ? SpaceAuditOutcome.Failed
                : SpaceAuditOutcome.Succeeded;
        var reasonCode = executed.Exception switch
        {
            SpaceProblemException problem => problem.Code,
            not null => SpaceErrorSanitizer.Classify(
                executed.Exception,
                "SPACE_EXTERNAL_READ_FAILED").ReasonCode,
            _ => null,
        };
        await TryAppendReadAsync(
            ReadInput(
                context,
                operation,
                resourceId,
                siteId,
                outcome,
                reasonCode,
                status,
                executed.Result,
                executed.Exception),
            CancellationToken.None);
    }

    private static SpaceAuditEventInput ReadInput(
        ActionExecutingContext context,
        SpaceAuditOperationAttribute operation,
        string? resourceId,
        Guid? siteId,
        string outcome,
        string? reasonCode,
        int? status = null,
        IActionResult? result = null,
        Exception? exception = null)
    {
        var safe = exception is null
            ? null
            : SpaceErrorSanitizer.Classify(
                exception,
                "SPACE_EXTERNAL_READ_FAILED");
        var (itemCount, authorizationVersion) = ResultEvidence(result);
        var request = context.HttpContext.Request;
        return new SpaceAuditEventInput(
            Action: operation.Action,
            ResourceType: operation.ResourceType,
            ResourceId: resourceId,
            Outcome: outcome,
            ReasonCode: reasonCode,
            SiteId: siteId,
            Evidence: new SpaceAuditEvidence(
                PermissionCode: operation.PermissionCode,
                AuthorizationResult: AuthorizationResult(outcome),
                ItemCount: itemCount,
                Status: status?.ToString(),
                ExceptionType: safe?.ExceptionType,
                ErrorFingerprint: safe?.Fingerprint,
                AuthorizationVersion: authorizationVersion),
            ClientType: "Web",
            IpAddress: context.HttpContext.Connection.RemoteIpAddress
                ?.ToString(),
            UserAgent: request.Headers.UserAgent.ToString());
    }

    private async Task TryAppendReadAsync(
        SpaceAuditEventInput input,
        CancellationToken cancellationToken)
    {
        try
        {
            await _writer.TryAppendAsync(input, cancellationToken);
        }
        catch
        {
            // Authorized read DTOs are already clipped. Read audit is
            // fail-open; the production writer emits a safe operational log.
        }
    }

    private static SpaceAuditOperationAttribute? ResolveOperation(
        ActionExecutingContext context)
    {
        var method = (context.ActionDescriptor as ControllerActionDescriptor)
            ?.MethodInfo;
        return method?
            .GetCustomAttributes(
                typeof(SpaceAuditOperationAttribute),
                inherit: true)
            .OfType<SpaceAuditOperationAttribute>()
            .SingleOrDefault();
    }

    private static string? ResourceId(
        ActionExecutingContext context,
        SpaceAuditOperationAttribute? operation)
    {
        if (string.IsNullOrWhiteSpace(operation?.ResourceIdArgument) ||
            !context.ActionArguments.TryGetValue(
                operation.ResourceIdArgument,
                out var value))
        {
            return null;
        }
        return value switch
        {
            Guid id when id != Guid.Empty => id.ToString(),
            string text when !string.IsNullOrWhiteSpace(text) => text,
            _ => null,
        };
    }

    private static Guid? GuidArgument(
        ActionExecutingContext context,
        string? argumentName)
    {
        if (string.IsNullOrWhiteSpace(argumentName) ||
            !context.ActionArguments.TryGetValue(argumentName, out var value) ||
            value is not Guid id || id == Guid.Empty)
        {
            return null;
        }
        return id;
    }

    private static (int? ItemCount, string? AuthorizationVersion)
        ResultEvidence(IActionResult? result)
    {
        var value = (result as ObjectResult)?.Value;
        return value switch
        {
            IReadOnlyList<SpacePortalOrganizationDto> organizations =>
                (organizations.Count, null),
            IReadOnlyList<SpacePortalSiteDto> sites =>
                (sites.Count, SingleAuthorizationVersion(
                    sites.Select(item => item.AuthorizationVersion))),
            SpacePortalPublishedSceneDto scene =>
                (scene.Floors.Count, scene.AuthorizationVersion),
            SpacePortalStockResponse stock =>
                (stock.Items.Count, stock.AuthorizationVersion),
            SpacePortalTaskResponse tasks =>
                (tasks.Items.Count, tasks.AuthorizationVersion),
            _ => (null, null),
        };
    }

    private static string? SingleAuthorizationVersion(
        IEnumerable<string> versions)
    {
        var values = versions.Distinct(StringComparer.Ordinal).Take(2).ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private static string AuthorizationResult(string outcome) =>
        outcome == SpaceAuditOutcome.Succeeded
            ? "Allowed"
            : outcome == SpaceAuditOutcome.Denied
                ? "Denied"
                : "Failed";

    private static bool IsSpaceMutation(
        ActionExecutingContext context,
        string method) =>
        context.Controller.GetType().Namespace == SpaceControllerNamespace &&
        (HttpMethods.IsPost(method) ||
         HttpMethods.IsPut(method) ||
         HttpMethods.IsPatch(method) ||
         HttpMethods.IsDelete(method));

    private static int StatusCodeOf(ActionExecutedContext executed)
    {
        if (executed.Exception is SpaceProblemException problem)
            return problem.StatusCode;
        if (executed.Exception is not null)
            return StatusCodes.Status500InternalServerError;

        var status = executed.Result switch
        {
            ForbidResult => StatusCodes.Status403Forbidden,
            ChallengeResult => StatusCodes.Status401Unauthorized,
            IStatusCodeActionResult { StatusCode: int value } => value,
            _ => executed.HttpContext.Response.StatusCode,
        };
        return status is >= 100 and <= 599
            ? status
            : StatusCodes.Status200OK;
    }

    private static ObjectResult Error(int status, string code) =>
        new(new
        {
            code = status,
            message = code,
            data = (object?)null,
        })
        {
            StatusCode = status,
        };
}
