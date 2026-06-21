using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 登录安全服务（S 类认证加固 T3）：账户锁定（防暴破）+ 登录画像维护。
/// 锁定中抛 <see cref="InvalidOperationException"/>("E-SEC-002")，由控制器边界转 BizException 本地化。
/// </summary>
public interface ILoginSecurityService
{
    /// <summary>账户锁定中（LockedUntil 未到）则抛 E-SEC-002；否则放行。</summary>
    void EnsureNotLocked(Sys_User user);

    /// <summary>记一次登录失败：滑动窗口累加计数，达阈值则锁定。</summary>
    Task RecordFailureAsync(Sys_User user);

    /// <summary>记一次登录成功：清零失败计数/锁定，刷新 LastLoginTime/Ip。</summary>
    Task RecordSuccessAsync(Sys_User user, string? ip);
}
