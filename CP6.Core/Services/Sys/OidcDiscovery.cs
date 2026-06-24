using System.Collections.Concurrent;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

namespace CP6.Core.Services.Sys;

/// <summary>
/// OIDC discovery 真实现（S 类 #3 T4）。按 authority 缓存
/// <see cref="ConfigurationManager{OpenIdConnectConfiguration}"/>（内部带 TTL + 自动刷新）。
/// 拉取/解析 .well-known/openid-configuration 失败 → InvalidOperationException("E-SEC-028")。
/// </summary>
public class OidcDiscovery : IOidcDiscovery
{
    private static readonly ConcurrentDictionary<string, ConfigurationManager<OpenIdConnectConfiguration>> _managers
        = new();

    public async Task<OidcEndpoints> GetAsync(string authority)
    {
        var mgr = _managers.GetOrAdd(authority, a => new ConfigurationManager<OpenIdConnectConfiguration>(
            a.TrimEnd('/') + "/.well-known/openid-configuration",
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true }));

        OpenIdConnectConfiguration config;
        try
        {
            config = await mgr.GetConfigurationAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("E-SEC-028", ex);
        }

        return new OidcEndpoints(
            config.AuthorizationEndpoint,
            config.TokenEndpoint,
            config.Issuer,
            config.SigningKeys);
    }
}
