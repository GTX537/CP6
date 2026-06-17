using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Plan;

/// <summary>钉住（Pegging）需求来源类型。</summary>
public enum PeggingSourceType
{
    /// <summary>受注（独立需求）。</summary>
    Order = 1,
    /// <summary>MPS（主生产计划，P3）。</summary>
    Mps = 2,
    /// <summary>上级计划订单（相关需求，自制半成品/原料展开）。</summary>
    ParentPlannedOrder = 3,
}

/// <summary>
/// 钉住关系（MRP P1 · spec §3.2）。把计划订单回溯到其需求来源（受注/上级计划订单），
/// 供"为什么要这张计划订单"的全链追溯，转单时一路传给下游。
/// </summary>
[Table("Plan_Pegging")]
public class Plan_Pegging : BaseBizEntity
{
    /// <summary>计划订单（FK → Plan_PlannedOrder.Id）</summary>
    public Guid PlannedOrderId { get; set; }

    /// <summary>来源类型（<see cref="PeggingSourceType"/>）</summary>
    public int SourceType { get; set; }

    /// <summary>来源单号（受注号 / 上级计划订单 ItemCd 等）</summary>
    [MaxLength(40)]
    public string? SourceRefNo { get; set; }

    /// <summary>该来源贡献的需求量</summary>
    [Column(TypeName = "decimal(21,8)")]
    public decimal Qty { get; set; }
}
