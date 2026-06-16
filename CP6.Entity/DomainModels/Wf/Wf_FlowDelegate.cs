using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 审批委派（OA 章07 §5）。委托人在有效期内把自己的审批权委派给代理人：流程引擎<b>建待办时</b>
/// 若原审批人正处委派期，则把 AssigneeId 替换为代理人，并在痕迹双记"代 X 审批"。
/// v1 仅"建待办时替换"（存量在途待办转派后续增强）；Scope 预留按流程限定（空=全部流程）。
/// </summary>
[Table("Wf_FlowDelegate")]
public class Wf_FlowDelegate : BaseTenantEntity
{
    /// <summary>委托人（原审批人）→ Sys_User.Id</summary>
    public Guid GrantorId { get; set; }

    /// <summary>代理人（受托人）→ Sys_User.Id</summary>
    public Guid DelegateId { get; set; }

    /// <summary>委派生效时间（含）</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>委派失效时间（含）</summary>
    public DateTime ValidTo { get; set; }

    /// <summary>是否启用（停用即时收回委派）</summary>
    public bool Enable { get; set; } = true;

    /// <summary>委派范围（预留：限定 FlowKey；空=全部流程）</summary>
    [MaxLength(100)]
    public string? Scope { get; set; }

    /// <summary>备注</summary>
    [MaxLength(200)]
    public string? Remark { get; set; }
}
