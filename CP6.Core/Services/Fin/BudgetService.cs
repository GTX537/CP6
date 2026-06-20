using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

public class BudgetService : IBudgetService
{
    private readonly CP6Context _db;
    private readonly FinSequenceService _seq;
    public BudgetService(CP6Context db, FinSequenceService seq) { _db = db; _seq = seq; }

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
    public Task<FinResult> SubmitForApprovalAsync(Guid versionId, Guid userId, string userName) => throw new NotImplementedException();
    public Task<FinResult> ActivateAsync(Guid versionId) => throw new NotImplementedException();
    public Task ActivateFromApprovalAsync(Guid versionId, string decidedBy) => throw new NotImplementedException();
    // ── C-2 实现 ──
    internal Task CopyIntoAsync(Guid targetVersionId, Guid? fromVersionId, int? fromActualFiscalYear) => Task.CompletedTask;
}
