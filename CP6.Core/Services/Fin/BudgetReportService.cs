using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 预算对比实际报告服务 (spec §9)。
/// 维度聚合：科目×成本中心×成本对象 → 匹配最具体预算桶；
/// 无匹配预算桶的实际发生额归入未编预算组(IsUnbudgeted=true)，不得静默丢弃(§9.3)。
/// 实际侧仿 TrialBalanceService：仅取 Posted 凭证，费用=借-贷，收入=贷-借。
/// </summary>
public class BudgetReportService : IBudgetReportService
{
    private readonly CP6Context _db;

    public BudgetReportService(CP6Context db) { _db = db; }

    public async Task<BudgetVsActualReport> BuildVsActualAsync(
        int fiscalYear, Guid? versionId, int periodFrom, int periodTo)
    {
        var rep = new BudgetVsActualReport { FiscalYear = fiscalYear, VersionId = versionId };

        // ── 1. 确定版本 ──────────────────────────────────────────────
        var version = versionId.HasValue
            ? await _db.BudgetVersions.AsNoTracking()
                       .FirstOrDefaultAsync(v => v.Id == versionId)
            : await (from v in _db.BudgetVersions.AsNoTracking()
                     join b in _db.Budgets.AsNoTracking() on v.BudgetId equals b.Id
                     where v.IsActive && b.FiscalYear == fiscalYear
                     select v).FirstOrDefaultAsync();

        // ── 2. 加载预算行 → 构建 budget-rows 字典（key = 4 维 pipe 串）──
        // key format: "{acctId}|{ccKey}|{coTypeKey}|{coIdKey}"
        // unbudgeted rows use a separate "|UB" suffix key — never searched via MatchBucketKey
        var rows = new Dictionary<string, BudgetVsActualRow>();

        if (version != null)
        {
            var lines = await _db.BudgetLines.AsNoTracking()
                                  .Where(l => l.VersionId == version.Id)
                                  .ToListAsync();
            var lineIds = lines.Select(l => l.Id).ToList();
            var periods = await _db.BudgetLinePeriods.AsNoTracking()
                                    .Where(p => lineIds.Contains(p.BudgetLineId))
                                    .ToListAsync();
            var lineAcctIds = lines.Select(l => l.AccountId).Distinct().ToList();
            var accts = await _db.GlAccounts.AsNoTracking()
                                  .Where(a => lineAcctIds.Contains(a.Id))
                                  .ToListAsync();
            var acctMap = accts.ToDictionary(a => a.Id);

            foreach (var l in lines)
            {
                if (!acctMap.TryGetValue(l.AccountId, out var a)) continue;
                var key = BucketKey(l.AccountId, l.CostCenterKey, l.CostObjectTypeKey, l.CostObjectIdKey);
                var row = new BudgetVsActualRow
                {
                    AccountId      = l.AccountId,
                    AccountCode    = a.Code,
                    AccountName    = a.Name,
                    CostCenterId   = l.CostCenterId,
                    CostObjectType = l.CostObjectType,
                    CostObjectId   = l.CostObjectId
                };
                foreach (var p in periods.Where(p =>
                             p.BudgetLineId == l.Id &&
                             p.PeriodNo >= periodFrom &&
                             p.PeriodNo <= periodTo))
                {
                    row.BudgetPeriods[p.PeriodNo - 1] = p.Amount;
                }
                row.Budget = row.BudgetPeriods.Sum();
                rows[key] = row;
            }
        }

        // ── 3. 加载实际 (Posted, 仅 Expense+Revenue, 指定期间) ────────
        var actuals = await (
            from l in _db.JournalLines.AsNoTracking()
            join e in _db.JournalEntries.AsNoTracking() on l.EntryId equals e.Id
            join p in _db.FiscalPeriods.AsNoTracking()   on e.PeriodId equals p.Id
            join a in _db.GlAccounts.AsNoTracking()      on l.AccountId equals a.Id
            where e.Status == JournalStatus.Posted
               && p.FiscalYear == fiscalYear
               && p.PeriodNo >= periodFrom
               && p.PeriodNo <= periodTo
               && (a.Type == AccountType.Expense || a.Type == AccountType.Revenue)
            select new
            {
                l.AccountId,
                a.Code,
                a.Name,
                a.Type,
                l.CostCenterId,
                l.CostObjectType,
                l.CostObjectId,
                l.Debit,
                l.Credit,
                p.PeriodNo
            }).ToListAsync();

        // ── 4. 将实际分配到预算桶或未编预算桶 ───────────────────────
        foreach (var g in actuals.GroupBy(x => new
        {
            x.AccountId,
            x.Code,
            x.Name,
            x.Type,
            x.CostCenterId,
            x.CostObjectType,
            x.CostObjectId
        }))
        {
            var ccKey    = g.Key.CostCenterId   ?? Guid.Empty;
            var coType   = g.Key.CostObjectType ?? "";
            var coId     = g.Key.CostObjectId   ?? "";

            // 只在"真实预算桶"中搜索（!k.EndsWith("|UB") 防止误命中已添加的未编预算行）
            // 最具体匹配：按非通配维度数排序（与 BudgetGuard.MostSpecific 一致）。
            // ⚠ 不能用 key.Length：成本中心是 Guid，Guid.Empty 与真实 Guid 字符串等长，
            //   公司级桶与 cc 专属桶 key 等长，length 排序无法区分 cost-center 粒度。
            var matchKey = rows.Keys
                .Where(k => !k.EndsWith("|UB") &&
                            MatchBucketKey(k, g.Key.AccountId, ccKey, coType, coId))
                .OrderByDescending(BucketSpecificity)
                .ThenByDescending(k => k.Length)
                .FirstOrDefault();

            BudgetVsActualRow row;
            if (matchKey != null)
            {
                row = rows[matchKey];
            }
            else
            {
                // 未编预算：专属 key，可幂等合并同维度多条实际
                var ubKey = BucketKey(g.Key.AccountId, ccKey, coType, coId) + "|UB";
                if (!rows.TryGetValue(ubKey, out row!))
                {
                    row = new BudgetVsActualRow
                    {
                        AccountId      = g.Key.AccountId,
                        AccountCode    = g.Key.Code,
                        AccountName    = g.Key.Name,
                        CostCenterId   = g.Key.CostCenterId,
                        CostObjectType = g.Key.CostObjectType,
                        CostObjectId   = g.Key.CostObjectId,
                        IsUnbudgeted   = true
                    };
                    rows[ubKey] = row;
                }
            }

            // 累计实际金额（费用=借-贷，收入=贷-借）
            foreach (var x in g)
            {
                var amt = g.Key.Type == AccountType.Expense
                    ? x.Debit - x.Credit
                    : x.Credit - x.Debit;
                row.ActualPeriods[x.PeriodNo - 1] += amt;
            }
            row.Actual = row.ActualPeriods.Sum();
        }

        // ── 5. 排序：有预算行在前，未编预算在后；同组内按科目代码升序 ──
        rep.Rows = rows.Values
            .OrderBy(r => r.IsUnbudgeted)
            .ThenBy(r => r.AccountCode)
            .ToList();

        return rep;
    }

