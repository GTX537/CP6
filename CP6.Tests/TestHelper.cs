using CP6.Core.EFDbContext;
using CP6.Core.Services.Sys;
using CP6.Core.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CP6.Tests;

/// <summary>
/// 测试辅助类 — 创建 InMemory 数据库和 Mock 对象
///
/// 面试要点：
/// 1. InMemory DbContext：用内存数据库替代真实 DB，测试不依赖外部服务
/// 2. 每个测试用 Guid 作为数据库名，确保测试隔离（互不影响）
/// 3. CacheService 用 MemoryDistributedCache（内存版），行为和 Redis 一致
/// </summary>
public static class TestHelper
{
    /// <summary>
    /// 创建 InMemory DbContext（每次调用生成独立数据库）
    /// </summary>
    public static CP6Context CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        return new CP6Context(options);
    }

    /// <summary>
    /// 创建 InMemory DbContext，并注入假当前用户（字段审计 #4 测试用）
    /// </summary>
    public static CP6Context CreateInMemoryContext(ICurrentUserAccessor? user, ITenantContext? tenant = null)
    {
        var options = new DbContextOptionsBuilder<CP6Context>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new CP6Context(options, tenant, user);
    }

    /// <summary>
    /// 创建基于内存的 CacheService（不依赖 Redis）
    /// </summary>
    public static CacheService CreateCacheService()
    {
        var memoryCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        return new CacheService(memoryCache);
    }
}
