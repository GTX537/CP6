using CP6.Core.Services.Sys;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

public class PendingTokenStoreTests
{
    private static PendingTokenStore Make()
    {
        IDistributedCache c = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new PendingTokenStore(c, Options.Create(new SecurityOptions()));
    }

    [Fact]
    public void Create_get_consume_is_one_time()
    {
        var s = Make();
        var uid = Guid.NewGuid();
        var tid = Guid.NewGuid();
        var jti = s.Create(uid, tid, "2fa_verify");

        var got = s.Get(jti);
        Assert.NotNull(got);
        Assert.Equal(uid, got!.Value.userId);
        Assert.Equal(tid, got.Value.tenantId);
        Assert.Equal("2fa_verify", got.Value.purpose);

        s.Consume(jti);
        Assert.Null(s.Get(jti)); // 一次性：消费后即不存在
    }

    [Fact]
    public void Get_empty_or_unknown_returns_null()
    {
        var s = Make();
        Assert.Null(s.Get(""));
        Assert.Null(s.Get(Guid.NewGuid().ToString("N")));
    }
}
