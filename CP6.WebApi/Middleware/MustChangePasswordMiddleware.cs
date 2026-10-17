using CP6.WebApi.Localization;
using CP6.WebApi.Controllers.Sys;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace CP6.WebApi.Middleware;

/// <summary>
/// 强制改密拦截（S 类认证加固 T6）。令牌带 must_change_password=true（首次登录/管理员重置/密码过期）时，
/// 仅放行改密/登出、重新验证凭据及公开语言读取，其余拒绝（E-SEC-009）。**读 claim 不查库**（零额外查询；
/// 改密成功后下一次签发的令牌 claim 即为 false，无需即时反映）。须在 UseAuthentication 之后（User 已填充）。
/// </summary>
public class MustChangePasswordMiddleware
{
    private readonly RequestDelegate _next;
    // #5 T5 R3 兜底：impersonation 切出端点须放行（imp 令牌恒 must_change_password=false，
    // 此处为防误传 true 锁死的纵深保险，保证 platform admin 总能切出）。
    private static readonly string[] AllowPaths = { "/api/auth/change-password", "/api/auth/logout", "/api/platform/impersonation/end" };

    public MustChangePasswordMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx)
    {
        var path = ctx.Request.Path.Value ?? "";
        if (ctx.User?.Identity?.IsAuthenticated == true
            && ctx.User.FindFirst("must_change_password")?.Value == "true"
            && !AllowPaths.Any(a => CsrfMiddleware.PathMatches(path, a))
            && !IsRelogin(ctx, path)
            && !IsPublicLanguageRead(ctx))
            throw new BizException("E-SEC-009", 403);   // 403 Forbidden：须先改密

        await _next(ctx);
    }

    private static bool IsRelogin(HttpContext ctx, string path) => HttpMethods.IsPost(ctx.Request.Method)
        && (path.Equals("/api/auth/login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/api/auth/login/", StringComparison.OrdinalIgnoreCase));

    private static bool IsPublicLanguageRead(HttpContext ctx)
    {
        var endpoint = ctx.GetEndpoint();
        return HttpMethods.IsGet(ctx.Request.Method)
            && endpoint?.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo.AsType() == typeof(LangController)
            && endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null;
    }
}
