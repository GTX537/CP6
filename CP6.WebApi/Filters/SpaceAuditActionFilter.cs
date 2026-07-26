using CP6.Core.Services.Space.Observability;
using Microsoft.AspNetCore.Mvc;
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
        if (!IsSpaceMutation(context, method))
        {
            await next();
            return;
        }

        var controller =
            context.ActionDescriptor.RouteValues["controller"] ?? "Space";
        var action =
            context.ActionDescriptor.RouteValues["action"] ?? "Unknown";
        var resourceType = $"{controller}.{action}";
        var request = context.HttpContext.Request;
        var auditAction = $"space.http.{method.ToLowerInvariant()}";

        var started = await _writer.TryAppendAsync(
            new SpaceAuditEventInput(
                Action: auditAction,
                ResourceType: resourceType,
                ResourceId: request.Path.Value,
                Outcome: SpaceAuditOutcome.Started,
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
                    ResourceId: request.Path.Value,
                    Outcome: SpaceAuditOutcome.Failed,
                    ReasonCode: safe.ReasonCode,
                    Evidence: new SpaceAuditEvidence(
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
                ResourceId: request.Path.Value,
                Outcome: outcome,
                ReasonCode: safeError?.ReasonCode,
                Evidence: safeError is null
                    ? new SpaceAuditEvidence(Status: status.ToString())
                    : new SpaceAuditEvidence(
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
