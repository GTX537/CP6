using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class VmiService : IVmiService
{
    private readonly CP6Context _db;
    private readonly IWmsSequenceService _seq;
    private const string BillingPrefix = "VMI";

    public VmiService(CP6Context db, IWmsSequenceService seq)
    {
        _db = db; _seq = seq;
    }

    public async Task<List<VmiCustomerSummary>> SearchByCustomerAsync(string? customerCd)
    {
        var query = _db.Stocks.AsNoTracking()
            .Where(s => s.OwnerType == StockOwnerType.Customer && !s.IsDeleted);
        if (!string.IsNullOrWhiteSpace(customerCd)) query = query.Where(s => s.OwnerCd == customerCd);

        var grouped = await query
            .GroupBy(s => s.OwnerCd!)
            .Select(g => new VmiCustomerSummary
            {
                CustomerCd = g.Key,
                SkuCount = g.Select(x => x.ProductCd).Distinct().Count(),
                TotalPhysicalQty = g.Sum(x => x.PhysicalQty),
                TotalAllocatedQty = g.Sum(x => x.AllocatedQty),
                TotalAvailableQty = g.Sum(x => x.AvailableQty),
                EstimatedValue = g.Sum(x => x.UnitPrice.HasValue ? x.UnitPrice.Value * x.PhysicalQty : 0m),
            })
            .OrderBy(x => x.CustomerCd)
            .ToListAsync();
        return grouped;
    }

    public async Task<List<VmiStockDetail>> GetDetailsAsync(string customerCd)
    {
        if (string.IsNullOrWhiteSpace(customerCd)) return new();
        return await _db.Stocks.AsNoTracking()
            .Where(s => s.OwnerType == StockOwnerType.Customer && s.OwnerCd == customerCd && !s.IsDeleted)
            .OrderBy(s => s.ProductCd).ThenBy(s => s.LotNo)
            .Select(s => new VmiStockDetail
            {
                ProductCd = s.ProductCd, LotNo = s.LotNo,
                WarehouseCd = s.WarehouseCd, LocationCd = s.LocationCd,
                PhysicalQty = s.PhysicalQty, AvailableQty = s.AvailableQty,
                ReceiveDate = s.ReceiveDate, ExpiryDate = s.ExpiryDate,
            })
            .ToListAsync();
    }

    public async Task<List<VmiBilling>> SearchBillingsAsync(string? customerCd, string? yearMonth, bool? confirmed)
    {
        var query = _db.VmiBillings.AsNoTracking().Where(x => !x.IsDeleted);
        if (!string.IsNullOrWhiteSpace(customerCd)) query = query.Where(x => x.CustomerCd == customerCd);
        if (!string.IsNullOrWhiteSpace(yearMonth)) query = query.Where(x => x.YearMonth == yearMonth);
        if (confirmed.HasValue) query = query.Where(x => x.Confirmed == confirmed.Value);
        return await query.OrderByDescending(x => x.YearMonth).ThenBy(x => x.CustomerCd).ToListAsync();
    }

    public async Task<int> CalculateMonthlyBillingAsync(string yearMonth, decimal dailyStorageRate, string? userName)
    {
        if (string.IsNullOrWhiteSpace(yearMonth) || yearMonth.Length != 6)
            throw new InvalidOperationException("yearMonth must be YYYYMM (6 chars)");
        if (dailyStorageRate <= 0)
            throw new InvalidOperationException("dailyStorageRate > 0");

        if (!int.TryParse(yearMonth[..4], out var year) || !int.TryParse(yearMonth[4..], out var month) || month < 1 || month > 12)
            throw new InvalidOperationException("invalid yearMonth");
        int daysInMonth = DateTime.DaysInMonth(year, month);

        // 客先別 VMI 在庫サマリ取得（簡易：現時点の在庫を月末值とみなす）
        var summaries = await SearchByCustomerAsync(null);
        int upserted = 0;
        foreach (var s in summaries)
        {
            // 既存有り？
            var exists = await _db.VmiBillings.FirstOrDefaultAsync(b =>
                b.CustomerCd == s.CustomerCd && b.YearMonth == yearMonth && !b.IsDeleted);
            if (exists != null)
            {
                if (exists.Confirmed) continue; // 確定済はスキップ
                exists.SkuCount = s.SkuCount;
                exists.EndQty = s.TotalPhysicalQty;
                exists.AvgQty = (exists.BeginQty + exists.EndQty) / 2m;
                exists.DailyStorageRate = dailyStorageRate;
                exists.BillingAmount = exists.AvgQty * dailyStorageRate * daysInMonth;
                exists.CalculatedAt = DateTime.Now;
                exists.Modifier = userName; exists.ModifyDate = DateTime.Now;
            }
            else
            {
                var no = await _seq.NextAsync(BillingPrefix);
                _db.VmiBillings.Add(new VmiBilling
                {
                    BillingNo = no,
                    CustomerCd = s.CustomerCd, CustomerName = s.CustomerName,
                    YearMonth = yearMonth,
                    SkuCount = s.SkuCount,
                    BeginQty = s.TotalPhysicalQty, // 初回は月初=月末と看做す
                    EndQty = s.TotalPhysicalQty,
                    AvgQty = s.TotalPhysicalQty,
                    DailyStorageRate = dailyStorageRate,
                    BillingAmount = s.TotalPhysicalQty * dailyStorageRate * daysInMonth,
                    CalculatedAt = DateTime.Now,
                    Confirmed = false,
                    Creator = userName,
                });
            }
            upserted++;
        }
        await _db.SaveChangesAsync();
        return upserted;
    }

    public async Task ConfirmBillingAsync(string billingNo, string? userName)
    {
        var b = await _db.VmiBillings.FirstOrDefaultAsync(x => x.BillingNo == billingNo && !x.IsDeleted)
            ?? throw new InvalidOperationException("WM-MSG-070");
        if (b.Confirmed)
            throw new InvalidOperationException("WM-MSG-043: 既に確定済");
        b.Confirmed = true;
        b.Modifier = userName; b.ModifyDate = DateTime.Now;
        await _db.SaveChangesAsync();
    }
}
