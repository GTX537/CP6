using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Plan;

/// <summary>计划订单类型。</summary>
public enum PlannedOrderType
{
    /// <summary>采购（转 采购PR）。</summary>
    Purchase = 1,
    /// <summary>生产（转 MES 工单）。</summary>
    Production = 2,
}

/// <summary>计划订单状态（贯穿供给判定/复算存活/转单，spec §5.1/§5.2/§9）。</summary>
public enum PlannedOrderStatus
{
    /// <summary>建议（MRP 生成；复算时作废重生；不计入供给）。</summary>
    Suggested = 0,
    /// <summary>已确认（人确认；复算保留；计入 scheduled receipt 供给）。</summary>
    Confirmed = 1,
    /// <summary>已转单（已生成 PR/工单；保留；计入供给）。</summary>
    Converted = 2,
    /// <summary>已忽略（人工放弃；不计供给）。</summary>
    Ignored = 9,
}

/// <summary>
/// 计划订单（MRP P1 · spec §3.2）。MRP 净需求 &gt; 0 时按批量规则生成的供给建议，
/// 人确认后转 采购PR / MES 工单。
/// </summary>
[Table("Plan_PlannedOrder")]
public class Plan_PlannedOrder : BaseBizEntity
{
    /// <summary>所属运算批次（FK → Plan_MrpRun.Id）</summary>
    public Guid MrpRunId { get; set; }

    /// <summary>类型（<see cref="PlannedOrderType"/>，按 MakeOrBuy）</summary>
    public int Type { get; set; }

    /// <summary>品目CD</summary>
    [Required, MaxLength(20)]
    public string ItemCd { get; set; } = string.Empty;

    /// <summary>计划数量（经批量规则定批后）</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }

    /// <summary>需求日（到货/完工日，落桶日）</summary>
    public DateTime RequiredDate { get; set; }

    /// <summary>下达日 = 需求日 − 提前期</summary>
    public DateTime ReleaseDate { get; set; }

    /// <summary>状态（<see cref="PlannedOrderStatus"/>）</summary>
    public int Status { get; set; } = (int)PlannedOrderStatus.Suggested;

    /// <summary>转单后回填的下游单号（PR 号 / 工单号）</summary>
    [MaxLength(20)]
    public string? ConvertedDocNo { get; set; }
}
