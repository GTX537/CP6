using CP6.Entity.DTOs.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>
/// 部门（组织树）服务 —— PUB 章00。物化路径维护 + 移动防成环 + 删除护栏 + 树查询。
/// </summary>
public interface IDeptService
{
    /// <summary>部门树（按 Sort 排序，含负责人名）</summary>
    Task<List<DeptTreeNode>> TreeAsync();

    /// <summary>新增部门（生成 Id 物化路径；DeptCode 唯一 E-PUB-001）。返回新部门 Id</summary>
    Task<Guid> CreateAsync(DeptDto dto, Guid? parentId, string? user);

    /// <summary>编辑部门（DeptCode 只读，改名称/负责人/排序/启用）</summary>
    Task UpdateAsync(Guid id, DeptDto dto, string? user);

    /// <summary>移动部门（重算自身及子孙 Path；防成环 E-PUB-004）</summary>
    Task MoveAsync(Guid id, Guid? newParentId, string? user);

    /// <summary>删除部门（有子部门 E-PUB-002 / 有在职用户 E-PUB-003）</summary>
    Task DeleteAsync(Guid id);

    /// <summary>设部门负责人</summary>
    Task SetLeaderAsync(Guid id, Guid? leaderId, string? user);

    /// <summary>维护用户组织字段（部门/上级/邮箱）</summary>
    Task SetUserOrgAsync(Guid userId, UserOrgDto dto, string? user);
}
