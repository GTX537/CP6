using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 系统菜单/页面（MenuId 由用户自定义，不可重复）
/// </summary>
public class Sys_Menu
{
    /// <summary>
    /// 菜单/页面ID（自定义，不可重复）
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int MenuId { get; set; }

    /// <summary>
    /// 菜单名称
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string MenuName { get; set; } = string.Empty;

    /// <summary>
    /// 前端路由路径（如 /article）
    /// </summary>
    [MaxLength(200)]
    public string? RoutePath { get; set; }

    /// <summary>
    /// 稳定业务键（PUB 章02 权限资源键前缀，如 order）。
    /// 资源键 = MenuKey:ActionCode，不用易变的 MenuId/名称。唯一（过滤 null）。
    /// </summary>
    [MaxLength(100)]
    public string? MenuKey { get; set; }

    /// <summary>
    /// 图标
    /// </summary>
    [MaxLength(100)]
    public string? Icon { get; set; }

    /// <summary>
    /// 父级菜单ID（null 表示顶级菜单）
    /// </summary>
    public int? ParentId { get; set; }

    /// <summary>
    /// 排序号
    /// </summary>
    public int OrderNo { get; set; } = 0;

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateDate { get; set; } = DateTime.Now;
}
