using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收子账↔GL 勾稽实现（章04 §3，镜像 <see cref="ApReconcileService"/>）。子账未收（本位币）按发票记账汇率折算；
/// GL 余额取 AR_CONTROL 角色科目的已过账分录（借−贷，资产借方余额）。两者应恒等。
/// </summary>
public class ArReconcileService : IArReconcileService
{
    private readonly CP6Context _db;

    public ArReconcileService(CP6Context db) => _db = db;

    public async Task<ArReconcileResult> ReconcileArAsync()
    {
        // 子账：未结的应收发票（已过账/部分核销）未收余额，按记账汇率折本位币；红字反向（减少应收）
        var openInvoices = await _db.ArInvoices
            .Where(i => i.Status == ArInvoiceStatus.Posted || i.Status == ArInvoiceStatus.PartiallySettled)
            .Select(i => new { i.GrossAmount, i.SettledAmount, i.FxRate, i.IsCreditMemo })
            .ToListAsync();
        var subLedger = openInvoices.Sum(i =>
            Math.Round((i.GrossAmount - i.SettledAmount) * i.FxRate, 2, MidpointRounding.AwayFromZero)
            * (i.IsCreditMemo ? -1m : 1m));

        // GL：AR_CONTROL 控制科目的已过账分录（资产 → 借−贷）
        var arControl = await _db.GlAccounts.FirstOrDefaultAsync(a => a.Role == "AR_CONTROL" && a.IsActive);
        decimal glBalance = 0m;
        if (arControl != null)
        {
            var postedIds = _db.JournalEntries.Where(e => e.Status == JournalStatus.Posted).Select(e => e.Id);
            glBalance = await _db.JournalLines
                .Where(l => l.AccountId == arControl.Id && postedIds.Contains(l.EntryId))
                .SumAsync(l => l.Debit - l.Credit);
        }

        return new ArReconcileResult { SubLedger = subLedger, GlBalance = glBalance };
    }
}
