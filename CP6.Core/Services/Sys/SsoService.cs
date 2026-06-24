using System.Security.Cryptography;
using System.Text;
using CP6.Entity.DomainModels.Sys;
using Microsoft.IdentityModel.Tokens;

namespace CP6.Core.Services.Sys;

/// <summary>
/// SSO/OIDC 登录编排（S 类 #3）。CP6 作 SP，走 Authorization Code + PKCE(S256)。
/// T4：<see cref="BuildAuthorizeUrlAsync"/>（discovery + PKCE + state 落库）。
/// <see cref="HandleCallbackAsync"/> 由 T5 实现（届时构造函数会补充换码/验签/JIT 依赖）。
/// </summary>
public class SsoService : ISsoService
{
    private readonly ITenantSsoConfigService _cfg;
    private readonly ISsoStateStore _state;
    private readonly IOidcDiscovery _disco;

    public SsoService(ITenantSsoConfigService cfg, ISsoStateStore state, IOidcDiscovery disco)
    {
        _cfg = cfg;
        _state = state;
        _disco = disco;
    }

    public async Task<string> BuildAuthorizeUrlAsync(string tenantCode, string redirectUri, string? returnUrl)
    {
        var cfg = await _cfg.GetByTenantCodeAsync(tenantCode);
        if (cfg == null || !cfg.Enabled)
            throw new InvalidOperationException("E-SEC-020");

        var ep = await _disco.GetAsync(cfg.Authority);   // 不可达 → E-SEC-028

        var nonce = Guid.NewGuid().ToString("N");
        // PKCE code_verifier：32 字节高熵随机 → base64url（43 字符，RFC 7636 区间内）
        var codeVerifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var state = _state.Create(cfg.TenantId, nonce, codeVerifier, returnUrl);

        return $"{ep.AuthorizationEndpoint}?response_type=code"
             + $"&client_id={Uri.EscapeDataString(cfg.ClientId)}"
             + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
             + $"&scope={Uri.EscapeDataString(cfg.Scopes)}"
             + $"&state={state}"
             + $"&nonce={nonce}"
             + $"&code_challenge={challenge}"
             + "&code_challenge_method=S256";
    }

    public Task<Sys_User> HandleCallbackAsync(string code, string state, string redirectUri)
        => throw new NotImplementedException();   // T5 实现
}
