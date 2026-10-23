using CP6.Core.Services.Erp;
using CP6.Core.Services.Integration;
using CP6.Entity.DTOs.Wms;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public partial class StockDwellService
{
    private OperationsOwnerV1 Owner => _owner?.Owns(_db) == true ? _owner : throw new OrderPriceCorrectionException("AUTHORITY_FENCE_UNAVAILABLE", 503);
    public async Task<StockDwellSummaryDto> GetSummaryAsync(StockDwellQuery query, CancellationToken ct = default)
    {
        if (!_db.Database.IsNpgsql()) return await GetSummaryCoreAsync(query, ct);
        var input = OperationsOwnerV1.Freeze(query ?? new StockDwellQuery()); input.AsOfDate ??= DateTime.Today;
        var value = await GetObservationAsync(input, ct);
        // The old numeric DTO cannot carry hidden/unknown values or rounded intermediates.
        return new() { AsOfDate = value.AsOfDate, GroupBy = value.GroupBy,
            TotalQty = InventoryReportValuesV1.Legacy(value.TotalQty), TotalValue = InventoryReportValuesV1.Legacy(value.ReferenceValue),
            Over90Qty = InventoryReportValuesV1.Legacy(value.Over90Qty), Over90Value = InventoryReportValuesV1.Legacy(value.Over90ReferenceValue),
            Rows = value.Rows.Select(r => new StockDwellRowDto { GroupKey = r.GroupKey, GroupLabel = r.GroupLabel,
                TotalQty = InventoryReportValuesV1.Legacy(r.TotalQty), TotalValue = InventoryReportValuesV1.Legacy(r.ReferenceValue),
                Bucket0To30Qty = InventoryReportValuesV1.Legacy(r.Bucket0To30Qty), Bucket31To60Qty = InventoryReportValuesV1.Legacy(r.Bucket31To60Qty),
                Bucket61To90Qty = InventoryReportValuesV1.Legacy(r.Bucket61To90Qty), BucketOver90Qty = InventoryReportValuesV1.Legacy(r.BucketOver90Qty),
                OldestReceiveDate = r.OldestReceiveDate, OldestAgeDays = r.OldestAgeDays }).ToList() };
    }
    public Task<StockDwellObservationV1> GetObservationAsync(StockDwellQuery query, CancellationToken ct = default)
    {
        var owner = Owner; var input = OperationsOwnerV1.Freeze(query ?? new());
        return owner.ExecuteAsync(OperationsOwnerV1.StockDwell, "view", false, async (a, token) =>
        {
            var source = await a.SourceAsync(OperationsOwnerV1.Stock, token);
            var date = InventoryReportValuesV1.ObservationDate(input.AsOfDate); var groupBy = NormalizeGroupBy(input.GroupBy);
            var queryable = a.InventoryStocks(source.InventoryStocks()).Where(s => s.PhysicalQty > 0 && s.ReceiveDate != null);
            if (!string.IsNullOrWhiteSpace(input.WarehouseCd)) { var text = input.WarehouseCd.Trim(); queryable = queryable.Where(s => s.WarehouseCd == text); }
            if (!string.IsNullOrWhiteSpace(input.ProductCd)) { var text = input.ProductCd.Trim(); queryable = queryable.Where(s => s.ProductCd.Contains(text)); }
            if (!string.IsNullOrWhiteSpace(input.OwnerCd)) { var text = input.OwnerCd.Trim(); queryable = queryable.Where(s => s.OwnerCd == text); }
            var stocks = await queryable.Select(s => new StockDwellSource { ProductCd = s.ProductCd, OwnerType = s.OwnerType, OwnerCd = s.OwnerCd,
                PhysicalQty = s.PhysicalQty, UnitPrice = s.UnitPrice, ReceiveDate = s.ReceiveDate!.Value }).ToListAsync(token);
            var readable = a.AmountReadable && source.PriceReadable;
            var names = a.CustomerNameReadable && groupBy == StockDwellGroupBy.Customer ? await LoadCustomerNamesAsync(stocks, token) : new Dictionary<string, string>();
            int Age(StockDwellSource s) => Math.Max(0, (date - s.ReceiveDate.Date).Days);
            string Qty(IEnumerable<StockDwellSource> rows) => InventoryReportValuesV1.Sum(rows.Select(s => s.PhysicalQty)).ToString();
            string? Reference(IEnumerable<StockDwellSource> rows) => InventoryReportValuesV1.Reference(rows.Select(s => (s.PhysicalQty, s.UnitPrice)), readable);
            var groups = stocks.GroupBy(s => ResolveGroupKey(groupBy, s))
                .OrderByDescending(g => InventoryReportValuesV1.Sum(g.Where(s => Age(s) > 90).Select(s => s.PhysicalQty)))
                .ThenByDescending(g => InventoryReportValuesV1.Sum(g.Select(s => s.PhysicalQty))).ThenBy(g => g.Key, StringComparer.Ordinal);
            return new StockDwellObservationV1 { AsOfDate = date, GroupBy = groupBy, TotalQty = Qty(stocks), Over90Qty = Qty(stocks.Where(s => Age(s) > 90)),
                ReferenceValue = Reference(stocks), Over90ReferenceValue = Reference(stocks.Where(s => Age(s) > 90)), ReferenceReadable = readable,
                CustomerNameReadable = a.CustomerNameReadable, MissingPriceCount = readable ? stocks.Count(s => s.UnitPrice == null) : null,
                Rows = groups.Select(g => new StockDwellObservationRowV1 { GroupKey = g.Key, GroupLabel = ResolveGroupLabel(groupBy, g.Key, names),
                    TotalQty = Qty(g), ReferenceValue = Reference(g), Bucket0To30Qty = Qty(g.Where(s => Age(s) <= 30)),
                    Bucket31To60Qty = Qty(g.Where(s => Age(s) is >= 31 and <= 60)), Bucket61To90Qty = Qty(g.Where(s => Age(s) is >= 61 and <= 90)),
                    BucketOver90Qty = Qty(g.Where(s => Age(s) > 90)), OldestReceiveDate = g.Min(s => s.ReceiveDate.Date), OldestAgeDays = g.Max(Age) }).ToList() };
        }, ct);
    }
}
