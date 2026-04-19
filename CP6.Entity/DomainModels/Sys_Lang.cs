using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 多语言词条表，每行一个key，列对应各语言翻译
/// </summary>
public class Sys_Lang
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    /// <summary>
    /// 词条key，如 login.title、table.add
    /// </summary>
    [MaxLength(200)]
    [Required]
    public string LangKey { get; set; } = string.Empty;

    /// <summary>
    /// 简体中文
    /// </summary>
    [MaxLength(500)]
    public string? ZhCN { get; set; }

    /// <summary>
    /// 繁体中文
    /// </summary>
    [MaxLength(500)]
    public string? ZhTW { get; set; }

    /// <summary>
    /// 英语
    /// </summary>
    [MaxLength(500)]
    public string? En { get; set; }

    /// <summary>
    /// 日语
    /// </summary>
    [MaxLength(500)]
    public string? Ja { get; set; }

    /// <summary>
    /// 韩语
    /// </summary>
    [MaxLength(500)]
    public string? Ko { get; set; }
}
