using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class LoginSecurityServiceTests
{
    private static LoginSecurityService Make(CP6.Core.EFDbContext.CP6Context db, LockoutOptions l)
        => new(db, Options.Create(new SecurityOptions { Lockout = l }));

    [Fact]
    public async Task Locks_after_threshold_and_resets_on_success()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var u = new Sys_User { UserName = "x", Password = "h" };
        db.Sys_Users.Add(u);
        db.SaveChanges();
        var svc = Make(db, new LockoutOptions { MaxFailedAttempts = 3, LockoutMinutes = 15, ResetCounterMinutes = 15 });

        for (int i = 0; i < 3; i++) await svc.RecordFailureAsync(u);
        Assert.NotNull(u.LockedUntil);
        Assert.Throws<InvalidOperationException>(() => svc.EnsureNotLocked(u));   // 锁定期内即使密码对也拒

        u.LockedUntil = DateTime.Now.AddMinutes(-1);   // 模拟锁定过期
        svc.EnsureNotLocked(u);                        // 不抛
        await svc.RecordSuccessAsync(u, "1.2.3.4");
        Assert.Equal(0, u.FailedLoginCount);
        Assert.Null(u.LockedUntil);
        Assert.Equal("1.2.3.4", u.LastLoginIp);
        Assert.NotNull(u.LastLoginTime);
    }

    [Fact]
    public async Task Sliding_window_resets_counter_before_threshold()
    {
        using var db = TestHelper.CreateInMemoryContext();
        var u = new Sys_User { UserName = "y", Password = "h" };
        db.Sys_Users.Add(u);
        db.SaveChanges();
        var svc = Make(db, new LockoutOptions { MaxFailedAttempts = 3, LockoutMinutes = 15, ResetCounterMinutes = 15 });

        await svc.RecordFailureAsync(u);
        await svc.RecordFailureAsync(u);
        Assert.Equal(2, u.FailedLoginCount);
        // 距上次失败超过 ResetCounterMinutes → 计数滑动清零再累加
        u.LastFailedLoginAt = DateTime.Now.AddMinutes(-20);
        await svc.RecordFailureAsync(u);
        Assert.Equal(1, u.FailedLoginCount);   // 不是 3，未触发锁定
        Assert.Null(u.LockedUntil);
    }
}
