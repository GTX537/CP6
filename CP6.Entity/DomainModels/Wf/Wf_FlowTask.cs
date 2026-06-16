using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程待办任务（OA 章03 §3）。一个节点可建多条（会签：多人各一条）。
/// Status!=0 即"已办"，是 ActAsync 幂等闸门的依据。
/// </summary>
[Table("Wf_FlowTask")]
public class Wf_FlowTask : BaseEntity
{
    /// <summary>所属流程实例 → Wf_FlowInstance.Id</summary>
    public Guid InstanceId { get; set; }

    /// <summary>所在节点 Id</summary>
    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>处理人 → Sys_User.Id</summary>
    public Guid AssigneeId { get; set; }

    /// <summary>任务状态：0=待办 1=同意 2=驳回 3=作废(节点已结/退回时清在途) 4=挂起(前加签时原审批人临时挂起)</summary>
    public int Status { get; set; }

    /// <summary>会签规则快照（建任务时从节点复制）：all/any/veto</summary>
    [MaxLength(20)]
    public string? Countersign { get; set; }

    /// <summary>处理意见</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }

    /// <summary>加签来源（章07 §3）：null=正常节点任务；before=前加签(先于原审批人)；after=后加签(后于原审批人)</summary>
    [MaxLength(20)]
    public string? AddSignSource { get; set; }

    /// <summary>到期时间（章07 §4 超时扫描依据；空=不限时）</summary>
    public DateTime? DueAt { get; set; }

    /// <summary>超时是否已处理（幂等：硬动作 approve/reject/escalate 处理过置 true，扫描不再处理）</summary>
    public bool TimeoutHandled { get; set; }
}
