using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Fin;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Fin;

/// <summary>
/// 信用控制实现（章04 §3）。当前应收余额取未结应收发票 (Gross−Settled)×记账汇率折本位币（红字反向），
/// 与 <see cref="ArReconcileService"/> 子账口径一致。复用现有取引先 <c>BusinessPartner.CreditLimit</c>。
/// </summary>
public class CreditControlService : ICreditControlService
{
    private readonly CP6Context _db;

    public CreditControlService(CP6Context db) => _db = db;

    public async Task<CreditCheckResult> CheckCreditAsync(string customerId, decimal orderAmount)
    {
        var partner = await _db.BusinessPartners.FirstOrDefaultAsync(b => b.BpCd == customerId);
        var limit = partner?.CreditLimit ?? 0m;

        // 当前未结应收余额（本位币，红字反向）
        var openInvoices = await _db.ArInvoices
            .Where(i => i.CustomerId == customerId
                        && (i.Status == ArInvoiceStatus.Posted || i.Status == ArInvoiceStatus.PartiallySettled))
            .Select(i => new { i.GrossAmount, i.SettledAmount, i.FxRate, i.IsCreditMemo })
            .ToListAsync();
        var openAr = openInvoices.Sum(i =>
            Math.Round((i.GrossAmount - i.SettledAmount) * i.FxRate, 2, MidpointRounding.AwayFromZero)
            * (i.IsCreditMemo ? -1m : 1m));

        var exceeded = limit > 0m && openAr + orderAmount > limit;
        return new CreditCheckResult
        {
            CustomerId = customerId,
            CreditLimit = limit,
            OpenAr = openAr,
            OrderAmount = orderAmount,
            Exceeded = exceeded,
        };
    }
}
