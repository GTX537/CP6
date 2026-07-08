using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 角色-菜单映射表（多对多关系）。
/// P0-T3 补口：Sys_Role 租户化后本表随之带 <see cref="TenantId"/>（否则 A 租户改 RoleId=1 的菜单
/// 会串改 B 租户同号角色的菜单可见性）。int 自增 Id 主键保留（无外部引用）；
/// 照 Sys_OperLog/Sys_Role 先例：CP6Context 手注册全局过滤 + StampTenant 手补。
/// </summary>
public class Sys_RoleMenu
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>租户 Id（行级隔离；写入时由 CP6Context.StampTenant 自动盖当前租户，查询时全局过滤）。</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// 角色ID
    /// </summary>
    public int RoleId { get; set; }

    /// <summary>
    /// 菜单ID（页面ID）
    /// </summary>
    public int MenuId { get; set; }
}
