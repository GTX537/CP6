using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 损益表实现（章08 §3）。复用试算表【本期发生】（区间数）：收入按贷−借、费用按借−贷。
/// 费用按 Role 分主营成本（COGS）与营业费用（其余）；毛利=收入−COGS，净利润=毛利−营业费用。
/// （营业外收支/所得税细分依赖 Role 标注，暂并入营业费用，后续可拆。）
/// </summary>
public class IncomeStatementService : IIncomeStatementService
{
    private readonly CP6Context _db;
    private readonly ITrialBalanceService _trial;

    public IncomeStatementService(CP6Context db, ITrialBalanceService trial)
    {
        _db = db;
        _trial = trial;
    }

    public async Task<IncomeStatement> BuildAsync(Guid periodId)
    {
        var tb = await _trial.BuildAsync(periodId);
        var accByCode = await _db.GlAccounts.AsNoTracking()
            .ToDictionaryAsync(a => a.Code, a => new { a.Type, a.Role });

        var pnl = new IncomeStatement { PeriodId = periodId };

        foreach (var row in tb.Rows)
        {
            if (!accByCode.TryGetValue(row.Code, out var acc)) continue;

            if (acc.Type == AccountType.Revenue)
            {
                var amt = row.PeriodCredit - row.PeriodDebit;             // 收入：本期贷方净发生
                if (amt == 0m) continue;
                pnl.RevenueLines.Add(new FinReportLine(row.Code, row.Name, amt));
                pnl.Revenue += amt;
            }
            else if (acc.Type == AccountType.Expense)
            {
                var amt = row.PeriodDebit - row.PeriodCredit;             // 费用：本期借方净发生
                if (amt == 0m) continue;
                if (acc.Role == "COGS")
                {
                    pnl.CostLines.Add(new FinReportLine(row.Code, row.Name, amt));
                    pnl.Cost += amt;
                }
                else
                {
                    pnl.ExpenseLines.Add(new FinReportLine(row.Code, row.Name, amt));
                    pnl.OperatingExpense += amt;
                }
            }
        }

        pnl.GrossProfit = pnl.Revenue - pnl.Cost;
        pnl.NetProfit = pnl.GrossProfit - pnl.OperatingExpense;
        return pnl;
    }
}
