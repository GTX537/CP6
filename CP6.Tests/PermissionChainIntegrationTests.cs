using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests;

/// <summary>
/// [RequirePermission] 全后端链路集成测（去掉 HTTP 传输层）：
/// RoleAction → PermissionAggregator → ActionKeys → PermissionService.HasAction → 403/200。
/// 用真实 PermissionService / CurrentPermissionContext / PermissionAggregator + InMemory DB。
/// </summary>
public class PermissionChainIntegrationTests
{
    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    /// <summary>构造一个挂好真实权限链 + 登录用户 "u"（仅授 order:export）的 AuthorizationFilterContext。</summary>
    private static AuthorizationFilterContext BuildContext()
    {
        var db = NewDb();
        var uid = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1 });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 10, MenuName = "受注", MenuKey = "order" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 10, ActionCode = "export" });
        db.SaveChanges();

        var services = new ServiceCollection();
        services.AddSingleton(db);                       // 同一 InMemory 实例
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IPermissionAggregator, PermissionAggregator>();
        services.AddScoped<ICurrentPermissionContext, CurrentPermissionContext>();
        services.AddScoped<IPermissionService, PermissionService>();
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "u") }, "test"))
        };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;

        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    [Fact]
    public async Task GrantedAction_PassesThroughWholeChain()
    {
        var ctx = BuildContext();
        await new RequirePermissionAttribute("order", "export").OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result);   // 有 order:export → 放行
    }

    [Fact]
    public async Task UngrantedAction_Returns403ThroughWholeChain()
    {
        var ctx = BuildContext();
        await new RequirePermissionAttribute("order", "delete").OnAuthorizationAsync(ctx);
        var result = Assert.IsType<ObjectResult>(ctx.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);   // 无 order:delete → 403
    }
}
