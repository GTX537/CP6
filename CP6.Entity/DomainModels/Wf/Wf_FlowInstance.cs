using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程实例（OA 章03 §3）。状态机的"状态载体"：CurrentNode = 当前停留节点，Status = 实例总态。
/// VarsJson 存表单字段值快照（条件流转 ConditionEvaluator 的取值源）。全状态落库、幂等可重放。
/// </summary>
[Table("Wf_FlowInstance")]
public class Wf_FlowInstance : BaseEntity
{
    /// <summary>所属流程定义 FlowKey</summary>
    [Required, MaxLength(100)]
    public string FlowKey { get; set; } = string.Empty;

    /// <summary>业务类型（阶段2 接业务用；阶段1 可空）</summary>
    [MaxLength(50)]
    public string? BizType { get; set; }

    /// <summary>业务关联键（业务单号 / 表单数据 Id；阶段1 可空）</summary>
    [MaxLength(100)]
    public string? BizId { get; set; }

    /// <summary>当前节点 Id（状态机的"状态"）</summary>
    [MaxLength(100)]
    public string CurrentNode { get; set; } = string.Empty;

    /// <summary>实例状态：0=进行中 1=通过 2=驳回 3=撤回 4=挂起待指派</summary>
    public int Status { get; set; }

    /// <summary>流程变量（表单字段值快照，条件流转取值源）</summary>
    [Column(TypeName = "nvarchar(max)")]
    public string VarsJson { get; set; } = "{}";

    /// <summary>发起人 → Sys_User.Id</summary>
    public Guid StarterId { get; set; }
}
