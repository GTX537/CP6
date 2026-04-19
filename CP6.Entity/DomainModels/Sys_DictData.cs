using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 数据字典项，挂在某个 DictType 下
/// </summary>
public class Sys_DictData
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// 所属字典类型编码
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string TypeCode { get; set; } = string.Empty;

    /// <summary>
    /// 字典值编码，如 1、male
    /// </summary>
    [MaxLength(100)]
    [Required]
    public string Value { get; set; } = string.Empty;

    /// <summary>
    /// 显示文本，如 男
    /// </summary>
    [MaxLength(200)]
    [Required]
    public string Label { get; set; } = string.Empty;

    public int OrderNo { get; set; } = 0;

    public bool Enable { get; set; } = true;

    public DateTime CreateDate { get; set; } = DateTime.Now;
}
