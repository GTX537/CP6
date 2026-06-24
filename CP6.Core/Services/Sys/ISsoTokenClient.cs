using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// OIDC 授权码换 ID Token 接缝（S 类 #3 T5）。真实现走 IHttpClientFactory("sso") POST token_endpoint；
/// 测试以假实现返回本地签名的 id_token，使 <see cref="SsoService.HandleCallbackAsync"/> 的
/// 验签 + 映射/JIT 全程可单测，免连真 IdP。
/// </summary>
public interface ISsoTokenClient
{
    /// <summary>
    /// Authorization Code + PKCE 换 ID Token。成功返 id_token 串；非 2xx 或响应无 id_token → null。
    /// </summary>
    Task<string?> ExchangeCodeForIdTokenAsync(OidcEndpoints ep, Sys_TenantSsoConfig cfg, string clientSecret,
                                              string code, string codeVerifier, string redirectUri);
}
