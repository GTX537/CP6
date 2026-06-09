using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 系统角色（RoleId 由用户自定义，不可重复）
/// </summary>
public class Sys_Role
{
    /// <summary>
    /// 角色ID（自定义，不可重复）
    /// </summary>
    [Key]
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
