using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>抄送（WFS 读模型）。节点/路徑/提交/结束抄送均落一行；IsRead 给信箱"未读"标记。</summary>
// [审计豁免] 抄送运行时读模型：随流转落行、IsRead/ReadAt 高频翻转，非治理配置/权限授予面。
// 不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_FlowCc")]
public class Wf_FlowCc : BaseTenantEntity
{
    public Guid InstanceId { get; set; }
    public Guid RecipientId { get; set; }
    [MaxLength(100)] public string? AtNodeId { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
}
