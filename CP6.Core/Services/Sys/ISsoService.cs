using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// SSO/OIDC 登录编排（S 类 #3）。CP6 作 SP，走 Authorization Code + PKCE。
/// T4 实现 <see cref="BuildAuthorizeUrlAsync"/>；<see cref="HandleCallbackAsync"/> 由 T5 落地。
/// </summary>
public interface ISsoService
{
    /// <summary>
    /// 构造跳转 IdP 的 authorize URL（含 PKCE S256 + state/nonce）。
    /// 租户无配置或未启用 → InvalidOperationException("E-SEC-020")；discovery 不可达 → "E-SEC-028"。
    /// </summary>
    Task<string> BuildAuthorizeUrlAsync(string tenantCode, string redirectUri, string? returnUrl);

    /// <summary>回调换码 + 验签 + JIT 供给（T5 实现）。</summary>
    Task<Sys_User> HandleCallbackAsync(string code, string state, string redirectUri);
}
