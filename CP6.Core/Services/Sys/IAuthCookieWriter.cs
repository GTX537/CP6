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

    /// <summary>仅写 access Cookie（cp6_at），复用 <see cref="WriteAuthCookies"/> 同款 CookieOptions（多租户合规 #5 T5）。
    /// impersonation start/end 只换 access 身份，refresh/CSRF 不动 → access 过期/refresh 自然回平台超管=隐式切出窗口。</summary>
    void WriteAccessCookieOnly(HttpResponse resp, string accessJwt);

    /// <summary>清三 Cookie + cp6_2fa（登出调用，连带清 pending 半登录态）。</summary>
    void ClearAuthCookies(HttpResponse resp);

    /// <summary>写 pending Cookie（S 类 #2 T5）：cp6_2fa(httpOnly,Path=/api/auth)+cp6_csrf(评审#1 2FA 端点亦走双提交)。</summary>
    void WritePendingCookies(HttpResponse resp, string pendingToken, string csrf);

    /// <summary>清 pending Cookie（2FA 完成 / 取消时调用，含 cp6_2fa；cp6_csrf 由后续 WriteAuthCookies 覆盖）。</summary>
    void ClearPendingCookies(HttpResponse resp);
}
