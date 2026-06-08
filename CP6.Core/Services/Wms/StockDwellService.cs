using CP6.Core.EFDbContext;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;
using Microsoft.EntityFrameworkCore;

namespace CP6.Core.Services.Wms;

public class StockDwellService : IStockDwellService
{
    private const string SelfGroupKey = "SELF";
    private readonly CP6Context _db;

    public StockDwellService(CP6Context db)
    {
        _db = db;
    }

    public async Task<StockDwellSummaryDto> GetSummaryAsync(StockDwellQuery query, CancellationToken ct = default)
    {
        query ??= new StockDwellQuery();
        var asOfDate = (query.AsOfDate ?? DateTime.Today).Date;
        var groupBy = NormalizeGroupBy(query.GroupBy);

        var stocksQuery = _db.Stocks.AsNoTracking()
            .Where(s => !s.IsDeleted && s.PhysicalQty > 0m && s.ReceiveDate.HasValue);

        if (!string.IsNullOrWhiteSpace(query.WarehouseCd))
        {
            var warehouseCd = query.WarehouseCd.Trim();
            stocksQuery = stocksQuery.Where(s => s.WarehouseCd == warehouseCd);
        }

        if (!string.IsNullOrWhiteSpace(query.ProductCd))
        {
            var productCd = query.ProductCd.Trim();
            stocksQuery = stocksQuery.Where(s => s.ProductCd.Contains(productCd));
        }

        if (!string.IsNullOrWhiteSpace(query.OwnerCd))
        {
            var ownerCd = query.OwnerCd.Trim();
            stocksQuery = stocksQuery.Where(s => s.OwnerCd == ownerCd);
        }

        var stocks = await stocksQuery
            .Select(s => new StockDwellSource
            {
                ProductCd = s.ProductCd,
                OwnerType = s.OwnerType,
                OwnerCd = s.OwnerCd,
                PhysicalQty = s.PhysicalQty,
                UnitPrice = s.UnitPrice,
                ReceiveDate = s.ReceiveDate!.Value,
            })
            .ToListAsync(ct);

        var customerNames = await LoadCustomerNamesAsync(stocks, ct);

        var contributions = stocks.Select(s =>
        {
            var receiveDate = s.ReceiveDate.Date;
            var ageDays = Math.Max(0, (asOfDate - receiveDate).Days);
            var groupKey = ResolveGroupKey(groupBy, s);
            return new StockDwellContribution
            {
                GroupKey = groupKey,
                GroupLabel = ResolveGroupLabel(groupBy, groupKey, customerNames),
                ReceiveDate = receiveDate,
                AgeDays = ageDays,
                Qty = s.PhysicalQty,
                Value = s.PhysicalQty * (s.UnitPrice ?? 0m),
            };
        }).ToList();

        var rows = contributions
            .GroupBy(x => new { x.GroupKey, x.GroupLabel })
            .Select(g =>
            {
                var oldest = g.OrderBy(x => x.ReceiveDate).First();
                return new StockDwellRowDto
                {
                    GroupKey = g.Key.GroupKey,
                    GroupLabel = g.Key.GroupLabel,
                    TotalQty = g.Sum(x => x.Qty),
                    TotalValue = g.Sum(x => x.Value),
                    Bucket0To30Qty = g.Where(x => x.AgeDays <= 30).Sum(x => x.Qty),
                    Bucket31To60Qty = g.Where(x => x.AgeDays >= 31 && x.AgeDays <= 60).Sum(x => x.Qty),
                    Bucket61To90Qty = g.Where(x => x.AgeDays >= 61 && x.AgeDays <= 90).Sum(x => x.Qty),
                    BucketOver90Qty = g.Where(x => x.AgeDays > 90).Sum(x => x.Qty),
                    OldestReceiveDate = oldest.ReceiveDate,
                    OldestAgeDays = oldest.AgeDays,
                };
            })
            .OrderByDescending(x => x.BucketOver90Qty)
            .ThenByDescending(x => x.TotalQty)
            .ThenBy(x => x.GroupKey)
            .ToList();

        return new StockDwellSummaryDto
        {
            AsOfDate = asOfDate,
            GroupBy = groupBy,
            TotalQty = rows.Sum(x => x.TotalQty),
            TotalValue = rows.Sum(x => x.TotalValue),
            Over90Qty = rows.Sum(x => x.BucketOver90Qty),
            Over90Value = contributions.Where(x => x.AgeDays > 90).Sum(x => x.Value),
            Rows = rows,
        };
    }

    private async Task<Dictionary<string, string>> LoadCustomerNamesAsync(List<StockDwellSource> stocks, CancellationToken ct)
    {
        var customerCodes = stocks
            .Where(s => s.OwnerType == StockOwnerType.Customer && !string.IsNullOrWhiteSpace(s.OwnerCd))
            .Select(s => s.OwnerCd!)
            .Distinct()
            .ToList();

        if (customerCodes.Count == 0)
        {
            return new Dictionary<string, string>();
        }

        return await _db.BusinessPartners.AsNoTracking()
            .Where(bp => !bp.IsDeleted && customerCodes.Contains(bp.BpCd))
            .ToDictionaryAsync(bp => bp.BpCd, bp => bp.BpName, ct);
    }

    private static string NormalizeGroupBy(string? groupBy)
    {
        return string.Equals(groupBy, StockDwellGroupBy.Customer, StringComparison.OrdinalIgnoreCase)
            ? StockDwellGroupBy.Customer
            : StockDwellGroupBy.Product;
    }

    private static string ResolveGroupKey(string groupBy, StockDwellSource stock)
    {
        if (groupBy == StockDwellGroupBy.Customer)
        {
            return stock.OwnerType == StockOwnerType.Customer && !string.IsNullOrWhiteSpace(stock.OwnerCd)
                ? stock.OwnerCd!
                : SelfGroupKey;
        }

        return stock.ProductCd;
    }

    private static string ResolveGroupLabel(string groupBy, string groupKey, Dictionary<string, string> customerNames)
    {
        if (groupBy == StockDwellGroupBy.Customer && groupKey != SelfGroupKey)
        {
            return customerNames.GetValueOrDefault(groupKey) ?? groupKey;
        }

        return groupKey;
    }

    private sealed class StockDwellSource
    {
        public string ProductCd { get; set; } = string.Empty;
        public string OwnerType { get; set; } = StockOwnerType.Self;
        public string? OwnerCd { get; set; }
        public decimal PhysicalQty { get; set; }
        public decimal? UnitPrice { get; set; }
        public DateTime ReceiveDate { get; set; }
    }

    private sealed class StockDwellContribution
    {
        public string GroupKey { get; set; } = string.Empty;
        public string GroupLabel { get; set; } = string.Empty;
        public DateTime ReceiveDate { get; set; }
        public int AgeDays { get; set; }
        public decimal Qty { get; set; }
        public decimal Value { get; set; }
    }
}
