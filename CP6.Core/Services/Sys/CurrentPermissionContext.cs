using System.Text.Json;
using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;

namespace CP6.Core.Services.Sys;

/// <summary>
/// Request permission context backed by IDistributedCache. Production uses Redis,
/// so role changes invalidate every API replica instead of one process only.
/// </summary>
public class CurrentPermissionContext : ICurrentPermissionContext
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);
    private static readonly DistributedCacheEntryOptions CacheOptions = new()
    {
        SlidingExpiration = TimeSpan.FromMinutes(30),
    };

    private readonly IHttpContextAccessor _http;
    private readonly IDistributedCache _cache;
    private readonly CP6Context _db;
    private readonly IPermissionAggregator _aggregator;

    public CurrentPermissionContext(
        IHttpContextAccessor http,
        IDistributedCache cache,
        CP6Context db,
        IPermissionAggregator aggregator)
    {
        _http = http;
        _cache = cache;
        _db = db;
        _aggregator = aggregator;
    }

    public async Task<UserPermissionContext> GetAsync()
    {
        var name = _http.HttpContext?.User?.Identity?.Name
                   ?? throw new InvalidOperationException("Not authenticated");
        var user = await _db.Sys_Users.FirstOrDefaultAsync(x => x.UserName == name)
                   ?? throw new InvalidOperationException("User does not exist");
        return await GetOrBuildAsync(user.Id);
    }

    public Task<UserPermissionContext> PrewarmAsync(Guid userId)
        => GetOrBuildAsync(userId);

    public void Invalidate(Guid userId)
        => _cache.Remove(CacheKey(userId));

    public void InvalidateByRole(int roleId)
    {
        var users = _db.Sys_UserRoles
            .Where(x => x.RoleId == roleId)
            .Select(x => x.UserId)
            .Union(_db.Sys_Users
                .Where(x => x.RoleId == roleId)
                .Select(x => x.Id))
            .Distinct()
            .ToList();
        foreach (var userId in users)
            _cache.Remove(CacheKey(userId));
    }

    private async Task<UserPermissionContext> GetOrBuildAsync(Guid userId)
    {
        var key = CacheKey(userId);
        var cached = await _cache.GetStringAsync(key);
        if (!string.IsNullOrWhiteSpace(cached))
        {
            var context = JsonSerializer.Deserialize<UserPermissionContext>(
                cached,
                JsonOptions);
            if (context is not null)
            {
                await _cache.RefreshAsync(key);
                return context;
            }
        }

        var built = await _aggregator.BuildAsync(userId);
        await _cache.SetStringAsync(
            key,
            JsonSerializer.Serialize(built, JsonOptions),
            CacheOptions);
        return built;
    }

    private static string CacheKey(Guid userId) => $"perm-ctx:{userId}";
}
