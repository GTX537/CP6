using CP6.Core.EFDbContext;
using CP6.Core.Services.Wf;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BudgetService : IBudgetService
{
    private readonly CP6Context _db;
    private readonly FinSequenceService _seq;
    private readonly IApprovalService _approval;
    public BudgetService(CP6Context db, FinSequenceService seq, IApprovalService approval)
    { _db = db; _seq = seq; _approval = approval; }

    public Task<List<Budget>> ListBudgetsAsync() =>
        _db.Budgets.AsNoTracking().OrderByDescending(b => b.FiscalYear).ToListAsync();

    public async Task<FinResult<Budget>> CreateBudgetAsync(Budget dto, string user)
    {
        if (await _db.Budgets.AnyAsync(b => b.FiscalYear == dto.FiscalYear))
            return FinResult<Budget>.Fail("E-A5-BUDGET-001", dto.FiscalYear);
        var now = DateTime.Now;
        var seq = await _seq.NextAsync("BUD", now);
        dto.No = $"BUD-{dto.FiscalYear}-{seq.Split('-').Last()}";
        dto.Scope = BudgetScope.PnL;
        dto.IsActive = true;
        dto.Creator = user; dto.CreateDate = now;
        _db.Budgets.Add(dto);
        await _db.SaveChangesAsync();
        return FinResult<Budget>.Pass(dto);
    }

    public async Task<FinResult> UpdateBudgetAsync(Guid id, string name, string? description)
    {
        var b = await _db.Budgets.FindAsync(id);
        if (b == null) return FinResult.Fail("E-A5-BUDGET-404");
        b.Name = name; b.Description = description;
        b.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> DeactivateBudgetAsync(Guid id)
    {
        var b = await _db.Budgets.FindAsync(id);
        if (b == null) return FinResult.Fail("E-A5-BUDGET-404");
        b.IsActive = false;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public Task<List<BudgetVersion>> ListVersionsAsync(Guid budgetId) =>
        _db.BudgetVersions.AsNoTracking().Where(v => v.BudgetId == budgetId)
           .OrderBy(v => v.VersionNo).ToListAsync();

    public Task<BudgetVersion?> GetVersionAsync(Guid versionId) =>
        _db.BudgetVersions.FirstOrDefaultAsync(v => v.Id == versionId);

    public async Task<FinResult<BudgetVersion>> CreateVersionAsync(Guid budgetId, string name, string user,
        Guid? copyFromVersionId = null, int? copyFromActualFiscalYear = null)
    {
        var budget = await _db.Budgets.FindAsync(budgetId);
        if (budget == null) return FinResult<BudgetVersion>.Fail("E-A5-BUDGET-404");
        var maxNo = await _db.BudgetVersions.Where(v => v.BudgetId == budgetId)
            .Select(v => (int?)v.VersionNo).MaxAsync() ?? 0;
        var v = new BudgetVersion
        {
            BudgetId = budgetId, VersionNo = maxNo + 1, Name = name,
            Status = BudgetVersionStatus.Draft, IsActive = false,
            Creator = user, CreateDate = DateTime.Now,
        };
        _db.BudgetVersions.Add(v);
        await _db.SaveChangesAsync();
        if (copyFromVersionId.HasValue || copyFromActualFiscalYear.HasValue)
            await CopyIntoAsync(v.Id, copyFromVersionId, copyFromActualFiscalYear);
        return FinResult<BudgetVersion>.Pass(v);
    }

    public async Task<FinResult> UpdateVersionAsync(Guid versionId, string name, BudgetControlMode mode, BudgetControlBasis basis)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        v.Name = name; v.DefaultControlMode = mode; v.DefaultControlBasis = basis;
        v.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> DeleteVersionAsync(Guid versionId)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-005");
        var lineIds = await _db.BudgetLines.Where(l => l.VersionId == versionId).Select(l => l.Id).ToListAsync();
        _db.BudgetLinePeriods.RemoveRange(_db.BudgetLinePeriods.Where(p => lineIds.Contains(p.BudgetLineId)));
        _db.BudgetLines.RemoveRange(_db.BudgetLines.Where(l => l.VersionId == versionId));
        _db.BudgetVersions.Remove(v);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    // ── B-2 实现 ──
    public async Task<FinResult> SubmitForApprovalAsync(Guid versionId, Guid userId, string userName)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Draft) return FinResult.Fail("E-A5-VERSION-002");
        if (!await _db.BudgetLines.AnyAsync(l => l.VersionId == versionId)) return FinResult.Fail("E-A5-VERSION-006");

        var total = await _db.BudgetLines.Where(l => l.VersionId == versionId).SumAsync(l => l.AnnualAmount);
        var budget = await _db.Budgets.FindAsync(v.BudgetId);
        if (budget == null) return FinResult.Fail("E-A5-BUDGET-404");
        Guid instanceId;
        try
        {
            instanceId = await _approval.SubmitAsync("A5_Budget", versionId.ToString(), userId,
                new { fiscalYear = budget.FiscalYear, versionNo = v.VersionNo, totalAmount = total });
        }
        catch (InvalidOperationException)   // OA 防重：同 (bizType,bizId) 已有 Running 实例
        {
            return FinResult.Fail("E-A5-VERSION-003");
        }
        v.Status = BudgetVersionStatus.PendingApproval;
        v.ApprovalInstanceId = instanceId;
        v.ApprovalRef = instanceId.ToString();
        v.SubmittedAt = DateTime.Now; v.SubmittedBy = userName;
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task<FinResult> ActivateAsync(Guid versionId)
    {
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return FinResult.Fail("E-A5-VERSION-404");
        if (v.Status != BudgetVersionStatus.Approved) return FinResult.Fail("E-A5-VERSION-004");
        await ApplyActivationAsync(v);
        await _db.SaveChangesAsync();
        return FinResult.Pass();
    }

    public async Task ActivateFromApprovalAsync(Guid versionId, string decidedBy)
    {
        // OA 回调专用：与引擎共享 DbContext，全程基于已加载实体改值，不自行 SaveChanges
        var v = await _db.BudgetVersions.FindAsync(versionId);
        if (v == null) return;
        if (v.Status == BudgetVersionStatus.Approved || v.IsActive) return;   // 幂等
        if (v.Status != BudgetVersionStatus.PendingApproval) return;
        v.Status = BudgetVersionStatus.Approved;
        v.ApprovedAt = DateTime.Now; v.ApprovedBy = decidedBy;
        await ApplyActivationAsync(v);
        // 不 SaveChanges —— 由 OA 引擎统一持久化（同事务原子）
    }

    /// <summary>清同 Budget 下其它 Active→Archived，本版 IsActive=true。基于已加载实体改值。</summary>
    private async Task ApplyActivationAsync(BudgetVersion v)
    {
        var priorActive = await _db.BudgetVersions
            .Where(x => x.BudgetId == v.BudgetId && x.IsActive && x.Id != v.Id).ToListAsync();
        foreach (var p in priorActive) { p.IsActive = false; p.Status = BudgetVersionStatus.Archived; }
        v.IsActive = true;
    }

    // ── C-2 实现 ──
    internal async Task CopyIntoAsync(Guid targetVersionId, Guid? fromVersionId, int? fromActualFiscalYear)
    {
        if (fromVersionId.HasValue)
        {
            var srcLines = await _db.BudgetLines.AsNoTracking().Where(l => l.VersionId == fromVersionId.Value).ToListAsync();
            var srcIds = srcLines.Select(l => l.Id).ToList();
            var srcPeriods = await _db.BudgetLinePeriods.AsNoTracking().Where(p => srcIds.Contains(p.BudgetLineId)).ToListAsync();
            foreach (var sl in srcLines)
            {
                var nl = new BudgetLine
                {
                    VersionId = targetVersionId, AccountId = sl.AccountId,
                    CostCenterId = sl.CostCenterId, CostObjectType = sl.CostObjectType, CostObjectId = sl.CostObjectId,
                    AnnualAmount = sl.AnnualAmount, ControlMode = sl.ControlMode, ControlBasis = sl.ControlBasis, Memo = sl.Memo,
                };
                nl.NormalizeKeys();
                _db.BudgetLines.Add(nl);
                await _db.SaveChangesAsync();   // flush each line to get nl.Id before inserting its periods
                foreach (var p in srcPeriods.Where(p => p.BudgetLineId == sl.Id))
                    _db.BudgetLinePeriods.Add(new BudgetLinePeriod { BudgetLineId = nl.Id, PeriodNo = p.PeriodNo, Amount = p.Amount });
            }
            await _db.SaveChangesAsync();
        }
        if (fromActualFiscalYear.HasValue)
        {
            await CopyFromActualAsync(targetVersionId, fromActualFiscalYear.Value);
        }
    }

    // F-2 实现（上年实际聚合回填）。
    internal virtual async Task CopyFromActualAsync(Guid targetVersionId, int sourceFiscalYear)
    {
        var agg = await new BudgetReportService(_db).AggregateActualByBucketAsync(sourceFiscalYear);
        var lineSvc = new BudgetLineService(_db);
        foreach (var (key, periods) in agg)
        {
            await lineSvc.UpsertLineAsync(new BudgetLineDto
            {
                VersionId = targetVersionId,
                AccountId = key.Item1,
                CostCenterId = key.Item2 == Guid.Empty ? null : key.Item2,
                CostObjectType = key.Item3 == "" ? null : key.Item3,
                CostObjectId = key.Item4 == "" ? null : key.Item4,
                SpreadMode = "manual",
                Periods = periods,
                AnnualAmount = periods.Sum(),
            });
        }
    }
}
