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
    PermissionDenied = 8
}
