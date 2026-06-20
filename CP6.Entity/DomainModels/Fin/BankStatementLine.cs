using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>银行流水行（A4 · spec §2.2）。SignedAmount 由后端在 Amount/Direction 变化时统一物化，禁前端传入。</summary>
[Table("Fin_BankStatementLine")]
public class BankStatementLine : BaseTenantEntity
{
    public Guid StatementId { get; set; }
    public int LineNo { get; set; }
    /// <summary>交易/起息日</summary>
    public DateTime TxnDate { get; set; }
    /// <summary>方向：Deposit=1(入,↔银行GL借) / Withdrawal=2(出,↔银行GL贷)</summary>
    public BankLineDirection Direction { get; set; }
    /// <summary>金额（正数，方向由 Direction）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal Amount { get; set; }
    /// <summary>带符号金额（Deposit=+Amount，Withdrawal=−Amount）。后端物化，禁前端传入（spec §4.1）。</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal SignedAmount { get; private set; }
    /// <summary>统一重算带符号金额（Amount/Direction 任一变更后调用；唯一写入口）。</summary>
    public void RecomputeSigned() =>
        SignedAmount = Direction == BankLineDirection.Withdrawal ? -Amount : Amount;
    /// <summary>原币（外币账户），null=本位币</summary>
    [MaxLength(3)] public string? CurrencyCd { get; set; }
    [MaxLength(500)] public string? Description { get; set; }
    [MaxLength(200)] public string? CounterpartyName { get; set; }
    [MaxLength(100)] public string? RefNo { get; set; }
    /// <summary>流水余额（若文件有）</summary>
    [Column(TypeName = "decimal(18,2)")] public decimal? BalanceAfter { get; set; }
    /// <summary>来源：Imported=1 / Manual=2</summary>
    public BankLineSource Source { get; set; } = BankLineSource.Imported;
    /// <summary>匹配状态：Unmatched=0 / Matched=1 / MarkedPending=2</summary>
    public BankLineMatchStatus MatchStatus { get; set; } = BankLineMatchStatus.Unmatched;
    /// <summary>差异来源分类</summary>
    public BankLineCategory Category { get; set; } = BankLineCategory.None;
    /// <summary>匹配组 → BankReconMatch.Id（null=未匹配）</summary>
    public Guid? MatchGroupId { get; set; }
    /// <summary>单边项一键生成的当前有效 BankRecon 凭证（幂等键，spec §5.1）</summary>
    public Guid? GeneratedJournalEntryId { get; set; }
    public DateTime? GeneratedAt { get; set; }
    [MaxLength(100)] public string? GeneratedBy { get; set; }
    /// <summary>导入批次（追溯）</summary>
    [MaxLength(30)] public string? ImportBatchNo { get; set; }
    /// <summary>原始行 JSON（追溯）</summary>
    public string? RawRowJson { get; set; }
    /// <summary>原始行哈希（强重复判定）</summary>
    [MaxLength(64)] public string? RawRowHash { get; set; }
    /// <summary>去重指纹（spec §3.4）</summary>
    [MaxLength(128)] public string? Fingerprint { get; set; }

    /// <summary>乐观并发（撮合/改行核心实体）</summary>
    [Timestamp] public byte[]? RowVersion { get; set; }
}

public enum BankLineDirection { Deposit = 1, Withdrawal = 2 }
public enum BankLineSource { Imported = 1, Manual = 2 }
public enum BankLineMatchStatus { Unmatched = 0, Matched = 1, MarkedPending = 2 }
public enum BankLineCategory { None = 0, BankCharge = 1, InterestIncome = 2, Transfer = 3, Pending = 4, Other = 5 }
