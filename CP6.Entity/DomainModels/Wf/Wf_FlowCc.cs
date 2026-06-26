using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>抄送（WFS 读模型）。节点/路徑/提交/结束抄送均落一行；IsRead 给信箱"未读"标记。</summary>
[Table("Wf_FlowCc")]
public class Wf_FlowCc : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid RecipientId { get; set; }
    [MaxLength(100)] public string? AtNodeId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
