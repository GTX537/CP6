using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Pur;

/// <summary>
/// 询价单头（RFQ，采购 章06 §2）。采购的**价格发现**机制——把同一批需求同时发给 N 家供应商、收回报价、横向比价、选定后回写价表。
/// 一头三身：询价头 + 询价行（买什么）+ 被邀供应商（问谁）+ 报价（各家答什么）。
/// </summary>
/// <remarks>
/// RFQ 不对供应商产生采购义务，仅"请报个价"；报价（<see cref="RfqQuote"/>）是要约，选中后才转 <see cref="PurchaseOrder"/>。
/// 上游：未定供应商的 PR 行（<see cref="PurchaseRequestLine.SuggestSupplierId"/> 为空）汇成 RFQ。
/// 多租户继承 <see cref="BaseBizEntity"/>。
/// </remarks>
[Table("Pur_Rfq")]
public class Rfq : BaseBizEntity
{
    /// <summary>询价单号（采番 "RFQ"+yyyyMMdd+流水，同租户唯一）</summary>
    [Required, MaxLength(20)]
    public string RfqNo { get; set; } = string.Empty;

    /// <summary>询价日期</summary>
    public DateTime RfqDate { get; set; }

    /// <summary>报价截止日（供应商报价的截止时点）</summary>
    public DateTime? DueDate { get; set; }

    /// <summary>状态（<see cref="RfqStatus"/>）：草稿/邀请中/报价中/已比价选定/已转PO/关闭/取消</summary>
    public RfqStatus Status { get; set; } = RfqStatus.Draft;

    /// <summary>询价员（用户标识；与审计 Creator 并存）</summary>
    [MaxLength(50)]
    public string? Buyer { get; set; }

    /// <summary>来源采购申请号（从哪张 PR 发起，章05 转来；行级追溯在 RfqLine）</summary>
    [MaxLength(20)]
    public string? SourcePrNo { get; set; }

    /// <summary>备注</summary>
    [MaxLength(500)]
    public string? Remarks { get; set; }

    /// <summary>询价行（业务键 RfqNo 关联，级联）</summary>
    public List<RfqLine> Lines { get; set; } = new();

    /// <summary>被邀供应商（业务键 RfqNo 关联，级联）</summary>
    public List<RfqSupplier> Suppliers { get; set; } = new();

    /// <summary>报价矩阵（业务键 RfqNo 关联，级联；(供应商 × 行)）</summary>
    public List<RfqQuote> Quotes { get; set; } = new();
}

/// <summary>询价单状态（采购 章06 §2/§3）。</summary>
public enum RfqStatus
{
    /// <summary>草稿（已建，未邀供应商）</summary>
    Draft = 0,
    /// <summary>邀请中（已邀供应商，待发出/收报价）</summary>
    Inviting = 1,
    /// <summary>报价中（已收到至少一家报价）</summary>
    Quoting = 2,
    /// <summary>已比价选定（章06 §4 选定后）</summary>
    Selected = 3,
    /// <summary>已转 PO（章06 §6 选中报价转出）</summary>
    Converted = 4,
    /// <summary>关闭</summary>
    Closed = 8,
    /// <summary>取消</summary>
    Cancelled = 9,
}
