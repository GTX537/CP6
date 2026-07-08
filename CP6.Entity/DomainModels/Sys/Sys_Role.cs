using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 系统角色（P0-T3 租户化：复合主键 (TenantId, RoleId)，每租户独立角色集）。
/// RoleId 由用户自定义、在租户内不可重复；跨租户可同号（如各租户皆有 RoleId=1 管理员）。
/// int Id 用户自定义主键，故不继承 <see cref="CP6.Entity.BaseTenantEntity"/>（会引入冲突的 Guid Id 主键
/// 与审计列漂移）；照 Sys_OperLog 先例：显式带 <see cref="TenantId"/> 列 + CP6Context 手注册全局过滤/盖章。
/// </summary>
public class Sys_Role : IAuditable
{
    /// <summary>
    /// 租户 Id（行级隔离硬墙；复合主键第一段。写入时由 CP6Context.StampTenant 自动盖当前租户，查询时全局过滤）。
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// 角色ID（用户自定义，租户内不可重复；复合主键第二段）
    /// </summary>
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int RoleId { get; set; }

    /// <summary>
    /// 角色名称
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string RoleName { get; set; } = string.Empty;

    /// <summary>
    /// 角色描述
    /// </summary>
    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// 是否启用
    /// </summary>
    public bool Enable { get; set; } = true;

    /// <summary>
    /// 排序号
    /// </summary>
    public int OrderNo { get; set; } = 0;

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateDate { get; set; } = DateTime.Now;
}
