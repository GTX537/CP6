using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class ExpiryService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public async Task<List<ExpiryStockDto>> GetExpiringStocksAsync(int days = 30, string? warehouseCd = null)
    {
        if (!_db.Database.IsNpgsql()) return await GetExpiringStocksCoreAsync(days, warehouseCd);
        var result = await ReadObservationAsync(new() { AsOfDate = DateTime.Today, Days = days, WarehouseCd = warehouseCd, PageSize = 500 }, true);
        return result.Rows.Select(r => new ExpiryStockDto { StockId = r.StockId, WarehouseCd = r.WarehouseCd, LocationCd = r.LocationCd,
            ProductCd = r.ProductCd, LotNo = r.LotNo, PhysicalQty = InventoryReportValuesV1.Legacy(r.PhysicalQty),
            AvailableQty = InventoryReportValuesV1.Legacy(r.AvailableQty), UnitPrice = r.UnitPrice == null ? null : InventoryReportValuesV1.Legacy(r.UnitPrice),
            ExpiryDate = r.ExpiryDate, DaysUntilExpiry = r.DaysUntilExpiry, UnitCd = r.UnitCd,
            // There is no Finance valuation/currency source. Stored price arithmetic is not a loss amount.
            LossAmount = null }).ToList();
    }
    public Task<ExpiryObservationV1> GetObservationAsync(ExpiryObservationQueryV1 query, CancellationToken ct = default)
        => ReadObservationAsync(query, false, ct);
    private Task<ExpiryObservationV1> ReadObservationAsync(ExpiryObservationQueryV1 query, bool legacy, CancellationToken ct = default)
    {
        var owner = Owner; var input = OperationsOwnerV1.Freeze(query ?? new());
        return owner.ExecuteAsync(OperationsOwnerV1.Expiry, "view", false, async (a, token) =>
        {
            var source = await a.SourceAsync(OperationsOwnerV1.Stock, token);
            var date = InventoryReportValuesV1.ObservationDate(input.AsOfDate);
            if (!legacy && (input.Days is < 0 or > 365) || input.Page < 1 || input.PageSize is < 1 or > 500 ||
                (long)(input.Page - 1) * input.PageSize > int.MaxValue)
                throw new OrderPriceCorrectionException("INVALID_OBSERVATION_RANGE", 400);
            DateTime threshold;
            try { threshold = date.AddDays(input.Days); }
            catch (ArgumentOutOfRangeException) { throw new OrderPriceCorrectionException("INVALID_OBSERVATION_RANGE", 400); }
            var rows = a.InventoryStocks(source.InventoryStocks()).Where(s => s.PhysicalQty > 0 && s.ExpiryDate != null);
            rows = legacy ? rows.Where(s => s.ExpiryDate <= threshold) : rows.Where(s => s.ExpiryDate!.Value.Date <= threshold);
            if (!string.IsNullOrWhiteSpace(input.WarehouseCd)) { var text = input.WarehouseCd.Trim(); rows = rows.Where(s => s.WarehouseCd == text); }
            var count = await rows.CountAsync(token);
            var stocks = await rows.OrderBy(s => s.ExpiryDate).ThenBy(s => s.Id).Skip((input.Page - 1) * input.PageSize).Take(input.PageSize).ToListAsync(token);
            var price = a.PriceReadable && source.PriceReadable; var reference = a.AmountReadable && price;
            return new ExpiryObservationV1 { AsOfDate = date, Days = input.Days, Total = count, Page = input.Page, PageSize = input.PageSize,
                UnitPriceReadable = price, ReferenceReadable = reference, Rows = stocks.Select(s => new ExpiryObservationRowV1 {
                    StockId = s.Id, WarehouseCd = s.WarehouseCd, LocationCd = s.LocationCd, ProductCd = s.ProductCd, LotNo = s.LotNo,
                    PhysicalQty = InventoryReportValuesV1.Exact(s.PhysicalQty).ToString(), AvailableQty = InventoryReportValuesV1.Exact(s.AvailableQty).ToString(),
                    UnitPrice = price && s.UnitPrice != null ? InventoryReportValuesV1.Exact(s.UnitPrice.Value).ToString() : null,
                    ReferenceValue = InventoryReportValuesV1.Reference([(s.PhysicalQty, s.UnitPrice)], reference),
                    ExpiryDate = s.ExpiryDate!.Value.Date, DaysUntilExpiry = (s.ExpiryDate.Value.Date - date).Days, UnitCd = s.UnitCd }).ToList() };
        }, ct);
    }
}
