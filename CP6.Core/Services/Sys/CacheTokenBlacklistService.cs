using Microsoft.Extensions.Caching.Distributed;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 基于 <see cref="IDistributedCache"/> 的 jti 黑名单实现（S 类认证加固 T5）。
/// 已配 Redis 则用 Redis（多实例共享），否则进程内分布式缓存兜底（Program.cs §缓存注册）。
/// </summary>
public class CacheTokenBlacklistService : ITokenBlacklistService
{
    private readonly IDistributedCache _cache;
    public CacheTokenBlacklistService(IDistributedCache cache) => _cache = cache;

    private static string Key(string jti) => $"sec:bl:{jti}";

    public Task BlacklistAsync(string jti, TimeSpan ttl)
        => _cache.SetStringAsync(Key(jti), "1",
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = ttl });

    public async Task<bool> IsBlacklistedAsync(string jti)
        => await _cache.GetStringAsync(Key(jti)) != null;
}
