using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace CP6.Core.Utilities;

/// <summary>
/// 缓存服务 — 封装 IDistributedCache，提供强类型读写
///
/// 面试要点：
/// 1. IDistributedCache 是 .NET 的标准缓存接口
///    - 开发环境：用 AddDistributedMemoryCache()（内存缓存，零配置）
///    - 生产环境：用 AddStackExchangeRedisCache()（Redis，一行切换）
/// 2. Cache-Aside 模式：先查缓存 → 没有就查 DB → 写入缓存
/// 3. 缓存失效策略：写操作时主动清除相关缓存（RemoveAsync）
/// 4. 序列化：用 JSON 存储复杂对象，Redis 中存的是字符串
/// </summary>
public class CacheService
{
    private readonly IDistributedCache _cache;

    // 缓存 Key 前缀常量（方便管理和批量清除）
    public const string LangKeyPrefix = "lang:";    // lang:zh-CN, lang:en
    public const string DictKeyPrefix = "dict:";     // dict:gender, dict:article_type
    public const string DashboardKey = "dashboard:summary";

    public CacheService(IDistributedCache cache)
    {
        _cache = cache;
    }

    /// <summary>
    /// 获取缓存（泛型版），Cache-Aside 模式的 "查缓存" 步骤
    /// </summary>
    public async Task<T?> GetAsync<T>(string key) where T : class
    {
        var json = await _cache.GetStringAsync(key);
        if (json == null) return null;
        return JsonSerializer.Deserialize<T>(json);
    }

    /// <summary>
    /// 设置缓存，支持指定过期时间
    /// </summary>
    public async Task SetAsync<T>(string key, T value, TimeSpan? expiration = null)
    {
        var json = JsonSerializer.Serialize(value);
        var options = new DistributedCacheEntryOptions
        {
            // 滑动过期：最后一次访问后 N 时间过期
            SlidingExpiration = expiration ?? TimeSpan.FromMinutes(30)
        };
        await _cache.SetStringAsync(key, json, options);
    }

    /// <summary>
    /// 删除缓存（数据变更时调用）
    /// </summary>
    public async Task RemoveAsync(string key)
    {
        await _cache.RemoveAsync(key);
    }

    /// <summary>
    /// Cache-Aside 模式的完整实现：
    /// 1. 查缓存 → 有就直接返回
    /// 2. 没有 → 执行 factory 函数查 DB
    /// 3. 查到后写入缓存 → 返回
    /// </summary>
    public async Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null) where T : class
    {
        // Step 1: 查缓存
        var cached = await GetAsync<T>(key);
        if (cached != null) return cached;

        // Step 2: 缓存未命中，查数据库
        var data = await factory();

        // Step 3: 写入缓存
        await SetAsync(key, data, expiration);

        return data;
    }
}
