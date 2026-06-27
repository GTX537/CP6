using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Oa;

/// <summary>用户 Id → 显示名（NickName ?? UserName）批量解析。信箱多服务共用（DRY）。租户自动隔离。</summary>
public static class OaUserNames
{
    public static async Task<Dictionary<Guid, string>> ResolveAsync(CP6Context db, IEnumerable<Guid> ids)
    {
        var set = ids.Where(i => i != Guid.Empty).Distinct().ToList();
        if (set.Count == 0) return new();
        return await db.Sys_Users
            .Where(u => set.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.NickName) ? u.UserName : u.NickName!);
    }
}
