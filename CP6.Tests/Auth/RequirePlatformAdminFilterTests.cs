using System.Security.Claims;
using CP6.Core.Auth;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace CP6.Tests.Auth;

/// <summary>
/// [RequirePlatformAdmin] 三道闸单元测（直构 AuthorizationFilterContext，无 WebApplicationFactory）。
/// 验顺序铁律：imp 拒(E-SEC-034) 优先 → claim 快判(E-SEC-031) → DB 回查纵深(E-SEC-031)。
/// </summary>
public class RequirePlatformAdminFilterTests
{
    private static ClaimsPrincipal MakeUser(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)),
            authenticationType: "Test");
        return new ClaimsPrincipal(identity);
    }

    private static AuthorizationFilterContext MakeCtx(CP6Context db, ClaimsPrincipal user)
    {
        var services = new ServiceCollection();
        services.AddSingleton(db);   // 让 RequestServices.GetService<CP6Context>() 解析得到
        var http = new DefaultHttpContext
        {
            User = user,
            RequestServices = services.BuildServiceProvider()
        };
        var actionContext = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
    }

    private static string? MessageOf(IActionResult? result)
    {
        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(403, obj.StatusCode);
        var value = obj.Value!;
        return value.GetType().GetProperty("message")!.GetValue(value) as string;
    }

    private static Sys_User SeedUser(CP6Context db, Guid id, bool isPlatformAdmin, bool enable)
    {
        var u = new Sys_User
        {
            Id = id,
            UserName = "u_" + id.ToString("N"),
            Password = "x",
            Enable = enable,
            IsPlatformAdmin = isPlatformAdmin,
            TenantId = TenantContext.DefaultTenant
        };
        db.Sys_Users.Add(u);
        db.SaveChanges();
        return u;
    }

    // (a) 无 is_platform_admin claim → GATE 2 拦截 → 403 E-SEC-031。
    [Fact]
    public async Task NoPlatformAdminClaim_Returns403_ESec031()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var user = MakeUser((ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()));
        var ctx = MakeCtx(db, user);

        await new RequirePlatformAdminAttribute().OnAuthorizationAsync(ctx);

        Assert.Equal("E-SEC-031", MessageOf(ctx.Result));
    }

    // (b) claim 在，但 DB 中该 user 非平台超管（被撤销）→ GATE 3 纵深拦截 → 403 E-SEC-031。
    [Fact]
    public async Task ClaimButDbRevoked_Returns403_ESec031()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var uid = Guid.NewGuid();
        SeedUser(db, uid, isPlatformAdmin: false, enable: true);

        var user = MakeUser(
            ("is_platform_admin", "true"),
            (ClaimTypes.NameIdentifier, uid.ToString()));
        var ctx = MakeCtx(db, user);

        await new RequirePlatformAdminAttribute().OnAuthorizationAsync(ctx);

        Assert.Equal("E-SEC-031", MessageOf(ctx.Result));
    }

    // (b') claim 在，DB 中是平台超管但已停用（Enable=false）→ GATE 3 纵深拦截 → 403 E-SEC-031。
    [Fact]
    public async Task ClaimButDbDisabled_Returns403_ESec031()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var uid = Guid.NewGuid();
        SeedUser(db, uid, isPlatformAdmin: true, enable: false);

        var user = MakeUser(
            ("is_platform_admin", "true"),
            (ClaimTypes.NameIdentifier, uid.ToString()));
        var ctx = MakeCtx(db, user);

        await new RequirePlatformAdminAttribute().OnAuthorizationAsync(ctx);

        Assert.Equal("E-SEC-031", MessageOf(ctx.Result));
    }

    // (c) impersonator_id claim 在（即使 is_platform_admin=true）→ GATE 1 优先拦截 → 403 E-SEC-034。
    [Fact]
    public async Task ImpersonationSuspendsPlatformPower_Returns403_ESec034()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var uid = Guid.NewGuid();
        // 即使 DB 是合格平台超管，imp 期间也必须拒。
        SeedUser(db, uid, isPlatformAdmin: true, enable: true);

        var user = MakeUser(
            ("impersonator_id", Guid.NewGuid().ToString()),
            ("is_platform_admin", "true"),
            (ClaimTypes.NameIdentifier, uid.ToString()));
        var ctx = MakeCtx(db, user);

        await new RequirePlatformAdminAttribute().OnAuthorizationAsync(ctx);

        Assert.Equal("E-SEC-034", MessageOf(ctx.Result));
    }

    // (d) claim 在 + DB IsPlatformAdmin=true && Enable=true → 放行（ctx.Result == null）。
    [Fact]
    public async Task ValidPlatformAdmin_Passes_NullResult()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var uid = Guid.NewGuid();
        SeedUser(db, uid, isPlatformAdmin: true, enable: true);

        var user = MakeUser(
            ("is_platform_admin", "true"),
            (ClaimTypes.NameIdentifier, uid.ToString()));
        var ctx = MakeCtx(db, user);

        await new RequirePlatformAdminAttribute().OnAuthorizationAsync(ctx);

        Assert.Null(ctx.Result);
    }

    // TenantScope：进入切到 Y，离开还原到 X。
    [Fact]
    public void TenantScope_SwitchesAndRestores()
    {
        var x = Guid.Parse("00000000-0000-0000-0000-0000000000B1");
        var y = Guid.Parse("00000000-0000-0000-0000-0000000000C1");
        var tenant = new TenantContext { CurrentTenantId = x };

        using (new TenantScope(tenant, y))
        {
            Assert.Equal(y, tenant.CurrentTenantId);
        }

        Assert.Equal(x, tenant.CurrentTenantId);
    }
}
