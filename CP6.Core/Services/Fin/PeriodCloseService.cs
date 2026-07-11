using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 月结/锁期工作流实现（章02 §3）。组合 FiscalPeriodService(上期/下期) + TrialBalanceService(试算)。
/// 错误码：140 期间不存在 / 142 期间已结 / 143 有未过账凭证 / 144 试算不平 / 145 上期未结 / 146 期间未结(无需反结)。
/// 年结（波D）：404 年度已锁定拒记账 / 405 财年12期未全结 / 406 财年已年结(幂等) / 407 财年未年结(无需反年结) /
/// 408 本年利润科目(3103)缺失 / 409 年结依赖未注入。
/// </summary>
public class PeriodCloseService : IPeriodCloseService
{
    /// <summary>本年利润科目编码（无 Role 锚点，按 COA 编码定位；CN-GAAP=3103）。</summary>
    private const string CurrentYearProfitCode = "3103";
    /// <summary>未分配利润角色锚点。</summary>
    private const string RetainedEarningsRole = "RETAINED_EARNINGS";

    private readonly CP6Context _db;
    private readonly IFiscalPeriodService _periods;
    private readonly ITrialBalanceService _trial;
    private readonly IFxRevaluationService? _reval;
    private readonly IAssetDepreciationService? _deprec;
    private readonly IJournalEntryService? _journal;
    private readonly ILogger<PeriodCloseService>? _logger;

