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
            // 带边界精确匹配：仅 /api/auth/login(/...) 豁免，杜绝 /api/auth/login-xxx 这类同前缀端点被静默豁免
            var exempt = PathMatches(path, "/api/auth/login");
            if (!exempt && UnsafeMethods.Contains(ctx.Request.Method.ToUpperInvariant()))
            {
                var cookie = ctx.Request.Cookies[AuthCookieWriter.CsrfCookie];
                var header = ctx.Request.Headers["X-CSRF-Token"].ToString();
                if (string.IsNullOrEmpty(cookie) || cookie != header)
                    throw new BizException("E-SEC-010", 403);   // 403 Forbidden：CSRF 校验失败（spec §5.3）
            }
        }
        await _next(ctx);
    }

    /// <summary>路径段边界匹配：path == prefix 或 path 以 "prefix/" 起头（避免同前缀误匹配）。</summary>
    internal static bool PathMatches(string path, string prefix)
        => path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
           || path.StartsWith(prefix + "/", StringComparison.OrdinalIgnoreCase);
}
