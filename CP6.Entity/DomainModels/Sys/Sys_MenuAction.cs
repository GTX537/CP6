using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 菜单操作点（按钮/功能点）—— PUB 章02。
/// 定义某菜单下可被授权的操作，如 order 菜单的 query/add/edit/delete/export。
/// 资源键 = Sys_Menu.MenuKey + ":" + ActionCode。
/// </summary>
[Table("Sys_MenuAction")]
public class Sys_MenuAction : BaseTenantEntity
{
    /// <summary>所属菜单 → Sys_Menu.MenuId（int，B1-D1）</summary>
    public int MenuId { get; set; }

    /// <summary>操作码（如 query/add/edit/delete/export）</summary>
    [MaxLength(50)]
    public string ActionCode { get; set; } = "";

    /// <summary>操作名（显示用，如 导出）</summary>
    [MaxLength(100)]
    public string ActionName { get; set; } = "";

    /// <summary>排序号</summary>
    public int Sort { get; set; }
}
