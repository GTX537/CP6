using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 角色-操作点 授权 —— PUB 章02。
/// 某角色对某菜单某操作的授权记录；多角色聚合时取 ActionKeys 并集。
/// </summary>
[Table("Sys_RoleAction")]
public class Sys_RoleAction : BaseTenantEntity, IAuditable
{
    /// <summary>角色 → Sys_Role.RoleId（int，B1-D1）</summary>
    public int RoleId { get; set; }

    /// <summary>菜单 → Sys_Menu.MenuId（int）</summary>
    public int MenuId { get; set; }

    /// <summary>操作码（→ Sys_MenuAction.ActionCode）</summary>
    [MaxLength(50)]
    public string ActionCode { get; set; } = "";
}
