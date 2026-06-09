using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Erp;

/// <inheritdoc />
public class FxRateService : IFxRateService
{
    private readonly CP6Context _db;

    public FxRateService(CP6Context db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<(string CurrencyCd, decimal Rate)> ResolveForCustomerAsync(string customerCd, DateTime asOf, CancellationToken ct = default)
    {
        var currency = await _db.BusinessPartners.AsNoTracking()
            .Where(b => b.BpCd == customerCd && !b.IsDeleted)
            .Select(b => b.CurrencyCd)
            .FirstOrDefaultAsync(ct);

        if (FxConstants.IsBase(currency))
        {
            return (FxConstants.BaseCurrency, 1m);
        }

        var rate = await ResolveRateAsync(currency!, asOf, ct)
            ?? throw new InvalidOperationException(
                $"PA-MSG-FX-404: 通貨 {currency} の {asOf:yyyy-MM-dd} 時点の為替レートが未登録です");

        return (currency!.ToUpperInvariant(), rate);
    }

    /// <inheritdoc />
    public async Task<decimal?> ResolveRateAsync(string currencyCd, DateTime asOf, CancellationToken ct = default)
    {
        if (FxConstants.IsBase(currencyCd))
        {
            return 1m;
        }

        var cur = currencyCd.ToUpperInvariant();
        var date = asOf.Date;
        var rate = await _db.FxRates.AsNoTracking()
            .Where(r => !r.IsDeleted && r.CurrencyCd == cur && r.RateDate <= date)
            .OrderByDescending(r => r.RateDate)
            .Select(r => (decimal?)r.Rate)
            .FirstOrDefaultAsync(ct);
        return rate;
    }

    // ───────── マスタ CRUD ─────────

    public async Task<List<FxRate>> ListAsync(string? currencyCd = null, CancellationToken ct = default)
    {
        var q = _db.FxRates.AsNoTracking().Where(r => !r.IsDeleted);
        if (!string.IsNullOrWhiteSpace(currencyCd))
        {
            var cur = currencyCd.Trim().ToUpperInvariant();
            q = q.Where(r => r.CurrencyCd == cur);
        }
        return await q.OrderBy(r => r.CurrencyCd).ThenByDescending(r => r.RateDate).ToListAsync(ct);
    }

    public async Task<FxRate> CreateAsync(FxRate rate, string? userName, CancellationToken ct = default)
    {
        Validate(rate);
        rate.Id = rate.Id == Guid.Empty ? Guid.NewGuid() : rate.Id;
        rate.CurrencyCd = rate.CurrencyCd.Trim().ToUpperInvariant();
        rate.RateDate = rate.RateDate.Date;
        rate.Creator = userName;
        rate.CreateDate = DateTime.Now;
        rate.IsDeleted = false;
        _db.FxRates.Add(rate);
        await _db.SaveChangesAsync(ct);
        return rate;
    }

    public async Task UpdateAsync(Guid id, FxRate rate, string? userName, CancellationToken ct = default)
    {
        Validate(rate);
        var existing = await _db.FxRates.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
            ?? throw new InvalidOperationException("PA-MSG-FX-NOTFOUND: 為替レートが見つかりません");
        existing.CurrencyCd = rate.CurrencyCd.Trim().ToUpperInvariant();
        existing.RateDate = rate.RateDate.Date;
        existing.Rate = rate.Rate;
        existing.Remarks = rate.Remarks;
        existing.Modifier = userName;
        existing.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(Guid id, string? userName, CancellationToken ct = default)
    {
        var existing = await _db.FxRates.FirstOrDefaultAsync(r => r.Id == id && !r.IsDeleted, ct)
            ?? throw new InvalidOperationException("PA-MSG-FX-NOTFOUND: 為替レートが見つかりません");
        existing.IsDeleted = true;
        existing.Modifier = userName;
        existing.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    private static void Validate(FxRate rate)
    {
        if (string.IsNullOrWhiteSpace(rate.CurrencyCd))
            throw new InvalidOperationException("PA-MSG-FX-001: 通貨CDは必須です");
        if (FxConstants.IsBase(rate.CurrencyCd))
            throw new InvalidOperationException($"PA-MSG-FX-002: 基軸通貨 {FxConstants.BaseCurrency} はレート登録不要です");
        if (rate.Rate <= 0)
            throw new InvalidOperationException("PA-MSG-FX-003: レートは0より大きい値を入力してください");
    }
}
