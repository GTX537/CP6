using CP6.Core.EFDbContext;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 权限聚合器实现 —— PUB 章01~04。
/// 从真实启用用户及存在且启用的角色读取；菜单/操作并集、数据范围MAX、字段存行MIN。
/// </summary>
public class PermissionAggregator : IPermissionAggregator
{
    private readonly CP6Context _db;
    public PermissionAggregator(CP6Context db) => _db = db;

    /// <summary>Compatibility alias for CRM: all permission reads now require enabled persisted roles.</summary>
    public Task<UserPermissionContext> BuildEnabledRolesAsync(Guid userId) => BuildAsync(userId);

    public Task<UserPermissionContext> BuildAsync(Guid userId) => BuildAsync(userId, CancellationToken.None);

    public async Task<UserPermissionContext> BuildAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (_db.Database.IsNpgsql())
            return await PostgreSqlPermissionReader.ReadAsync(_db, userId, cancellationToken);

        var user = await _db.Sys_Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == userId && u.Enable, cancellationToken)
                   ?? throw new CurrentPermissionUnavailableException();
        var assigned = await _db.Sys_UserRoles.AsNoTracking().Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId).ToListAsync(cancellationToken);
        if (user.RoleId is int primary) assigned.Add(primary);
        var roleIds = await _db.Sys_Roles.AsNoTracking().Where(r => r.Enable && assigned.Contains(r.RoleId))
            .Select(r => r.RoleId).ToListAsync(cancellationToken);
        var ctx = new UserPermissionContext
        {
            UserId = userId,
            UserName = user.UserName,
            RoleIds = roleIds
        };

        // 章03 本部门 / 及下级 所需的部门定位
        if (user.DeptId is Guid did)
        {
            var dept = await _db.Sys_Depts.AsNoTracking().SingleOrDefaultAsync(d => d.Id == did, cancellationToken);
            ctx.DeptId = did;
            ctx.DeptPath = dept?.Path ?? "";
        }

        // 菜单并集（章01 §9）：角色挂的菜单 → 取 MenuKey（过滤未配 key 的）
        ctx.MenuKeys = (await _db.Sys_RoleMenus.AsNoTracking()
                .Where(rm => roleIds.Contains(rm.RoleId))
                .Join(_db.Sys_Menus.AsNoTracking(), rm => rm.MenuId, m => m.MenuId, (rm, m) => m.MenuKey)
                .Where(k => k != null)
                .ToListAsync(cancellationToken))
            .Select(k => k!)
            .ToHashSet();

        await FillActionKeysAsync(ctx, roleIds, cancellationToken);   // 章02（B 阶段）
        await FillDataScopesAsync(ctx, roleIds, cancellationToken);   // 章03（C 阶段）
        await FillFieldPermsAsync(ctx, roleIds, cancellationToken);   // 章04（D 阶段）
        return ctx;
    }

    // ───── 章02 操作权并集（B 阶段）─────
    private async Task FillActionKeysAsync(UserPermissionContext ctx, List<int> roleIds, CancellationToken cancellationToken)
    {
        // "menuKey:actionCode" 并集；过滤未配 MenuKey 的菜单（否则会拼出脏键 ":action"）
        ctx.ActionKeys = (await (
                from ra in _db.Sys_RoleActions.AsNoTracking().Where(ra => roleIds.Contains(ra.RoleId))
                join m in _db.Sys_Menus.AsNoTracking() on ra.MenuId equals m.MenuId
                where m.MenuKey != null
                select m.MenuKey + ":" + ra.ActionCode)
            .ToListAsync(cancellationToken))
            .ToHashSet();
    }

    // ───── 章03 数据范围最宽聚合（C 阶段）─────
    private async Task FillDataScopesAsync(UserPermissionContext ctx, List<int> roleIds, CancellationToken cancellationToken)
    {
        var rows = await _db.Sys_RoleDataScopes.AsNoTracking().Where(ds => roleIds.Contains(ds.RoleId)).ToListAsync(cancellationToken);
        // 同资源多角色取 MAX ScopeType（最宽）
        ctx.DataScopes = rows.GroupBy(d => d.ResourceKey)
            .ToDictionary(g => g.Key, g => g.Max(x => x.ScopeType));
        // ScopeType=4 自定义部门：同资源并集
        ctx.CustomDeptIds = rows.Where(d => d.ScopeType == 4)
            .GroupBy(d => d.ResourceKey)
            .ToDictionary(g => g.Key, g => g.SelectMany(x => ParseGuids(x.CustomDeptIds)).Distinct().ToList());
    }

    internal static IEnumerable<Guid> ParseGuids(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) yield break;
        foreach (var part in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (Guid.TryParse(part, out var g)) yield return g;
    }

    // ───── 章04 字段权限最宽聚合（D 阶段）─────
    private async Task FillFieldPermsAsync(UserPermissionContext ctx, List<int> roleIds, CancellationToken cancellationToken)
    {
        // 同字段多角色取 MIN Access（最可见：1可读写 < 2只读 < 3隐藏）
        ctx.FieldPerms = (await _db.Sys_RoleFieldPerms.AsNoTracking().Where(fp => roleIds.Contains(fp.RoleId)).ToListAsync(cancellationToken))
            .GroupBy(fp => fp.ResourceKey)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(x => x.FieldName).ToDictionary(fg => fg.Key, fg => fg.Min(x => x.Access)));
    }
}
