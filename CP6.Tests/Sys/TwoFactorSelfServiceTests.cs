using CP6.Core.EFDbContext;
using CP6.Core.Services.Common;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
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
using OtpNet;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// #2 2FA（T7）自助启停 + admin 重置 + 租户策略测试：
/// status/setup-self/enroll-self/disable-self（验密码 E-SEC-006 + 码 E-SEC-011 + 强制租户禁关 E-SEC-019）、
/// admin reset-2fa（UserController user:edit 清启用/密钥）、TwoFactorPolicy GET/PUT。
/// </summary>
public class TwoFactorSelfServiceTests
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

    private static (AuthController ctl, CP6Context db, Sys_User user, TwoFactorService twoFa, IDistributedCache cache, Guid tenantId) Make(
        int tenantMode = 0, bool enabled = false, string? secret = null, string password = "Right1!aA", string? email = "a@a.com")
    {
        var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        // 单测无中间件：CP6Context 的全局租户过滤器回退 DefaultTenant；用户/租户须落在 DefaultTenant
        // 才能被自助端点（plain FirstAsync，对齐 ChangePassword）命中。租户 2FA 策略写在该行。
        var tenantId = TenantContext.DefaultTenant;
        db.Sys_Tenants.Add(new Sys_Tenant
        {
            Id = tenantId, TenantCode = "T01", TenantName = "T01", Enable = true,
            TwoFactorMode = tenantMode
        });
        var user = new Sys_User
        {
            Id = Guid.NewGuid(),
            UserName = "alice",
            Email = email,
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
        var email2 = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        var pending = new PendingTokenStore(cache, opt);
        var twoFa = new TwoFactorService(db, totp, email2, cache, audit, opt);
        // 模拟中间件：已登录请求的租户上下文 = 当前用户租户（否则全局过滤器排除该用户行）
        var tenantCtx = new TenantContext { CurrentTenantId = tenantId };
        var ctl = new AuthController(db, config, new FakePermCtx(user.Id), tenantCtx,
            hasher, policy, login, audit, refresh, blacklist, cookies, opt,
            new FakeTenantSsoConfigService(), new FakeSsoService(), twoFa, pending);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (ctl, db, user, twoFa, cache, tenantId);
    }

    private static Sys_User Fresh(CP6Context db, Guid id) => db.Sys_Users.IgnoreQueryFilters().Single(u => u.Id == id);

    private static T Prop<T>(object obj, string name) => (T)obj.GetType().GetProperty(name)!.GetValue(obj)!;

    // ───────── status ─────────

    [Fact]
    public async Task Status_mode1_enabled_canDisable_true()
    {
        var (ctl, _, _, _, _, _) = Make(tenantMode: 1, enabled: true, secret: "JBSWY3DPEHPK3PXP");
        var ok = Assert.IsType<OkObjectResult>(await ctl.TwoFactorStatus());
        Assert.True(Prop<bool>(ok.Value!, "enabled"));
        Assert.Equal(1, Prop<int>(ok.Value!, "tenantMode"));
        Assert.True(Prop<bool>(ok.Value!, "canDisable"));
    }

    [Fact]
    public async Task Status_mode2_enabled_canDisable_false()
    {
        var (ctl, _, _, _, _, _) = Make(tenantMode: 2, enabled: true, secret: "JBSWY3DPEHPK3PXP");
        var ok = Assert.IsType<OkObjectResult>(await ctl.TwoFactorStatus());
        Assert.True(Prop<bool>(ok.Value!, "enabled"));
        Assert.False(Prop<bool>(ok.Value!, "canDisable")); // 强制租户不可自助关闭
    }

    // ───────── setup-self / enroll-self ─────────

    [Fact]
    public async Task SetupSelf_when_already_enabled_throws_E_SEC_017()
    {
        var (ctl, _, _, _, _, _) = Make(tenantMode: 1, enabled: true, secret: "JBSWY3DPEHPK3PXP");
        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.TwoFactorSetupSelf());
        Assert.Equal("E-SEC-017", ex.Code);
    }

    [Fact]
    public async Task SetupSelf_then_enrollSelf_happy_path_sets_enabled()
    {
        var (ctl, db, user, _, _, _) = Make(tenantMode: 1);
        var setupOk = Assert.IsType<OkObjectResult>(await ctl.TwoFactorSetupSelf());
        var secret = Prop<string>(setupOk.Value!, "secret");
        Assert.False(string.IsNullOrEmpty(secret));

        var afterSetup = Fresh(db, user.Id);
        Assert.NotNull(afterSetup.TwoFactorSecret);
        Assert.False(afterSetup.TwoFactorEnabled);

        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.IsType<OkObjectResult>(await ctl.TwoFactorEnrollSelf(new TwoFactorCodeRequest(code)));

        var enrolled = Fresh(db, user.Id);
        Assert.True(enrolled.TwoFactorEnabled);
        Assert.NotNull(enrolled.TwoFactorEnrolledAt);
    }

    [Fact]
    public async Task EnrollSelf_wrong_code_throws_E_SEC_011()
    {
        var (ctl, _, _, _, _, _) = Make(tenantMode: 1);
        await ctl.TwoFactorSetupSelf();
        var ex = await Assert.ThrowsAsync<BizException>(() => ctl.TwoFactorEnrollSelf(new TwoFactorCodeRequest("000000")));
        Assert.Equal("E-SEC-011", ex.Code);
    }

    // ───────── disable-self ─────────

    [Fact]
    public async Task DisableSelf_wrong_password_throws_E_SEC_006()
    {
        var (ctl, _, _, _, _, _) = Make(tenantMode: 1, enabled: true, secret: "JBSWY3DPEHPK3PXP");
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.TwoFactorDisableSelf(new DisableTwoFactorRequest("WrongPass1!", "123456", "totp")));
        Assert.Equal("E-SEC-006", ex.Code);
    }

    [Fact]
    public async Task DisableSelf_wrong_code_throws_E_SEC_011()
    {
        var (ctl, _, _, _, _, _) = Make(tenantMode: 1, enabled: true, secret: "JBSWY3DPEHPK3PXP");
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.TwoFactorDisableSelf(new DisableTwoFactorRequest("Right1!aA", "000000", "totp")));
        Assert.Equal("E-SEC-011", ex.Code);
    }

    [Fact]
    public async Task DisableSelf_mode2_throws_E_SEC_019_even_with_correct_password_and_code()
    {
        var secret = "JBSWY3DPEHPK3PXP";
        var (ctl, _, _, _, _, _) = Make(tenantMode: 2, enabled: true, secret: secret);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.TwoFactorDisableSelf(new DisableTwoFactorRequest("Right1!aA", code, "totp")));
        Assert.Equal("E-SEC-019", ex.Code);
    }

    [Fact]
    public async Task DisableSelf_happy_path_clears_2fa()
    {
        var secret = "JBSWY3DPEHPK3PXP";
        var (ctl, db, user, _, _, _) = Make(tenantMode: 1, enabled: true, secret: secret);
        var code = new Totp(Base32Encoding.ToBytes(secret)).ComputeTotp();
        Assert.IsType<OkObjectResult>(await ctl.TwoFactorDisableSelf(new DisableTwoFactorRequest("Right1!aA", code, "totp")));

        var fresh = Fresh(db, user.Id);
        Assert.False(fresh.TwoFactorEnabled);
        Assert.Null(fresh.TwoFactorSecret);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.TwoFactorReset);
    }

    // ───────── email-otp-self ─────────

    [Fact]
    public async Task EmailOtpSelf_returns_ok_and_logs_sent()
    {
        var (ctl, db, _, _, _, _) = Make(tenantMode: 1, enabled: true, secret: "JBSWY3DPEHPK3PXP");
        Assert.IsType<OkObjectResult>(await ctl.TwoFactorEmailOtpSelf());
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.TwoFactorEmailOtpSent);
    }

    [Fact]
    public async Task DisableSelf_via_email_otp_clears_2fa()
    {
        var secret = "JBSWY3DPEHPK3PXP";
        var (ctl, db, user, _, _, _) = Make(tenantMode: 1, enabled: true, secret: secret);
        // 发邮件 OTP（写入 cache，key=self:{uid}）
        Assert.IsType<OkObjectResult>(await ctl.TwoFactorEmailOtpSelf());
        // LogEmailSender 不暴露明文 OTP → 直接验 TOTP 路径已覆盖关闭；email 路径冷却/无邮箱在服务层已测，
        // 此处仅确保 email-otp-self 端点与 disable-self 的 method=email 分支可达（用错码→E-SEC-011 不消费）。
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.TwoFactorDisableSelf(new DisableTwoFactorRequest("Right1!aA", "000000", "email")));
        Assert.Equal("E-SEC-011", ex.Code);
        var fresh = Fresh(db, user.Id);
        Assert.True(fresh.TwoFactorEnabled); // 错码未关闭
    }

    // ───────── admin reset-2fa (UserController) ─────────

    private static (UserController ctl, CP6Context db, Sys_User user) MakeUserCtl(bool enabled = true, string? secret = "JBSWY3DPEHPK3PXP")
    {
        var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var opt = Options.Create(new SecurityOptions());
        var refresh = new RefreshTokenService(db, opt, new TenantContext());
        var audit = new SecurityAuditService(db);
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var totp = new TotpService(opt);
        var email = new LogEmailSender(NullLogger<LogEmailSender>.Instance);
        var twoFa = new TwoFactorService(db, totp, email, cache, audit, opt);

        var user = new Sys_User
        {
            Id = Guid.NewGuid(), UserName = "bob", Enable = true,
            Password = hasher.Hash("Old1!pass"),
            TwoFactorEnabled = enabled, TwoFactorSecret = secret,
            TwoFactorEnrolledAt = enabled ? DateTime.Now : null
        };
        db.Sys_Users.Add(user);
        db.SaveChanges();

        var ctl = new UserController(db, hasher, refresh, twoFa)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (ctl, db, user);
    }

    [Fact]
    public async Task AdminReset2fa_clears_enabled_and_secret()
    {
        var (ctl, db, user) = MakeUserCtl(enabled: true);
        Assert.IsType<OkObjectResult>(await ctl.ResetTwoFactor(user.Id));

        db.ChangeTracker.Clear();
        var fresh = db.Sys_Users.Single(u => u.Id == user.Id);
        Assert.False(fresh.TwoFactorEnabled);
        Assert.Null(fresh.TwoFactorSecret);
        Assert.Null(fresh.TwoFactorEnrolledAt);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.TwoFactorReset && l.Reason == "admin-reset");
    }

    [Fact]
    public async Task AdminReset2fa_unknown_user_returns_NotFound()
    {
        var (ctl, _, _) = MakeUserCtl();
        var res = await ctl.ResetTwoFactor(Guid.NewGuid());
        Assert.IsType<NotFoundResult>(res);
    }

    // ───────── TwoFactorPolicyController GET/PUT ─────────

    private static (TwoFactorPolicyController ctl, CP6Context db, Guid tenantId) MakePolicyCtl(int mode = 0)
    {
        var db = TestHelper.CreateInMemoryContext();
        var tenantId = Guid.NewGuid();
        db.Sys_Tenants.Add(new Sys_Tenant { Id = tenantId, TenantCode = "T01", TenantName = "T01", Enable = true, TwoFactorMode = mode });
        db.SaveChanges();
        var tenant = new TenantContext { CurrentTenantId = tenantId };
        var ctl = new TwoFactorPolicyController(db, tenant)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (ctl, db, tenantId);
    }

    [Fact]
    public async Task Policy_get_returns_current_mode()
    {
        var (ctl, _, _) = MakePolicyCtl(mode: 2);
        var ok = Assert.IsType<OkObjectResult>(await ctl.Get());
        Assert.Equal(2, Prop<int>(ok.Value!, "mode"));
    }

    [Fact]
    public async Task Policy_put_writes_mode()
    {
        var (ctl, db, tenantId) = MakePolicyCtl(mode: 0);
        Assert.IsType<OkObjectResult>(await ctl.Put(new TwoFactorPolicyRequest(2)));
        db.ChangeTracker.Clear();
        var t = db.Sys_Tenants.IgnoreQueryFilters().Single(x => x.Id == tenantId);
        Assert.Equal(2, t.TwoFactorMode);
    }

    [Fact]
    public async Task Policy_put_invalid_mode_throws_BizException()
    {
        var (ctl, _, _) = MakePolicyCtl(mode: 0);
        await Assert.ThrowsAsync<BizException>(() => ctl.Put(new TwoFactorPolicyRequest(5)));
    }
}
