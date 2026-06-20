using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 预算过账守卫（静态，同 CP6Context 直查，无 DI/无循环依赖，仿 BankReconGuard）。
/// 挂手工凭证 PostAsync（Task E-2 负责钩入 JournalEntryService）。
/// spec §7：仅拦截 Block 模式的费用科目超支；Warn/None 透传。
/// </summary>
public static class BudgetGuard
{
    /// <summary>
    /// 过账守卫入口：评估凭证中所有 Block 费用行，首条超支即拒。
    /// 无激活版本 / 无 Block 预算行 → 直接放行（对正常过账完全透明）。
    /// </summary>
    public static async Task<FinResult> CheckPostingAsync(CP6Context db, JournalEntry entry)
    {
        var results = await BudgetEvaluator.EvaluateAsync(db, entry, blockOnly: true);
        var firstBlock = results.FirstOrDefault(w => w.IsBlock && w.Exceeded);
        return firstBlock == null
            ? FinResult.Pass()
            : FinResult.Fail("E-A5-BUDGET-EXCEEDED",
                firstBlock.AccountCode,
                firstBlock.Budget,
                firstBlock.Used,
                firstBlock.Incoming,
                firstBlock.Used + firstBlock.Incoming - firstBlock.Budget);
    }
}

/// <summary>
/// 核心评估器：守卫(blockOnly=true) 与 预检全量(blockOnly=false, Warn+Block) 共用。
/// spec §7 实现要点：
///   (a) 落期：PeriodId 优先，fallback VoucherDate 年月匹配。
///   (b) 仅费用科目（AccountType.Expense）参与控制。
///   (c) 同 entry 同桶多行合并 incoming（防拆行绕过）。
///   (d) 最具体维度匹配（spec §7.3）：非空预算维度全等；非空维度最多者优先。
///   (e) YTD 口径：期号 1..当前；Period 口径：当前期号仅当期。
///   (f) 实际 = 该财年该期间范围内已过账凭证行的净借方（Debit-Credit）。
/// </summary>
public static class BudgetEvaluator
{
    public static async Task<List<BudgetEvalResult>> EvaluateAsync(CP6Context db, JournalEntry entry, bool blockOnly)
    {
        var results = new List<BudgetEvalResult>();

        // 1. 落期：优先 PeriodId，fallback VoucherDate（spec §7.1）
        FiscalPeriod? period = null;
        if (entry.PeriodId != Guid.Empty)
            period = await db.FiscalPeriods.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == entry.PeriodId);
        period ??= await db.FiscalPeriods.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Year == entry.VoucherDate.Year && p.Month == entry.VoucherDate.Month);
        if (period == null) return results;

        int fy = period.FiscalYear, pno = period.PeriodNo;

        // 2. 该财年 Active 版本
        var version = await (from v in db.BudgetVersions.AsNoTracking()
                             join b in db.Budgets.AsNoTracking() on v.BudgetId equals b.Id
                             where v.IsActive && b.FiscalYear == fy
                             select v).FirstOrDefaultAsync();
        if (version == null) return results;

        // 3. 该版本费用类预算行（含 12 期）；按 AccountId 收窄
        var entryAcctIds = entry.Lines.Select(l => l.AccountId).Distinct().ToList();

        var budgetLines = await (from l in db.BudgetLines.AsNoTracking()
                                 join a in db.GlAccounts.AsNoTracking() on l.AccountId equals a.Id
                                 where l.VersionId == version.Id
                                       && a.Type == AccountType.Expense
                                       && entryAcctIds.Contains(l.AccountId)
                                 select l).ToListAsync();
        if (budgetLines.Count == 0) return results;

        var blIds = budgetLines.Select(l => l.Id).ToList();
        var blPeriods = await db.BudgetLinePeriods.AsNoTracking()
            .Where(p => blIds.Contains(p.BudgetLineId))
            .ToListAsync();

        // 4. 预加载科目类型字典（凭证行中的科目）
        var acctTypes = await db.GlAccounts.AsNoTracking()
            .Where(a => entryAcctIds.Contains(a.Id))
            .ToDictionaryAsync(a => a.Id, a => a);

        // 5. 费用行按桶合并本 entry 消耗（防拆行绕过）
        var incoming = new Dictionary<Guid, decimal>();  // BudgetLine.Id → 净消耗
        var lineMeta = new Dictionary<Guid, BudgetLine>();

        foreach (var jl in entry.Lines)
        {
            if (!acctTypes.TryGetValue(jl.AccountId, out var acct) || acct.Type != AccountType.Expense)
                continue;

            var consume = jl.Debit - jl.Credit;
            var bl = MostSpecific(budgetLines, jl);
            if (bl == null) continue;

            var mode = bl.ControlMode ?? version.DefaultControlMode;
            if (blockOnly && mode != BudgetControlMode.Block) continue;
            if (mode == BudgetControlMode.None) continue;

            incoming[bl.Id] = incoming.GetValueOrDefault(bl.Id) + consume;
            lineMeta[bl.Id] = bl;
        }

        if (incoming.Count == 0) return results;

        // 6. 逐桶取口径算已用 + 本次 vs 预算
        foreach (var (blId, inc) in incoming)
        {
            var bl = lineMeta[blId];
            var basis = bl.ControlBasis ?? version.DefaultControlBasis;
            var mode = bl.ControlMode ?? version.DefaultControlMode;

            // YTD = 期1..当前；Period = 当前期仅当期
            int fromP = basis == BudgetControlBasis.Ytd ? 1 : pno;
            int toP = pno;

            var budget = blPeriods
                .Where(p => p.BudgetLineId == blId && p.PeriodNo >= fromP && p.PeriodNo <= toP)
                .Sum(p => p.Amount);

            var used = await ActualForBucketAsync(db, bl, fy, fromP, toP);
            var acctCode = acctTypes[bl.AccountId].Code;

            results.Add(new BudgetEvalResult
            {
                AccountCode = acctCode,
                Budget = budget,
                Used = used,
                Incoming = inc,
                IsBlock = mode == BudgetControlMode.Block,
                Exceeded = used + inc > budget,
            });
        }

        return results;
    }

    /// <summary>
    /// 最具体匹配（spec §7.3）：非空预算维度全等于凭证行；取非空维度最多者。
    /// 空维度（Guid.Empty / ""）= 通配，任意凭证行维度都能命中。
    /// 非空维度 = 精确约束，仅凭证行该维度相同才命中。
    /// </summary>
    private static BudgetLine? MostSpecific(List<BudgetLine> lines, JournalLine jl)
    {
        var ccKey = jl.CostCenterId ?? Guid.Empty;
        var coTypeKey = jl.CostObjectType ?? "";
        var coIdKey = jl.CostObjectId ?? "";

        return lines
            .Where(l => l.AccountId == jl.AccountId
                && (l.CostCenterKey == Guid.Empty || l.CostCenterKey == ccKey)
                && (l.CostObjectTypeKey == "" || l.CostObjectTypeKey == coTypeKey)
                && (l.CostObjectIdKey == "" || l.CostObjectIdKey == coIdKey))
            .OrderByDescending(l =>
                (l.CostCenterKey != Guid.Empty ? 1 : 0) +
                (l.CostObjectTypeKey != "" ? 1 : 0) +
                (l.CostObjectIdKey != "" ? 1 : 0))
            .ThenByDescending(l => l.CostCenterKey != Guid.Empty)   // 成本中心优先级最高
            .FirstOrDefault();
    }

    /// <summary>
    /// 桶已过账实际（费用净借方），财年 fy 期号 [fromP, toP]。
    /// 仅计入 JournalStatus.Posted 的凭证；维度过滤与预算行一致。
    /// </summary>
    private static async Task<decimal> ActualForBucketAsync(CP6Context db, BudgetLine bl, int fy, int fromP, int toP)
    {
        var q = from l in db.JournalLines.AsNoTracking()
                join e in db.JournalEntries.AsNoTracking() on l.EntryId equals e.Id
                join p in db.FiscalPeriods.AsNoTracking() on e.PeriodId equals p.Id
                where e.Status == JournalStatus.Posted
                      && l.AccountId == bl.AccountId
                      && p.FiscalYear == fy
                      && p.PeriodNo >= fromP
                      && p.PeriodNo <= toP
                select new { l.Debit, l.Credit, l.CostCenterId, l.CostObjectType, l.CostObjectId };

        var rows = await q.ToListAsync();

        return rows
            .Where(r =>
                (bl.CostCenterKey == Guid.Empty || (r.CostCenterId ?? Guid.Empty) == bl.CostCenterKey) &&
                (bl.CostObjectTypeKey == "" || (r.CostObjectType ?? "") == bl.CostObjectTypeKey) &&
                (bl.CostObjectIdKey == "" || (r.CostObjectId ?? "") == bl.CostObjectIdKey))
            .Sum(r => r.Debit - r.Credit);
    }
}

/// <summary>单桶评估结果（供守卫判断 + 前端预检展示）。</summary>
public class BudgetEvalResult
{
    public string AccountCode { get; set; } = "";
    public decimal Budget { get; set; }
    public decimal Used { get; set; }
    public decimal Incoming { get; set; }
    public bool IsBlock { get; set; }
    public bool Exceeded { get; set; }
}
