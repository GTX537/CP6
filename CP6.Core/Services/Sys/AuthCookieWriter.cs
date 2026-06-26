using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 三认证 Cookie 写入实现（S 类认证加固 T6）。Secure/SameSite 由 <see cref="AuthCookieOptions"/> 配置
/// （Development 覆盖 Secure=false 保本地 http 可跑 gstack QA）。
/// </summary>
public class AuthCookieWriter : IAuthCookieWriter
{
    public const string AccessCookie = "cp6_at";
    public const string RefreshCookie = "cp6_rt";
    public const string CsrfCookie = "cp6_csrf";
    /// <summary>2FA pending 半登录 Cookie（S 类 #2 T5）。Path=/api/auth（与 2FA 端点共享）。</summary>
    public const string PendingCookie = "cp6_2fa";
    /// <summary>refresh 令牌 cookie 仅在 /api/auth 路径回传（缩小暴露面：刷新/登出端点均在此前缀下）。</summary>
    public const string RefreshPath = "/api/auth";

    private readonly AuthCookieOptions _c;
    public AuthCookieWriter(IOptions<SecurityOptions> opt) => _c = opt.Value.Cookie;

    private SameSiteMode Same => Enum.TryParse<SameSiteMode>(_c.SameSite, true, out var m) ? m : SameSiteMode.Strict;

    /// <summary>生成 CSRF 双提交令牌（CSPRNG 随机串，登录/刷新时写入 cp6_csrf）。</summary>
    public static string NewCsrfToken()
        => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public void WriteAuthCookies(HttpResponse resp, string accessJwt, string rawRefresh, string csrf)
    {
        resp.Cookies.Append(AccessCookie, accessJwt,
            new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same, Path = "/" });
        resp.Cookies.Append(RefreshCookie, rawRefresh,
            new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same, Path = RefreshPath });
        // CSRF 令牌须可被前端 JS 读出回填 X-CSRF-Token 头 → 不 httpOnly
        resp.Cookies.Append(CsrfCookie, csrf,
            new CookieOptions { HttpOnly = false, Secure = _c.Secure, SameSite = Same, Path = "/" });
    }

    public void WriteAccessCookieOnly(HttpResponse resp, string accessJwt)
    {
        // 同 WriteAuthCookies 的 access 分支（httpOnly/Secure/SameSite/Path="/")；不触碰 refresh/csrf/pending。
        resp.Cookies.Append(AccessCookie, accessJwt,
            new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same, Path = "/" });
    }

    public void ClearAuthCookies(HttpResponse resp)
    {
        foreach (var (name, path) in new[] { (AccessCookie, "/"), (RefreshCookie, RefreshPath), (CsrfCookie, "/"), (PendingCookie, RefreshPath) })
            resp.Cookies.Append(name, "",
                new CookieOptions { Expires = DateTimeOffset.UnixEpoch, Path = path, Secure = _c.Secure, SameSite = Same });
    }

    public void WritePendingCookies(HttpResponse resp, string pendingToken, string csrf)
    {
        resp.Cookies.Append(PendingCookie, pendingToken,
            new CookieOptions { HttpOnly = true, Secure = _c.Secure, SameSite = Same, Path = RefreshPath });
        // 2FA 端点（POST /api/auth/2fa/*）也走 CSRF 双提交（评审#1）→ 写 cp6_csrf 供前端读出回填
        resp.Cookies.Append(CsrfCookie, csrf,
            new CookieOptions { HttpOnly = false, Secure = _c.Secure, SameSite = Same, Path = "/" });
    }

    public void ClearPendingCookies(HttpResponse resp)
    {
        resp.Cookies.Append(PendingCookie, "",
            new CookieOptions { Expires = DateTimeOffset.UnixEpoch, Path = RefreshPath, Secure = _c.Secure, SameSite = Same });
    }
}
