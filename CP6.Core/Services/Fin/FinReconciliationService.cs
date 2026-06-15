using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 财务每日对账实现（章10 §5）。三项一致性：①AP 子账↔GL 应付控制 ②AR 子账↔GL 应收控制
/// ③各开启期间试算平衡。复用 Plan1 TrialBalance + Plan2 ReconcileAp/Ar，不重复造逻辑。
/// </summary>
public class FinReconciliationService : IFinReconciliationService
{
    private readonly CP6Context _db;
    private readonly ITrialBalanceService _trial;
    private readonly IApReconcileService _apRecon;
    private readonly IArReconcileService _arRecon;

    public FinReconciliationService(CP6Context db, ITrialBalanceService trial,
        IApReconcileService apRecon, IArReconcileService arRecon)
    {
        _db = db;
        _trial = trial;
        _apRecon = apRecon;
        _arRecon = arRecon;
    }

    public async Task<FinReconciliationResult> ReconcileAsync()
    {
        var issues = new List<FinReconcileIssue>();

        // ① AP 子账 ↔ GL 应付控制
        var ap = await _apRecon.ReconcileApAsync();
        if (!ap.IsMatched)
            issues.Add(new FinReconcileIssue("AP", $"AP 子账未付合计 {ap.SubLedger} ≠ GL 应付控制余额 {ap.GlBalance}"));

        // ② AR 子账 ↔ GL 应收控制
        var ar = await _arRecon.ReconcileArAsync();
        if (!ar.IsMatched)
            issues.Add(new FinReconcileIssue("AR", $"AR 子账未收合计 {ar.SubLedger} ≠ GL 应收控制余额 {ar.GlBalance}"));

        // ③ 各开启期间试算平衡（不平=数据完整性 bug）
        var openPeriods = await _db.FiscalPeriods.Where(p => p.Status == PeriodStatus.Open).ToListAsync();
        foreach (var p in openPeriods)
        {
            var tb = await _trial.BuildAsync(p.Id);
            if (!tb.IsBalanced)
                issues.Add(new FinReconcileIssue("TrialBalance", $"期间 {p.Year}-{p.Month:D2} 试算不平"));
        }

        return new FinReconciliationResult { Issues = issues };
    }
}