    public PeriodCloseService(CP6Context db, IFiscalPeriodService periods, ITrialBalanceService trial,
        IFxRevaluationService? reval = null, IAssetDepreciationService? deprec = null,
        ILogger<PeriodCloseService>? logger = null, IJournalEntryService? journal = null)
    {
        _db = db;
        _periods = periods;
        _trial = trial;
        _reval = reval;
        _deprec = deprec;
        _journal = journal;
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

    public async Task<FinResult> YearCloseAsync(int fiscalYear, string userId)
    {
        if (_journal == null) return FinResult.Fail("E-FIN-409");

        var periods = await _db.FiscalPeriods
            .Where(p => p.FiscalYear == fiscalYear)
            .OrderBy(p => p.PeriodNo).ToListAsync();

        // ① 幂等：已年结（12 期中任一 YearClosed）→ 拒，不重记
        if (periods.Any(p => p.Status == PeriodStatus.YearClosed))
            return FinResult.Fail("E-FIN-406", fiscalYear);

        // ② 必须满 12 期且全部 Closed
        if (periods.Count < 12 || periods.Any(p => p.Status != PeriodStatus.Closed))
            return FinResult.Fail("E-FIN-405", fiscalYear);

        var lastPeriod = periods[^1];                     // PeriodNo 最大＝财年末期
        var carryDate = lastPeriod.PeriodEnd;
        var yearTag = $"YC-{fiscalYear}";
        var periodIds = periods.Select(p => p.Id).ToList();

        // 损益（收入/费用）科目在本财年 12 期内的已过账净额（借-贷）
        var plAccounts = await _db.GlAccounts
            .Where(a => (a.Type == AccountType.Revenue || a.Type == AccountType.Expense) && a.IsActive)
            .Select(a => a.Id).ToListAsync();
        var plIds = plAccounts.ToHashSet();

        var plLines = await (from l in _db.JournalLines
                             join e in _db.JournalEntries on l.EntryId equals e.Id
                             where e.Status == JournalStatus.Posted && periodIds.Contains(e.PeriodId)
                             select new { l.AccountId, l.Debit, l.Credit }).ToListAsync();

        var balances = plLines.Where(x => plIds.Contains(x.AccountId))
            .GroupBy(x => x.AccountId)
            .Select(g => new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
            .Where(x => x.Net != 0m)
            .ToList();

        // ③ 空财年（无损益余额）→ 不产生凭证，仍锁年
        if (balances.Count == 0)
        {
            await LockYearAsync(periods, userId);
            _logger?.LogInformation("年结 {FY} 无损益余额，仅锁年（不产生凭证） by {User}", fiscalYear, userId);
            return FinResult.Pass();
        }

        // 3103 本年利润（无 Role，按编码）/ 3104 未分配利润（Role）
        var profit = await _db.GlAccounts.FirstOrDefaultAsync(a => a.Code == CurrentYearProfitCode && a.IsActive);
        if (profit == null) return FinResult.Fail("E-FIN-408", CurrentYearProfitCode);
        var retained = await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == RetainedEarningsRole && a.IsActive);
        if (retained == null) return FinResult.Fail("E-FIN-141", RetainedEarningsRole);

        // ★ 年结凭证须落财年末期（已 Closed）：由年结进程暂开该期承接结转分录，过账后再连同全年锁 YearClosed。
        //   仍走 AutoPostAsync（借贷平衡/科目合法性兜底），不绕过校验。
        lastPeriod.Status = PeriodStatus.Open;
        await _db.SaveChangesAsync();

        // 凭证一：损益逐科目反向清零，净额对 3103
        var v1 = new JournalEntry
        {
            VoucherDate = carryDate,
            Source = VoucherSource.Carryover,
            SourceDocNo = yearTag,
            Description = $"{fiscalYear} 年度损益结转（结转本年利润）",
        };
        decimal clrDebit = 0m, clrCredit = 0m;
        foreach (var b in balances)
        {
            if (b.Net > 0m)      // 借方余额（费用）→ 贷记冲平
            {
                v1.Lines.Add(new JournalLine { AccountId = b.AccountId, Credit = b.Net });
                clrCredit += b.Net;
            }
            else                 // 贷方余额（收入）→ 借记冲平
            {
                v1.Lines.Add(new JournalLine { AccountId = b.AccountId, Debit = -b.Net });
                clrDebit += -b.Net;
            }
        }
        var profitNet = clrDebit - clrCredit;   // >0 净利（3103 贷）；<0 净亏（3103 借）
        if (profitNet > 0m) v1.Lines.Add(new JournalLine { AccountId = profit.Id, Credit = profitNet });
        else if (profitNet < 0m) v1.Lines.Add(new JournalLine { AccountId = profit.Id, Debit = -profitNet });
        // profitNet==0（损益相抵）→ 无 3103 行，v1 借贷自平

        var r1 = await _journal.AutoPostAsync(v1);
        if (!r1.Ok) { lastPeriod.Status = PeriodStatus.Closed; await _db.SaveChangesAsync(); return r1; }

        // 凭证二：3103 → 3104（仅当存在净损益）
        if (profitNet != 0m)
        {
            var v2 = new JournalEntry
            {
                VoucherDate = carryDate,
                Source = VoucherSource.Carryover,
                SourceDocNo = $"{yearTag}-RE",
                Description = $"{fiscalYear} 年度净利结转未分配利润",
            };
            if (profitNet > 0m)   // 净利：3103 借 / 3104 贷
            {
                v2.Lines.Add(new JournalLine { AccountId = profit.Id, Debit = profitNet });
                v2.Lines.Add(new JournalLine { AccountId = retained.Id, Credit = profitNet });
            }
            else                  // 净亏：3103 贷 / 3104 借
            {
                v2.Lines.Add(new JournalLine { AccountId = profit.Id, Credit = -profitNet });
                v2.Lines.Add(new JournalLine { AccountId = retained.Id, Debit = -profitNet });
            }
            var r2 = await _journal.AutoPostAsync(v2);
            if (!r2.Ok) { lastPeriod.Status = PeriodStatus.Closed; await _db.SaveChangesAsync(); return r2; }
        }

        // 全年 12 期锁 YearClosed
        await LockYearAsync(periods, userId);
        _logger?.LogInformation(
            "年结 {FY} 完成：损益结转 + 3103→3104 + 锁年（净额 {Net}） by {User}", fiscalYear, profitNet, userId);
        return FinResult.Pass();
    }

    public async Task<FinResult> ReopenYearAsync(int fiscalYear, string userId)
    {
        var periods = await _db.FiscalPeriods
            .Where(p => p.FiscalYear == fiscalYear)
            .OrderBy(p => p.PeriodNo).ToListAsync();
        if (periods.Count == 0 || periods.All(p => p.Status != PeriodStatus.YearClosed))
            return FinResult.Fail("E-FIN-407", fiscalYear);
        if (_journal == null) return FinResult.Fail("E-FIN-409");

        // ★ 不用 ReverseAsync：原凭证被标 Reversed 会掉出 Status==Posted 余额口径，而红冲凭证 Posted 单边计入
        //   → 净效果=多冲一次（损益翻倍、3104 残值）。改为：**原两张年结凭证保持 Posted**，另投一张反向
        //   Carryover 凭证（YC-{fy}-REOPEN）经 AutoPostAsync 过账；原+反向同计 → 损益余额恢复原值、
        //   3103/3104 归零，再年结读到正确损益不翻倍。
        //   幂等自查重：按「本财年全部 YC-{fy}* 结转凭证（含历史 REOPEN）的每科目净额」取负——
        //   close→reopen 反复循环时历史轮次已互抵，只冲最后一轮净效果；净额全 0（空财年）→ 不产生凭证仅解锁。
        var yearTag = $"YC-{fiscalYear}";
        var carryNet = await (from l in _db.JournalLines
                              join e in _db.JournalEntries on l.EntryId equals e.Id
                              where e.Status == JournalStatus.Posted
                                    && e.Source == VoucherSource.Carryover
                                    && e.SourceDocNo != null && e.SourceDocNo.StartsWith(yearTag)
                              group new { l.Debit, l.Credit } by l.AccountId into g
                              select new { AccountId = g.Key, Net = g.Sum(x => x.Debit) - g.Sum(x => x.Credit) })
                             .ToListAsync();
        var toNegate = carryNet.Where(x => x.Net != 0m).ToList();

        if (toNegate.Count > 0)
        {
            // 反向凭证须落财年末期（当前 YearClosed）：暂开末期承接，仍走 AutoPostAsync（借贷平衡/科目兜底）。
            var lastPeriod = periods[^1];
            lastPeriod.Status = PeriodStatus.Open;
            await _db.SaveChangesAsync();

            var rv = new JournalEntry
            {
                VoucherDate = lastPeriod.PeriodEnd,
                Source = VoucherSource.Carryover,
                SourceDocNo = $"{yearTag}-REOPEN",
                Description = $"{fiscalYear} 反年结（反向冲销年结结转，原年结凭证保持已过账）",
            };
            foreach (var b in toNegate)
            {
                if (b.Net > 0m) rv.Lines.Add(new JournalLine { AccountId = b.AccountId, Credit = b.Net });
                else rv.Lines.Add(new JournalLine { AccountId = b.AccountId, Debit = -b.Net });
            }
            var rr = await _journal.AutoPostAsync(rv);
            if (!rr.Ok)
            {
                lastPeriod.Status = PeriodStatus.YearClosed;
                await _db.SaveChangesAsync();
                return rr;
            }
        }

        // 12 期回 Closed（解除年度锁定，恢复到月结态）
        var now = DateTime.Now;
        foreach (var p in periods)
        {
            p.Status = PeriodStatus.Closed;
            p.Modifier = userId;
            p.ModifyDate = now;
        }
        await _db.SaveChangesAsync();

        _logger?.LogWarning(
            "会计年度反年结 FY={FY} by {User} —— 危险动作（改历史，反向冲销 {Cnt} 科目净额 + 12 期回 Closed）",
            fiscalYear, userId, toNegate.Count);
        return FinResult.Pass();
    }

    /// <summary>将财年全部期间置 YearClosed（年度锁定）。</summary>
    private async Task LockYearAsync(List<FiscalPeriod> periods, string userId)
    {
        var now = DateTime.Now;
        foreach (var p in periods)
        {
            p.Status = PeriodStatus.YearClosed;
            p.Modifier = userId;
            p.ModifyDate = now;
        }
        await _db.SaveChangesAsync();
    }
}
