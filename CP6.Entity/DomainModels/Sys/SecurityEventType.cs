namespace CP6.Entity.DomainModels.Sys;

/// <summary>安全事件类型（S 类认证加固 T3）。存 <see cref="Sys_SecurityLog.EventType"/> 的 int 值。</summary>
public enum SecurityEventType
{
    LoginSuccess = 1,
    LoginFailed = 2,
    AccountLocked = 3,
    Logout = 4,
    PasswordChanged = 5,
    TokenRefreshed = 6,
    TokenReuseDetected = 7,
    PermissionDenied = 8,

    // 9~14 保留给 #2 2FA（spec §1 R1：#3 自洽不硬依赖 #2，刻意留空段）

    // ───── S 类 #3 SSO/OIDC（spec §2.4）─────
    SsoLoginSuccess = 15,
    SsoLoginFailed = 16,
    SsoUserProvisioned = 17,
    SsoConfigChanged = 18
}
