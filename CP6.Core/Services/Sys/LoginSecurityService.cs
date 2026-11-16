using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 登录安全服务实现。错误经 <see cref="InvalidOperationException"/> 携带 E-SEC 码（Core 层惯例）。
/// </summary>
public partial class LoginSecurityService : ILoginSecurityService
{
    private readonly CP6Context _db;
    private readonly LockoutOptions _l;
    private readonly IDataProtector? _lockoutProtector;

    public LoginSecurityService(CP6Context db, IOptions<SecurityOptions> opt, IDataProtectionProvider? protection = null)
    {
        _db = db;
        _l = opt.Value.Lockout;
        _lockoutProtector = protection?.CreateProtector("CP6.Authentication.Lockout.Original.v1");
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
        if (_db.Database.IsNpgsql())
            await RequireNoPendingLoginLockoutAsync(user.TenantId, user.Id, CancellationToken.None);
        user.FailedLoginCount = 0;
        user.LockedUntil = null;
        user.LastFailedLoginAt = null;
        user.LastLoginTime = DateTime.Now;
        user.LastLoginIp = ip;
        await _db.SaveChangesAsync();
    }
}
