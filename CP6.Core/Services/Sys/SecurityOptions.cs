namespace CP6.Core.Services.Sys;

public class SecurityOptions {
    public PasswordPolicyOptions Password { get; set; } = new();
    public LockoutOptions Lockout { get; set; } = new();
    public TokenOptions Token { get; set; } = new();
    public AuthCookieOptions Cookie { get; set; } = new();
    public CsrfOptions Csrf { get; set; } = new();
    public TwoFactorOptions TwoFactor { get; set; } = new();
    public SsoOptions Sso { get; set; } = new();
    public NativeClientOptions NativeClient { get; set; } = new();
    /// <summary>S 类 #5 多租户合规：impersonation 替身令牌有效期（分钟，默认 30）。</summary>
    public int ImpersonationMinutes { get; set; } = 30;
}
public class SsoOptions {
    public int StateMinutes { get; set; } = 10;
    public string CallbackPath { get; set; } = "/api/auth/sso/callback";
    public string PostLoginRedirect { get; set; } = "/sso/landing";
    public int HttpTimeoutSeconds { get; set; } = 15;
    public string? PublicBaseUrl { get; set; }
    public string? FrontendBaseUrl { get; set; }
}
public class TwoFactorOptions { public string Issuer{get;set;}="CP6"; public int CodeWindow{get;set;}=1; public int EmailOtpMinutes{get;set;}=5; public int EmailOtpLength{get;set;}=6; public int EmailResendCooldownSeconds{get;set;}=60; public int PendingTokenMinutes{get;set;}=5; public bool EmailFallbackEnabled{get;set;}=true; }
public class PasswordPolicyOptions { public int MinLength {get;set;}=8; public bool RequireUpper{get;set;}=true; public bool RequireLower{get;set;}=true; public bool RequireDigit{get;set;}=true; public bool RequireSymbol{get;set;} public int ExpiryDays{get;set;} public int HistoryCount{get;set;}=3; public int BcryptWorkFactor{get;set;}=11; }
public class LockoutOptions { public int MaxFailedAttempts{get;set;}=5; public int LockoutMinutes{get;set;}=15; public int ResetCounterMinutes{get;set;}=15; }
public class TokenOptions { public int AccessTokenMinutes{get;set;}=15; public int RefreshTokenDays{get;set;}=7; }
public class AuthCookieOptions { public bool Secure{get;set;}=true; public string SameSite{get;set;}="Strict"; }
public class NativeClientOptions
{
    public int SsoGrantMinutes { get; set; } = 2;
    public string[] AllowedRedirectUris { get; set; } =
        ["cp6-desktop://auth/callback", "cp6-mobile://auth/callback"];
    public ClientReleaseOptions Windows { get; set; } = new();
    public ClientReleaseOptions Android { get; set; } = new();
}
public class ClientReleaseOptions
{
    public string LatestVersion { get; set; } = "1.0.0";
    public string MinimumVersion { get; set; } = "1.0.0";
    public string? DownloadUrl { get; set; }
    public string? Sha256 { get; set; }
}
// CSRF 双提交开关（T6）：默认开启（生产正确）；开发/QA 在 T9 前端注入 X-CSRF-Token 头前，
// 由 appsettings.Development.json 置 false，避免全站写请求被 403 阻断（计划 T6/T9 co-dependency）。
public class CsrfOptions { public bool Enabled{get;set;}=true; }
