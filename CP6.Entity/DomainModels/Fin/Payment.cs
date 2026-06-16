using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 付款单（章03 §3②）。对供应商的付款；过账生成"借应付/贷银行"凭证（预付走"借预付/贷银行"）。
/// 经核销（<see cref="ApSettlement"/>）与应付发票多对多勾稽。全外币：<see cref="FxRate"/> 为付款日汇率，
/// 与发票记账汇率之差在核销时产生已实现汇兑损益。
/// </summary>
[Table("Fin_Payment")]
public class Payment : BaseTenantEntity
{
    /// <summary>付款单号（采番 PAY-yyyyMM-nnnn）</summary>
    [MaxLength(30)] public string No { get; set; } = string.Empty;

    /// <summary>供应商编码</summary>
    [Required, MaxLength(20)] public string SupplierId { get; set; } = string.Empty;

    /// <summary>付款日期</summary>
    public DateTime PayDate { get; set; }

    // —— 全外币 ——
    /// <summary>币种，null/JPY=本位币</summary>
    [MaxLength(3)] public string? CurrencyCd { get; set; }

    /// <summary>付款日汇率（外币1单位→JPY），本位币=1</summary>
    [Column(TypeName = "decimal(18,6)")] public decimal FxRate { get; set; } = 1m;

    /// <summary>付款金额（原币）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }

    /// <summary>已用于核销的金额（原币）。预付未核销部分挂预付账款</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SettledAmount { get; set; }

    /// <summary>付款方式</summary>
    public PaymentMethod Method { get; set; } = PaymentMethod.Transfer;

    /// <summary>银行账户 Id（决定贷方银行存款科目）</summary>
    public Guid BankAccountId { get; set; }

    /// <summary>是否预付款（借预付账款，不直接冲应付）</summary>
    public bool IsPrepayment { get; set; }

    /// <summary>状态</summary>
    public PaymentStatus Status { get; set; } = PaymentStatus.Posted;

    /// <summary>过账生成的凭证 Id</summary>
    public Guid? JournalEntryId { get; set; }
}

/// <summary>付款方式（章03）。</summary>
public enum PaymentMethod
{
    /// <summary>电汇/转账</summary>
    Transfer = 1,
    /// <summary>现金</summary>
    Cash = 2,
    /// <summary>票据</summary>
    Check = 3,
    /// <summary>其他</summary>
    Other = 9,
}

/// <summary>付款状态（章03）。付款即过账，无草稿；撤销走红冲。</summary>
public enum PaymentStatus
{
    /// <summary>已过账</summary>
    Posted = 1,
    /// <summary>已撤销（红冲）</summary>
    Reversed = 2,
}
