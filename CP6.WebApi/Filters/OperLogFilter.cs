using System.Diagnostics;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Erp;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace CP6.WebApi.Filters;

/// <summary>
/// 采集白名单技术遥测。Kafka 不可用时用独立上下文尝试落库，失败不改变原业务结果。
/// 认证和日志本身使用各自的审计入口；默认不记录 GET。
/// </summary>
public class OperLogFilter : IAsyncActionFilter
{
    private readonly CP6Context _context;
    private readonly IOperLogTransport _transport;
    private readonly bool _includeGet;

    public OperLogFilter(
        CP6Context context,
        IOperLogTransport transport,
        IConfiguration config)
    {
        _context = context;
        _transport = transport;

        var section = config.GetSection("OperLog");
        _includeGet = section.GetValue<bool?>("IncludeGet") ?? false;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var received = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var method = context.HttpContext.Request.Method;
        var path = context.HttpContext.Request.Path;
        var isSpace = path.StartsWithSegments("/api/space");

        var resultContext = await next();
        stopwatch.Stop();

        // 始终跳过：登录（防密码泄露）与日志接口自身（防递归）
        if (path.StartsWithSegments("/api/operlog") || path.StartsWithSegments("/api/auth"))
            return;

        // GET 默认不记录；OperLog:IncludeGet=true 时才记录（"任何操作都记录"）
        if (method == "GET" && !_includeGet)
            return;

        var route = context.ActionDescriptor as ControllerActionDescriptor;
        var controllerName = route?.ControllerName ?? context.RouteData.Values["controller"]?.ToString();
        var actionName = route?.ActionName ?? context.RouteData.Values["action"]?.ToString();
        var routeKey = route?.AttributeRouteInfo?.Template ?? $"{controllerName}/{actionName}";
        var statusCode = StatusCodeOf(resultContext);
        var principal = context.HttpContext.User;
        var effective = principal.Identity?.IsAuthenticated == true
            ? ParseGuid(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value) : null;
        var actual = effective.HasValue ? ParseGuid(principal.FindFirst("impersonator_id")?.Value) : null;
        var native = context.HttpContext.Items[AuditEvidenceV1.HttpItem] as AuditEvidenceV1;
        var outcome = resultContext.Exception is OrderPriceCorrectionException e
            && (e.Message.EndsWith("OUTCOME_UNKNOWN", StringComparison.Ordinal)
                || e.Message.EndsWith("OPERATION_UNKNOWN", StringComparison.Ordinal)) ? "UNKNOWN"
            : statusCode is 401 or 403 ? "HTTP_DENIED" : statusCode >= 400 ? "HTTP_ERROR" : native?.Outcome ?? "HTTP_OBSERVED";

        var log = new Sys_OperLog
        {
            TenantId = _context.CurrentTenantId,
            ImpersonatorId = actual,
            HttpMethod = method is "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" ? method : "OTHER",
            RequestUrl = routeKey,
            Controller = controllerName,
            Action = actionName,
            StatusCode = statusCode,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            CreateDate = received.LocalDateTime,
            AuditAtUtc = received,
            AuditEvidenceJson = new AuditEvidenceV1("REQUEST_RECEIVED_UTC", "BEST_EFFORT_TELEMETRY",
                native?.ActorKind ?? (actual.HasValue ? "IMPERSONATED" : effective.HasValue ? "USER" : "UNBOUND"),
                native?.ActualActor ?? actual ?? effective, native?.EffectiveActor ?? effective, native?.Service,
                native?.PermissionVersion ?? context.HttpContext.Items[AuditEvidenceV1.PermissionVersionItem] as string, native?.OperationId, outcome,
                Activity.Current?.TraceId.ToHexString() ?? Guid.NewGuid().ToString("N"), RecordedAtUtc: DateTimeOffset.UtcNow,
                DataPolicy: "HTTP_TELEMETRY_V1", Action: actionName, OriginalOperationDigest: native?.OriginalOperationDigest,
                SnapshotAsOfUtc: context.HttpContext.Items[AuditEvidenceV1.SnapshotAsOfItem] as DateTimeOffset?).Encode()
        };

        // 优先投递 Kafka。
        var published = false;
        if (_transport.IsConnected)
        {
            try
            {
                await _transport.PublishAsync(log);
                published = true;
            }
            catch (Exception ex)
            {
                WriteSafeFailure(isSpace ? "SPACE_OPERLOG_TRANSPORT_FAILED" : "OPERLOG_TRANSPORT_FAILED", ex);
            }
        }

        // Kafka 不可用时尝试旁路写入数据库。
        if (!published)
        {
            try
            {
                if (_context.Database.IsNpgsql())
                {
                    // 旁路日志不能刷入业务动作遗留的跟踪变更。
                    await using var scope = context.HttpContext.RequestServices.CreateAsyncScope();
                    var services = scope.ServiceProvider;
                    services.GetRequiredService<ITenantContext>().CurrentTenantId = log.TenantId;
                    var logContext = services.GetRequiredService<CP6Context>();
                    logContext.Sys_OperLogs.Add(log);
                    await logContext.SaveChangesAsync();
                }
                else
                {
                    _context.Sys_OperLogs.Add(log);
                    await _context.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                // 降级写 DB 失败也只作旁路记录，绝不影响主业务接口
                WriteSafeFailure(isSpace ? "SPACE_OPERLOG_DB_FALLBACK_FAILED" : "OPERLOG_DB_FALLBACK_FAILED", ex);
            }
        }
    }

    private static Guid? ParseGuid(string? input)
        => Guid.TryParse(input, out var id) && id != Guid.Empty ? id : null;

    private static void WriteSafeFailure(string reasonCode, Exception exception)
    {
        var safe = SpaceErrorSanitizer.Classify(exception, reasonCode);
        Console.WriteLine(
            $"[OperLog] {safe.ReasonCode} {safe.ExceptionType} {safe.Fingerprint}");
    }

    private static int StatusCodeOf(ActionExecutedContext executed)
    {
        if (executed.Exception is OrderPriceCorrectionException owner)
            return owner.Status;
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
}
