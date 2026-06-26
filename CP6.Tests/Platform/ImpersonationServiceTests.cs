using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Platform;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Platform;

/// <summary>
/// 块② impersonation heart 单测（直构服务 + InMemory + spy 黑名单/审计 + DefaultHttpContext.Response）。
/// 逐项证明安全不变量：R2 双向 jti 黑名单、R3 imp 令牌 mustChange 恒 false、R5 审计落平台租户、
/// R9-c 真身已撤销拒、菜单非空、ExpiresInMinutes == ImpersonationMinutes。
/// </summary>
public class ImpersonationServiceTests
{
    private static readonly Guid TargetTenant = Guid.Parse("00000000-0000-0000-0000-0000000000C9");
    private const string Secret = "unit-test-secret-key-min-32-chars-aaaaaaaa";
    private const string Issuer = "CP6";
    private const string Audience = "CP6.Web";
    private const int ImpMinutes = 30;
    private const int AccessMinutes = 15;

    // ── 记录 (jti, ttl) 调用的黑名单 spy。
    private sealed class SpyBlacklist : ITokenBlacklistService
    {
        public readonly List<(string jti, TimeSpan ttl)> Calls = new();
        public Task BlacklistAsync(string jti, TimeSpan ttl) { Calls.Add((jti, ttl)); return Task.CompletedTask; }
        public Task<bool> IsBlacklistedAsync(string jti) => Task.FromResult(false);
    }

    // ── 在 LogAsync 调用瞬间快照 _tenant.CurrentTenantId（证 R5），并落 DB（与真实 SecurityAuditService 行为一致）。
    private sealed class SpyAudit : ISecurityAuditService
    {
        private readonly CP6Context _db;
        private readonly ITenantContext _tenant;
        public readonly List<(SecurityEventType type, Guid tenantAtCall)> Calls = new();
        public SpyAudit(CP6Context db, ITenantContext tenant) { _db = db; _tenant = tenant; }
        public async Task LogAsync(SecurityEventType type, Guid? userId, string? userName, string? requestTenantCode, string? ip, string? ua, string? reason = null)
        {
            Calls.Add((type, _tenant.CurrentTenantId));
            _db.Sys_SecurityLogs.Add(new Sys_SecurityLog { UserId = userId, UserName = userName, EventType = (int)type, RequestTenantCode = requestTenantCode, Reason = reason, CreatedAt = DateTime.Now });
            await _db.SaveChangesAsync();
        }
    }

