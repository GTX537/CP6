using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 应收账龄实现（章04 §3，镜像 <see cref="ApAgingService"/>）。未收余额 = (Gross−Settled)×记账汇率（本位币），
/// 红字反向；按 (基准日 − 到期日) 天数落桶。
/// </summary>
public class ArAgingService : IArAgingService
{
    private readonly CP6Context _db;

    public ArAgingService(CP6Context db) => _db = db;

    public async Task<List<ArAgingRow>> AgingAsync(DateTime asOf, string? customerId = null)
    {
        var q = _db.ArInvoices.Where(i =>
            i.Status == ArInvoiceStatus.Posted || i.Status == ArInvoiceStatus.PartiallySettled);
        if (!string.IsNullOrEmpty(customerId)) q = q.Where(i => i.CustomerId == customerId);

        var invs = await q
            .Select(i => new { i.CustomerId, i.GrossAmount, i.SettledAmount, i.FxRate, i.DueDate, i.IsCreditMemo })
            .ToListAsync();

        var asOfDate = asOf.Date;
        return invs
            .GroupBy(i => i.CustomerId)
            .Select(g =>
            {
                var row = new ArAgingRow { CustomerId = g.Key };
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
            .OrderBy(r => r.CustomerId)
            .ToList();
    }
}
