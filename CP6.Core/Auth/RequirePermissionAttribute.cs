using CP6.Core.Services.Sys;
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
            context.Result = new ObjectResult(new { code = 500, message = "权限服务未注册" })
            { StatusCode = StatusCodes.Status500InternalServerError };
            return;
        }

        if (!await svc.HasActionAsync(_menu, _action))
        {
            context.Result = new ObjectResult(new { code = 403, message = $"无权限：{_menu}:{_action}" })
            { StatusCode = StatusCodes.Status403Forbidden };
        }
    }
}
