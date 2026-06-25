using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using OtpNet;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// #2 2FA（T6）登录分支 + 4 端点编排测试：
/// 强制租户首登→twoFactorRequired/mustEnroll、verify TOTP 写三 Cookie + pending 消费（重放 E-SEC-013）、
/// 错码 E-SEC-011 + FailedLoginCount++、enroll 态点 email-otp → E-SEC-014。
/// </summary>
public class AuthController2faTests
{
    private sealed class FakePermCtx : ICurrentPermissionContext
    {
        private readonly Guid _uid;
        public FakePermCtx(Guid uid) => _uid = uid;
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(new UserPermissionContext { UserId = _uid });
        public Task<UserPermissionContext> PrewarmAsync(Guid userId) => Task.FromResult(new UserPermissionContext { UserId = userId, RoleIds = new() });
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    private static (AuthController ctl, CP6Context db, Sys_User user, TwoFactorService twoFa) Make(
        int tenantMode = 0, bool enabled = false, string? secret = null, string password = "Right1!aA")
    {
        var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var tenantId = Guid.NewGuid();
        db.Sys_Tenants.Add(new Sys_Tenant
        {
            Id = tenantId, TenantCode = "T01", TenantName = "T01", Enable = true,
            TwoFactorMode = tenantMode
        });
        var user = new Sys_User
        {
            Id = Guid.NewGuid(),
            UserName = "alice",
            Email = "a@a.com",
            Password = hasher.Hash(password),
            Enable = true,
            TenantId = tenantId,
            TwoFactorEnabled = enabled,
            TwoFactorSecret = secret
        };
        db.Sys_Users.Add(user);
        db.SaveChanges();

        var opt = Options.Create(new SecurityOptions());
        var policy = new PasswordPolicyService(db, opt, hasher);
        var login = new LoginSecurityService(db, opt);
        var audit = new SecurityAuditService(db);
        var refresh = new RefreshTokenService(db, opt, new TenantContext());
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var blacklist = new CacheTokenBlacklistService(cache);
        var cookies = new AuthCookieWriter(opt);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["JWT:Secret"] = "cp6_test_secret_key_at_least_32_bytes_long!!",
            ["JWT:Issuer"] = "cp6",
            ["JWT:Audience"] = "cp6",
        }).Build();
        var totp = new TotpService(opt);
        var email = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        var pending = new PendingTokenStore(cache, opt);
        var twoFa = new TwoFactorService(db, totp, email, cache, audit, opt);
        var ctl = new AuthController(db, config, new FakePermCtx(user.Id), new TenantContext(),
            hasher, policy, login, audit, refresh, blacklist, cookies, opt,
            new FakeTenantSsoConfigService(), new FakeSsoService(), twoFa, pending);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (ctl, db, user, twoFa);
    }

    private static void CopyPendingCookieAcrossRequests(AuthController ctl)
    {
        // 模拟浏览器：上一次响应写的 Set-Cookie → 下一次请求带回（仅未清的 + 有 value 的）
        var setCookies = ctl.Response.Headers[HeaderNames.SetCookie].ToArray();
        var dict = new Dictionary<string, string>();
        foreach (var sc in setCookies)
        {
            if (!SetCookieHeaderValue.TryParse(sc, out var parsed)) continue;
            var name = parsed.Name.Value!;
            var val = parsed.Value.Value ?? "";
            // 过期的清 Cookie（Expires=Unix epoch）不带回
            if (parsed.Expires is { } exp && exp < DateTimeOffset.UtcNow.AddYears(-1)) continue;
            if (!string.IsNullOrEmpty(val)) dict[name] = val;
        }
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        if (dict.Count > 0)
            ctl.Request.Headers["Cookie"] = string.Join("; ", dict.Select(kv => $"{kv.Key}={kv.Value}"));
    }

    [Fact]
    public async Task Enforced_tenant_unenabled_user_login_returns_mustEnroll_no_auth_cookies()
    {
        var (ctl, db, _, _) = Make(tenantMode: 2);
        var ok = Assert.IsType<OkObjectResult>(await ctl.Login(new LoginRequest { UserName = "alice", TenantCode = "T01", Password = "Right1!aA" }));
        var v = ok.Value!.GetType();
        Assert.True((bool)v.GetProperty("twoFactorRequired")!.GetValue(ok.Value)!);
        Assert.True((bool)v.GetProperty("mustEnroll")!.GetValue(ok.Value)!);

        var setCookies = ctl.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(AuthCookieWriter.PendingCookie, setCookies);
        Assert.DoesNotContain(AuthCookieWriter.AccessCookie + "=", setCookies); // 防把空清 Cookie 也算
        Assert.DoesNotContain(AuthCookieWriter.RefreshCookie + "=", setCookies);

        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.TwoFactorChallenged && l.Reason == "enroll");
    }

    [Fact]
    public async Task Enabled_user_login_returns_mustEnroll_false()
    {
        var totpSecret = "JBSWY3DPEHPK3PXP";
        var (ctl, _, _, _) = Make(tenantMode: 1, enabled: true, secret: totpSecret);
        var ok = Assert.IsType<OkObjectResult>(await ctl.Login(new LoginRequest { UserName = "alice", TenantCode = "T01", Password = "Right1!aA" }));
        var v = ok.Value!.GetType();
        Assert.True((bool)v.GetProperty("twoFactorRequired")!.GetValue(ok.Value)!);
        Assert.False((bool)v.GetProperty("mustEnroll")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task Verify_correct_totp_completes_login_and_pending_is_one_time()
    {
        var totpSecret = "JBSWY3DPEHPK3PXP";
        var (ctl, db, user, _) = Make(tenantMode: 1, enabled: true, secret: totpSecret);

        await ctl.Login(new LoginRequest { UserName = "alice", TenantCode = "T01", Password = "Right1!aA" });
        CopyPendingCookieAcrossRequests(ctl);

        var code = new Totp(Base32Encoding.ToBytes(totpSecret)).ComputeTotp();
        var ok = Assert.IsType<OkObjectResult>(await ctl.TwoFactorVerify(new TwoFactorVerifyRequest(code, null)));

        var setCookies = ctl.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(AuthCookieWriter.AccessCookie + "=ey", setCookies); // 真 JWT 起头
        Assert.Contains(AuthCookieWriter.RefreshCookie, setCookies);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.TwoFactorVerified && l.UserName == "alice");
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.LoginSuccess && l.Reason == "2fa");

        // 重放同一 pending → 已 Consume → E-SEC-013
        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.TwoFactorVerify(new TwoFactorVerifyRequest(code, null)));
        Assert.Equal("E-SEC-013", ex.Code);
    }

    [Fact]
    public async Task Verify_wrong_code_E_SEC_011_and_failedLoginCount_inc()
    {
        var totpSecret = "JBSWY3DPEHPK3PXP";
        var (ctl, db, user, _) = Make(tenantMode: 1, enabled: true, secret: totpSecret);
        await ctl.Login(new LoginRequest { UserName = "alice", TenantCode = "T01", Password = "Right1!aA" });
        CopyPendingCookieAcrossRequests(ctl);

        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.TwoFactorVerify(new TwoFactorVerifyRequest("000000", null)));
        Assert.Equal("E-SEC-011", ex.Code);

        var fresh = db.Sys_Users.IgnoreQueryFilters().Single(u => u.Id == user.Id);
        Assert.Equal(1, fresh.FailedLoginCount);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.TwoFactorFailed);
    }

    [Fact]
    public async Task Email_otp_during_enroll_purpose_throws_E_SEC_014()
    {
        var (ctl, _, _, _) = Make(tenantMode: 2);  // 入会态
        await ctl.Login(new LoginRequest { UserName = "alice", TenantCode = "T01", Password = "Right1!aA" });
        CopyPendingCookieAcrossRequests(ctl);

        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.TwoFactorEmailOtp());
        Assert.Equal("E-SEC-014", ex.Code);
    }

    [Fact]
    public async Task Setup_then_enroll_with_valid_totp_completes_login()
    {
        var (ctl, db, user, _) = Make(tenantMode: 2);  // 入会态
        await ctl.Login(new LoginRequest { UserName = "alice", TenantCode = "T01", Password = "Right1!aA" });
        CopyPendingCookieAcrossRequests(ctl);

        // setup → 拿密钥
        var setupOk = Assert.IsType<OkObjectResult>(await ctl.TwoFactorSetup());
        var secret = (string)setupOk.Value!.GetType().GetProperty("secret")!.GetValue(setupOk.Value)!;
        Assert.False(string.IsNullOrEmpty(secret));

        // 用户已写入 secret
        var fresh = db.Sys_Users.IgnoreQueryFilters().Single(u => u.Id == user.Id);
        Assert.NotNull(fresh.TwoFactorSecret);
        Assert.False(fresh.TwoFactorEnabled);

        // enroll → 真码
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.IsType<OkObjectResult>(await ctl.TwoFactorEnroll(new TwoFactorCodeRequest(code)));

        // 已 Enabled + 三 Cookie 写
        fresh = db.Sys_Users.IgnoreQueryFilters().Single(u => u.Id == user.Id);
        Assert.True(fresh.TwoFactorEnabled);
        Assert.NotNull(fresh.TwoFactorEnrolledAt);

        var setCookies = ctl.Response.Headers["Set-Cookie"].ToString();
        Assert.Contains(AuthCookieWriter.AccessCookie + "=ey", setCookies);
    }

    [Fact]
    public async Task ReadPending_without_cookie_throws_E_SEC_013()
    {
        var (ctl, _, _, _) = Make();
        // 不登录，直接打 2FA 端点
        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.TwoFactorVerify(new TwoFactorVerifyRequest("123456", null)));
        Assert.Equal("E-SEC-013", ex.Code);
    }
}
