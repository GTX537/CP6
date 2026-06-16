using CP6.Core.Services.Common;

namespace CP6.WebApi.Middleware;

/// <summary>
/// 租户中间件（OA 章10 §5）。从 JWT 的 tenant_id claim 解析当前租户，写入请求级 <see cref="ITenantContext"/>，
/// 供 CP6Context 全局查询过滤 + 写入盖章使用。无有效 claim（匿名/旧 token）→ 保持默认租户。
/// 须排在 UseAuthentication 之后（此时 User 已解析）、业务处理之前。
/// </summary>
public class TenantMiddleware
{
    private readonly RequestDelegate _next;

    public TenantMiddleware(RequestDelegate next) => _next = next;

    public async Task Invoke(HttpContext ctx, ITenantContext tenant)
    {
        var tid = ctx.User?.FindFirst("tenant_id")?.Value;
        if (Guid.TryParse(tid, out var g) && g != Guid.Empty) tenant.CurrentTenantId = g;
        await _next(ctx);
    }
}
