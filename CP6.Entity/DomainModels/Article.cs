using System.ComponentModel.DataAnnotations;

namespace CP6.Entity.DomainModels;

/// <summary>
/// 文章实体 - 我们的第一个业务表
/// </summary>
public class Article : BaseEntity
{
    /// <summary>
    /// 标题
    /// </summary>
    [MaxLength(200)]
    [Required]
    public string Title { get; set; } = string.Empty;

    /// <summary>
    /// 内容
    /// </summary>
    public string? Content { get; set; }

    /// <summary>
    /// 作者
    /// </summary>
    [MaxLength(100)]
    public string? Author { get; set; }

    /// <summary>
    /// 是否发布
    /// </summary>
    public bool IsPublished { get; set; } = false;
}
