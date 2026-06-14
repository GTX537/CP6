using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CP6.Entity.DomainModels.Fin;

/// <summary>
/// 会计期间（章02 §1）。月结粒度=年+月。凭证按 VoucherDate 归期；报表按期出、结账后锁死。
/// 财年可 ≠ 日历年（FiscalYearStartMonth 可配，日本 4 月起）：FiscalYear/PeriodNo 按财年口径汇总，
/// Year/Month 保留日历口径。
/// </summary>
[Table("Fin_FiscalPeriod")]
public class FiscalPeriod : BaseEntity
{
    /// <summary>财年（可 ≠ 日历年）</summary>
    public int FiscalYear { get; set; }

    /// <summary>日历年</summary>
    public int Year { get; set; }

    /// <summary>日历月 1..12</summary>
    public int Month { get; set; }

    /// <summary>财年内第几期（1..12）</summary>
    public int PeriodNo { get; set; }

    /// <summary>期间起日（含），算期初余额的分界</summary>
    public DateTime PeriodStart { get; set; }

    /// <summary>期间止日（含）</summary>
    public DateTime PeriodEnd { get; set; }

    /// <summary>状态：Open / Closed</summary>
    public PeriodStatus Status { get; set; }

    /// <summary>结账时间</summary>
    public DateTime? ClosedAt { get; set; }

    /// <summary>结账人</summary>
    [MaxLength(100)]
    public string? ClosedBy { get; set; }
}

/// <summary>会计期间状态</summary>
public enum PeriodStatus
{
    /// <summary>开启（可记账）</summary>
    Open = 0,
    /// <summary>已结账（锁期，禁记账）</summary>
    Closed = 1,
}
