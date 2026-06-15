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
    private readonly CP6Context _db;
    private readonly ITrialBalanceService _trial;

    public BalanceSheetService(CP6Context db, ITrialBalanceService trial)
    {
        _db = db;
        _trial = trial;
    }

    public async Task<BalanceSheet> BuildAsync(Guid periodId)
    {
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

        bs.CurrentProfit = revenue - expense;                              // 本年利润（年结前未转未分配利润）
        bs.TotalAssets = bs.Assets.Sum(x => x.Amount);
        bs.TotalLiabilities = bs.Liabilities.Sum(x => x.Amount);
        bs.TotalEquity = bs.Equity.Sum(x => x.Amount) + bs.CurrentProfit;  // 本年利润并入权益侧
        bs.TotalLiabEquity = bs.TotalLiabilities + bs.TotalEquity;
        bs.IsBalanced = bs.TotalAssets == bs.TotalLiabEquity;
        return bs;
    }
}
