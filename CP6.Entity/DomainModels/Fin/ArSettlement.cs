using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 应收核销（章04 §3，镜像 ApSettlement）。收款与应收发票多对多勾稽；尾差（现金折扣/已实现汇兑损益）
/// 写一张差额冲销凭证。复用 <see cref="SettlementDiffType"/>。
/// </summary>
[Table("Fin_ArSettlement")]
public class ArSettlement : BaseTenantEntity
{
    /// <summary>收款单 Id</summary>
    public Guid ReceiptId { get; set; }

    /// <summary>应收发票 Id</summary>
    public Guid ArInvoiceId { get; set; }

    /// <summary>本次核销金额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SettledAmount { get; set; }

    /// <summary>差额（本位币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal DiffAmount { get; set; }

    public SettlementDiffType DiffType { get; set; } = SettlementDiffType.None;
    public Guid? DiffAccountId { get; set; }
    public Guid? DiffJournalEntryId { get; set; }
}
