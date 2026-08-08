using System.Diagnostics;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Space.Observability;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Auth;

/// <summary>
/// 功能权限后端强校验 —— PUB 章02 §4。贴在控制器/动作上，命中 "menuKey:action" 才放行，否则 403。
/// <para>与 [Authorize]（验登录）配合：[Authorize] 先验身份，本特性再验操作权。</para>
/// <para>特性不能构造注入，用 RequestServices 服务定位取 <see cref="IPermissionService"/>。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePermissionAttribute : Attribute, IAsyncAuthorizationFilter
{
    private readonly string _menu;
    private readonly string _action;

    public bool UseProblemDetails { get; set; }

    public RequirePermissionAttribute(string menu, string action)
    {
        _menu = menu;
        _action = action;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var svc = context.HttpContext.RequestServices.GetService<IPermissionService>();
        if (svc == null)
        {
            context.Result = UseProblemDetails
                ? ProblemResult(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "SPACE_PERMISSION_SERVICE_UNAVAILABLE",
                    "The Space permission service is unavailable.",
                    "Try the request again later.",
                    "retry",
                    true)
                : new ObjectResult(new { code = 500, message = "权限服务未注册" })
                {
                    StatusCode = StatusCodes.Status500InternalServerError,
                };
            return;
        }

        if (!await svc.HasActionAsync(_menu, _action))
        {
            var auditRead =
                _menu == "space-audit" && _action == "read";
            var message = auditRead
                ? "SPACE_AUDIT_READ_FORBIDDEN"
                : $"无权限：{_menu}:{_action}";
            context.Result = UseProblemDetails
                ? ProblemResult(
                    context,
                    StatusCodes.Status403Forbidden,
                    "SPACE_PERMISSION_DENIED",
                    "The Space request was denied.",
                    "Request access to use this Space operation.",
                    "request-access",
                    false)
                : new ObjectResult(new { code = 403, message })
                {
                    StatusCode = StatusCodes.Status403Forbidden,
                };

            await AuditSpaceDenialAsync(context, auditRead);
        }
    }

    private static ObjectResult ProblemResult(
        AuthorizationFilterContext context,
        int status,
        string code,
        string title,
        string detail,
        string recoveryAction,
        bool retryable)
    {
        var http = context.HttpContext;
        var problem = new ProblemDetails
        {
            Type = $"https://cp6.example/problems/{ToSlug(code)}",
            Title = title,
            Status = status,
            Detail = detail,
            Instance = http.Request.Path,
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] =
            http.Response.Headers["X-Trace-ID"].FirstOrDefault()
            ?? Activity.Current?.TraceId.ToHexString()
            ?? http.TraceIdentifier;
        problem.Extensions["correlationId"] =
            http.Response.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? http.Request.Headers["X-Correlation-ID"].FirstOrDefault()
            ?? string.Empty;
        problem.Extensions["recovery"] = new
        {
            action = recoveryAction,
            retryable,
        };

        return new ObjectResult(problem)
        {
            StatusCode = status,
            ContentTypes = { "application/problem+json" },
        };
    }

    private static string ToSlug(string code) =>
        code.ToLowerInvariant().Replace('_', '-');

    private async Task AuditSpaceDenialAsync(
        AuthorizationFilterContext context,
        bool auditRead)
    {
        var request = context.HttpContext.Request;
        if (!request.Path.StartsWithSegments("/api/space"))
            return;

        var writer = context.HttpContext.RequestServices
            .GetService<ISpaceAuditWriter>();
        if (writer is null)
            return;

        var requestAborted = context.HttpContext.RequestAborted;
        try
        {
            await writer.TryAppendAsync(
                new SpaceAuditEventInput(
                    Action: "space.permission.check",
                    ResourceType:
                        context.ActionDescriptor.DisplayName ??
                        "SpaceAction",
                    ResourceId: request.Path.Value,
                    Outcome: SpaceAuditOutcome.Denied,
                    ReasonCode: auditRead
                        ? "SPACE_AUDIT_READ_FORBIDDEN"
                        : "SPACE_PERMISSION_DENIED",
                    Evidence: new SpaceAuditEvidence(
                        PermissionCode: $"{_menu}:{_action}",
                        AuthorizationResult: "Denied"),
                    ClientType: "Web",
                    IpAddress:
                        context.HttpContext.Connection.RemoteIpAddress
                            ?.ToString(),
                    UserAgent: request.Headers["User-Agent"].ToString()),
                requestAborted);
        }
        catch (OperationCanceledException)
            when (requestAborted.IsCancellationRequested)
        {
            // The 403 result was already established. Host/request
            // cancellation must not turn a permission denial into a 500.
        }
        catch
        {
            // Permission denial audit is fail-open for the response. The
            // production writer emits a safe operational classification.
        }
    }
}
