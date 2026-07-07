using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Space;

/// <summary>
/// 编码规则（Space 章03 可配置编码引擎）。作用域 0租户默认/1楼层/2库区，就近覆盖优先。
/// Segments 存分段定义 JSON（章03 §3）。IsDefault 同作用域互斥。
/// </summary>
[Table("Space_CodeRule")]
public class Space_CodeRule : BaseBizEntity, IAuditable
{
    /// <summary>规则名称</summary>
    [Required, MaxLength(100)]
    public string RuleName { get; set; } = string.Empty;

    /// <summary>作用域类型：0租户默认 1楼层 2库区</summary>
    public int ScopeType { get; set; }

    /// <summary>作用域对象 Id（1→FloorId，2→ZoneId，0→null）</summary>
    public Guid? ScopeId { get; set; }

    /// <summary>分段定义 JSON（章03 §3）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string Segments { get; set; } = "[]";

    /// <summary>是否该作用域默认规则（同作用域互斥）</summary>
    public bool IsDefault { get; set; }
}
