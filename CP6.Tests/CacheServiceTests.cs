namespace CP6.Tests;

/// <summary>
/// CacheService 单元测试
///
/// 面试要点：
/// 1. Arrange-Act-Assert (AAA) 模式：准备数据 → 执行操作 → 验证结果
/// 2. 测试 Cache-Aside 模式的三个核心行为：
///    - 首次查询 = 缓存未命中（调用 factory）
///    - 再次查询 = 缓存命中（不调用 factory）
///    - 删除缓存后 = 缓存未命中（重新调用 factory）
/// </summary>
public class CacheServiceTests
{
    [Fact]
    public async Task GetOrSetAsync_FirstCall_ShouldCallFactory()
    {
        // Arrange
        var cache = TestHelper.CreateCacheService();
        var callCount = 0;

        // Act：首次调用，缓存为空，应该执行 factory
        var result = await cache.GetOrSetAsync("test:key", async () =>
        {
            callCount++;
            return new List<string> { "A", "B", "C" };
        });

        // Assert
        Assert.Equal(3, result.Count);
        Assert.Equal(1, callCount); // factory 被调用了 1 次
    }

    [Fact]
    public async Task GetOrSetAsync_SecondCall_ShouldReturnCached()
    {
        // Arrange
        var cache = TestHelper.CreateCacheService();
        var callCount = 0;
        Func<Task<List<string>>> factory = async () =>
        {
            callCount++;
            return new List<string> { "X", "Y" };
        };

        // Act：第一次调用
        await cache.GetOrSetAsync("test:cached", factory);
        // Act：第二次调用（应该命中缓存）
        var result = await cache.GetOrSetAsync("test:cached", factory);

        // Assert
        Assert.Equal(2, result.Count);
        Assert.Equal(1, callCount); // factory 只被调用了 1 次（第二次走缓存）
    }

    [Fact]
    public async Task RemoveAsync_ShouldInvalidateCache()
    {
        // Arrange
        var cache = TestHelper.CreateCacheService();
        var callCount = 0;
        Func<Task<string>> factory = async () =>
        {
            callCount++;
            return $"value-{callCount}";
        };

        // Act：写入缓存
        var first = await cache.GetOrSetAsync("test:remove", factory);
        // Act：删除缓存
        await cache.RemoveAsync("test:remove");
        // Act：再次查询（缓存已清除，应该重新调用 factory）
        var second = await cache.GetOrSetAsync("test:remove", factory);

        // Assert
        Assert.Equal("value-1", first);
        Assert.Equal("value-2", second); // factory 被调用了第 2 次
        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task SetAsync_GetAsync_ShouldRoundTrip()
    {
        // Arrange
        var cache = TestHelper.CreateCacheService();
        var data = new TestDto("hello", 42);

        // Act
        await cache.SetAsync("test:dto", data);
        var result = await cache.GetAsync<TestDto>("test:dto");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("hello", result.Name);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task GetAsync_NonExistentKey_ShouldReturnNull()
    {
        // Arrange
        var cache = TestHelper.CreateCacheService();

        // Act
        var result = await cache.GetAsync<string>("non:existent");

        // Assert
        Assert.Null(result);
    }

    public record TestDto(string Name, int Value);
}
