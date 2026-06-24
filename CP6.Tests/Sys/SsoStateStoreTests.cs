using CP6.Core.Services.Sys;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>T3(SSO) ISsoStateStore — state/nonce/PKCE 一次性，IDistributedCache 后端。</summary>
public class SsoStateStoreTests
{
    private static (SsoStateStore store, IDistributedCache cache) Make(int stateMinutes = 10)
    {
        var cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var sec = Options.Create(new SecurityOptions { Sso = new SsoOptions { StateMinutes = stateMinutes } });
        return (new SsoStateStore(cache, sec), cache);
    }

    [Fact]
    public void Create_get_consume_is_one_time()
    {
        var (store, _) = Make();
        var tid = Guid.NewGuid();

        var state = store.Create(tid, "nonce-1", "verifier-1", "/home");
        Assert.False(string.IsNullOrEmpty(state));

        var got = store.Get(state);
        Assert.NotNull(got);
        Assert.Equal(tid, got!.TenantId);
        Assert.Equal("nonce-1", got.Nonce);
        Assert.Equal("verifier-1", got.CodeVerifier);
        Assert.Equal("/home", got.ReturnUrl);

        store.Consume(state);
        Assert.Null(store.Get(state));   // 一次性
    }

    [Fact]
    public void Get_returns_null_for_unknown_state()
    {
        var (store, _) = Make();
        Assert.Null(store.Get("does-not-exist"));
    }

    [Fact]
    public void Returns_null_returnUrl_when_not_supplied()
    {
        var (store, _) = Make();
        var state = store.Create(Guid.NewGuid(), "n", "v", returnUrl: null);
        Assert.Null(store.Get(state)!.ReturnUrl);
    }

    [Fact]
    public void Distinct_calls_produce_distinct_states()
    {
        var (store, _) = Make();
        var s1 = store.Create(Guid.NewGuid(), "n1", "v1", null);
        var s2 = store.Create(Guid.NewGuid(), "n2", "v2", null);
        Assert.NotEqual(s1, s2);
    }
}
