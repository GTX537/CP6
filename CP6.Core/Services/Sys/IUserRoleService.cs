using CP6.Entity.DTOs.Sys;

namespace CP6.Core.Services.Sys;

/// <summary>用户-角色 分配服务 —— PUB 章01 §5/§8。</summary>
public interface IUserRoleService
{
    /// <summary>取某用户的全部角色 + 主角色。</summary>
    Task<UserRolesDto> GetAsync(Guid userId);

    /// <summary>保存某用户的角色集合 + 主角色（diff 增删中间表，写主角色，失效缓存）。
    /// 主角色须 ∈ roleIds，否则抛 E-PUB-011。</summary>
    Task SaveAsync(Guid userId, List<int> roleIds, int? primaryRoleId, string? @operator);

    /// <summary>历史数据迁移：把现有 Sys_User.RoleId 幂等写入 Sys_UserRole。返回新增行数。</summary>
    Task<int> MigrateAsync();
}
