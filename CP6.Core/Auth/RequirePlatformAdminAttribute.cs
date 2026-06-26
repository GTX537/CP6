using System.Security.Claims;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Core.Auth;

/// <summary>
/// 平台超管（带外授权闸） —— 多租户合规 #5。贴在控制器/动作上，只有平台超管放行。
/// <para>关键：平台超管权<b>无法</b>经 RBAC 自助提权（租户管理员够不到 <c>IsPlatformAdmin</c>），
/// 故走带外 claim + DB 回查，与租户内权限体系彻底解耦。</para>
/// <para>三道闸顺序为铁律（勿改顺）：</para>
/// <list type="number">
///   <item>impersonation 期间挂起平台权（imp 拒优先）→ E-SEC-034。</item>
///   <item>claim 快判（无 <c>is_platform_admin=true</c>）→ E-SEC-031。</item>
///   <item>DB 回查纵深（捕获"已撤销但 token 未过期"）→ E-SEC-031。</item>
/// </list>
/// <para>特性不能构造注入，用 RequestServices 服务定位取 <see cref="CP6Context"/>。</para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class RequirePlatformAdminAttribute : Attribute, IAsyncAuthorizationFilter
{
    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        // GATE 1：impersonation 期间挂起平台权（imp 拒优先于一切）。
        if (user.FindFirst("impersonator_id") != null)
        {
            Forbid(context, "E-SEC-034");
            return;
        }

        // GATE 2：claim 快判。
        if (user.FindFirst("is_platform_admin")?.Value != "true")
        {
            Forbid(context, "E-SEC-031");
            return;
        }

        // GATE 3：DB 回查纵深防御（撤销而 token 未过期 / 账号停用）。
        var db = context.HttpContext.RequestServices.GetService<CP6Context>();
        if (db == null)
        {
            Forbid(context, "E-SEC-031");
            return;
        }

        if (!Guid.TryParse(user.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var uid))
        {
            Forbid(context, "E-SEC-031");
            return;
        }

        var ok = await db.Sys_Users
            .IgnoreQueryFilters()
            .AnyAsync(u => u.Id == uid && u.IsPlatformAdmin && u.Enable);
        if (!ok)
        {
            Forbid(context, "E-SEC-031");
        }
        // pass → 不设 Result，放行管道继续。
    }

    private static void Forbid(AuthorizationFilterContext ctx, string code)
        => ctx.Result = new ObjectResult(new { code = 403, message = code }) { StatusCode = 403 };
}
