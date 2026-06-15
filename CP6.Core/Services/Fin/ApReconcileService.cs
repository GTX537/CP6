using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付子账↔GL 勾稽实现（章03 §4）。子账未付（本位币）按发票记账汇率折算；
/// GL 余额取 AP_CONTROL 角色科目的已过账分录（贷−借）。两者应恒等。
/// </summary>
public class ApReconcileService : IApReconcileService
{
    private readonly CP6Context _db;

    public ApReconcileService(CP6Context db) => _db = db;

    public async Task<ApReconcileResult> ReconcileApAsync()
    {
        // 子账：未结的应付发票（已过账/部分核销）未付余额，按记账汇率折本位币
        var openInvoices = await _db.ApInvoices
            .Where(i => i.Status == ApInvoiceStatus.Posted || i.Status == ApInvoiceStatus.PartiallySettled)
            .Select(i => new { i.GrossAmount, i.SettledAmount, i.FxRate })
            .ToListAsync();
        var subLedger = openInvoices.Sum(i =>
            Math.Round((i.GrossAmount - i.SettledAmount) * i.FxRate, 2, MidpointRounding.AwayFromZero));

        // GL：AP_CONTROL 控制科目的已过账分录（负债 → 贷−借）
        var apControl = await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == "AP_CONTROL" && a.IsActive);
        decimal glBalance = 0m;
        if (apControl != null)
        {
            var postedIds = _db.JournalEntries.Where(e => e.Status == JournalStatus.Posted).Select(e => e.Id);
            glBalance = await _db.JournalLines
                .Where(l => l.AccountId == apControl.Id && postedIds.Contains(l.EntryId))
                .SumAsync(l => l.Credit - l.Debit);
        }

        return new ApReconcileResult { SubLedger = subLedger, GlBalance = glBalance };
    }
}
