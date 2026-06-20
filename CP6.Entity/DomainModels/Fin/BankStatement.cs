using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>对账会话头（A4 · spec §2.1）。每账户每期一个会话；Locked* 仅 Lock 时写快照，Open 态实时重算。</summary>
[Table("Fin_BankStatement")]
public class BankStatement : BaseTenantEntity
{
    /// <summary>会话号 BKR-yyyy-MM-NNNNN（FinSequenceService key=BKR）</summary>
    [Required, MaxLength(30)] public string No { get; set; } = string.Empty;
    /// <summary>银行账户 → BankAccount.Id</summary>
    public Guid BankAccountId { get; set; }
    /// <summary>财务期间主键 → FiscalPeriod.Id（对齐 EnsureOpenAsync/结账）</summary>
    public Guid FiscalPeriodId { get; set; }
    /// <summary>期间起（冗余展示，由 FiscalPeriod 派生）</summary>
    public DateTime PeriodStart { get; set; }
    /// <summary>期间止（冗余展示）</summary>
    public DateTime PeriodEnd { get; set; }
    /// <summary>对账单日期（展示）</summary>
    public DateTime? StatementDate { get; set; }
    /// <summary>币种（取自 BankAccount，null=本位币）</summary>
    [MaxLength(3)] public string? CurrencyCd { get; set; }
    /// <summary>对账单期初余额</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal OpeningBalance { get; set; }
    /// <summary>对账单期末余额</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal ClosingBalance { get; set; }
    /// <summary>状态：Open=0 / Locked=1</summary>
    public BankStatementStatus Status { get; set; } = BankStatementStatus.Open;
    /// <summary>末次导入文件名</summary>
    [MaxLength(255)] public string? ImportFileName { get; set; }

    // ── 锁定快照（仅 Lock 成功时写；非 Open 态真相来源，spec §2.1/§7.1）──
    [Column(TypeName = "decimal(18,2)")] public decimal? LockedStatementInternalDiff { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? LockedReconciledDiff { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? LockedBankAdjustedBalance { get; set; }
    [Column(TypeName = "decimal(18,2)")] public decimal? LockedBookAdjustedBalance { get; set; }
    /// <summary>完整调节表 JSON 快照（审计追溯）</summary>
    public string? LockSnapshotJson { get; set; }
    public DateTime? LockedAt { get; set; }
    [MaxLength(100)] public string? LockedBy { get; set; }

    /// <summary>乐观并发（显式加，BaseTenantEntity 不带）</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}

/// <summary>会话状态</summary>
public enum BankStatementStatus { Open = 0, Locked = 1 }
