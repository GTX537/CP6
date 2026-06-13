using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 用户-角色 多对多中间表 —— PUB 章01 多角色 RBAC。
/// 一个用户可挂多个角色；权限按"操作并集、数据最宽、字段最宽"合并。
/// 主角色仍记在 <see cref="Sys_User.RoleId"/>（默认 / 显示用），此表存全部角色。
/// </summary>
[Table("Sys_UserRole")]
public class Sys_UserRole : BaseEntity   // Id GUID + Creator/CreateDate/Modifier/ModifyDate
{
    /// <summary>用户 → Sys_User.Id（Guid）</summary>
    public Guid UserId { get; set; }

    /// <summary>角色 → Sys_Role.RoleId（int，B1-D1：保留现状 int 键，不迁 GUID）</summary>
    public int RoleId { get; set; }
}
