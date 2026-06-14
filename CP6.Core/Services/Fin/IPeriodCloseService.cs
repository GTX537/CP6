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
}
