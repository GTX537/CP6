namespace CP6.Core.Services.Sys;

public class SecurityOptions {
    public PasswordPolicyOptions Password { get; set; } = new();
    public LockoutOptions Lockout { get; set; } = new();
    public TokenOptions Token { get; set; } = new();
    public AuthCookieOptions Cookie { get; set; } = new();
}
public class PasswordPolicyOptions { public int MinLength {get;set;}=8; public bool RequireUpper{get;set;}=true; public bool RequireLower{get;set;}=true; public bool RequireDigit{get;set;}=true; public bool RequireSymbol{get;set;} public int ExpiryDays{get;set;} public int HistoryCount{get;set;}=3; public int BcryptWorkFactor{get;set;}=11; }
public class LockoutOptions { public int MaxFailedAttempts{get;set;}=5; public int LockoutMinutes{get;set;}=15; public int ResetCounterMinutes{get;set;}=15; }
public class TokenOptions { public int AccessTokenMinutes{get;set;}=15; public int RefreshTokenDays{get;set;}=7; }
public class AuthCookieOptions { public bool Secure{get;set;}=true; public string SameSite{get;set;}="Strict"; }
