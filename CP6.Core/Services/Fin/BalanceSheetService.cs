using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 资产负债表实现（章08 §2）。复用试算表期末余额(closeBal，按正常方向带号)，按 AccountType 分资产/负债/权益。
/// 收入−费用 = 本年利润并入权益侧 → 资产 = 负债 + 权益 + 本年利润 恒平（源于借贷恒等）。
/// 报表 = 科目余额的视图，无独立存储，永远与总账一致。
/// </summary>
public class BalanceSheetService : IBalanceSheetService
{
    /// <summary>往年未年结损益「现算」入期初未分配利润的合成行编码（非真实 GL 科目，故带后缀 .PY）。</summary>
    private const string PriorProfitCode = "3104.PY";

    private readonly CP6Context _db;
    private readonly ITrialBalanceService _trial;

    public BalanceSheetService(CP6Context db, ITrialBalanceService trial)
    {
        _db = db;
        _trial = trial;
    }

    public async Task<BalanceSheet> BuildAsync(Guid periodId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId)
                     ?? throw new InvalidOperationException("E-FIN-140");   // 期间不存在（与试算表同码）
        var tb = await _trial.BuildAsync(periodId);   // 复用 02 章三栏试算表（含 E-FIN-140 期间校验）
        var accByCode = await _db.GlAccounts.AsNoTracking().ToDictionaryAsync(a => a.Code, a => a.Type);

        var bs = new BalanceSheet { PeriodId = periodId };
        decimal revenue = 0m, expense = 0m;

        foreach (var row in tb.Rows)
        {
            if (!accByCode.TryGetValue(row.Code, out var type)) continue;
            switch (type)
            {
                case AccountType.Asset:
                    if (row.CloseBal != 0m) bs.Assets.Add(new FinReportLine(row.Code, row.Name, row.CloseBal));
                    break;
                case AccountType.Liability:
                    if (row.CloseBal != 0m) bs.Liabilities.Add(new FinReportLine(row.Code, row.Name, row.CloseBal));
                    break;
                case AccountType.Equity:
                    if (row.CloseBal != 0m) bs.Equity.Add(new FinReportLine(row.Code, row.Name, row.CloseBal));
                    break;
                case AccountType.Revenue: revenue += row.CloseBal; break;   // 收入/费用不进资产负债表，差额转本年利润
                case AccountType.Expense: expense += row.CloseBal; break;
            }
        }

        // ★本年利润按【本财年内发生额】口径（D.2）：损益科目期末余额是建账以来累计。年结后（D.1）
        //   损益已被 Carryover 结转清零 → CloseBal 天然只剩本财年，此段 priorPl 恒为 0，行为同旧口径；
        //   但跨年未年结时 CloseBal 含往年累计，直接入本年利润会虚增。故按报表期所属财年起点截断：
        //   财年起点 = 报表期 PeriodStart 回退 (PeriodNo-1) 个月（PeriodNo 为财年内 1..12，适配非日历财年）。
        var fyStart = period.PeriodStart.AddMonths(-(period.PeriodNo - 1));

        // 本财年起点之前的损益累计净额（贷-借 = 利润方向；收入贷、费用借统一为 Credit-Debit）——往年未结转损益。
        var priorPl = await (from l in _db.JournalLines
                             join e in _db.JournalEntries on l.EntryId equals e.Id
                             join a in _db.GlAccounts on l.AccountId equals a.Id
                             where e.Status == JournalStatus.Posted
                                   && (a.Type == AccountType.Revenue || a.Type == AccountType.Expense)
                                   && e.VoucherDate < fyStart
                             select l.Credit - l.Debit).SumAsync();

        var plTotal = revenue - expense;          // 损益期末累计（= 旧口径本年利润，恒等于 tb 各损益 CloseBal 之和）
        bs.CurrentProfit = plTotal - priorPl;     // 仅本财年发生额

        // 往年未年结损益 → 现算入「期初未分配利润」合成行，保持借贷恒等（priorPl==0 即年结后/首年→不列行）。
        // 恒等证明：priorPl + CurrentProfit ≡ plTotal，故 TotalEquity 与旧口径逐分不差，资产=负债+权益仍平。
        if (priorPl != 0m)
            bs.Equity.Add(new FinReportLine(PriorProfitCode, "以前年度未结转损益（期初未分配利润现算）", priorPl));

        bs.TotalAssets = bs.Assets.Sum(x => x.Amount);
        bs.TotalLiabilities = bs.Liabilities.Sum(x => x.Amount);
        bs.TotalEquity = bs.Equity.Sum(x => x.Amount) + bs.CurrentProfit;  // 本年利润并入权益侧
        bs.TotalLiabEquity = bs.TotalLiabilities + bs.TotalEquity;
        bs.IsBalanced = bs.TotalAssets == bs.TotalLiabEquity;
        return bs;
    }
}
