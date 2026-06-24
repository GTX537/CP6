using System.Collections.Specialized;
using System.Web;
using CP6.Core.Services.Sys;
using CP6.Entity.DomainModels.Sys;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// T4(SSO) SsoService.BuildAuthorizeUrlAsync — discovery 接缝 + PKCE S256 + state 落库。
/// 真 SsoStateStore(MemoryDistributedCache) + 假 IOidcDiscovery + 假 ITenantSsoConfigService。
/// </summary>
public class SsoServiceAuthorizeTests
{
    private const string AuthEndpoint = "https://login.example.com/authorize";

    /// <summary>固定返回授权端点的假 discovery（不触真 IdP）。</summary>
    private sealed class FakeDiscovery : IOidcDiscovery
    {
        public Task<OidcEndpoints> GetAsync(string authority) =>
            Task.FromResult(new OidcEndpoints(
                AuthEndpoint,
                "https://login.example.com/token",
                authority,
                new List<SecurityKey>()));
    }

    /// <summary>InMemory 假配置服务：按 code 取出预置 cfg（可为 null / 未启用）。</summary>
    private sealed class FakeConfig : ITenantSsoConfigService
    {
        private readonly Sys_TenantSsoConfig? _cfg;
        public FakeConfig(Sys_TenantSsoConfig? cfg) => _cfg = cfg;

        public Task<Sys_TenantSsoConfig?> GetByTenantCodeAsync(string tenantCode) => Task.FromResult(_cfg);
        public Task<Sys_TenantSsoConfig?> GetByTenantIdAsync(Guid tenantId) => Task.FromResult(_cfg);
        public Task UpsertAsync(Guid tenantId, SsoConfigInput input) => Task.CompletedTask;
        public string DecryptClientSecret(Sys_TenantSsoConfig cfg) => "secret";
    }

    private static Sys_TenantSsoConfig Cfg(bool enabled = true) => new()
    {
        TenantId = Guid.NewGuid(),
        Authority = "https://login.example.com",
        ClientId = "cp6-sp",
        Scopes = "openid email profile",
        EmailClaim = "email",
        Enabled = enabled,
    };

    private static (SsoService svc, SsoStateStore store) Make(Sys_TenantSsoConfig? cfg)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var sec = Options.Create(new SecurityOptions());
        var store = new SsoStateStore(cache, sec);
        var svc = new SsoService(new FakeConfig(cfg), store, new FakeDiscovery());
        return (svc, store);
    }

    [Fact]
    public async Task Authorize_builds_url_with_pkce_and_stores_state()
    {
        var (svc, store) = Make(Cfg());

        var url = await svc.BuildAuthorizeUrlAsync("T01", "https://cp6.example.com/callback", "/home");

        Assert.StartsWith(AuthEndpoint + "?", url);
        var qs = HttpUtility.ParseQueryString(new Uri(url).Query);

        Assert.Equal("code", qs["response_type"]);
        Assert.Equal("cp6-sp", qs["client_id"]);
        Assert.Equal("https://cp6.example.com/callback", qs["redirect_uri"]);
        Assert.Equal("openid email profile", qs["scope"]);
        Assert.False(string.IsNullOrEmpty(qs["state"]));
        Assert.False(string.IsNullOrEmpty(qs["nonce"]));
        Assert.False(string.IsNullOrEmpty(qs["code_challenge"]));
        Assert.Equal("S256", qs["code_challenge_method"]);

        // state 可从 store 取出且 nonce/codeVerifier 非空
        var saved = store.Get(qs["state"]!);
        Assert.NotNull(saved);
        Assert.Equal(qs["nonce"], saved!.Nonce);
        Assert.False(string.IsNullOrEmpty(saved.CodeVerifier));
        Assert.Equal("/home", saved.ReturnUrl);
    }

    [Fact]
    public async Task Authorize_throws_when_not_configured()
    {
        var (svc, _) = Make(cfg: null);
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.BuildAuthorizeUrlAsync("T01", "https://cp6.example.com/callback", null));
        Assert.Equal("E-SEC-020", ex.Message);
    }

    [Fact]
    public async Task Authorize_throws_when_disabled()
    {
        var (svc, _) = Make(Cfg(enabled: false));
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.BuildAuthorizeUrlAsync("T01", "https://cp6.example.com/callback", null));
        Assert.Equal("E-SEC-020", ex.Message);
    }
}
