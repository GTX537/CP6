using CP6.Core.Services.Sys;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Xunit;

namespace CP6.Tests.Sys;

/// <summary>
/// 登出 jti 黑名单（S 类认证加固 T5）。基于 IDistributedCache，TTL 到期自动清除。
/// </summary>
public class TokenBlacklistServiceTests
{
    private static CacheTokenBlacklistService Make()
    {
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        return new CacheTokenBlacklistService(cache);
    }

    [Fact]
    public async Task Blacklisted_jti_is_detected()
    {
        var svc = Make();
        Assert.False(await svc.IsBlacklistedAsync("jti-1"));   // 未拉黑前不命中
        await svc.BlacklistAsync("jti-1", TimeSpan.FromMinutes(5));
        Assert.True(await svc.IsBlacklistedAsync("jti-1"));     // 拉黑后命中
    }

    [Fact]
    public async Task Unrelated_jti_is_not_blacklisted()
    {
        var svc = Make();
        await svc.BlacklistAsync("jti-1", TimeSpan.FromMinutes(5));
        Assert.False(await svc.IsBlacklistedAsync("jti-2"));    // 仅拉黑命中项，不误伤其它 jti
    }
}
