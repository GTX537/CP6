using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程审批痕迹（OA 章03 §3）。每次动作（提交/同意/驳回/撤回/挂起…）追加一条，构成审批时间线。
/// 仅追加、不更新，CreateDate(BaseEntity) = 动作发生时刻。
/// </summary>
[Table("Wf_FlowHistory")]
public class Wf_FlowHistory : BaseEntity
{
    /// <summary>所属流程实例 → Wf_FlowInstance.Id</summary>
    public Guid InstanceId { get; set; }

    /// <summary>动作所在节点 Id</summary>
    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>动作执行人 → Sys_User.Id</summary>
    public Guid ActorId { get; set; }

    /// <summary>动作：submit/approve/reject/withdraw/suspend/…</summary>
    [Required, MaxLength(50)]
    public string Action { get; set; } = string.Empty;

    /// <summary>意见</summary>
    [MaxLength(1000)]
    public string? Comment { get; set; }
}
