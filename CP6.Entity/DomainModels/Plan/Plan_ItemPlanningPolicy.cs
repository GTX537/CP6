using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Plan;

/// <summary>批量规则（MRP P1 · spec §5.3）。</summary>
public enum PlanLotRule
{
    /// <summary>按需（lot-for-lot）：净需求即批量。</summary>
    LotForLot = 1,
    /// <summary>最小起订量（MOQ）：净需求向上取至 MoqQty。</summary>
    Moq = 2,
    /// <summary>订货倍数：净需求向上取至 MultipleQty 的整数倍。</summary>
    Multiple = 3,
    /// <summary>整卷取整：纸卷整卷供应（按 MultipleQty 取整，预留 P1 同倍数口径）。</summary>
    RoundRoll = 4,
}

/// <summary>自制/采购区分（MRP P1）。</summary>
public enum PlanMakeOrBuy
{
    /// <summary>自制：MRP 生成生产计划订单，提前期取路线汇总。</summary>
    Make = 1,
    /// <summary>采购：MRP 生成采购计划订单，提前期取 PurchaseLeadDays。</summary>
    Buy = 2,
}

/// <summary>
/// 计划主数据：品目计划策略（MRP P1 · spec §3.2）。
/// 安全库存 / 采购提前期 / 批量规则 / 自制采购，按品目维护；缺则引擎用默认（lot-for-lot + 0 提前期）。
/// </summary>
[Table("Plan_ItemPlanningPolicy")]
public class Plan_ItemPlanningPolicy : BaseBizEntity
{
    /// <summary>品目CD（业务键，唯一）</summary>
    [Required, MaxLength(20)]
    public string ItemCd { get; set; } = string.Empty;

    /// <summary>安全库存</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal SafetyStock { get; set; }

    /// <summary>采购提前期（日）——采购类品目用</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal PurchaseLeadDays { get; set; }

    /// <summary>批量规则（<see cref="PlanLotRule"/>）</summary>
    public int LotRule { get; set; } = (int)PlanLotRule.LotForLot;

    /// <summary>最小起订量（LotRule=Moq 时用）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? MoqQty { get; set; }

    /// <summary>订货倍数（LotRule=Multiple/RoundRoll 时用）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal? MultipleQty { get; set; }

    /// <summary>自制/采购（<see cref="PlanMakeOrBuy"/>）</summary>
    public int MakeOrBuy { get; set; } = (int)PlanMakeOrBuy.Buy;

    /// <summary>非持久化：本次为缺主数据时合成的默认策略（引擎据此告警）。</summary>
    [NotMapped]
    public bool IsDefault { get; set; }
}
