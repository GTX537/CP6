using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>资产处置单（出售/报废/转让/盘亏，spec §2.5）。</summary>
[Table("Fin_AssetDisposal")]
public class AssetDisposal : BaseTenantEntity
{
    [Required, MaxLength(30)] public string No { get; set; } = string.Empty;   // FAD-yyyy-MM-NNNNN
    public Guid AssetCardId { get; set; }
    public AssetDisposalType DisposalType { get; set; }
    public DateTime DisposalDate { get; set; }
    public Guid FiscalPeriodId { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal OriginalValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal AccumulatedDepreciation { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetBookValue { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal Proceeds { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal TaxAmount { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal DisposalExpense { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal NetGainLoss { get; set; }
    public Guid ClearingAccountId { get; set; }
    public Guid GainLossAccountId { get; set; }
    /// <summary>收/付款银行 GlAccountId（收款与清理费付款共用；Proceeds>0 或 Expense>0 时必填，否则 FA010）</summary>
    public Guid? ReceiptBankAccountId { get; set; }
    public AssetDisposalStatus Status { get; set; } = AssetDisposalStatus.Draft;
    /// <summary>Confirm 时快照卡片原状态，供反冲精确还原（§4.4）</summary>
    public AssetStatus? PriorStatus { get; set; }
    public Guid? JournalEntryId { get; set; }
    public Guid? FinalDeprecEntryId { get; set; }
    public DateTime? ConfirmedAt { get; set; }
    [MaxLength(100)] public string? ConfirmedBy { get; set; }
    public DateTime? ReversedAt { get; set; }
    [MaxLength(100)] public string? ReversedBy { get; set; }
    [MaxLength(500)] public string? Reason { get; set; }
    [Timestamp] public byte[]? RowVersion { get; set; }
}