    private static IConfiguration JwtConfig() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT:Secret"] = Secret,
            ["JWT:Issuer"] = Issuer,
            ["JWT:Audience"] = Audience
        }).Build();

    private static (ImpersonationService svc, CP6Context db, SpyBlacklist bl, SpyAudit audit, TenantContext tenant, AuthCookieWriter cookies)
        Make(Guid currentTenant)
    {
        var tenant = new TenantContext { CurrentTenantId = currentTenant };
        var options = new DbContextOptionsBuilder<CP6Context>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        var db = new CP6Context(options, tenant);
        var bl = new SpyBlacklist();
        var audit = new SpyAudit(db, tenant);
        var sec = Options.Create(new SecurityOptions
        {
            ImpersonationMinutes = ImpMinutes,
            Token = new TokenOptions { AccessTokenMinutes = AccessMinutes },
            Cookie = new AuthCookieOptions { Secure = false, SameSite = "Strict" }
        });
        var cookies = new AuthCookieWriter(sec);
        var svc = new ImpersonationService(db, bl, audit, tenant, sec, cookies, JwtConfig());
        return (svc, db, bl, audit, tenant, cookies);
    }

    private static Guid SeedUser(CP6Context db, Guid tenantId, int? roleId = 1, bool enable = true,
        bool isPlatformAdmin = false, bool mustChange = false, string? userName = null)
    {
        var id = Guid.NewGuid();
        db.Sys_Users.Add(new Sys_User
        {
            Id = id,
            UserName = userName ?? $"u_{id:N}",
            Password = "x",
            TenantId = tenantId,
            RoleId = roleId,
            Enable = enable,
            IsPlatformAdmin = isPlatformAdmin,
            MustChangePassword = mustChange
        });
        db.SaveChanges();
        return id;
    }

    private static void SeedTenant(CP6Context db, Guid id, bool enable = true, string code = "TGT", string name = "Target Tenant")
    {
        db.Sys_Tenants.Add(new Sys_Tenant { Id = id, TenantCode = code, TenantName = name, Enable = enable });
        db.SaveChanges();
    }

    /// <summary>给目标用户角色配几条菜单（证 R8 menus 非空）。</summary>
    private static void SeedMenusForRole(CP6Context db, int roleId)
    {
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 9001, MenuName = "Dash", RoutePath = "/dash", OrderNo = 1, Enable = true });
        db.Sys_Menus.Add(new Sys_Menu { MenuId = 9002, MenuName = "Wf", RoutePath = "/wf", OrderNo = 2, Enable = true });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = roleId, MenuId = 9001 });
        db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = roleId, MenuId = 9002 });
        db.SaveChanges();
    }

    // ── 平台超管令牌的 ClaimsPrincipal（start 用）。
    private static ClaimsPrincipal PlatformPrincipal(Guid adminId, string jti, string userName = "platadmin", int expMinutes = AccessMinutes)
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(expMinutes).ToUnixTimeSeconds();
        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, adminId.ToString()),
            new Claim(ClaimTypes.Name, userName),
            new Claim("jti", jti),
            new Claim("is_platform_admin", "true"),
            new Claim("exp", exp.ToString())
        }, "test"));
    }

    // ── imp 令牌的 ClaimsPrincipal（end 用）：身份=目标用户，带 impersonator_id=真身。
    private static ClaimsPrincipal ImpPrincipal(Guid targetId, Guid impersonatorId, string jti, int expMinutes = ImpMinutes)
    {
        var exp = DateTimeOffset.UtcNow.AddMinutes(expMinutes).ToUnixTimeSeconds();
        return new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, targetId.ToString()),
            new Claim("jti", jti),
            new Claim("impersonator_id", impersonatorId.ToString()),
            new Claim("exp", exp.ToString())
        }, "test"));
    }

    private static JwtSecurityToken ParseToken(HttpResponse resp)
    {
        var setCookie = resp.Headers["Set-Cookie"].ToString();
        // 取 cp6_at=...; 段
        var marker = AuthCookieWriter.AccessCookie + "=";
        var start = setCookie.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, "Set-Cookie 未含 cp6_at");
        start += marker.Length;
        var end = setCookie.IndexOf(';', start);
        var jwt = end >= 0 ? setCookie[start..end] : setCookie[start..];
        return new JwtSecurityTokenHandler().ReadJwtToken(jwt);
    }

    // ════════════════════════ Start 路径 ════════════════════════

    [Fact]
    public async Task Start_HappyPath_IssuesImpTokenWithCorrectClaims()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        SeedTenant(db, TargetTenant);
        var target = SeedUser(db, TargetTenant, roleId: 1, mustChange: true);   // 目标须改密
        SeedMenusForRole(db, 1);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        var r = await svc.StartAsync(TargetTenant, target, null, PlatformPrincipal(adminId, "old-plat-jti"), ctx.Response);

        var jwt = ParseToken(ctx.Response);
        Assert.Equal(TargetTenant.ToString(), jwt.Claims.First(c => c.Type == "tenant_id").Value);
        Assert.Equal(target.ToString(), jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(adminId.ToString(), jwt.Claims.First(c => c.Type == "impersonator_id").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "is_platform_admin");
        // R3：目标 MustChangePassword=true，但 imp 令牌恒 false。
        Assert.Equal("false", jwt.Claims.First(c => c.Type == "must_change_password").Value);

        // 仅写 access Cookie，未写 refresh/csrf。
        var setCookie = ctx.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(AuthCookieWriter.AccessCookie + "=", setCookie);
        Assert.DoesNotContain(AuthCookieWriter.RefreshCookie + "=", setCookie);
        Assert.DoesNotContain(AuthCookieWriter.CsrfCookie + "=", setCookie);

        // R8 + Expires。
        Assert.NotEmpty(r.Menus);
        Assert.Equal(ImpMinutes, r.ExpiresInMinutes);
        Assert.Equal(TargetTenant, r.TenantId);
        Assert.Equal(target, r.UserId);
    }

    [Fact]
    public async Task Start_NullUserId_PicksFirstEnabledAdmin()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        SeedTenant(db, TargetTenant);
        SeedUser(db, TargetTenant, roleId: 3, userName: "lowpriv");          // 非管理员
        var admin = SeedUser(db, TargetTenant, roleId: 1, userName: "tadmin");
        SeedMenusForRole(db, 1);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        var r = await svc.StartAsync(TargetTenant, null, "audit step-in", PlatformPrincipal(adminId, "j1"), ctx.Response);

        Assert.Equal(admin, r.UserId);   // 选中 RoleId==1 的 admin
    }

    [Fact]
    public async Task Start_TenantAbsent_Throws_ESec032()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-032",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.StartAsync(TargetTenant, null, null, PlatformPrincipal(adminId, "j"), ctx.Response))).Message);
    }

    [Fact]
    public async Task Start_TenantSuspended_Throws_ESec032()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        SeedTenant(db, TargetTenant, enable: false);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-032",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.StartAsync(TargetTenant, null, null, PlatformPrincipal(adminId, "j"), ctx.Response))).Message);
    }

    [Fact]
    public async Task Start_NoEligibleUser_Throws_ESec035()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        SeedTenant(db, TargetTenant);   // 无任何 RoleId==1 启用用户
        SeedUser(db, TargetTenant, roleId: 5, userName: "noadmin");
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-035",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.StartAsync(TargetTenant, null, null, PlatformPrincipal(adminId, "j"), ctx.Response))).Message);
    }

    [Fact]
    public async Task Start_ExplicitUserId_DisabledOrWrongTenant_Throws_ESec035()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        SeedTenant(db, TargetTenant);
        var disabled = SeedUser(db, TargetTenant, roleId: 1, enable: false);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-035",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.StartAsync(TargetTenant, disabled, null, PlatformPrincipal(adminId, "j"), ctx.Response))).Message);
    }

    [Fact]
    public async Task Start_R2_BlacklistsOldPlatformJti_Once()
    {
        var (svc, db, bl, _, _, _) = Make(TenantContext.DefaultTenant);
        SeedTenant(db, TargetTenant);
        SeedUser(db, TargetTenant, roleId: 1);
        SeedMenusForRole(db, 1);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        await svc.StartAsync(TargetTenant, null, null, PlatformPrincipal(adminId, "OLD-PLAT-JTI"), ctx.Response);

        Assert.Single(bl.Calls);
        Assert.Equal("OLD-PLAT-JTI", bl.Calls[0].jti);
        Assert.True(bl.Calls[0].ttl > TimeSpan.Zero);
    }

    [Fact]
    public async Task Start_R5_AuditCapturedOnPlatformTenant()
    {
        // 当前上下文置非默认租户，证 TenantScope 把审计落回平台租户。
        var (svc, db, _, audit, _, _) = Make(TargetTenant);
        SeedTenant(db, TargetTenant);
        SeedUser(db, TargetTenant, roleId: 1);
        SeedMenusForRole(db, 1);
        var adminId = SeedUser(db, TargetTenant, isPlatformAdmin: true);

        var ctx = new DefaultHttpContext();
        await svc.StartAsync(TargetTenant, null, null, PlatformPrincipal(adminId, "j"), ctx.Response);

        var call = Assert.Single(audit.Calls);
        Assert.Equal(SecurityEventType.ImpersonationStarted, call.type);
        Assert.Equal(TenantContext.DefaultTenant, call.tenantAtCall);

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.ImpersonationStarted);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }

    // ════════════════════════ End 路径 ════════════════════════

    [Fact]
    public async Task End_HappyPath_ReSignsPlatformAdminToken()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true, userName: "platadmin");
        var targetId = SeedUser(db, TargetTenant, roleId: 1);
        SeedMenusForRole(db, 1);

        var ctx = new DefaultHttpContext();
        var r = await svc.EndAsync(ImpPrincipal(targetId, adminId, "imp-jti"), ctx.Response);

        var jwt = ParseToken(ctx.Response);
        Assert.Equal(adminId.ToString(), jwt.Claims.First(c => c.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal("true", jwt.Claims.First(c => c.Type == "is_platform_admin").Value);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "impersonator_id");
        Assert.NotEmpty(r.Menus);
    }

    [Fact]
    public async Task End_NoImpersonatorClaim_Throws_ESec031()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);

        // 普通平台令牌（无 impersonator_id）。
        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-031",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.EndAsync(PlatformPrincipal(adminId, "j"), ctx.Response))).Message);
    }

    [Fact]
    public async Task End_R2_BlacklistsImpJti_Once()
    {
        var (svc, db, bl, _, _, _) = Make(TenantContext.DefaultTenant);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);
        var targetId = SeedUser(db, TargetTenant, roleId: 1);
        SeedMenusForRole(db, 1);

        var ctx = new DefaultHttpContext();
        await svc.EndAsync(ImpPrincipal(targetId, adminId, "IMP-JTI"), ctx.Response);

        Assert.Single(bl.Calls);
        Assert.Equal("IMP-JTI", bl.Calls[0].jti);
        Assert.True(bl.Calls[0].ttl > TimeSpan.Zero);
    }

    [Fact]
    public async Task End_R9c_RealIdentityRevoked_Throws_ESec031()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        // 真身在 impersonating 期间被撤销平台超管。
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: false);
        var targetId = SeedUser(db, TargetTenant, roleId: 1);

        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-031",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.EndAsync(ImpPrincipal(targetId, adminId, "imp"), ctx.Response))).Message);
    }

    [Fact]
    public async Task End_R9c_RealIdentityDisabled_Throws_ESec031()
    {
        var (svc, db, _, _, _, _) = Make(TenantContext.DefaultTenant);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true, enable: false);   // 真身被停用
        var targetId = SeedUser(db, TargetTenant, roleId: 1);

        var ctx = new DefaultHttpContext();
        Assert.Equal("E-SEC-031",
            (await Assert.ThrowsAsync<InvalidOperationException>(() =>
                svc.EndAsync(ImpPrincipal(targetId, adminId, "imp"), ctx.Response))).Message);
    }

    [Fact]
    public async Task End_R5_AuditCapturedOnPlatformTenant()
    {
        // 当前上下文置目标租户（imp 期间），证 EndAsync 显式切回平台租户后再审计。
        var (svc, db, _, audit, tenant, _) = Make(TargetTenant);
        var adminId = SeedUser(db, TenantContext.DefaultTenant, isPlatformAdmin: true);
        var targetId = SeedUser(db, TargetTenant, roleId: 1);
        SeedMenusForRole(db, 1);

        var ctx = new DefaultHttpContext();
        await svc.EndAsync(ImpPrincipal(targetId, adminId, "imp"), ctx.Response);

        var call = Assert.Single(audit.Calls);
        Assert.Equal(SecurityEventType.ImpersonationEnded, call.type);
        Assert.Equal(TenantContext.DefaultTenant, call.tenantAtCall);
        Assert.Equal(TenantContext.DefaultTenant, tenant.CurrentTenantId);   // 切回平台租户

        var log = await db.Sys_SecurityLogs.IgnoreQueryFilters()
            .FirstAsync(l => l.EventType == (int)SecurityEventType.ImpersonationEnded);
        Assert.Equal(TenantContext.DefaultTenant, log.TenantId);
    }
}
