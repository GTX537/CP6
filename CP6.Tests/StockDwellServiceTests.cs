using CP6.Core.EFDbContext;
using CP6.Core.Services.Wms;
using CP6.Entity.DomainModels;
using CP6.Entity.DomainModels.Wms;
using CP6.Entity.DTOs;

namespace CP6.Tests;

public class StockDwellServiceTests
{
    private static CP6Context NewDb() => TestHelper.CreateInMemoryContext();

    private static StockDwellService NewService(CP6Context db) => new(db);

    [Fact]
    public async Task GetSummaryAsync_BucketsStockByReceiveAge()
    {
        await using var db = NewDb();
        var asOf = new DateTime(2026, 6, 7);
        db.Stocks.AddRange(
            NewStock("P001", asOf.AddDays(-10), 10m),
            NewStock("P001", asOf.AddDays(-45), 20m, lotNo: "L02"),
            NewStock("P001", asOf.AddDays(-75), 30m, lotNo: "L03"),
            NewStock("P001", asOf.AddDays(-120), 40m, lotNo: "L04"));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new StockDwellQuery
        {
            GroupBy = StockDwellGroupBy.Product,
            AsOfDate = asOf,
        });

        var row = Assert.Single(result.Rows);
        Assert.Equal("P001", row.GroupKey);
        Assert.Equal(100m, row.TotalQty);
        Assert.Equal(10m, row.Bucket0To30Qty);
        Assert.Equal(20m, row.Bucket31To60Qty);
        Assert.Equal(30m, row.Bucket61To90Qty);
        Assert.Equal(40m, row.BucketOver90Qty);
        Assert.Equal(40m, result.Over90Qty);
        Assert.Equal(asOf.AddDays(-120), row.OldestReceiveDate);
        Assert.Equal(120, row.OldestAgeDays);
    }

    [Fact]
    public async Task GetSummaryAsync_GroupByProduct_AggregatesAcrossWarehousesAndLots()
    {
        await using var db = NewDb();
        var asOf = new DateTime(2026, 6, 7);
        db.Stocks.AddRange(
            NewStock("P001", asOf.AddDays(-12), 5m, unitPrice: 4m, warehouseCd: "W01", lotNo: "L01"),
            NewStock("P001", asOf.AddDays(-35), 7m, unitPrice: 4m, warehouseCd: "W02", lotNo: "L02"),
            NewStock("P002", asOf.AddDays(-95), 3m, unitPrice: 8m, warehouseCd: "W01", lotNo: "L03"));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new StockDwellQuery
        {
            GroupBy = StockDwellGroupBy.Product,
            AsOfDate = asOf,
        });

        Assert.Equal(2, result.Rows.Count);
        var p1 = result.Rows.Single(x => x.GroupKey == "P001");
        Assert.Equal("P001", p1.GroupLabel);
        Assert.Equal(12m, p1.TotalQty);
        Assert.Equal(48m, p1.TotalValue);
        Assert.Equal(15m, result.TotalQty);
        Assert.Equal(72m, result.TotalValue);
    }

    [Fact]
    public async Task GetSummaryAsync_GroupByCustomer_UsesOwnerCustomerAndSelfBucket()
    {
        await using var db = NewDb();
        var asOf = new DateTime(2026, 6, 7);
        db.BusinessPartners.Add(new BusinessPartner
        {
            BpCd = "C001",
            BpName = "Customer One",
            BaseCd = "B01",
            CustomerFlg = true,
        });
        db.Stocks.AddRange(
            NewStock("P001", asOf.AddDays(-20), 4m, ownerType: StockOwnerType.Customer, ownerCd: "C001"),
            NewStock("P002", asOf.AddDays(-20), 6m, ownerType: StockOwnerType.Self, ownerCd: null));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new StockDwellQuery
        {
            GroupBy = StockDwellGroupBy.Customer,
            AsOfDate = asOf,
        });

        Assert.Equal(2, result.Rows.Count);
        var customer = result.Rows.Single(x => x.GroupKey == "C001");
        var self = result.Rows.Single(x => x.GroupKey == "SELF");
        Assert.Equal("Customer One", customer.GroupLabel);
        Assert.Equal(4m, customer.TotalQty);
        Assert.Equal("SELF", self.GroupLabel);
        Assert.Equal(6m, self.TotalQty);
    }

    [Fact]
    public async Task GetSummaryAsync_FiltersByWarehouseProductAndOwner()
    {
        await using var db = NewDb();
        var asOf = new DateTime(2026, 6, 7);
        db.Stocks.AddRange(
            NewStock("P001", asOf.AddDays(-10), 5m, warehouseCd: "W01", lotNo: "L01", ownerType: StockOwnerType.Customer, ownerCd: "C001"),
            NewStock("P001", asOf.AddDays(-10), 7m, warehouseCd: "W02", lotNo: "L02", ownerType: StockOwnerType.Customer, ownerCd: "C001"),
            NewStock("P002", asOf.AddDays(-10), 11m, warehouseCd: "W01", lotNo: "L03", ownerType: StockOwnerType.Customer, ownerCd: "C001"),
            NewStock("P001", asOf.AddDays(-10), 13m, warehouseCd: "W01", lotNo: "L04", ownerType: StockOwnerType.Customer, ownerCd: "C002"));
        await db.SaveChangesAsync();

        var result = await NewService(db).GetSummaryAsync(new StockDwellQuery
        {
            GroupBy = StockDwellGroupBy.Product,
            WarehouseCd = "W01",
            ProductCd = "P001",
            OwnerCd = "C001",
            AsOfDate = asOf,
        });

        var row = Assert.Single(result.Rows);
        Assert.Equal("P001", row.GroupKey);
        Assert.Equal(5m, row.TotalQty);
        Assert.Equal(5m, result.TotalQty);
    }

    private static Stock NewStock(
        string productCd,
        DateTime receiveDate,
        decimal qty,
        decimal unitPrice = 10m,
        string warehouseCd = "W01",
        string locationCd = "A01",
        string lotNo = "L01",
        string ownerType = StockOwnerType.Self,
        string? ownerCd = null)
        => new()
        {
            WarehouseCd = warehouseCd,
            LocationCd = locationCd,
            ProductCd = productCd,
            LotNo = lotNo,
            PhysicalQty = qty,
            AllocatedQty = 0m,
            AvailableQty = qty,
            UnitPrice = unitPrice,
            ReceiveDate = receiveDate,
            OwnerType = ownerType,
            OwnerCd = ownerCd,
            QcStatus = StockQcStatus.Passed,
        };
}
