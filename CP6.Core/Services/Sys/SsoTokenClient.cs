using System.Text.Json;
using CP6.Entity.DomainModels.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// <see cref="ISsoTokenClient"/> 真实现（S 类 #3 T5）。用命名 HttpClient "sso"（DI 配超时）POST
/// token_endpoint 完成 Authorization Code + PKCE 换码。非 2xx 或缺 id_token 一律返 null
/// （上层 → E-SEC-023），不抛异常以免泄露 IdP 端细节。
/// </summary>
public class SsoTokenClient : ISsoTokenClient
{
    private readonly IHttpClientFactory _f;

    public SsoTokenClient(IHttpClientFactory f) => _f = f;

    public async Task<string?> ExchangeCodeForIdTokenAsync(OidcEndpoints ep, Sys_TenantSsoConfig cfg, string clientSecret,
                                                           string code, string codeVerifier, string redirectUri)
    {
        var c = _f.CreateClient("sso");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,
            ["client_id"] = cfg.ClientId,
            ["client_secret"] = clientSecret,
            ["code_verifier"] = codeVerifier,
        });

        HttpResponseMessage resp;
        try
        {
            resp = await c.PostAsync(ep.TokenEndpoint, form);
        }
        catch
        {
            return null;   // 网络/超时 → 换码失败
        }

        if (!resp.IsSuccessStatusCode)
            return null;

        try
        {
            var json = await resp.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.TryGetProperty("id_token", out var idTok)
                ? idTok.GetString()
                : null;
        }
        catch
        {
            return null;   // 响应非 JSON / 解析失败
        }
    }
}
