using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity;

/// <summary>
/// 所有实体的公共基类，包含每张表都需要的公共字段
/// </summary>
public abstract class BaseEntity
{
    /// <summary>
    /// 主键，使用 Guid 自动生成
    /// </summary>
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public Guid Id { get; set; }

    /// <summary>
    /// 创建人
    /// </summary>
    [MaxLength(100)]
    public string? Creator { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreateDate { get; set; } = DateTime.Now;

    /// <summary>
    /// 修改人
    /// </summary>
    [MaxLength(100)]
    public string? Modifier { get; set; }

    /// <summary>
    /// 修改时间
    /// </summary>
    public DateTime? ModifyDate { get; set; }
}
