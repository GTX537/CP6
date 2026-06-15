using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 收款单（章04 §3，镜像 Payment）。客户回款；过账生成"借银行/贷应收"凭证（预收走"借银行/贷预收账款"）。
/// 经 ArSettlement 与应收发票多对多核销。全外币：FxRate 为收款日汇率，与发票汇率之差产生已实现汇兑损益。
/// </summary>
[Table("Fin_Receipt")]
public class Receipt : BaseEntity
{
    /// <summary>收款单号（采番 RCP-yyyyMM-nnnn）</summary>
    [MaxLength(30)] public string No { get; set; } = string.Empty;

    /// <summary>客户编码</summary>
    [Required, MaxLength(20)] public string CustomerId { get; set; } = string.Empty;

    /// <summary>收款日期</summary>
    public DateTime ReceiptDate { get; set; }

    [MaxLength(3)] public string? CurrencyCd { get; set; }
    [Column(TypeName = "decimal(18,6)")] public decimal FxRate { get; set; } = 1m;

    /// <summary>收款金额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    /// <summary>已用于核销的金额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SettledAmount { get; set; }

    /// <summary>收款方式</summary>
    public PaymentMethod Method { get; set; } = PaymentMethod.Transfer;

    /// <summary>收款银行账户 Id</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>是否预收款（借银行/贷预收账款，不冲应收）</summary>
    public bool IsAdvance { get; set; }

    public ReceiptStatus Status { get; set; } = ReceiptStatus.Posted;
    public Guid? JournalEntryId { get; set; }
}

/// <summary>收款状态（章04，镜像 PaymentStatus）。</summary>
public enum ReceiptStatus
{
    Posted = 1,
    Reversed = 2,
}
