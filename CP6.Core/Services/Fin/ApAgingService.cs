using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应付账龄实现（章03 §5）。未付余额 = (Gross−Settled)×记账汇率（本位币），红字反向；
/// 按 (基准日 − 到期日) 天数落桶。
/// </summary>
public class ApAgingService : IApAgingService
{
    private readonly CP6Context _db;

    public ApAgingService(CP6Context db) => _db = db;

    public async Task<List<ApAgingRow>> AgingAsync(DateTime asOf, string? supplierId = null)
    {
        var q = _db.ApInvoices.Where(i =>
            i.Status == ApInvoiceStatus.Posted || i.Status == ApInvoiceStatus.PartiallySettled);
        if (!string.IsNullOrEmpty(supplierId)) q = q.Where(i => i.SupplierId == supplierId);

        var invs = await q
            .Select(i => new { i.SupplierId, i.GrossAmount, i.SettledAmount, i.FxRate, i.DueDate, i.IsCreditMemo })
            .ToListAsync();

        var asOfDate = asOf.Date;
        return invs
            .GroupBy(i => i.SupplierId)
            .Select(g =>
            {
                var row = new ApAgingRow { SupplierId = g.Key };
                foreach (var i in g)
                {
                    var open = Math.Round((i.GrossAmount - i.SettledAmount) * i.FxRate, 2, MidpointRounding.AwayFromZero)
                               * (i.IsCreditMemo ? -1m : 1m);
                    var overdueDays = (asOfDate - i.DueDate.Date).Days;
                    if (overdueDays <= 0) row.NotDue += open;
                    else if (overdueDays <= 30) row.Days1To30 += open;
                    else if (overdueDays <= 60) row.Days31To60 += open;
                    else row.Days60Plus += open;
                }
                return row;
            })
            .OrderBy(r => r.SupplierId)
            .ToList();
    }
}
