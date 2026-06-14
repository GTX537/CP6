using CP6.Entity.DomainModels.Fin;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 会计期间服务（章02 §1）。期间生成/归期/锁期判定。财年起始月可配（FiscalYearStartMonth）。
/// </summary>
public interface IFiscalPeriodService
{
    /// <summary>纯算：日历(年,月) → (财年, 财年内期次)，按 FiscalYearStartMonth。</summary>
    (int FiscalYear, int PeriodNo) ComputeFiscal(int year, int month);

    /// <summary>按记账日期解析期间（不创建，不存在返回 null）。</summary>
    Task<FiscalPeriod?> ResolveAsync(DateTime date);

    /// <summary>按记账日期取期间，不存在则创建为 Open。</summary>
    Task<FiscalPeriod> EnsureOpenAsync(DateTime date, string? user = null);

    /// <summary>按(年,月)取期间（含跨年滚动归一），不存在则创建为 Open。</summary>
    Task<FiscalPeriod> EnsureOpenAsync(int year, int month, string? user = null);

    /// <summary>期间是否 Open（过账/结账前判定）。</summary>
    Task<bool> IsOpenAsync(Guid periodId);

    /// <summary>上一日历月期间（不跳月结账判定用）；不存在返回 null。</summary>
    Task<FiscalPeriod?> PreviousAsync(Guid periodId);

    /// <summary>期间列表（可按日历年过滤），按 年/月 倒序。</summary>
    Task<List<FiscalPeriod>> ListAsync(int? year = null);
}
