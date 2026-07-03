using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>服务任务异步停泊作业台账(§2.3)。
/// 每条记录 = 一个停泊等待执行/重试的服务任务 job。
/// 活跃 job(Status IN Pending/Running)靠 filtered unique index 保证同 token 同 node 至多一条。
/// 表名单数 Wf_ServiceJob,DbSet 复数 Wf_ServiceJobs(对齐全部 Wf_* 实体命名约定)。</summary>
[Table("Wf_ServiceJob")]
public class Wf_ServiceJob : BaseTenantEntity
{
    // TenantId 来自 BaseTenantEntity；Id/CreateDate/ModifyDate 来自 BaseEntity

    public Guid InstanceId { get; set; }           // FK→Wf_FlowInstance
    public Guid TokenId { get; set; }              // 要恢复的停泊 token
    [MaxLength(100)] public string NodeId { get; set; } = "";   // 服务任务节点 Id(与 Wf_FlowToken.NodeId / Wf_FlowInstance.CurrentNode 同口径)
    [MaxLength(50)]  public string Kind { get; set; } = "";     // dataWriteback/webApi/timer

    public string? ActionRefJson { get; set; }     // 固化动作绑定快照(§3.5),防流程定义漂移

    public DateTime DueAtUtc { get; set; }         // async 动作=入队时刻(UTC);timer=未来到期时刻(UTC)
    public int Status { get; set; }                // ServiceJobStatus(§2.4)

    // 尝试计数(P0-1):统一 sync 失败降级 / async 首执 / timer 到期执行
    public int AttemptCount { get; set; }          // 已实际执行次数
    public int MaxAttempts { get; set; }           // 最大总尝试次数(= node.ServiceMaxRetries + 1,默认 4)
    public DateTime NextAttemptAtUtc { get; set; } // 退避后下次可执行时刻(扫描门控,UTC)

    // 租约(P0-4):替代 ModifyDate 式 reaper,防多 worker 抢同一 job
    [MaxLength(200)] public string? LockedBy { get; set; }    // workerId
    public DateTime? LockedAtUtc { get; set; }
    public DateTime? LockExpiresAtUtc { get; set; }

    [MaxLength(1000)] public string? LastError { get; set; }  // 最后失败原因(截断 ≤1000 字)
    public DateTime? CompletedAtUtc { get; set; }             // 终态完成时刻(P2-4,排查/保留用)

    /// <summary>乐观并发令牌(P0-4),防多 worker 并发抢占同一 job。</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}
