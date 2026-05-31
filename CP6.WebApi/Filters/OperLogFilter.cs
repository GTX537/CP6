using System.Diagnostics;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Utilities;
using CP6.Entity.DomainModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace CP6.WebApi.Filters;

/// <summary>
/// 操作日志 ActionFilter（支持 RabbitMQ 异步写入）
///
/// 改造思路：
/// - RabbitMQ 可用 → 发消息到队列（非阻塞，Consumer 异步写 DB）
/// - RabbitMQ 不可用 → 降级为直接写 DB（保证日志不丢失）
///
/// 面试要点：
/// 1. 消息队列实现"异步解耦"：日志写入不影响接口响应时间
/// 2. 降级策略：MQ 挂了不影响业务，fallback 到同步模式
/// 3. MOM 系统中同样模式：设备数据上报 → MQ → 异步入库
/// </summary>
public class OperLogFilter : IAsyncActionFilter
{
    private readonly CP6Context _context;
    private readonly RabbitMQService _mq;

    public OperLogFilter(CP6Context context, RabbitMQService mq)
    {
        _context = context;
        _mq = mq;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var stopwatch = Stopwatch.StartNew();

        string? requestBody = null;
        if (context.HttpContext.Request.Method is "POST" or "PUT" or "DELETE")
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

        var method = context.HttpContext.Request.Method;
        var path = context.HttpContext.Request.Path.Value ?? "";
        if (method == "GET"
            || path.Contains("/api/operlog", StringComparison.OrdinalIgnoreCase)
            || path.Contains("/api/auth", StringComparison.OrdinalIgnoreCase))
            return;

        var userName = context.HttpContext.User.FindFirst(ClaimTypes.Name)?.Value;
        var controllerName = context.RouteData.Values["controller"]?.ToString();
        var actionName = context.RouteData.Values["action"]?.ToString();
        var clientIp = context.HttpContext.Connection.RemoteIpAddress?.ToString();

        var statusCode = 200;
        if (resultContext.Exception != null)
            statusCode = 500;
        else if (resultContext.Result is ObjectResult objResult)
            statusCode = objResult.StatusCode ?? 200;

        var log = new Sys_OperLog
        {
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

        if (_mq.IsConnected)
        {
            // MQ 可用 → 异步发消息（不阻塞当前请求）
            try
            {
                await _mq.PublishAsync(RabbitMQService.OperLogQueue, log);
            }
            catch (Exception ex)
            {
                // 可接入本地日志(如NLog/Serilog)或控制台，防止序列化/网络异常导致业务中断
                Console.WriteLine($"[OperLog] MQ发布日志失败: {ex.Message}");
            }
        }
        else
        {
            // MQ 不可用 → 降级为直接写 DB
            try
            {
                _context.Sys_OperLogs.Add(log);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // 降级写DB失败时，作为旁路逻辑，绝不能影响主业务接口的正常响应
                Console.WriteLine($"[OperLog] 降级写入DB日志失败: {ex.Message}");
            }
        }
    }
}
