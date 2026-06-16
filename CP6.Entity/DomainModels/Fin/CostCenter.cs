using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 成本中心（分析性会计维度）。章01 §4.1.1。不影响借贷恒等，但让费用能按
/// 机台/工序/部门切分（"哪台印刷机最烧钱"）——纸箱厂成本会计的差异化卖点（06 章重度使用）。
/// </summary>
/// <remarks>
/// MVP 阶段凭证行可先不填 CostCenterId，但维度现在就占位——几万行凭证落地后再加要回填历史，极贵。
/// 机台型成本中心可 <see cref="LinkMachineId"/> 直接挂 MES 的 Machine，OEE/停机与成本归集天然对齐。
/// </remarks>
[Table("Fin_CostCenter")]
public class CostCenter : BaseTenantEntity
{
    /// <summary>编码，如 "PRT-01"（印刷机1号）。唯一</summary>
    [Required, MaxLength(30)]
    public string Code { get; set; } = string.Empty;

    /// <summary>名称</summary>
    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    /// <summary>类型：机台/工序/部门</summary>
    public CostCenterType Type { get; set; }

    /// <summary>树形：部门 &gt; 工序 &gt; 机台</summary>
    public Guid? ParentId { get; set; }

    /// <summary>可关联 MES 的 Machine（机台直接对上，复用便宜）</summary>
    [MaxLength(50)]
    public string? LinkMachineId { get; set; }

    /// <summary>停用</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>成本中心类型</summary>
public enum CostCenterType
{
    /// <summary>部门</summary>
    Department = 1,
    /// <summary>工序</summary>
    Process = 2,
    /// <summary>机台</summary>
    Machine = 3,
}
