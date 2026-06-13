using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>角色授权配置实现 —— PUB 章02。</summary>
public class RolePermService : IRolePermService
{
    private readonly CP6Context _db;
    private readonly ICurrentPermissionContext _current;

    public RolePermService(CP6Context db, ICurrentPermissionContext current)
    {
        _db = db;
        _current = current;
    }

    public async Task<List<MenuActionDto>> GetMenuActionsAsync(int menuId) =>
        await _db.Sys_MenuActions
            .Where(a => a.MenuId == menuId)
            .OrderBy(a => a.Sort)
            .Select(a => new MenuActionDto { ActionCode = a.ActionCode, ActionName = a.ActionName, Sort = a.Sort })
            .ToListAsync();

    public async Task<List<MenuActionFullDto>> GetAllMenuActionsAsync() =>
        await _db.Sys_MenuActions
            .OrderBy(a => a.MenuId).ThenBy(a => a.Sort)
            .Select(a => new MenuActionFullDto { MenuId = a.MenuId, ActionCode = a.ActionCode, ActionName = a.ActionName, Sort = a.Sort })
            .ToListAsync();

    public async Task SaveMenuActionsAsync(int menuId, List<MenuActionDto> actions, string? @operator)
    {
        actions ??= new();
        var existing = await _db.Sys_MenuActions.Where(a => a.MenuId == menuId).ToListAsync();
        var targetCodes = actions.Select(a => a.ActionCode).ToHashSet();

        _db.Sys_MenuActions.RemoveRange(existing.Where(a => !targetCodes.Contains(a.ActionCode)));   // 删多余

        foreach (var a in actions)
        {
            var cur = existing.FirstOrDefault(x => x.ActionCode == a.ActionCode);
            if (cur == null)
                _db.Sys_MenuActions.Add(new Sys_MenuAction
                {
                    Id = Guid.NewGuid(),
                    MenuId = menuId,
                    ActionCode = a.ActionCode,
                    ActionName = a.ActionName,
                    Sort = a.Sort,
                    Creator = @operator,
                    CreateDate = DateTime.Now
                });
            else
            {
                cur.ActionName = a.ActionName;   // 改名/改序
                cur.Sort = a.Sort;
                cur.Modifier = @operator;
                cur.ModifyDate = DateTime.Now;
            }
        }
        await _db.SaveChangesAsync();
    }

    public async Task<RolePermDto> GetRolePermAsync(int roleId) => new()
    {
        MenuIds = await _db.Sys_RoleMenus.Where(rm => rm.RoleId == roleId).Select(rm => rm.MenuId).ToListAsync(),
        Actions = await _db.Sys_RoleActions.Where(ra => ra.RoleId == roleId)
            .Select(ra => new RoleActionItem { MenuId = ra.MenuId, ActionCode = ra.ActionCode }).ToListAsync()
    };

    public async Task SaveRolePermAsync(int roleId, RolePermDto dto, string? @operator)
    {
        var menuIds = (dto.MenuIds ?? new()).Distinct().ToList();
        var menuSet = menuIds.ToHashSet();
        var actions = dto.Actions ?? new();

        // 操作点必须属于已授菜单
        if (actions.Any(a => !menuSet.Contains(a.MenuId)))
            throw new InvalidOperationException("E-PUB-021");

        // diff RoleMenu
        var existMenus = await _db.Sys_RoleMenus.Where(rm => rm.RoleId == roleId).ToListAsync();
        var existMenuIds = existMenus.Select(m => m.MenuId).ToHashSet();
        _db.Sys_RoleMenus.RemoveRange(existMenus.Where(m => !menuSet.Contains(m.MenuId)));
        foreach (var mid in menuIds.Where(m => !existMenuIds.Contains(m)))
            _db.Sys_RoleMenus.Add(new Sys_RoleMenu { RoleId = roleId, MenuId = mid });

        // diff RoleAction（业务键 = MenuId + ActionCode）
        var existActions = await _db.Sys_RoleActions.Where(ra => ra.RoleId == roleId).ToListAsync();
        var targetKeys = actions.Select(a => (a.MenuId, a.ActionCode)).ToHashSet();
        var existKeys = existActions.Select(a => (a.MenuId, a.ActionCode)).ToHashSet();
        _db.Sys_RoleActions.RemoveRange(existActions.Where(a => !targetKeys.Contains((a.MenuId, a.ActionCode))));
        foreach (var a in actions.Where(a => !existKeys.Contains((a.MenuId, a.ActionCode))))
            _db.Sys_RoleActions.Add(new Sys_RoleAction
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                MenuId = a.MenuId,
                ActionCode = a.ActionCode,
                Creator = @operator,
                CreateDate = DateTime.Now
            });

