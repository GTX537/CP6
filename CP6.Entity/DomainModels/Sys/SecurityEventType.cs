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

    // ───── S 类 #2 2FA（spec §5）─────
    TwoFactorChallenged = 9,
    TwoFactorVerified = 10,
    TwoFactorFailed = 11,
    TwoFactorEnrolled = 12,
    TwoFactorReset = 13,
    TwoFactorEmailOtpSent = 14,

    // ───── S 类 #3 SSO/OIDC（spec §2.4）─────
    SsoLoginSuccess = 15,
    SsoLoginFailed = 16,
    SsoUserProvisioned = 17,
    SsoConfigChanged = 18,

    // ───── S 类 #5 多租户合规（spec §4/§5/§6）─────
    TenantCreated = 19,
    TenantUpdated = 20,
    TenantSuspended = 21,
    TenantReactivated = 22,
    PlatformAdminGranted = 23,
    PlatformAdminRevoked = 24,
    ImpersonationStarted = 25,
    ImpersonationEnded = 26,
    GdprTenantExported = 27,
    GdprSubjectExported = 28,
    GdprTenantErased = 29,
    GdprSubjectErased = 30
}
