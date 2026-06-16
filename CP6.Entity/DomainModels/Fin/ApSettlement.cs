using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 应付核销（章03 §3③/§4）。付款与应付发票的多对多勾稽：一笔付款可核销多张发票，一张发票可被多笔付款核销。
/// 核销本身不产新凭证（勾稽关系），但尾差（现金折扣/舍入/已实现汇兑损益）写一张差额冲销凭证。
/// </summary>
[Table("Fin_ApSettlement")]
public class ApSettlement : BaseTenantEntity
{
    /// <summary>付款单 Id</summary>
    public Guid PaymentId { get; set; }

    /// <summary>应付发票 Id</summary>
    public Guid ApInvoiceId { get; set; }

    /// <summary>本次核销金额（原币，冲减发票欠款）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SettledAmount { get; set; }

    /// <summary>差额（本位币，写冲销凭证用；现金折扣为原币折本位币，汇差为本位币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal DiffAmount { get; set; }

    /// <summary>差额类型</summary>
    public SettlementDiffType DiffType { get; set; } = SettlementDiffType.None;

    /// <summary>差额冲销科目 Id（有差额时必填：财务费用/汇兑损益等）</summary>
    public Guid? DiffAccountId { get; set; }

    /// <summary>差额冲销生成的凭证 Id</summary>
    public Guid? DiffJournalEntryId { get; set; }
}

/// <summary>核销尾差类型（章03 §4）。</summary>
public enum SettlementDiffType
{
    /// <summary>无差额</summary>
    None = 0,
    /// <summary>现金折扣（提前付款折让）</summary>
    CashDiscount = 1,
    /// <summary>舍入差</summary>
    Rounding = 2,
    /// <summary>已实现汇兑损益（发票汇率 vs 付款汇率，F2-D1）</summary>
    FxDiff = 3,
}
