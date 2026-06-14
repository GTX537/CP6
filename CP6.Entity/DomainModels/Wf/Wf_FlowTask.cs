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

    /// <summary>任务状态：0=待办 1=同意 2=驳回 3=作废(节点已结/撤回时清在途)</summary>
    public int Status { get; set; }

    /// <summary>会签规则快照（建任务时从节点复制）：all/any/veto</summary>
    [MaxLength(20)]
    public string? Countersign { get; set; }

    /// <summary>处理意见</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
