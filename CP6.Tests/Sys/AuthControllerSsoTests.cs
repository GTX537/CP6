using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// #3 SSO（T6）AuthController 端点 + 强制 SSO 拦截编排测试：直接 new 控制器 + InMemory + 假 ISsoService/ITenantSsoConfigService。
/// </summary>
public class AuthControllerSsoTests
{
    private sealed class FakePermCtx : ICurrentPermissionContext
    {
        private readonly Guid _uid;
        public FakePermCtx(Guid uid) => _uid = uid;
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(new UserPermissionContext { UserId = _uid });
        // 登录成功路径会走 PrewarmAsync 聚合菜单：返空角色集 → 菜单查询为空（不依赖 seed）。
        public Task<UserPermissionContext> PrewarmAsync(Guid userId) => Task.FromResult(new UserPermissionContext { UserId = userId, RoleIds = new() });
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    private static (AuthController ctl, CP6Context db, FakeTenantSsoConfigService ssoCfg, FakeSsoService sso)
        Make(Sys_User? seedUser = null)
    {
        var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var uid = seedUser?.Id ?? Guid.NewGuid();
        if (seedUser != null) { db.Sys_Users.Add(seedUser); db.SaveChanges(); }

        var opt = Options.Create(new SecurityOptions());
        var policy = new PasswordPolicyService(db, opt, hasher);
        var login = new LoginSecurityService(db, opt);
        var audit = new SecurityAuditService(db);
        var refresh = new RefreshTokenService(db, opt, new TenantContext());
        var blacklist = new CacheTokenBlacklistService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var cookies = new AuthCookieWriter(opt);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT:Secret"] = "cp6_test_secret_key_at_least_32_bytes_long!!",
            ["JWT:Issuer"] = "cp6",
            ["JWT:Audience"] = "cp6",
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
        }).Build();
        var ssoCfg = new FakeTenantSsoConfigService();
        var sso = new FakeSsoService();
        var ctl = new AuthController(db, config, new FakePermCtx(uid), new TenantContext(), hasher, policy, login, audit, refresh, blacklist, cookies, opt, ssoCfg, sso);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (ctl, db, ssoCfg, sso);
    }

    [Fact]
    public async Task Authorize_returns_authorize_url()
    {
        var (ctl, _, _, sso) = Make();
        sso.OnAuthorize = (tc, ru, run) => Task.FromResult("https://idp.example/authorize?response_type=code&state=abc");

        var result = Assert.IsType<OkObjectResult>(await ctl.SsoAuthorize("T01"));
        var url = result.Value!.GetType().GetProperty("authorizeUrl")!.GetValue(result.Value) as string;
        Assert.Equal("https://idp.example/authorize?response_type=code&state=abc", url);
    }

    [Fact]
    public async Task Authorize_throws_bizexception_on_invalid_op()
    {
        var (ctl, _, _, sso) = Make();
        sso.OnAuthorize = (tc, ru, run) => throw new InvalidOperationException("E-SEC-020");

        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.SsoAuthorize("T01"));
        Assert.Equal("E-SEC-020", ex.Code);
    }

    [Fact]
    public async Task Callback_success_writes_cookies_audits_and_redirects_to_landing()
    {
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "sso-bob", Enable = true, TenantId = TenantContext.DefaultTenant };
        var (ctl, db, _, sso) = Make(user);
        sso.OnCallback = (code, state, ru) => Task.FromResult(user);

        var result = Assert.IsType<RedirectResult>(await ctl.SsoCallback("the-code", "the-state"));
        Assert.Equal("http://localhost:5173/sso/landing", result.Url);

        // 三认证 Cookie 写入
        var setCookies = ctl.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(AuthCookieWriter.AccessCookie, setCookies);
        Assert.Contains(AuthCookieWriter.RefreshCookie, setCookies);
        Assert.Contains(AuthCookieWriter.CsrfCookie, setCookies);

        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.SsoLoginSuccess && l.UserName == "sso-bob");
    }

    [Fact]
    public async Task Callback_failure_redirects_with_error_and_writes_no_cookies()
    {
        var (ctl, db, _, sso) = Make();
        sso.OnCallback = (code, state, ru) => throw new InvalidOperationException("E-SEC-024");

        var result = Assert.IsType<RedirectResult>(await ctl.SsoCallback("bad", "state"));
        Assert.Equal("http://localhost:5173/sso/landing?error=E-SEC-024", result.Url);

        Assert.Equal(string.Empty, ctl.Response.Headers["Set-Cookie"].ToString());   // 不写任何认证 Cookie
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.SsoLoginFailed && l.Reason == "E-SEC-024");
    }

    [Fact]
    public async Task Enforced_tenant_blocks_password_login_with_E_SEC_021()
    {
        var hasher = new BCryptPasswordHasher();
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "carol", Password = hasher.Hash("Right1!aA"), Enable = true, TenantId = TenantContext.DefaultTenant, AllowPasswordFallback = false };
        var (ctl, db, ssoCfg, _) = Make(user);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantContext.DefaultTenant, TenantCode = "DEF", TenantName = "Default", Enable = true });
        db.SaveChanges();
        ssoCfg.ByTenantId = new Sys_TenantSsoConfig { TenantId = TenantContext.DefaultTenant, Authority = "https://idp", ClientId = "c", Enabled = true, Enforced = true };

        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.Login(new LoginRequest { UserName = "carol", TenantCode = "DEF", Password = "Right1!aA" }));
        Assert.Equal("E-SEC-021", ex.Code);
        Assert.Contains(db.Sys_SecurityLogs, l => l.Reason == "sso enforced");
    }

    [Fact]
    public async Task Break_glass_user_logs_in_despite_enforced_sso()
    {
        var hasher = new BCryptPasswordHasher();
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "glass", Password = hasher.Hash("Right1!aA"), Enable = true, TenantId = TenantContext.DefaultTenant, AllowPasswordFallback = true };
        var (ctl, db, ssoCfg, _) = Make(user);
        db.Sys_Tenants.Add(new Sys_Tenant { Id = TenantContext.DefaultTenant, TenantCode = "DEF", TenantName = "Default", Enable = true });
        db.SaveChanges();
        ssoCfg.ByTenantId = new Sys_TenantSsoConfig { TenantId = TenantContext.DefaultTenant, Authority = "https://idp", ClientId = "c", Enabled = true, Enforced = true };

        var result = Assert.IsType<OkObjectResult>(await ctl.Login(new LoginRequest { UserName = "glass", TenantCode = "DEF", Password = "Right1!aA" }));
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.LoginSuccess && l.UserName == "glass");
    }
}
