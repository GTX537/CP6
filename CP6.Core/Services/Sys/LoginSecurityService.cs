using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 登录安全服务实现。错误经 <see cref="InvalidOperationException"/> 携带 E-SEC 码（Core 层惯例）。
/// </summary>
public class LoginSecurityService : ILoginSecurityService
{
    private readonly CP6Context _db;
    private readonly LockoutOptions _l;

    public LoginSecurityService(CP6Context db, IOptions<SecurityOptions> opt)
    {
        _db = db;
        _l = opt.Value.Lockout;
    }

    public void EnsureNotLocked(Sys_User user)
    {
        if (user.LockedUntil is { } until && until > DateTime.Now)
            throw new InvalidOperationException("E-SEC-002");
    }

    public async Task RecordFailureAsync(Sys_User user)
    {
        // 滑动重置：距上次失败超过 ResetCounterMinutes 则计数先清零再累加
        if (user.LastFailedLoginAt is { } last && (DateTime.Now - last).TotalMinutes > _l.ResetCounterMinutes)
            user.FailedLoginCount = 0;
        user.FailedLoginCount++;
        user.LastFailedLoginAt = DateTime.Now;
        if (user.FailedLoginCount >= _l.MaxFailedAttempts)
            user.LockedUntil = DateTime.Now.AddMinutes(_l.LockoutMinutes);
        await _db.SaveChangesAsync();
    }

    public async Task RecordSuccessAsync(Sys_User user, string? ip)
    {
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastFailedLoginAt = null;
        user.LastLoginTime = DateTime.Now;
        user.LastLoginIp = ip;
        await _db.SaveChangesAsync();
    }
}
