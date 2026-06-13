using CP6.Core.EFDbContext;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace CP6.Core.Services.Sys;

/// <summary>
/// <see cref="ICurrentPermissionContext"/> 实现 —— IMemoryCache（单机）缓存权限上下文。
/// PUB 章01 §8.2/8.3。多实例部署改 Redis（CP6 已有 CacheService/IDistributedCache 基建）。
/// </summary>
public class CurrentPermissionContext : ICurrentPermissionContext
{
    private readonly IHttpContextAccessor _http;
    private readonly IMemoryCache _cache;
    private readonly CP6Context _db;
    private readonly IPermissionAggregator _agg;

    public CurrentPermissionContext(IHttpContextAccessor http, IMemoryCache cache, CP6Context db, IPermissionAggregator agg)
    {
        _http = http;
        _cache = cache;
        _db = db;
        _agg = agg;
    }

    public async Task<UserPermissionContext> GetAsync()
    {
        var name = _http.HttpContext?.User?.Identity?.Name
                   ?? throw new InvalidOperationException("未登录");
        var user = await _db.Sys_Users.FirstOrDefaultAsync(u => u.UserName == name)
                   ?? throw new InvalidOperationException("用户不存在");

        return await _cache.GetOrCreateAsync(CacheKey(user.Id), async e =>
        {
            e.SlidingExpiration = TimeSpan.FromMinutes(30);
            return await _agg.BuildAsync(user.Id);
        }) ?? throw new InvalidOperationException("权限上下文构建失败");
    }

    public void Invalidate(Guid userId) => _cache.Remove(CacheKey(userId));

    public void InvalidateByRole(int roleId)
    {
        // 该角色全部用户 = Sys_UserRole.RoleId==roleId（附加角色）∪ Sys_User.RoleId==roleId（主角色）
        var users = _db.Sys_UserRoles.Where(ur => ur.RoleId == roleId).Select(ur => ur.UserId)
            .Union(_db.Sys_Users.Where(u => u.RoleId == roleId).Select(u => u.Id))
            .Distinct().ToList();
        foreach (var uid in users) _cache.Remove(CacheKey(uid));
    }

    private static string CacheKey(Guid uid) => $"perm-ctx:{uid}";
}
