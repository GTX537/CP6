using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Sys;

/// <summary>
/// 数据字典类型，如 gender、status、article_type
/// </summary>
public class Sys_DictType
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// 类型编码（唯一），如 gender、status
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>
    /// 类型名称，如 性别、状态
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string TypeName { get; set; } = string.Empty;

    public bool Enable { get; set; } = true;

    public int OrderNo { get; set; } = 0;

    public DateTime CreateDate { get; set; } = DateTime.Now;
}
