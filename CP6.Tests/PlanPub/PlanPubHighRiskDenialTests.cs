using System.Reflection;
using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Plan;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.PlanPub;

/// <summary>
/// M-PLAN/PUB Task 3 权限拒绝用例（403 断言）——覆盖真相源 §三高危 3 键对应端点。
/// 口径照仓内既有先例 <c>PermissionChainIntegrationTests</c> / <c>FourGranularityIntegrationTests</c> /
/// <c>PurHighRiskDenialTests</c>（M-PUR 已裁定口径）：走**真实后端聚合链**
/// （PermissionAggregator → CurrentPermissionContext → PermissionService，InMemory DB），
/// 一个**已登录但无该操作权**的用户经生产 <see cref="RequirePermissionAttribute"/> 请求高危端点 → 断言 403。
///
/// 关于「无认证 401」口径取舍（照 M-PUR T3 裁定）：本仓 [Authorize] 认证层（401）在 HTTP 传输层，
/// 进程内 <c>RequirePermission</c> 过滤器不经它；且 <c>CurrentPermissionContext.GetAsync</c> 对无 Identity.Name
/// 会 throw "未登录"（非返回 401）。故遵循 brief 明列的可行口径「**无权限身份 → 403**」——与既有三先例
/// 逐字一致（授一无关键放行 + 目标键 403）。
///
/// 高危 3 键（真相源 §三：plan-mrp:run / plan-mrp:convert / pub-codegen:save）以**测试内独立字面量**声明；
/// 并经反射交叉核验：每个 (控制器.方法) 确实携带该 (menu,action) [RequirePermission]——若生产端点改名/改键，
/// 本用例的 403 oracle 亦随之破（不会静默漂移）。
/// </summary>
public class PlanPubHighRiskDenialTests
{
    /// <summary>真相源 §三高危 3 键：(控制器名.方法名, menu-key, action)。独立字面量，非引用生产常量。</summary>
    private static readonly (string endpoint, string menu, string action)[] HighRiskKeys =
    {
        ("MrpController.Run",         "plan-mrp",    "run"),      // MRP 全量重算：作废重生建议态计划订单 + 逐层 net 重生
        ("MrpController.Convert",     "plan-mrp",    "convert"),  // 转单=创建采购/生产承诺（跨模块，当前委托 P1 桩，闸门先落地）
        ("CodeGenController.Save",    "pub-codegen", "save"),     // 代码生成元数据写盘覆盖：RemoveRange 旧列后整体重插
    };

    /// <summary>无关的「良性放行键」——证明链非「全盘拒绝」假绿（有此键的请求必须放行）。非高危：pub-seq:add。</summary>
    private const string BenignMenu = "pub-seq";
    private const string BenignAction = "add";

    private static CP6Context NewDb()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
            .Options;
        return new CP6Context(options);
    }

    /// <summary>登录用户 "u"（RoleId=1），**仅授 pub-seq:add** 一个良性键——高危 3 键一概未授。</summary>
    private static ServiceProvider BuildProvider()
    {
        var db = NewDb();
        db.Sys_Users.Add(new Sys_User { Id = Guid.NewGuid(), UserName = "u", Password = "x", RoleId = 1 });
        // 良性键锚定菜单（pub-seq 有 MenuKey 才能 join 出 ActionKeys；MenuId 112 = 采番规则）
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 112, MenuName = "采番规则", MenuKey = BenignMenu });
        db.Sys_RoleActions.Add(new Sys_RoleAction { Id = Guid.NewGuid(), RoleId = 1, MenuId = 112, ActionCode = BenignAction });
        db.SaveChanges();

        var services = new ServiceCollection();
        services.AddSingleton(db);
        services.AddMemoryCache();
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
        return provider;
    }

    private static AuthorizationFilterContext AuthCtx(ServiceProvider provider) =>
        new(new ActionContext(
                provider.GetRequiredService<IHttpContextAccessor>().HttpContext!,
                new RouteData(), new ActionDescriptor()),
            new List<IFilterMetadata>());

    private static (string menu, string action)? ReadEndpointPermission(string endpoint)
    {
        var parts = endpoint.Split('.');
        var ctrl = typeof(MrpController).Assembly.GetTypes()
            .FirstOrDefault(t => (t.Namespace == "CP6.WebApi.Controllers.Plan"
                                  || t.Namespace == "CP6.WebApi.Controllers.Pub")
                                 && t.Name == parts[0]);
        var m = ctrl?.GetMethod(parts[1], BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        if (m == null) return null;
        var data = CustomAttributeData.GetCustomAttributes(m)
            .FirstOrDefault(d => d.AttributeType == typeof(RequirePermissionAttribute));
        if (data == null) return null;
        var args = data.ConstructorArguments;
        return ((string)args[0].Value!, (string)args[1].Value!);
    }

    [Fact]
    public async Task BenignGrantedAction_PassesChain()
    {
        // 正控：有 pub-seq:add 的用户请求该键 → 放行（证明链非全盘拒绝，403 用例才有意义）。
        var provider = BuildProvider();
        var ctx = AuthCtx(provider);
        await new RequirePermissionAttribute(BenignMenu, BenignAction).OnAuthorizationAsync(ctx);
        Assert.Null(ctx.Result);
    }

    [Fact]
    public async Task UnauthorizedUser_Is403_OnEveryHighRiskEndpoint()
    {
        var provider = BuildProvider();
        var failures = new List<string>();

        foreach (var (endpoint, menu, action) in HighRiskKeys)
        {
            // 交叉核验：该高危键确实贴在对应生产端点上（防端点改名/改键使 403 oracle 静默漂移）。
            var onEndpoint = ReadEndpointPermission(endpoint);
            if (onEndpoint != (menu, action))
            {
                failures.Add($"{endpoint}：生产端点 [RequirePermission] = {onEndpoint?.ToString() ?? "无"}，与高危 oracle ({menu},{action}) 不符");
                continue;
            }

            // 无该操作权的登录用户经生产过滤器请求高危端点 → 必须 403。
            var ctx = AuthCtx(provider);
            await new RequirePermissionAttribute(menu, action).OnAuthorizationAsync(ctx);
            var result = ctx.Result as ObjectResult;
            if (result?.StatusCode != StatusCodes.Status403Forbidden)
                failures.Add($"{endpoint} ({menu}:{action})：期望 403，实际 {result?.StatusCode?.ToString() ?? "放行(null)"}");
        }

        Assert.True(failures.Count == 0, "高危端点未 fail-closed:\n" + string.Join("\n", failures));
        Assert.Equal(3, HighRiskKeys.Length);   // 收口：覆盖真相源 §三全 3 高危键
    }
}
