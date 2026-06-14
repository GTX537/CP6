using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 试算平衡表服务实现（章02 §2.3）。期初=本期开始日之前已过账累计（含历史）；本期=本期间已过账；
/// 期末=期初+本期。实时滚算（不存快照，F-D5）。不平 → 记错误日志告警（数据完整性 bug）。
/// </summary>
public class TrialBalanceService : ITrialBalanceService
{
    private readonly CP6Context _db;
    private readonly ILogger<TrialBalanceService>? _logger;

    public TrialBalanceService(CP6Context db, ILogger<TrialBalanceService>? logger = null)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<TrialBalance> BuildAsync(Guid periodId)
    {
        var period = await _db.FiscalPeriods.FindAsync(periodId)
                     ?? throw new InvalidOperationException("E-FIN-140");   // 期间不存在

        // 全部已过账分录（实时滚算；大数据量再加 PeriodBalance 快照，YAGNI）
        var posted = await (from l in _db.JournalLines
                            join e in _db.JournalEntries on l.EntryId equals e.Id
                            where e.Status == JournalStatus.Posted
                            select new { l.AccountId, l.Debit, l.Credit, e.PeriodId, e.VoucherDate })
                           .ToListAsync();

        var movement = posted.Where(x => x.PeriodId == periodId)
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => (D: g.Sum(x => x.Debit), C: g.Sum(x => x.Credit)));

        // ★期初含历史：本期开始日之前的全部已过账累计
        var opening = posted.Where(x => x.VoucherDate < period.PeriodStart)
            .GroupBy(x => x.AccountId)
            .ToDictionary(g => g.Key, g => (D: g.Sum(x => x.Debit), C: g.Sum(x => x.Credit)));

        var ids = opening.Keys.Union(movement.Keys).ToList();
        var accounts = await _db.GlAccounts.Where(a => ids.Contains(a.Id)).ToDictionaryAsync(a => a.Id);

        var tb = new TrialBalance { PeriodId = periodId };
        decimal closingDebitTotal = 0, closingCreditTotal = 0;

        foreach (var accId in ids)
        {
            if (!accounts.TryGetValue(accId, out var acc)) continue;   // 跳过悬挂引用（理论不应有）
            var (oD, oC) = opening.GetValueOrDefault(accId);
            var (mD, mC) = movement.GetValueOrDefault(accId);

            var sign = acc.NormalSide == AccountSide.Debit ? 1 : -1;
            var openBal = sign * (oD - oC);                  // 期初（正常方向带号）
            var closeBal = sign * ((oD + mD) - (oC + mC));    // 期末 = 期初 + 本期

            tb.Rows.Add(new TrialBalanceRow(acc.Code, acc.Name, openBal, mD, mC, closeBal, acc.NormalSide));

            // 平衡校验用"原始借贷净额"（不带正常方向号）：net>0 计入借方余额，net<0 计入贷方余额
            var net = (oD + mD) - (oC + mC);
            if (net > 0) closingDebitTotal += net;
            else if (net < 0) closingCreditTotal += -net;
        }

        tb.Rows = tb.Rows.OrderBy(r => r.Code).ToList();
        tb.MovementBalanced = tb.Rows.Sum(r => r.PeriodDebit) == tb.Rows.Sum(r => r.PeriodCredit);
        tb.ClosingBalanced = closingDebitTotal == closingCreditTotal;

        if (!tb.IsBalanced)
            _logger?.LogError(
                "试算不平 PeriodId={PeriodId} Movement(借{MD}=贷{MC}?{MB}) Closing(借{CD}=贷{CC}?{CB}) —— 数据完整性 bug，需排查",
                periodId, tb.Rows.Sum(r => r.PeriodDebit), tb.Rows.Sum(r => r.PeriodCredit), tb.MovementBalanced,
                closingDebitTotal, closingCreditTotal, tb.ClosingBalanced);

        return tb;
    }
}
