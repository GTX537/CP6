using CP6.WebApi.Localization;

namespace CP6.WebApi.Middleware;

/// <summary>
/// 强制改密拦截（S 类认证加固 T6）。令牌带 must_change_password=true（首次登录/管理员重置/密码过期）时，
/// 除改密与登出端点外一律拒绝（E-SEC-009），逼用户先改密。**读 claim 不查库**（claim 优先，零额外查询；
/// 改密成功后下一次签发的令牌 claim 即为 false，无需即时反映）。须在 UseAuthentication 之后（User 已填充）。
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;
    private static readonly string[] AllowPaths = { "/api/auth/change-password", "/api/auth/logout" };

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (ctx.User?.Identity?.IsAuthenticated == true
            && ctx.User.FindFirst("must_change_password")?.Value == "true"
            && !AllowPaths.Any(a => CsrfMiddleware.PathMatches(path, a)))   // 带边界匹配，杜绝同前缀端点误放行
            throw new BizException("E-SEC-009", 403);   // 403 Forbidden：须先改密

        await _next(ctx);
    }
}
