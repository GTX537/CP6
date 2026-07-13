using CP6.Core.Services.Sys;
using CP6.WebApi.Localization;
using Microsoft.Extensions.Options;

namespace CP6.WebApi.Middleware;

/// <summary>
/// CSRF 双提交校验（S 类认证加固 T6）。Cookie 化后浏览器自动带 cp6_at，需防跨站伪造写请求：
/// 非安全方法须同时带 cp6_csrf cookie 与 X-CSRF-Token 头且二者相等（攻击者跨站无法读非 httpOnly
/// cookie 也无法设自定义头）。开关 Security:Csrf:Enabled（默认 true；开发/QA 在 T9 前端注入头前置 false）。
/// 登录端点豁免（登录时尚无 csrf cookie）；refresh 不豁免（轮换是高价值写操作）。
/// </summary>
public class CsrfMiddleware
{
    private readonly RequestDelegate _next;
    private readonly bool _enabled;
    private static readonly string[] UnsafeMethods = { "POST", "PUT", "PATCH", "DELETE" };

    public CsrfMiddleware(RequestDelegate next, IOptions<SecurityOptions> opt)
    {
        _next = next;
        _enabled = opt.Value.Csrf.Enabled;
    }

    public async Task Invoke(HttpContext ctx)
    {
        if (_enabled)
        {
            var path = ctx.Request.Path.Value ?? "";
            if (!IsExempt(path) && UnsafeMethods.Contains(ctx.Request.Method.ToUpperInvariant()))
            {
                var cookie = ctx.Request.Cookies[AuthCookieWriter.CsrfCookie];
                var header = ctx.Request.Headers["X-CSRF-Token"].ToString();
                if (string.IsNullOrEmpty(cookie) || cookie != header)
                    throw new BizException("E-SEC-010", 403);   // 403 Forbidden：CSRF 校验失败（spec §5.3）
            }
        }
        await _next(ctx);
    }

    /// <summary>CSRF 豁免路径（段边界匹配，杜绝同前缀误豁免）：
    /// ① 登录端点（登录时尚无 csrf cookie，杜绝 /api/auth/login-xxx 这类同前缀端点被静默豁免）；
    /// ② SignalR hub 路径（negotiate 是 POST 但不改业务状态）——豁免安全论据：
    ///    (a) 现有 4 个 hub(notify/mes/wms/space)均无状态变更方法，仅 Subscribe/Unsubscribe 组操作
    ///        (Groups.Add/RemoveToGroupAsync)，即便被跨站触发也无业务副作用；
    ///    (b) CORS 显式 allowlist（Program.cs WithOrigins + AllowCredentials，无通配源）挡跨站 negotiate。
    ///    ⚠ 前瞻警示：未来若给任一 hub 加可 invoke 的状态变更方法，须重新评估此豁免（届时应移除 /hubs 整段
    ///      豁免或改为仅豁免 negotiate 端点），否则跨站可经 hub 方法绕过 CSRF。
    /// 票11：否则 /hubs/*/negotiate 被 403 拦，实时通知连不上。/hubs 前缀覆盖 notify/mes/wms/space 全部 hub。</summary>
    internal static bool IsExempt(string path)
        => PathMatches(path, "/api/auth/login")
           || PathMatches(path, "/hubs");

    /// <summary>路径段边界匹配：path == prefix 或 path 以 "prefix/" 起头（避免同前缀误匹配）。</summary>
    internal static bool PathMatches(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
