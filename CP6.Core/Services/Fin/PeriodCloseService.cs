using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 月结/锁期工作流实现（章02 §3）。组合 FiscalPeriodService(上期/下期) + TrialBalanceService(试算)。
/// 错误码：140 期间不存在 / 142 期间已结 / 143 有未过账凭证 / 144 试算不平 / 145 上期未结 / 146 期间未结(无需反结)。
/// </summary>
public class PeriodCloseService : IPeriodCloseService
{
    private readonly CP6Context _db;
    private readonly IFiscalPeriodService _periods;
    private readonly ITrialBalanceService _trial;
    private readonly IFxRevaluationService? _reval;
    private readonly IAssetDepreciationService? _deprec;
    private readonly ILogger<PeriodCloseService>? _logger;

    public PeriodCloseService(CP6Context db, IFiscalPeriodService periods, ITrialBalanceService trial,
        IFxRevaluationService? reval = null, IAssetDepreciationService? deprec = null,
        ILogger<PeriodCloseService>? logger = null)
    {
        _db = db;
        _periods = periods;
        _trial = trial;
        _reval = reval;
        _deprec = deprec;
        _logger = logger;
    }

    public async Task<FinResult> PreCloseCheckAsync(Guid periodId)
    {
        var p = await _db.FiscalPeriods.FindAsync(periodId);
        if (p == null) return FinResult.Fail("E-FIN-140");
        if (p.Status == PeriodStatus.Closed) return FinResult.Fail("E-FIN-142");

        // ① 不能有未过账凭证（草稿/待复核）赖在本期
        var pending = await _db.JournalEntries.CountAsync(e =>
            e.PeriodId == periodId &&
            (e.Status == JournalStatus.Draft || e.Status == JournalStatus.PendingReview));
        if (pending > 0) return FinResult.Fail("E-FIN-143", pending);

        // ② 试算必须平
        var tb = await _trial.BuildAsync(periodId);
        if (!tb.IsBalanced) return FinResult.Fail("E-FIN-144");

        // ③ 上一期间必须已结（不跳月）
        var prev = await _periods.PreviousAsync(periodId);
        if (prev is { Status: PeriodStatus.Open }) return FinResult.Fail("E-FIN-145");

        // ④ A3 §6.1 硬预检：工作量法在用资产本期未录工作量 → 硬阻断（结账钩子 Accrue 会触 FA008，前移明示）
        if (_deprec != null)
        {
            var wl = await _deprec.PreCloseWorkloadCheckAsync(periodId);
            if (!wl.Ok) return wl;
        }

        return FinResult.Pass();
    }

    public async Task<FinResult> CloseAsync(Guid periodId, string userId)
    {
        var check = await PreCloseCheckAsync(periodId);
        if (!check.Ok) return check;

        // ★ A3 §6.1：结账前兜底计提折旧（三态幂等：Posted→Pass / Draft→Post / 无→Run+Post）。失败阻断结账。
        // 折旧先于汇兑重估，因折旧凭证可能影响外币科目余额（如折旧费用通过损益结转影响留存）。
        if (_deprec != null)
        {
            var dr = await _deprec.AccrueAsync(periodId, userId);
            if (!dr.Ok) return dr;
        }

        // ★ 章07 §4：结账前对未结外币 AP/AR 余额做期末未实现汇兑重估（重估凭证落本期 + 冲回凭证落下期初）。
        if (_reval != null)
        {
            var rr = await _reval.RevalueAsync(periodId, userId);
            if (!rr.Ok) return rr;
        }

        var p = await _db.FiscalPeriods.FindAsync(periodId);
        p!.Status = PeriodStatus.Closed;
        p.ClosedAt = DateTime.Now;
        p.ClosedBy = userId;
        p.Modifier = userId;
        p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();

        // 下一期间自动置 Open（不存在则创建，含跨年滚动）
        await _periods.EnsureOpenAsync(p.Year, p.Month + 1, userId);
        return FinResult.Pass();
    }

    public async Task<FinResult> ReopenAsync(Guid periodId, string userId)
    {
        var p = await _db.FiscalPeriods.FindAsync(periodId);
        if (p == null) return FinResult.Fail("E-FIN-140");
        if (p.Status != PeriodStatus.Closed) return FinResult.Fail("E-FIN-146");

        p.Status = PeriodStatus.Open;
        p.ClosedAt = null;
        p.ClosedBy = null;
        p.Modifier = userId;
        p.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();

        // 危险动作留痕（控制器层另有 [RequirePermission] 高权限 + OperLog 自动记录）
        _logger?.LogWarning(
            "会计期间反结账 PeriodId={PeriodId} {Year}-{Month} by {User} —— 危险动作（已报税月份重开=改历史）",
            periodId, p.Year, p.Month, userId);
        return FinResult.Pass();
    }
}