    // ── F-2: 上年实际按桶聚合（12期数组）────────────────────────────
    public async Task<Dictionary<(Guid acct, Guid cc, string coType, string coId), decimal[]>>
        AggregateActualByBucketAsync(int fiscalYear)
    {
        var actuals = await (
            from l in _db.JournalLines.AsNoTracking()
            join e in _db.JournalEntries.AsNoTracking() on l.EntryId equals e.Id
            join p in _db.FiscalPeriods.AsNoTracking() on e.PeriodId equals p.Id
            join a in _db.GlAccounts.AsNoTracking() on l.AccountId equals a.Id
            where e.Status == JournalStatus.Posted && p.FiscalYear == fiscalYear
                  && (a.Type == AccountType.Expense || a.Type == AccountType.Revenue)
            select new { l.AccountId, a.Type, l.CostCenterId, l.CostObjectType, l.CostObjectId, l.Debit, l.Credit, p.PeriodNo }
        ).ToListAsync();

        var result = new Dictionary<(Guid, Guid, string, string), decimal[]>();
        foreach (var x in actuals)
        {
            if (x.PeriodNo < 1 || x.PeriodNo > 12) continue;
            var key = (x.AccountId, x.CostCenterId ?? Guid.Empty, x.CostObjectType ?? "", x.CostObjectId ?? "");
            if (!result.TryGetValue(key, out var arr)) { arr = new decimal[12]; result[key] = arr; }
            var amt = x.Type == AccountType.Expense ? x.Debit - x.Credit : x.Credit - x.Debit;
            arr[x.PeriodNo - 1] += amt;
        }
        return result;
    }

    // ── F-3: 预检全量预警（复用 BudgetEvaluator blockOnly=false）───
    public async Task<List<BudgetWarningDto>> PreCheckAsync(JournalEntry entry)
    {
        var evals = await BudgetEvaluator.EvaluateAsync(_db, entry, blockOnly: false);
        return evals.Where(e => e.Exceeded).Select(e => new BudgetWarningDto
        {
            AccountCode = e.AccountCode, Budget = e.Budget, Used = e.Used, Incoming = e.Incoming, IsBlock = e.IsBlock,
        }).ToList();
    }

    // ── 辅助 ─────────────────────────────────────────────────────────

    /// <summary>构造预算桶 key（4 维 pipe 串）。</summary>
    private static string BucketKey(Guid acct, Guid cc, string coType, string coId)
        => $"{acct}|{cc}|{coType}|{coId}";

    /// <summary>
    /// 预算桶非通配维度数（spec §7.3 最具体匹配，与 BudgetGuard 一致）。
    /// 成本中心是 Guid，Guid.Empty 与真实 Guid 字符串等长，故不能用 key.Length 判具体度。
    /// </summary>
    private static int BucketSpecificity(string budgetKey)
    {
        var parts = budgetKey.Split('|');
        if (parts.Length != 4) return -1;
        int score = 0;
        if (Guid.TryParse(parts[1], out var cc) && cc != Guid.Empty) score++;
        if (parts[2] != "") score++;
        if (parts[3] != "") score++;
        return score;
    }

    /// <summary>
    /// 判断预算桶 key 是否能覆盖给定实际维度。
    /// 预算桶维度为 Guid.Empty / "" 表示"公司级（不限）"，可匹配任意实际维度；
    /// 非空则精确匹配。
    /// </summary>
    private static bool MatchBucketKey(
        string budgetKey, Guid acct, Guid cc, string coType, string coId)
    {
        var parts = budgetKey.Split('|');
        // 标准预算桶 key 有且仅有 4 段；超过 4 段（如 "|UB"）不参与此匹配
        if (parts.Length != 4) return false;
        if (!Guid.TryParse(parts[0], out var bAcct) || bAcct != acct) return false;
        if (!Guid.TryParse(parts[1], out var bCc))  return false;
        var bType = parts[2];
        var bId   = parts[3];

        return (bCc == Guid.Empty || bCc == cc)
            && (bType == ""        || bType == coType)
            && (bId   == ""        || bId   == coId);
    }
}