        await _db.SaveChangesAsync();
        _current.InvalidateByRole(roleId);   // 角色权限变更 → 失效该角色全部用户缓存
    }

    public async Task<List<string>> MyActionsAsync() =>
        (await _current.GetAsync()).ActionKeys.OrderBy(k => k).ToList();

    // ───── 章03 数据权限配置 ─────

    public List<DataScopeResourceDto> GetDataScopeResources() =>
        DataScopeRegistry.All
            .Select(r => new DataScopeResourceDto { Key = r.Key, Name = r.Name, Supports = r.Supports, Default = r.Default })
            .OrderBy(r => r.Key)
            .ToList();

    public async Task<List<RoleDataScopeDto>> GetRoleDataScopesAsync(int roleId) =>
        (await _db.Sys_RoleDataScopes.Where(d => d.RoleId == roleId).ToListAsync())
        .Select(d => new RoleDataScopeDto
        {
            ResourceKey = d.ResourceKey,
            ScopeType = d.ScopeType,
            CustomDeptIds = ParseGuids(d.CustomDeptIds)
        })
        .ToList();

    public async Task SaveRoleDataScopesAsync(int roleId, List<RoleDataScopeDto> items, string? @operator)
    {
        items ??= new();
        foreach (var it in items)
        {
            if (!DataScopeRegistry.Supports(it.ResourceKey, it.ScopeType))
                throw new InvalidOperationException("E-PUB-031");   // 范围类型不被该资源支持
            if (it.ScopeType == 4 && (it.CustomDeptIds == null || it.CustomDeptIds.Count == 0))
                throw new InvalidOperationException("E-PUB-032");   // 自定义范围必须选部门
        }

        // 全量替换该角色的数据范围
        var existing = await _db.Sys_RoleDataScopes.Where(d => d.RoleId == roleId).ToListAsync();
        _db.Sys_RoleDataScopes.RemoveRange(existing);
        foreach (var it in items)
            _db.Sys_RoleDataScopes.Add(new Sys_RoleDataScope
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                ResourceKey = it.ResourceKey,
                ScopeType = it.ScopeType,
                CustomDeptIds = it.ScopeType == 4 ? string.Join(",", it.CustomDeptIds) : null,
                Creator = @operator,
                CreateDate = DateTime.Now
            });

        await _db.SaveChangesAsync();
        _current.InvalidateByRole(roleId);
    }

    private static List<Guid> ParseGuids(string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv)) return new();
        return csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => Guid.TryParse(p, out var g) ? g : (Guid?)null)
            .Where(g => g.HasValue).Select(g => g!.Value).ToList();
    }

    // ───── 章04 字段权限配置 ─────

    public List<FieldResourceDto> GetFieldResources() =>
        FieldRegistry.Resources
            .Select(key => new FieldResourceDto
            {
                Key = key,
                Fields = FieldRegistry.Fields(key).Select(f => new FieldDefDto { Name = f.Name, Label = f.Label }).ToList()
            })
            .OrderBy(r => r.Key)
            .ToList();

    public async Task<List<RoleFieldPermDto>> GetRoleFieldPermsAsync(int roleId) =>
        await _db.Sys_RoleFieldPerms.Where(f => f.RoleId == roleId)
            .Select(f => new RoleFieldPermDto { ResourceKey = f.ResourceKey, FieldName = f.FieldName, Access = f.Access })
            .ToListAsync();

    public async Task SaveRoleFieldPermsAsync(int roleId, List<RoleFieldPermDto> items, string? @operator)
    {
        items ??= new();
        foreach (var it in items)
            if (!FieldRegistry.Has(it.ResourceKey, it.FieldName))
                throw new InvalidOperationException("E-PUB-041");   // 字段不在注册表内

        // 仅替换本次涉及的资源（前端按资源逐个保存，不能误删其它资源配置）；可读写(1)=默认不落库
        var resourceKeys = items.Select(i => i.ResourceKey).Distinct().ToList();
        var existing = await _db.Sys_RoleFieldPerms
            .Where(f => f.RoleId == roleId && resourceKeys.Contains(f.ResourceKey)).ToListAsync();
        _db.Sys_RoleFieldPerms.RemoveRange(existing);
        foreach (var it in items.Where(x => x.Access != 1))
            _db.Sys_RoleFieldPerms.Add(new Sys_RoleFieldPerm
            {
                Id = Guid.NewGuid(),
                RoleId = roleId,
                ResourceKey = it.ResourceKey,
                FieldName = it.FieldName,
                Access = it.Access,
                Creator = @operator,
                CreateDate = DateTime.Now
            });

        await _db.SaveChangesAsync();
        _current.InvalidateByRole(roleId);
    }

    public async Task<List<string>> MyReadonlyAsync(string resourceKey)
    {
        var ctx = await _current.GetAsync();
        if (!ctx.FieldPerms.TryGetValue(resourceKey, out var fields)) return new();
        return fields.Where(kv => kv.Value == 2).Select(kv => kv.Key).OrderBy(k => k).ToList();
    }
}
