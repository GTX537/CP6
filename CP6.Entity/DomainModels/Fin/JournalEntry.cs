using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 记账凭证头（章01 §4）。头-行结构，整张借方合计必须等于贷方合计（铁律①）。
/// 凭证不可改不可删，只能红冲（铁律②）——故无 Update/Delete，状态机走 Post/Reverse。
/// 手工凭证强制 maker-checker（过账人≠制单人）；自动凭证可信直过（AutoPosted）。
/// </summary>
[Table("Fin_JournalEntry")]
public class JournalEntry : BaseTenantEntity
{
    /// <summary>凭证号，如 "GL-2026-06-00012"，按期间采番（FinSequence）</summary>
    [MaxLength(30)]
    public string No { get; set; } = string.Empty;

    /// <summary>记账日期（决定落在哪个会计期间）</summary>
    public DateTime VoucherDate { get; set; }

    /// <summary>所属会计期间（章02）</summary>
    public Guid PeriodId { get; set; }

    /// <summary>来源：手工/AP/AR/成本/结转/红冲</summary>
    public VoucherSource Source { get; set; }

    /// <summary>来源单据号（如 AP 发票号），便于追溯</summary>
    [MaxLength(50)]
    public string? SourceDocNo { get; set; }

    /// <summary>状态机：草稿/待复核/已过账/已驳回/已红冲</summary>
    public JournalStatus Status { get; set; }

    /// <summary>摘要</summary>
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    // —— maker-checker（手工凭证强制双人复核）——
    /// <summary>制单人</summary>
    [MaxLength(100)]
    public string MakerId { get; set; } = string.Empty;

    /// <summary>制单时间</summary>
    public DateTime MakerAt { get; set; }

    /// <summary>过账人（过账时填，且必须 ≠ MakerId）</summary>
    [MaxLength(100)]
    public string? CheckerId { get; set; }

    /// <summary>过账时间</summary>
    public DateTime? CheckerAt { get; set; }

    /// <summary>驳回原因</summary>
    [MaxLength(500)]
    public string? RejectReason { get; set; }

    /// <summary>自动凭证可信直过（源头业务已审批，CheckerId 记 "SYSTEM"）</summary>
    public bool AutoPosted { get; set; }

    // —— 红冲（凭证不可改不可删，只能红冲）——
    /// <summary>本凭证被哪张红冲凭证冲掉</summary>
    public Guid? ReversedById { get; set; }

    /// <summary>本凭证是哪张原凭证的红冲</summary>
    public Guid? ReverseOfId { get; set; }

    /// <summary>分录行</summary>
    public List<JournalLine> Lines { get; set; } = new();
}

/// <summary>凭证来源</summary>
public enum VoucherSource
{
    /// <summary>手工（强制双人复核）</summary>
    Manual = 0,
    /// <summary>应付 AP 自动凭证</summary>
    AP = 1,
    /// <summary>应收 AR 自动凭证</summary>
    AR = 2,
    /// <summary>成本结转</summary>
    Cost = 3,
    /// <summary>期末/年结结转</summary>
    Carryover = 4,
    /// <summary>红冲</summary>
    Reversal = 5,
    /// <summary>期末未实现汇兑重估（含下期初冲回）</summary>
    FxReval = 6,
    /// <summary>A4 银行对账单边项自动凭证</summary>
    BankRecon = 7,
    /// <summary>A3 月末折旧 / 处置月补提折旧汇总凭证</summary>
    Depreciation = 8,
    /// <summary>A3 资产处置结转凭证</summary>
    AssetDisposal = 9,
    /// <summary>波A 库存移动自动凭证（入库暂估/盘盈/盘亏/报废）</summary>
    Inventory = 10,
}

/// <summary>凭证状态机</summary>
public enum JournalStatus
{
    /// <summary>草稿</summary>
    Draft = 0,
    /// <summary>待复核</summary>
    PendingReview = 1,
    /// <summary>已过账（冻结，只能红冲）</summary>
    Posted = 2,
    /// <summary>已驳回</summary>
    Rejected = 3,
    /// <summary>已红冲</summary>
    Reversed = 4,
}
