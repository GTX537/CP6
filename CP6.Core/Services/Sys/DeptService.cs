using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Sys;
using CP6.Entity.DTOs.Sys;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 部门服务实现（PUB 章00）。物化路径 /{rootId}/.../{selfId}/：
/// "本部门及下级"子树 = 一句 Path.StartsWith(前缀)，查询层零递归；代价是移动时重算子孙 Path（低频）。
/// </summary>
public class DeptService : IDeptService
{
    private readonly CP6Context _db;
    public DeptService(CP6Context db) => _db = db;

    public async Task<Guid> CreateAsync(DeptDto d, Guid? parentId, string? user)
    {
        if (await _db.Sys_Depts.AnyAsync(x => x.DeptCode == d.DeptCode))
            throw new InvalidOperationException("E-PUB-001");   // 部门编码已存在

        var id = Guid.NewGuid();                                // EF Core sentinel：客户端非默认 Guid 即用之
        var parentPath = "/";
        if (parentId is Guid pid)
        {
            var parent = await _db.Sys_Depts.FindAsync(pid)
                         ?? throw new InvalidOperationException($"上级部门不存在: {pid}");
            parentPath = parent.Path;
        }

        var e = new Sys_Dept
        {
            Id = id,
            ParentId = parentId,
            DeptCode = d.DeptCode,
            DeptName = d.DeptName,
            Path = $"{parentPath}{id}/",
            LeaderId = d.LeaderId,
            Sort = d.Sort,
            Enable = d.Enable,
            Creator = user,
            CreateDate = DateTime.Now,
        };
        _db.Sys_Depts.Add(e);
        await _db.SaveChangesAsync();
        return id;
    }

    public async Task UpdateAsync(Guid id, DeptDto d, string? user)
    {
        var e = await _db.Sys_Depts.FindAsync(id)
                ?? throw new InvalidOperationException("E-PUB-001");
        // DeptCode 只读（建后不可改，被引用）；改名称/负责人/排序/启用
        e.DeptName = d.DeptName;
        e.LeaderId = d.LeaderId;
        e.Sort = d.Sort;
        e.Enable = d.Enable;
        e.Modifier = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task MoveAsync(Guid id, Guid? newParentId, string? user)
    {
        var dept = await _db.Sys_Depts.FindAsync(id)
                   ?? throw new InvalidOperationException("E-PUB-001");

        var newParentPath = "/";
        if (newParentId is Guid pid)
        {
            var np = await _db.Sys_Depts.FindAsync(pid)
                     ?? throw new InvalidOperationException($"上级部门不存在: {pid}");
            // 防成环：目标不能是自身或自身子孙（其 Path 不能以本部门 Path 为前缀）
            if (np.Path.StartsWith(dept.Path))
                throw new InvalidOperationException("E-PUB-004");
            newParentPath = np.Path;
        }

        var oldPrefix = dept.Path;
        var newPrefix = $"{newParentPath}{id}/";

        // 重算自身及全部子孙：旧前缀 → 新前缀（子孙整体平移）
        var subtree = await _db.Sys_Depts.Where(x => x.Path.StartsWith(oldPrefix)).ToListAsync();
        foreach (var s in subtree)
            s.Path = newPrefix + s.Path.Substring(oldPrefix.Length);

        dept.ParentId = newParentId;
        dept.Modifier = user;
        dept.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task DeleteAsync(Guid id)
    {
        if (await _db.Sys_Depts.AnyAsync(x => x.ParentId == id))
            throw new InvalidOperationException("E-PUB-002");   // 有子部门
        if (await _db.Sys_Users.AnyAsync(x => x.DeptId == id))
            throw new InvalidOperationException("E-PUB-003");   // 有在职用户

        var e = await _db.Sys_Depts.FindAsync(id);
        if (e != null)
        {
            _db.Sys_Depts.Remove(e);
            await _db.SaveChangesAsync();
        }
    }

    public async Task SetLeaderAsync(Guid id, Guid? leaderId, string? user)
    {
        var e = await _db.Sys_Depts.FindAsync(id)
                ?? throw new InvalidOperationException("E-PUB-001");
        e.LeaderId = leaderId;
        e.Modifier = user;
        e.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task SetUserOrgAsync(Guid userId, UserOrgDto d, string? user)
    {
        var u = await _db.Sys_Users.FindAsync(userId)
                ?? throw new InvalidOperationException("用户不存在");
        u.DeptId = d.DeptId;
        u.ManagerId = d.ManagerId;
        u.Email = d.Email;
        u.Modifier = user;
        u.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }

    public async Task<List<DeptTreeNode>> TreeAsync()
    {
        var all = await _db.Sys_Depts.OrderBy(x => x.Sort).ToListAsync();

        var leaderIds = all.Where(d => d.LeaderId != null).Select(d => d.LeaderId!.Value).Distinct().ToList();
        var leaders = await _db.Sys_Users
            .Where(u => leaderIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.NickName ?? u.UserName);

        var nodes = all.ToDictionary(d => d.Id, d => new DeptTreeNode
        {
            Id = d.Id,
            ParentId = d.ParentId,
            DeptCode = d.DeptCode,
            DeptName = d.DeptName,
            LeaderId = d.LeaderId,
            LeaderName = d.LeaderId is Guid lid && leaders.TryGetValue(lid, out var n) ? n : null,
            Sort = d.Sort,
            Enable = d.Enable,
        });

        var roots = new List<DeptTreeNode>();
        foreach (var node in nodes.Values)
        {
            if (node.ParentId is Guid pid && nodes.TryGetValue(pid, out var parent))
                parent.Children.Add(node);
            else
                roots.Add(node);
        }
        return roots;
    }
}
