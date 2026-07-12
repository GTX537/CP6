using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Wf;

/// <summary>
/// 流程令牌（WFS P1 运行时内核）。一个 token = 实例内一个活动执行点（停留节点）。
/// 单路径审批：一实例恒一 Active token；并行分叉：一实例多 Active token 并存。
/// 血缘：ParentTokenId 串嵌套层级；ForkId 标同批分叉（parallelJoin 靠"同 ForkId 计数"认亲）。
/// "实例进行中" = 存在 Active token（取代旧"CurrentNode 单值"判定）。
/// </summary>
// [审计豁免] 运行时执行点令牌：分叉/合流内核态(Status/StagePlanJson 高频翻转)，正确性由 FlowToken 内核测试锁定，
// 非治理配置/权限授予面。不贴 IAuditable，OawfAuditTests 负测试坐实零审计行。
[Table("Wf_FlowToken")]
public class Wf_FlowToken : BaseTenantEntity
{
    public Guid InstanceId { get; set; }

    [MaxLength(100)]
    public string NodeId { get; set; } = string.Empty;

    /// <summary>0=Active 1=Consumed 2=Cancelled。见 FlowTokenStatus。</summary>
    public int Status { get; set; }

    /// <summary>父令牌 Id（嵌套血缘）。根 token=null。</summary>
    public Guid? ParentTokenId { get; set; }

    /// <summary>分叉批次 Id（同批共享，join 认亲计数）。根/线性 token=null。</summary>
    public Guid? ForkId { get; set; }

    /// <summary>本 token 当前 approval 节点的冻结运行计划(RuntimeApprovalStage[] JSON)。进多档审批节点时算一次写入;
    /// 单档/非审批节点 = null。推进/退回基于它,不再每次现查 → 杜绝 managerChain 档位漂移。</summary>
    public string? StagePlanJson { get; set; }
}
