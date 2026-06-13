using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 权限聚合器实现 —— PUB 章01~04。
/// 菜单/操作并集、数据/字段最宽。<c>FillActionKeys/DataScopes/FieldPerms</c> 由 B/C/D 阶段填充，
/// 本阶段（A）先 no-op，保证地基可独立编译/测试。
/// </summary>
public class PermissionAggregator : IPermissionAggregator
{
    private readonly CP6Context _db;
    public PermissionAggregator(CP6Context db) => _db = db;

    public async Task<UserPermissionContext> BuildAsync(Guid userId)
    {
        var user = await _db.Sys_Users.FindAsync(userId)
                   ?? throw new InvalidOperationException("E-PUB-404");

        var roleIds = await GetAllRoleIdsAsync(userId);
        var ctx = new UserPermissionContext
        {
            UserId = userId,
            UserName = user.UserName,
            RoleIds = roleIds
        };

        // 章03 本部门 / 及下级 所需的部门定位
        if (user.DeptId is Guid did)
        {
            var dept = await _db.Sys_Depts.FindAsync(did);
            ctx.DeptId = did;
            ctx.DeptPath = dept?.Path ?? "";
        }

        // 菜单并集（章01 §9）：角色挂的菜单 → 取 MenuKey（过滤未配 key 的）
        ctx.MenuKeys = (await _db.Sys_RoleMenus
                .Where(rm => roleIds.Contains(rm.RoleId))
                .Join(_db.Sys_Menus, rm => rm.MenuId, m => m.MenuId, (rm, m) => m.MenuKey)
                .Where(k => k != null)
                .ToListAsync())
            .Select(k => k!)
            .ToHashSet();

        await FillActionKeysAsync(ctx, roleIds);   // 章02（B 阶段）
        await FillDataScopesAsync(ctx, roleIds);   // 章03（C 阶段）
        await FillFieldPermsAsync(ctx, roleIds);   // 章04（D 阶段）
        return ctx;
    }

    /// <summary>全部角色 = Sys_UserRole 附加角色 ∪ Sys_User.RoleId 主角色，去重。</summary>
    private async Task<List<int>> GetAllRoleIdsAsync(Guid userId)
    {
        var roles = await _db.Sys_UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        var primary = (await _db.Sys_Users.FindAsync(userId))?.RoleId;
        if (primary is int p && !roles.Contains(p)) roles.Add(p);
        return roles.Distinct().ToList();
    }

    // ───── 章02 操作权并集（B 阶段）─────
    private async Task FillActionKeysAsync(UserPermissionContext ctx, List<int> roleIds)
    {
        // "menuKey:actionCode" 并集；过滤未配 MenuKey 的菜单（否则会拼出脏键 ":action"）
        ctx.ActionKeys = (await (
                from ra in _db.Sys_RoleActions.Where(ra => roleIds.Contains(ra.RoleId))
                join m in _db.Sys_Menus on ra.MenuId equals m.MenuId
                where m.MenuKey != null
                select m.MenuKey + ":" + ra.ActionCode)
            .ToListAsync())
            .ToHashSet();
    }

    // ───── 章03 数据范围最宽聚合（C 阶段）─────
    private async Task FillDataScopesAsync(UserPermissionContext ctx, List<int> roleIds)
    {
        var rows = await _db.Sys_RoleDataScopes.Where(ds => roleIds.Contains(ds.RoleId)).ToListAsync();
        // 同资源多角色取 MAX ScopeType（最宽）
        ctx.DataScopes = rows.GroupBy(d => d.ResourceKey)
            .ToDictionary(g => g.Key, g => g.Max(x => x.ScopeType));
        // ScopeType=4 自定义部门：同资源并集
        ctx.CustomDeptIds = rows.Where(d => d.ScopeType == 4)
            .GroupBy(d => d.ResourceKey)
            .ToDictionary(g => g.Key, g => g.SelectMany(x => ParseGuids(x.CustomDeptIds)).Distinct().ToList());
    }

    private static IEnumerable<Guid> ParseGuids(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) yield break;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Guid.TryParse(part, out var g)) yield return g;
    }

    // ───── 章04 字段权限最宽聚合（D 阶段）─────
    private async Task FillFieldPermsAsync(UserPermissionContext ctx, List<int> roleIds)
    {
        // 同字段多角色取 MIN Access（最可见：1可读写 < 2只读 < 3隐藏）
        ctx.FieldPerms = (await _db.Sys_RoleFieldPerms.Where(fp => roleIds.Contains(fp.RoleId)).ToListAsync())
            .GroupBy(fp => fp.ResourceKey)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.FieldName).ToDictionary(fg => fg.Key, fg => fg.Min(x => x.Access)));
    }
}
