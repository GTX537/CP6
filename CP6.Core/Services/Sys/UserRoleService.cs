using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>用户-角色 分配服务实现 —— PUB 章01。</summary>
public class UserRoleService : IUserRoleService
{
    private readonly CP6Context _db;
    private readonly ICurrentPermissionContext _current;

    public UserRoleService(CP6Context db, ICurrentPermissionContext current)
    {
        _db = db;
        _current = current;
    }

    public async Task<UserRolesDto> GetAsync(Guid userId)
    {
        var roleIds = await _db.Sys_UserRoles
            .Where(ur => ur.UserId == userId)
            .Select(ur => ur.RoleId)
            .ToListAsync();
        var primary = (await _db.Sys_Users.FindAsync(userId))?.RoleId;
        if (primary is int p && !roleIds.Contains(p)) roleIds.Add(p);   // 主角色并入展示（口径同聚合）
        return new UserRolesDto
        {
            RoleIds = roleIds.Distinct().OrderBy(x => x).ToList(),
            PrimaryRoleId = primary
        };
    }

    public async Task SaveAsync(Guid userId, List<int> roleIds, int? primaryRoleId, string? @operator)
    {
        roleIds = (roleIds ?? new()).Distinct().ToList();
        if (primaryRoleId is int p && !roleIds.Contains(p))
            throw new InvalidOperationException("E-PUB-011");   // 主角色必须在角色集合内

        var user = await _db.Sys_Users.FindAsync(userId)
                   ?? throw new InvalidOperationException("E-PUB-404");

        var existing = await _db.Sys_UserRoles.Where(ur => ur.UserId == userId).ToListAsync();
        var existingIds = existing.Select(e => e.RoleId).ToHashSet();
        var target = roleIds.ToHashSet();

        _db.Sys_UserRoles.RemoveRange(existing.Where(e => !target.Contains(e.RoleId)));   // 删多余
        foreach (var rid in roleIds.Where(r => !existingIds.Contains(r)))                 // 增缺失
            _db.Sys_UserRoles.Add(new Sys_UserRole
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                RoleId = rid,
                Creator = @operator,
                CreateDate = DateTime.Now
            });

        user.RoleId = primaryRoleId;   // 写主角色
        user.Modifier = @operator;
        user.ModifyDate = DateTime.Now;

        await _db.SaveChangesAsync();
        _current.Invalidate(userId);   // 角色变更 → 失效该用户上下文缓存
    }

    public async Task<int> MigrateAsync()
    {
        var users = await _db.Sys_Users
            .Where(u => u.RoleId != null)
            .Select(u => new { u.Id, u.RoleId })
            .ToListAsync();
        var existing = (await _db.Sys_UserRoles
                .Select(ur => new { ur.UserId, ur.RoleId })
                .ToListAsync())
            .Select(e => (e.UserId, e.RoleId))
            .ToHashSet();

        var count = 0;
        foreach (var u in users)
        {
            var rid = u.RoleId!.Value;
            if (existing.Contains((u.Id, rid))) continue;   // 幂等
            _db.Sys_UserRoles.Add(new Sys_UserRole { Id = Guid.NewGuid(), UserId = u.Id, RoleId = rid });
            existing.Add((u.Id, rid));
            count++;
        }
        await _db.SaveChangesAsync();
        return count;
    }
}
