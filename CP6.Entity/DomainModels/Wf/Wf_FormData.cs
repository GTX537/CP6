using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 表单数据（OA 章02）。一次提交一行，DataJson 存字段值快照；与流程实例经 BizId 关联
/// （阶段1 BizId 可空，阶段3 接业务后回填流程实例 Id / 业务单号）。
/// </summary>
// [审计豁免] 表单提交数据：运行时一次提交一行的字段值快照(DataJson)，改版不动旧数据本身即留痕，
// 非治理配置/权限授予面。不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_FormData")]
public class Wf_FormData : BaseTenantEntity
{
    /// <summary>Pinned immutable form definition. Null only for unrecoverable legacy rows.</summary>
    public Guid? FormDefVersionId { get; set; }

    /// <summary>对应表单定义 FormKey</summary>
    [Required, MaxLength(100)]
    public string FormKey { get; set; } = string.Empty;

    /// <summary>提交时的表单定义版本（留痕：改版不动旧数据）</summary>
    public int FormVersion { get; set; }

    /// <summary>Client idempotency token. Null is retained for legacy rows.</summary>
    [MaxLength(100)]
    public string? SubmissionKey { get; set; }

    [MaxLength(64)]
    public string? RequestHash { get; set; }

    public Guid? SubmittedBy { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }

    /// <summary>业务关联键（流程实例 Id / 业务单号；阶段1 可空）</summary>
    [MaxLength(100)]
    public string? BizId { get; set; }

    /// <summary>字段值快照 JSON</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string DataJson { get; set; } = "{}";

    [Timestamp]
    public byte[]? RowVersion { get; set; }
}
