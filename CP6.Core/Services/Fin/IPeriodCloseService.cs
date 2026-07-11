namespace CP6.Core.Services.Fin;

/// <summary>
/// 月结/锁期工作流（章02 §3）。结账=把期间置 Closed，之后凭证落不进（PostAsync 锁期判定生效）。
/// 反结账危险（已报税月份重开=改历史），限高权限 + 留痕。
/// </summary>
public interface IPeriodCloseService
{
    /// <summary>结账前检查清单：未过账凭证 / 试算不平 / 上期未结（不跳月）。</summary>
    Task<FinResult> PreCloseCheckAsync(Guid periodId);

    /// <summary>结账（过检查 → Closed + 留结账人/时间 → 下期 EnsureOpen）。</summary>
    Task<FinResult> CloseAsync(Guid periodId, string userId);

    /// <summary>反结账（仅 Closed 可反；置回 Open + 留痕）。</summary>
    Task<FinResult> ReopenAsync(Guid periodId, string userId);

    /// <summary>
    /// 年结（章02 §3 / 波D）。校验财年 12 期全 Closed → 损益逐科目反向清零净额入 3103 本年利润 →
    /// 3103 结转 3104 未分配利润（两张 Carryover 凭证走 AutoPostAsync）→ 全年 12 期锁 YearClosed。
    /// 净利/净亏两向皆对；空财年不产生凭证仍锁年。幂等：已年结拒（E-FIN-406）。
    /// </summary>
    Task<FinResult> YearCloseAsync(int fiscalYear, string userId);

    /// <summary>
    /// 反年结（高危，章02 §3 / 波D）。红冲两张年结凭证（ReverseAsync）+ 12 期回 Closed + 留痕。
    /// 仅已年结财年可反（否则 E-FIN-407）。
    /// </summary>
    Task<FinResult> ReopenYearAsync(int fiscalYear, string userId);
}
