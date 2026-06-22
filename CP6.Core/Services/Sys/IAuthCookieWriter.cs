using Microsoft.AspNetCore.Http;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 认证 Cookie 写入器（S 类认证加固 T6）。三 Cookie 双层防护：
/// <c>cp6_at</c>（access JWT，httpOnly，防 XSS 读取）、<c>cp6_rt</c>（refresh 原始令牌，httpOnly，
/// Path 限 /api/auth）、<c>cp6_csrf</c>（CSRF 双提交令牌，非 httpOnly 供前端读出回填请求头）。
/// </summary>
public interface IAuthCookieWriter
{
    /// <summary>写三 Cookie（登录 / 刷新成功后调用）。</summary>
    void WriteAuthCookies(HttpResponse resp, string accessJwt, string rawRefresh, string csrf);

    /// <summary>清三 Cookie（登出调用）。</summary>
    void ClearAuthCookies(HttpResponse resp);
}
