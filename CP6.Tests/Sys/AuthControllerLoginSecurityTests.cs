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
/// Login 流的锁定 + 审计 + 防枚举集成测试（T3）。只覆盖失败路径
/// （成功路径触达 Prewarm/菜单/token，留 gstack QA T10）。
/// </summary>
public class AuthControllerLoginSecurityTests
{
    private sealed class FakePermCtx : ICurrentPermissionContext
    {
        public Task<UserPermissionContext> GetAsync() => throw new NotImplementedException();
        public Task<UserPermissionContext> PrewarmAsync(Guid userId) => throw new NotImplementedException();
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    private static (AuthController ctl, CP6Context db, Sys_User user) Make(string plainPassword, int maxFailed = 3)
    {
        var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "bob", Password = hasher.Hash(plainPassword), Enable = true, TenantId = TenantContext.DefaultTenant };
        db.Sys_Users.Add(user);
        db.SaveChanges();
        var opt = Options.Create(new SecurityOptions { Lockout = new LockoutOptions { MaxFailedAttempts = maxFailed, LockoutMinutes = 15, ResetCounterMinutes = 15 } });
        var policy = new PasswordPolicyService(db, opt, hasher);
        var login = new LoginSecurityService(db, opt);
        var audit = new SecurityAuditService(db);
        var refresh = new RefreshTokenService(db, opt, new TenantContext());
        var blacklist = new CacheTokenBlacklistService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var cookies = new AuthCookieWriter(opt);
        var ctl = new AuthController(db, new ConfigurationBuilder().Build(), new FakePermCtx(), new TenantContext(), hasher, policy, login, audit, refresh, blacklist, cookies, opt, new FakeTenantSsoConfigService(), new FakeSsoService());
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (ctl, db, user);
    }

    [Fact]
    public async Task Wrong_password_records_failure_audits_and_throws_generic()
    {
        var (ctl, db, user) = Make("Right1!aA");
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.Login(new LoginRequest { UserName = "bob", Password = "wrongpw" }));
        Assert.Equal("E-SEC-001", ex.Code);   // 不区分用户/密码错（防枚举）
        var fresh = db.Sys_Users.Single(u => u.Id == user.Id);
        Assert.Equal(1, fresh.FailedLoginCount);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.LoginFailed);
    }

    [Fact]
    public async Task Unknown_user_audits_and_throws_generic()
    {
        var (ctl, db, _) = Make("Right1!aA");
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.Login(new LoginRequest { UserName = "ghost", Password = "whatever" }));
        Assert.Equal("E-SEC-001", ex.Code);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.LoginFailed && l.UserName == "ghost");
    }

    [Fact]
    public async Task Disabled_account_with_correct_password_throws_E_SEC_003_and_audits()
    {
        var (ctl, db, user) = Make("Right1!aA");
        user.Enable = false;
        db.SaveChanges();

        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.Login(new LoginRequest { UserName = "bob", Password = "Right1!aA" }));
        Assert.Equal("E-SEC-003", ex.Code);
        Assert.Contains(db.Sys_SecurityLogs, l => l.UserName == "bob" && l.Reason == "account disabled");
        // 禁用账号不应被刷成功画像
        Assert.Null(db.Sys_Users.Single(u => u.Id == user.Id).LastLoginTime);
    }

    [Fact]
    public async Task Locks_after_threshold_then_rejects_even_correct_password()
    {
        var (ctl, db, user) = Make("Right1!aA", maxFailed: 3);
        for (int i = 0; i < 3; i++)
            await Assert.ThrowsAsync<BizException>(() =>
                ctl.Login(new LoginRequest { UserName = "bob", Password = "wrongpw" }));

        // 锁定后即使密码正确也被拒，且返回锁定码
        var ex = await Assert.ThrowsAsync<BizException>(() =>
            ctl.Login(new LoginRequest { UserName = "bob", Password = "Right1!aA" }));
        Assert.Equal("E-SEC-002", ex.Code);
        Assert.Contains(db.Sys_SecurityLogs, l => l.EventType == (int)SecurityEventType.AccountLocked);
    }
}
