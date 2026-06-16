using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 审批绑定（OA 章05 §3）。业务类型 → 审批流程的映射：业务侧调 IApprovalService.SubmitAsync(bizType,…)
/// 时，按本表的 BizType 找到对应 FlowKey 起流程。一种业务类型一条启用绑定（UX 唯一索引）。
/// ConditionJson 预留"按表单字段选不同流程"（v1 直用 FlowKey，条件选流程后续增强）。
/// </summary>
[Table("Wf_ApprovalBinding")]
public class Wf_ApprovalBinding : BaseEntity
{
    /// <summary>业务类型标识（如 "FinJournalPost"、"PO"），与 IApprovalService/IApprovalCallback 对应</summary>
    [Required, MaxLength(50)]
    public string BizType { get; set; } = string.Empty;

    /// <summary>绑定的流程定义 FlowKey（Wf_FlowDef.FlowKey）</summary>
    [Required, MaxLength(100)]
    public string FlowKey { get; set; } = string.Empty;

    /// <summary>是否启用（停用后该业务类型不再走审批）</summary>
    public bool Enable { get; set; } = true;

    /// <summary>条件选流程（预留，v1 未用；将来按 formSnapshot 字段在多个 FlowKey 间择一）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string? ConditionJson { get; set; }

    /// <summary>备注</summary>
    [MaxLength(200)]
    public string? Remark { get; set; }
}
