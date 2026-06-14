using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 表单数据（OA 章02）。一次提交一行，DataJson 存字段值快照；与流程实例经 BizId 关联
/// （阶段1 BizId 可空，阶段3 接业务后回填流程实例 Id / 业务单号）。
/// </summary>
[Table("Wf_FormData")]
public class Wf_FormData : BaseEntity
{
    /// <summary>对应表单定义 FormKey</summary>
    [Required, MaxLength(100)]
    public string FormKey { get; set; } = string.Empty;

    /// <summary>提交时的表单定义版本（留痕：改版不动旧数据）</summary>
    public int FormVersion { get; set; }

    /// <summary>业务关联键（流程实例 Id / 业务单号；阶段1 可空）</summary>
    [MaxLength(100)]
    public string? BizId { get; set; }

    /// <summary>字段值快照 JSON</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string DataJson { get; set; } = "{}";
}
