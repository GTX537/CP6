using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Plan;

/// <summary>MRP 运算状态。</summary>
public enum MrpRunStatus
{
    /// <summary>运算中。</summary>
    Running = 0,
    /// <summary>已完成（计划订单/净需求已落库）。</summary>
    Completed = 1,
    /// <summary>失败（成环等）。</summary>
    Failed = 9,
}

/// <summary>
/// MRP 运算批次（MRP P1 · spec §3.2）。一次 regenerative 运算 = 一条 MrpRun，
/// 其下的计划订单/净需求明细以 MrpRunId 关联。
/// </summary>
[Table("Plan_MrpRun")]
public class Plan_MrpRun : BaseBizEntity
{
    /// <summary>运算批号（业务键，唯一）</summary>
    [Required, MaxLength(20)]
    public string RunNo { get; set; } = string.Empty;

    /// <summary>运算时刻</summary>
    public DateTime RunAt { get; set; }

    /// <summary>运算范围（JSON：品目/受注/日桶口径等，P1 留作审计追溯）</summary>
    public string? ScopeJson { get; set; }

    /// <summary>状态（<see cref="MrpRunStatus"/>）</summary>
    public int Status { get; set; } = (int)MrpRunStatus.Running;
}
