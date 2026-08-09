using StackExchange.Redis;

namespace CP6.WebApi.Services;

public interface INativeSsoGrantCache
{
    Task SetAsync(
        string key,
        string value,
        TimeSpan lifetime,
        CancellationToken ct = default);

    Task<string?> GetAsync(
        string key,
        CancellationToken ct = default);

    Task<bool> RemoveIfValueMatchesAsync(
        string key,
        string expectedValue,
        CancellationToken ct = default);
}

public sealed class MemoryNativeSsoGrantCache : INativeSsoGrantCache
{
    private readonly object _sync = new();
    private readonly Dictionary<string, CacheEntry> _entries =
        new(StringComparer.Ordinal);

    public Task SetAsync(
        string key,
        string value,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            _entries[key] = new CacheEntry(
                value,
                DateTimeOffset.UtcNow.Add(lifetime));
        }
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(
        string key,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return Task.FromResult<string?>(null);
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key);
                return Task.FromResult<string?>(null);
            }
            return Task.FromResult<string?>(entry.Value);
        }
    }

    public Task<bool> RemoveIfValueMatchesAsync(
        string key,
        string expectedValue,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_sync)
        {
            if (!_entries.TryGetValue(key, out var entry))
                return Task.FromResult(false);
            if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                _entries.Remove(key);
                return Task.FromResult(false);
            }
            if (!string.Equals(
                    entry.Value,
                    expectedValue,
                    StringComparison.Ordinal))
            {
                return Task.FromResult(false);
            }

            _entries.Remove(key);
            return Task.FromResult(true);
        }
    }

    private sealed record CacheEntry(
        string Value,
        DateTimeOffset ExpiresAt);
}

public sealed class RedisNativeSsoGrantCache : INativeSsoGrantCache, IDisposable
{
    private readonly Lazy<IConnectionMultiplexer> _connection;
    private readonly string _keyPrefix;

    public RedisNativeSsoGrantCache(
        string configuration,
        string keyPrefix)
    {
        _connection = new Lazy<IConnectionMultiplexer>(
            () => ConnectionMultiplexer.Connect(configuration),
            LazyThreadSafetyMode.ExecutionAndPublication);
        _keyPrefix = keyPrefix;
    }

    public async Task SetAsync(
        string key,
        string value,
        TimeSpan lifetime,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await Database.StringSetAsync(Key(key), value, lifetime);
    }

    public async Task<string?> GetAsync(
        string key,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var value = await Database.StringGetAsync(Key(key));
        return value.HasValue ? value.ToString() : null;
    }

    public async Task<bool> RemoveIfValueMatchesAsync(
        string key,
        string expectedValue,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var transaction = Database.CreateTransaction();
        transaction.AddCondition(
            Condition.StringEqual(Key(key), expectedValue));
        var delete = transaction.KeyDeleteAsync(Key(key));
        var committed = await transaction.ExecuteAsync();
        return committed && await delete;
    }

    public void Dispose()
    {
        if (_connection.IsValueCreated)
            _connection.Value.Dispose();
    }

    private IDatabase Database => _connection.Value.GetDatabase();
    private RedisKey Key(string key) => $"{_keyPrefix}{key}";
}
