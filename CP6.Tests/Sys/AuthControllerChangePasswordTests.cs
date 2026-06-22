using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Services.Common;
using CP6.Entity.DomainModels.Sys;
using CP6.WebApi.Controllers.Sys;
using CP6.WebApi.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// change-password 端点编排测试（T2）：直接 new 控制器 + InMemory；
/// 用最小 ICurrentPermissionContext 桩固定当前用户。
/// </summary>
public class AuthControllerChangePasswordTests
{
    private sealed class FakePermCtx : ICurrentPermissionContext
    {
        private readonly Guid _uid;
        public FakePermCtx(Guid uid) => _uid = uid;
        public Task<UserPermissionContext> GetAsync() => Task.FromResult(new UserPermissionContext { UserId = _uid });
        public Task<UserPermissionContext> PrewarmAsync(Guid userId) => throw new NotImplementedException();
        public void Invalidate(Guid userId) { }
        public void InvalidateByRole(int roleId) { }
    }

    private static (AuthController ctl, CP6Context db, Sys_User user) Make(string currentPlain, int historyCount = 3)
    {
        var db = TestHelper.CreateInMemoryContext();
        var hasher = new BCryptPasswordHasher();
        var user = new Sys_User { Id = Guid.NewGuid(), UserName = "alice", Password = hasher.Hash(currentPlain), MustChangePassword = true };
        db.Sys_Users.Add(user);
        db.SaveChanges();
        var opt = Options.Create(new SecurityOptions { Password = new PasswordPolicyOptions { HistoryCount = historyCount } });
        var policy = new PasswordPolicyService(db, opt, hasher);
        var login = new LoginSecurityService(db, opt);
        var audit = new SecurityAuditService(db);
        var refresh = new RefreshTokenService(db, opt, new TenantContext());
        var blacklist = new CacheTokenBlacklistService(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        var cfg = new ConfigurationBuilder().Build();
        var ctl = new AuthController(db, cfg, new FakePermCtx(user.Id), new TenantContext(), hasher, policy, login, audit, refresh, blacklist, opt);
        ctl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (ctl, db, user);
    }

    [Fact]
    public async Task Happy_path_rehashes_records_history_and_clears_mustchange()
    {
        var (ctl, db, user) = Make("OldPass1!");
        var oldHash = user.Password;

        var result = await ctl.ChangePassword(new ChangePasswordRequest("OldPass1!", "NewPass2@"));

        Assert.IsType<OkObjectResult>(result);
        var fresh = db.Sys_Users.Single(u => u.Id == user.Id);
        Assert.True(new BCryptPasswordHasher().Verify("NewPass2@", fresh.Password));
        Assert.False(fresh.MustChangePassword);
        Assert.NotNull(fresh.PasswordChangedAt);
        var hist = db.Sys_PasswordHistories.Single(h => h.UserId == user.Id);
        Assert.Equal(oldHash, hist.PasswordHash);   // 旧哈希入历史
    }

    [Fact]
    public async Task Wrong_current_password_throws()
    {
        var (ctl, _, _) = Make("OldPass1!");
        await Assert.ThrowsAsync<BizException>(() =>
            ctl.ChangePassword(new ChangePasswordRequest("WRONG!9", "NewPass2@")));
    }

    [Fact]
    public async Task Weak_new_password_throws()
    {
        var (ctl, _, _) = Make("OldPass1!");
        await Assert.ThrowsAsync<BizException>(() =>
            ctl.ChangePassword(new ChangePasswordRequest("OldPass1!", "weak")));
    }

    [Fact]
    public async Task Reused_new_password_throws()
    {
        var (ctl, db, user) = Make("OldPass1!");
        db.Sys_PasswordHistories.Add(new Sys_PasswordHistory
        {
            UserId = user.Id,
            PasswordHash = new BCryptPasswordHasher().Hash("ReusedP1!"),
            ChangedAt = DateTime.Now
        });
        db.SaveChanges();
        await Assert.ThrowsAsync<BizException>(() =>
            ctl.ChangePassword(new ChangePasswordRequest("OldPass1!", "ReusedP1!")));
    }

    [Fact]
    public async Task Revokes_all_active_refresh_tokens_on_success()
    {
        var (ctl, db, user) = Make("OldPass1!");
        // 预置一条该用户的有效刷新令牌
        db.Sys_RefreshTokens.Add(new Sys_RefreshToken
        {
            UserId = user.Id,
            TokenHash = "EXISTING_HASH",
            ExpiresAt = DateTime.Now.AddDays(7)
        });
        db.SaveChanges();

        await ctl.ChangePassword(new ChangePasswordRequest("OldPass1!", "NewPass2@"));

        // 清跟踪器强制重载：验证吊销与改密随同一次保存真落库（原子）
        db.ChangeTracker.Clear();
        var token = db.Sys_RefreshTokens.IgnoreQueryFilters().Single(t => t.UserId == user.Id);
        Assert.NotNull(token.RevokedAt);   // 改密强制旧刷新令牌失效
    }

    [Fact]
    public async Task History_trimmed_to_HistoryCount()
    {
        var (ctl, db, user) = Make("Curr3nt!A", historyCount: 2);
        var h = new BCryptPasswordHasher();
        // 预置 2 条更久远历史
        db.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = user.Id, PasswordHash = h.Hash("OldOne1!"), ChangedAt = DateTime.Now.AddDays(-3) });
        db.Sys_PasswordHistories.Add(new Sys_PasswordHistory { UserId = user.Id, PasswordHash = h.Hash("OldTwo2!"), ChangedAt = DateTime.Now.AddDays(-2) });
        db.SaveChanges();

        // 改密会把当前("Curr3nt!A")旧哈希入历史 → 共 3 条 → 裁剪保留最近 2 条
        await ctl.ChangePassword(new ChangePasswordRequest("Curr3nt!A", "BrandNew9#"));

        var rows = db.Sys_PasswordHistories.Where(x => x.UserId == user.Id).ToList();
        Assert.Equal(2, rows.Count);
        Assert.DoesNotContain(rows, r => h.Verify("OldOne1!", r.PasswordHash));   // 最久远被裁
        Assert.Contains(rows, r => h.Verify("Curr3nt!A", r.PasswordHash));        // 新入历史保留
    }
}
