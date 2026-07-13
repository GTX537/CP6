using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>触发流水 = 审计台账 + 幂等闸（D7）。占坑行：InstanceId==null && Error==null。</summary>
[Table("Wf_TriggerFire")]
public class Wf_TriggerFire : BaseTenantEntity
{
    public Guid TriggerId { get; set; }

    /// <summary>复合唯一索引（TenantId,TriggerId,IdempotencyKey）＝幂等闸权威判据；键非空必填，无需 filtered（D7）</summary>
    [MaxLength(200)] public string IdempotencyKey { get; set; } = "";

    public DateTime FiredUtc { get; set; }

    /// <summary>成功发起的流程实例；null=占坑未完成或失败</summary>
    public Guid? InstanceId { get; set; }

    /// <summary>同 WfTriggerType（冗余便查）</summary>
    public int Source { get; set; }

    /// <summary>发起失败原因（结构化码+detail）</summary>
    [MaxLength(1000)] public string? Error { get; set; }

    /// <summary>message/event 负载 SHA-256（审计，不存原文）</summary>
    [MaxLength(64)] public string? PayloadHash { get; set; }
}
