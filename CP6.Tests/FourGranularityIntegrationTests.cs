using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity;
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
/// 四粒度合一 capstone（PUB 章01~04，去 HTTP 传输层）：
/// 同一登录用户经真实聚合链同时受 操作权(403)/数据权(查询注入)/字段权(掩码) 三道后端强校验。
/// </summary>
public class FourGranularityIntegrationTests
{
    private sealed class FakeOrder : IDataScoped
    {
        public string? Creator { get; set; }
        public Guid? DeptId { get; set; }
    }

    private sealed class OrderDto
    {
        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }
    }

    private static CP6Context NewDb(Guid uid, Guid dept) // user u: 主角色1, 部门 dept
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        var db = new CP6Context(options);
        db.Sys_Depts.Add(new Sys_Dept { Id = dept, DeptCode = "A", DeptName = "A", Path = $"/{dept}/" });
        db.Sys_Users.Add(new Sys_User { Id = uid, UserName = "u", Password = "x", RoleId = 1, DeptId = dept });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 10, MenuName = "受注", MenuKey = "order" });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 10, ActionCode = "query" });   // 仅 query
        db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "order", ScopeType = 2 });   // 本部门
        db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm { Id = Guid.NewGuid(), RoleId = 1, ResourceKey = "order", FieldName = "Amount", Access = 3 });   // Amount 隐藏
        db.SaveChanges();
        return db;
    }

    private static ServiceProvider Provider(CP6Context db, string userName)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddScoped<IPermissionAggregator, PermissionAggregator>();
        services.AddScoped<ICurrentPermissionContext, CurrentPermissionContext>();
        services.AddScoped<IPermissionService, PermissionService>();
        services.AddScoped<IDataScopeFilter, DataScopeFilter>();
        services.AddScoped<IFieldPermService, FieldPermService>();
        var provider = services.BuildServiceProvider();

        var http = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, userName) }, "test"))
        };
        provider.GetRequiredService<IHttpContextAccessor>().HttpContext = http;
        return provider;
    }

    private static AuthorizationFilterContext AuthCtx(ServiceProvider provider)
    {
        var http = provider.GetRequiredService<IHttpContextAccessor>().HttpContext!;
        return new AuthorizationFilterContext(
            new ActionContext(http, new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());
    }

    [Fact]
    public async Task AllFourGranularities_EnforcedTogether()
    {
        var uid = Guid.NewGuid();
        var dept = Guid.NewGuid();
        using var db = NewDb(uid, dept);
        var provider = Provider(db, "u");

        // ① 操作权：query 放行 / delete 403
        var allow = AuthCtx(provider);
        await new RequirePermissionAttribute("order", "query").OnAuthorizationAsync(allow);
        Assert.Null(allow.Result);

        var deny = AuthCtx(provider);
        await new RequirePermissionAttribute("order", "delete").OnAuthorizationAsync(deny);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(deny.Result).StatusCode);

        // 取已聚合上下文（数据权 + 字段权 一并验证已合并）
        var ctx = await provider.GetRequiredService<ICurrentPermissionContext>().GetAsync();
        Assert.Equal(2, ctx.DataScopes["order"]);
        Assert.Equal(3, ctx.FieldPerms["order"]["Amount"]);

        // ② 数据权：本部门(2) → 仅返回本部门行
        var rows = new[]
        {
            new FakeOrder { DeptId = dept },                 // 本部门
            new FakeOrder { DeptId = Guid.NewGuid() }        // 他部门
        }.AsQueryable();
        var filtered = provider.GetRequiredService<IDataScopeFilter>().Apply(rows, "order", ctx).ToList();
        Assert.Single(filtered);
        Assert.Equal(dept, filtered[0].DeptId);

        // ③ 字段权：Amount 隐藏 → 掩码为 null，UnitPrice 保留
        var dto = new OrderDto { UnitPrice = 10, Amount = 999 };
        await provider.GetRequiredService<IFieldPermService>().MaskHiddenAsync(dto, "order");
        Assert.Null(dto.Amount);
        Assert.Equal(10, dto.UnitPrice);
    }
}
