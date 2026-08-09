using System.Diagnostics;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Space.Observability;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;

namespace CP6.WebApi.Filters;

/// <summary>
/// 操作日志 ActionFilter（操作ログ = Kafka 専任）
///
/// 設計：操作ログは高スループット・append-only・保持/再生が要る監査ストリーム →
///       Kafka が機能適性で最適。生産者は IOperLogTransport（Kafka 実装）へ投げる。
/// - Kafka 就绪 → 投递到 topic（PrimaryTransport な議論は不要、唯一の落库担当）。
/// - Kafka 不可用 → 降级直接写 DB（保证日志不丢）。
/// - OperLog:IncludeGet=true → 连 GET 查询也记录（"任何操作都记录"）。
///
/// （RabbitMQ は業務イベント通知/アラート専任へ転用。本フィルタは関与しない。）
///
/// 始终跳过：/api/auth（防密码泄露）、/api/operlog（防自我递归）。
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
        var stopwatch = Stopwatch.StartNew();
        var method = context.HttpContext.Request.Method;
        var path = context.HttpContext.Request.Path.Value ?? "";
        var isSpace =
            context.HttpContext.Request.Path.StartsWithSegments("/api/space");

        string? requestBody = null;
        if (!isSpace &&
            (method is "POST" or "PUT" or "PATCH" or "DELETE"))
        {
            // シリアライズ不可な引数（CancellationToken / IFormFile / Stream 等）は除外する。
            // 除外しないと CancellationToken.WaitHandle.Handle(IntPtr) で
            // NotSupportedException が起き、本来の業務 API まで 500 になる。
            var args = context.ActionArguments
                .Where(kv => kv.Value is not CancellationToken
                             && kv.Value is not IFormFile
                             && kv.Value is not IFormFileCollection
                             && kv.Value is not Stream)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            if (args.Count > 0)
            {
                // ログ採取は旁路処理。万一シリアライズに失敗しても業務を止めない。
                try
                {
                    requestBody = System.Text.Json.JsonSerializer.Serialize(args);
                    if (requestBody.Length > 2000)
                        requestBody = requestBody[..2000] + "...(truncated)";
                }
                catch (Exception ex)
                {
                    requestBody = $"(serialize failed: {ex.GetType().Name})";
                }
            }
        }

        var resultContext = await next();
        stopwatch.Stop();

        // 始终跳过：登录（防密码泄露）与日志接口自身（防递归）
        if (path.Contains("/api/operlog", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth", StringComparison.OrdinalIgnoreCase))
            return;

        // GET 默认不记录；OperLog:IncludeGet=true 时才记录（"任何操作都记录"）
        if (method == "GET" && !_includeGet)
            return;

        var userName = context.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        var controllerName = context.RouteData.Values["controller"]?.ToString();
        var actionName = context.RouteData.Values["action"]?.ToString();
        var clientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString();

        var statusCode = StatusCodeOf(resultContext);

        var log = new Sys_OperLog
        {
            // 章10：在此盖当前请求租户——既覆盖 DB 降级写，也随 Kafka payload 带到消费者落库（消费者 scope 是默认租户）
            TenantId = _context.CurrentTenantId,
            // S 类 #5 R7：替身操作时从 impersonator_id claim 填真实平台超管 Id。
            // 在构造处一次性设置 → Kafka payload 序列化与 DB 降级两条路径自然透传同一字段（非每路径分别填）。
            ImpersonatorId = Guid.TryParse(context.HttpContext.User.FindFirst("impersonator_id")?.Value, out var impId)
                              ? impId
                              : (Guid?)null,
            UserName = userName,
            HttpMethod = method,
            RequestUrl = path,
            Controller = controllerName,
            Action = actionName,
            RequestBody = requestBody,
            StatusCode = statusCode,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            ClientIp = clientIp,
            CreateDate = DateTime.Now
        };

        // 投递到 Kafka（操作ログ専任通道）
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
                if (isSpace)
                    WriteSafeFailure("SPACE_OPERLOG_TRANSPORT_FAILED", ex);
                else
                    Console.WriteLine($"[OperLog] 通道 {_transport.Name} 投递失败: {ex.Message}");
            }
        }

        // Kafka 不可用 → 降级直接写 DB（保证日志不丢失）
        if (!published)
        {
            try
            {
                _context.Sys_OperLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // 降级写 DB 失败也只作旁路记录，绝不影响主业务接口
                if (isSpace)
                    WriteSafeFailure("SPACE_OPERLOG_DB_FALLBACK_FAILED", ex);
                else
                    Console.WriteLine($"[OperLog] 降级写入DB日志失败: {ex.Message}");
            }
        }
    }

    private static void WriteSafeFailure(string reasonCode, Exception exception)
    {
        var safe = SpaceErrorSanitizer.Classify(exception, reasonCode);
        Console.WriteLine(
            $"[OperLog] {safe.ReasonCode} {safe.ExceptionType} {safe.Fingerprint}");
    }

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
}
