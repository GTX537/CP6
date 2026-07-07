using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 模板（Space 章00 §4 / 章01 模板化生成）。货架/库区参数快照，批量生成的蓝本。
/// Params 存参数 JSON（快照，非活引用）。
/// </summary>
[Table("Space_Template")]
public class Space_Template : BaseBizEntity, IAuditable
{
    /// <summary>模板编码（租户内唯一）</summary>
    [Required, MaxLength(50)]
    public string TemplateCode { get; set; } = string.Empty;

    /// <summary>模板名称</summary>
    [Required, MaxLength(100)]
    public string TemplateName { get; set; } = string.Empty;

    /// <summary>模板类型：1货架 2库区</summary>
    public int TemplateType { get; set; } = 1;

    /// <summary>参数 JSON 快照（章01）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string Params { get; set; } = "{}";
}
