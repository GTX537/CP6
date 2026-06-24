using Microsoft.IdentityModel.Tokens;

namespace CP6.Core.Services.Sys;

/// <summary>
/// OIDC discovery 接缝（S 类 #3 T4）。按 authority 拉取 .well-known/openid-configuration，
/// 缓存解析后的端点 + 签名密钥。注入式接缝：测试以假实现绕开真 IdP。
/// </summary>
public interface IOidcDiscovery
{
    /// <summary>取 authority 对应的 OIDC 端点集；拉取/解析失败 → InvalidOperationException("E-SEC-028")。</summary>
    Task<OidcEndpoints> GetAsync(string authority);
}

/// <summary>OIDC discovery 文档映射结果（授权端点 + token 端点 + 颁发者 + 验签密钥集）。</summary>
public record OidcEndpoints(
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string Issuer,
    ICollection<SecurityKey> SigningKeys);
